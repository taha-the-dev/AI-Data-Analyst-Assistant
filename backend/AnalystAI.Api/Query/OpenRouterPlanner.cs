using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnalystAI.Api.Analysis;

namespace AnalystAI.Api.Query;

/// <summary>
/// OpenRouter as the planner — one key, many models, an OpenAI-compatible API.
///
/// It does the same two jobs as every other planner and neither of them is
/// arithmetic: it reads the question and returns a <see cref="QuerySpec"/>, and
/// once the engine has computed the figures it writes the sentence describing
/// them. Everything it returns goes through <see cref="SpecValidator"/> first,
/// and every failure — no key, a rate limit, a timeout, a malformed reply —
/// falls back to <see cref="KeywordPlanner"/>. The screen keeps working; only
/// the wording gets plainer.
/// </summary>
public partial class OpenRouterPlanner(
    HttpClient http,
    IConfiguration configuration,
    KeywordPlanner fallback,
    ILogger<OpenRouterPlanner> logger) : IQuestionPlanner
{
    private const string Endpoint = "https://openrouter.ai/api/v1/chat/completions";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Models this deployment offers. Free-tier slugs, because the key that
    /// ships with the project is a free-tier key; any OpenRouter slug works if
    /// it is set in configuration.
    /// </summary>
    public static readonly string[] Models =
    [
        "nvidia/nemotron-3-super-120b-a12b:free",
        "nvidia/nemotron-3-nano-30b-a3b:free",
        "google/gemma-4-31b-it:free",
        "z-ai/glm-5.2:free",
    ];

    public string Id => "openrouter";

    private string? ApiKey => ResolveKey(configuration);

    public string Model
    {
        get
        {
            var configured = configuration["OpenRouter:Model"];
            return string.IsNullOrWhiteSpace(configured) ? Models[0] : configured.Trim();
        }
    }

    /// <summary>
    /// The keyword plan, attributed to the keyword planner. Every path out of
    /// <see cref="PlanAsync"/> that does not reach OpenRouter comes through
    /// here, so a fallback can never be reported as a hosted answer.
    /// </summary>
    private PlannedSpec FellBack(string question, IReadOnlyList<string> priorTurns, DatasetSchema schema) =>
        new(fallback.Plan(question, priorTurns, schema), fallback.Id, fallback.Model);

    /// <summary>
    /// appsettings ships an empty ApiKey placeholder, and "" is not null — so a
    /// plain ?? chain would swallow the environment variable. Both sources are
    /// tested for actual content.
    /// </summary>
    internal static string? ResolveKey(IConfiguration configuration)
    {
        var fromConfig = configuration["OpenRouter:ApiKey"];
        if (!string.IsNullOrWhiteSpace(fromConfig)) return fromConfig.Trim();

        var fromEnvironment = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        return string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment.Trim();
    }

    public static bool IsConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(ResolveKey(configuration));

    public async Task<PlannedSpec> PlanAsync(
        string question, IReadOnlyList<string> priorTurns, DatasetSchema schema, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ApiKey)) return FellBack(question, priorTurns, schema);

        try
        {
            var content = await CompleteAsync(new
            {
                model = Model,
                temperature = 0,
                // Generous, because several models think out loud before they
                // emit the object and a truncated reply has no object at all.
                max_tokens = 1200,
                // Reasoning models otherwise return their working as the message
                // body; excluding it is the difference between an answer and a
                // transcript of one being written.
                reasoning = new { exclude = true },
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new { role = "system", content = "You return a single JSON object and nothing else." },
                    new { role = "user", content = Prompts.Plan(question, priorTurns, schema) },
                },
            }, ct);

            if (string.IsNullOrWhiteSpace(content)) return FellBack(question, priorTurns, schema);

            // Some models wrap the object in prose or a fenced block; take the
            // outermost object rather than rejecting an otherwise good plan.
            var json = ExtractObject(content);
            if (json is null) return FellBack(question, priorTurns, schema);

            var raw = JsonNode.Parse(json) as JsonObject;
            var spec = raw is null ? null : SpecValidator.TryBuild(raw, schema);

            if (spec is null)
            {
                logger.LogWarning("OpenRouter returned an unusable plan; using the keyword plan");
                return FellBack(question, priorTurns, schema);
            }

            return new PlannedSpec(spec, Id, Model);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OpenRouter planning failed; using the keyword planner");
            return FellBack(question, priorTurns, schema);
        }
    }

    public async Task<string> ExplainAsync(string question, QueryResult result, DatasetSchema schema, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ApiKey) || result.Figures.Count == 0)
            return fallback.Explain(question, result);

        try
        {
            var content = await CompleteAsync(new
            {
                model = Model,
                temperature = 0.2,
                max_tokens = 1200,
                reasoning = new { exclude = true },
                // Several models on OpenRouter narrate their working into the
                // message body, and `reasoning.exclude` does not stop the ones
                // that never separated it out in the first place. Asking for the
                // answer inside a JSON field does: the same JSON mode the plan
                // stage uses, with one key to read back.
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new { role = "system", content = "You describe computed figures in plain sentences. You never calculate. You reply with a single JSON object." },
                    new
                    {
                        role = "user",
                        // No placeholder text in the instruction: a model that
                        // echoes the template instead of writing would put the
                        // placeholder on screen.
                        content = Prompts.Explain(question, result)
                               + "\n\nReply with a single JSON object whose only key is \"answer\" "
                               + "and whose value is those sentences.",
                    },
                },
            }, ct);

            var text = ExtractAnswer(content);
            if (string.IsNullOrWhiteSpace(text) || !IsAnswer(text))
            {
                logger.LogWarning("OpenRouter did not return a usable answer; using the templated one");
                return fallback.Explain(question, result);
            }

            return text;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OpenRouter explanation failed; using the templated answer");
            return fallback.Explain(question, result);
        }
    }

    /// <summary>Returns the assistant message, or null for any failure — never throws on a bad status.</summary>
    private async Task<string?> CompleteAsync(object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        // OpenRouter attributes traffic with these; both are optional.
        request.Headers.Add("HTTP-Referer", "http://localhost:5173");
        request.Headers.Add("X-Title", "DataMind AI");

        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("OpenRouter returned {Status}: {Detail}", (int)response.StatusCode,
                detail.Length <= 400 ? detail : detail[..400]);
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonObject>(Json, ct);

        // A 200 can still carry an error body (upstream rate limits arrive this way).
        if (payload?["error"] is { } error)
        {
            logger.LogWarning("OpenRouter reported an error: {Error}", error.ToJsonString());
            return null;
        }

        return payload?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
    }

    /// <summary>
    /// Reads the answer back out of the JSON the explain call asks for.
    ///
    /// Models sometimes wrap it, nest it, or emit the object twice, so a failed
    /// parse is not the end: the answer field is pulled out by pattern before
    /// giving up. Raw JSON is never returned as prose — that would put braces on
    /// the screen — so an unreadable reply becomes null and the caller falls
    /// back to the templated answer.
    /// </summary>
    private static string? ExtractAnswer(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        var json = ExtractObject(content);
        if (json is not null)
        {
            try
            {
                var answer = (JsonNode.Parse(json) as JsonObject)?["answer"]?.ToString();
                if (!string.IsNullOrWhiteSpace(answer)) return answer.Trim();
            }
            catch (JsonException)
            {
                // Falls through to the pattern below.
            }
        }

        var match = AnswerField().Match(content);
        if (match.Success)
        {
            var value = match.Groups[1].Value
                .Replace("\\\"", "\"")
                .Replace("\\n", " ")
                .Replace("\\\\", "\\")
                .Trim();

            if (value.Length > 0) return value;
        }

        // Anything still carrying JSON punctuation is not a sentence.
        return content.Contains('{') ? null : content.Trim();
    }

    [System.Text.RegularExpressions.GeneratedRegex("\"answer\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"")]
    private static partial System.Text.RegularExpressions.Regex AnswerField();

    /// <summary>
    /// Last line of defence. A model that narrates its working, echoes the
    /// instruction, or answers in three words has not answered, and none of
    /// that reaches the screen: the templated answer is correct and readable,
    /// which is the bar.
    /// </summary>
    private static bool IsAnswer(string text)
    {
        // Two plain sentences do not fit in fewer than this many characters.
        if (text.Length < 40) return false;

        // Template placeholders, echoed rather than filled in.
        if (text.Contains('<') && text.Contains('>')) return false;

        string[] thinkingTells =
        [
            "we need to", "let me", "the user asks", "the user wants", "i should",
            "must use only", "okay,", "first, ", "let's ",
        ];

        var opening = text.Length <= 120 ? text.ToLowerInvariant() : text[..120].ToLowerInvariant();
        return !thinkingTells.Any(opening.Contains);
    }

    /// <summary>Pulls the outermost JSON object out of a reply that may be wrapped in prose.</summary>
    private static string? ExtractObject(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start ? content[start..(end + 1)] : null;
    }
}
