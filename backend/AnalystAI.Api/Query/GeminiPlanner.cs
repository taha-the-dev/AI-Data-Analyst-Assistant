using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnalystAI.Api.Analysis;

namespace AnalystAI.Api.Query;

/// <summary>
/// Google Gemini as the planner.
///
/// It does two jobs and neither of them is arithmetic. It reads the question and
/// returns a <see cref="QuerySpec"/>; then, after the engine has computed the
/// figures, it writes the sentence describing them. Everything it returns is
/// validated before use, and any failure falls back to
/// <see cref="KeywordPlanner"/>, so a missing key or an exhausted quota degrades
/// the wording rather than breaking the screen.
/// </summary>
public class GeminiPlanner(
    HttpClient http,
    IConfiguration configuration,
    KeywordPlanner fallback,
    ILogger<GeminiPlanner> logger) : IQuestionPlanner
{
    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Id => "gemini";

    private string? ApiKey => ResolveKey(configuration);

    public string Model => configuration["Gemini:Model"] ?? "gemini-2.5-flash";

    /// <summary>
    /// The keyword plan, attributed to the keyword planner. Every path out of
    /// <see cref="PlanAsync"/> that does not reach Gemini comes through here,
    /// so a fallback can never be reported as a hosted answer.
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
        var fromConfig = configuration["Gemini:ApiKey"];
        if (!string.IsNullOrWhiteSpace(fromConfig)) return fromConfig.Trim();

        var fromEnvironment = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        return string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment.Trim();
    }

    public static bool IsConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(ResolveKey(configuration));

    public async Task<PlannedSpec> PlanAsync(
        string question, IReadOnlyList<string> priorTurns, DatasetSchema schema, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ApiKey)) return FellBack(question, priorTurns, schema);

        var prompt = Prompts.Plan(question, priorTurns, schema);

        try
        {
            var response = await PostAsync(new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } },
                generationConfig = new
                {
                    temperature = 0,
                    responseMimeType = "application/json",
                    responseSchema = SpecSchema(),
                },
            }, ct);

            var text = ExtractText(response);
            if (string.IsNullOrWhiteSpace(text)) return FellBack(question, priorTurns, schema);

            var raw = JsonSerializer.Deserialize<JsonObject>(text, Json);
            return raw is null ? FellBack(question, priorTurns, schema) : Validate(raw, question, priorTurns, schema);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Gemini planning failed; using the keyword planner");
            return FellBack(question, priorTurns, schema);
        }
    }

    public async Task<string> ExplainAsync(string question, QueryResult result, DatasetSchema schema, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ApiKey) || result.Figures.Count == 0)
            return fallback.Explain(question, result);

        var prompt = Prompts.Explain(question, result);

        try
        {
            var response = await PostAsync(new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } },
                generationConfig = new { temperature = 0.2, responseMimeType = "text/plain" },
            }, ct);

            var text = ExtractText(response)?.Trim();
            return string.IsNullOrWhiteSpace(text) ? fallback.Explain(question, result) : text;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Gemini explanation failed; using the templated answer");
            return fallback.Explain(question, result);
        }
    }

    private async Task<JsonObject?> PostAsync(object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint}/{Model}:generateContent")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("x-goog-api-key", ApiKey);

        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Gemini returned {Status}: {Detail}", (int)response.StatusCode,
                detail.Length <= 400 ? detail : detail[..400]);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<JsonObject>(Json, ct);
    }

    private static string? ExtractText(JsonObject? response) =>
        response?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>();

    /// <summary>
    /// The shared validator decides what is usable; anything it rejects falls
    /// back to the keyword plan rather than reaching the engine.
    /// </summary>
    private PlannedSpec Validate(JsonObject raw, string question, IReadOnlyList<string> priorTurns, DatasetSchema schema)
    {
        var spec = SpecValidator.TryBuild(raw, schema);
        if (spec is not null) return new PlannedSpec(spec, Id, Model);

        logger.LogWarning("Gemini returned an unusable plan; using the keyword plan");
        return FellBack(question, priorTurns, schema);
    }

    private static object SpecSchema() => new
    {
        type = "OBJECT",
        properties = new
        {
            intent = new { type = "STRING" },
            groupBy = new { type = "STRING" },
            metric = new { type = "STRING" },
            aggregate = new { type = "STRING" },
            timeBucket = new { type = "STRING" },
            sort = new { type = "STRING" },
            limit = new { type = "INTEGER" },
            chart = new { type = "STRING" },
            title = new { type = "STRING" },
            filters = new
            {
                type = "ARRAY",
                items = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        column = new { type = "STRING" },
                        op = new { type = "STRING" },
                        value = new { type = "STRING" },
                    },
                    required = new[] { "column", "op", "value" },
                },
            },
        },
        required = new[] { "intent", "groupBy", "aggregate", "title" },
    };
}
