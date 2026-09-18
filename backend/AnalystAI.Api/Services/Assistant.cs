using System.Diagnostics;
using System.Text.Json;
using AnalystAI.Api.Analysis;
using AnalystAI.Api.Contracts;
using AnalystAI.Api.Data;
using AnalystAI.Api.Endpoints;
using AnalystAI.Api.Models;
using AnalystAI.Api.Query;
using AnalystAI.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace AnalystAI.Api.Services;

/// <summary>
/// Answers a question about the file in context: describe the file, plan,
/// validate, compute, explain, and record the turn.
///
/// This used to be written out twice, inside the /ask and /stream endpoints,
/// and the copies drifted — one checked for rows and the other did not, one
/// named the conversation and the other did not. Both endpoints now call this,
/// and differ only in how they deliver what it produces.
/// </summary>
internal sealed class Assistant(
    AppDbContext db,
    IDatasetContext datasets,
    SourceStore sources,
    DashboardService dashboards,
    IPlannerResolver planners,
    KeywordPlanner keyword)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Everything a question needs, checked before anything is computed or streamed.</summary>
    public sealed record Turn(ChatSession Session, Dataset Dataset, Frame Frame, string Question);

    public sealed record Answer(
        ChatMessage UserMessage,
        ChatMessage AssistantMessage,
        QueryResult Result,
        string Planner,
        int PlanMs);

    /// <summary>Called as each stage finishes, so a stream can show the working before the prose.</summary>
    public interface IProgress
    {
        Task PlannedAsync(QuerySpec spec, string planner, int planMs);
        Task ComputedAsync(QueryResult result);
    }

    /// <summary>Validates the question and loads the conversation and the file, or says why it cannot.</summary>
    public async Task<(Turn? Turn, IResult? Problem)> PrepareAsync(
        int sessionId, int? datasetId, string? question, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(question))
            return (null, Problems.BadRequest("The question is empty", "Ask a question about the file in context."));
        if (question.Length > InputLimits.QuestionLength)
            return (null, Problems.BadRequest("The question is too long",
                $"Keep a question under {InputLimits.QuestionLength:N0} characters."));

        var session = await db.ChatSessions.Include(s => s.Messages).FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
            return (null, Results.Problem(title: "Session not found", detail: $"No chat session with id {sessionId}.",
                statusCode: StatusCodes.Status404NotFound));

        // A conversation is about one file: once it has asked about one, every
        // later question in it is answered from that file, whichever is selected.
        // One from before conversations were tied to a file takes the file it
        // is asked about now.
        var requested = session.DatasetId ?? datasetId;
        var id = await datasets.ResolveAsync(requested, ct);
        if (id is null) return (null, Problems.NoDataset(requested));
        if (session.DatasetId is null)
        {
            session.DatasetId = id;
            await db.SaveChangesAsync(ct);
        }

        var dataset = await db.Datasets.AsNoTracking().FirstAsync(d => d.Id == id, ct);
        var frame = await sources.LoadAsync(dataset, ct);
        if (frame is null) return (null, Problems.NoSource(dataset.Name));
        if (frame.RowCount == 0) return (null, Problems.NoRows(dataset.Id));

        return (new Turn(session, dataset, frame, question.Trim()), null);
    }

    public async Task<Answer> AskAsync(Turn turn, IProgress? progress, CancellationToken ct)
    {
        var schema = DatasetSchema.From(turn.Frame, await dashboards.GetAsync(turn.Dataset, ct));

        // Stage 1 — plan. Prior turns go in so follow-ups resolve.
        var watch = Stopwatch.StartNew();
        var priorTurns = turn.Session.Messages.OrderBy(m => m.Id).TakeLast(6).Select(m => m.Content).ToList();
        var planner = await planners.ResolveAsync(ct);
        var planned = await planner.PlanAsync(turn.Question, priorTurns, schema, ct);

        // Every plan passes the same gate, whichever planner wrote it. One the
        // gate cannot repair falls back to the keyword rules, which it can.
        var spec = SpecValidator.Normalize(planned.Spec, schema);
        var attribution = planned.Attribution;
        if (spec is null)
        {
            spec = SpecValidator.Normalize(keyword.Plan(turn.Question, priorTurns, schema), schema)
                   ?? throw new InvalidOperationException("The file has no column to group by.");
            attribution = $"{keyword.Id}/{keyword.Model}";
        }
        var planMs = (int)watch.ElapsedMilliseconds;
        if (progress is not null) await progress.PlannedAsync(spec, attribution, planMs);

        // Stage 2 — compute. Every figure originates here.
        var result = FrameEngine.Run(turn.Frame, schema, spec);
        if (progress is not null) await progress.ComputedAsync(result);

        // Stage 3 — explain, from the computed figures only.
        var prose = await planner.ExplainAsync(turn.Question, result, schema, ct);

        var first = turn.Session.Messages.Count == 0;
        var now = DateTime.UtcNow;
        var user = new ChatMessage { SessionId = turn.Session.Id, Role = "user", Content = turn.Question, CreatedAt = now };
        var assistant = new ChatMessage
        {
            SessionId = turn.Session.Id,
            Role = "assistant",
            Content = prose,
            SpecJson = JsonSerializer.Serialize(result.Spec, Json),
            ResultJson = JsonSerializer.Serialize(new StoredResult(result.Figures, result.Unit), Json),
            RowsScanned = result.RowsScanned,
            LatencyMs = planMs + result.DurationMs,
            CreatedAt = now.AddMilliseconds(1),
        };

        db.ChatMessages.AddRange(user, assistant);
        turn.Session.UpdatedAt = now;
        if (first) turn.Session.Subtitle = Summarise(turn.Question);
        // Saved even if the caller has gone: the answer was computed and belongs in the history.
        await db.SaveChangesAsync(CancellationToken.None);

        return new Answer(user, assistant, result, attribution, planMs);
    }

    /// <summary>What an assistant turn stores beside its prose: the figures and how they are written.</summary>
    private sealed record StoredResult(IReadOnlyList<Figure> Figures, ValueUnitDto Unit);

    public static ChatMessageDto ToDto(ChatMessage m)
    {
        IReadOnlyList<Figure>? figures = null;
        ValueUnitDto? unit = null;
        if (m.ResultJson is { } stored)
        {
            // Turns stored before units were kept hold a bare array of figures.
            if (stored.TrimStart().StartsWith('['))
                figures = JsonSerializer.Deserialize<List<Figure>>(stored, Json);
            else if (JsonSerializer.Deserialize<StoredResult>(stored, Json) is { } result)
                (figures, unit) = (result.Figures, result.Unit);
        }

        return new ChatMessageDto(
            m.Id, m.Role, m.Content,
            m.SpecJson is null ? null : JsonSerializer.Deserialize<QuerySpec>(m.SpecJson, Json),
            figures, unit, m.RowsScanned, m.LatencyMs, m.CreatedAt);
    }

    private static string Summarise(string question) =>
        question.Length <= 48 ? question : question[..45].TrimEnd() + "...";
}
