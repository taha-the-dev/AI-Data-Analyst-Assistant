using System.Diagnostics;
using System.Text.Json;
using AnalystAI.Api.Contracts;
using AnalystAI.Api.Data;
using AnalystAI.Api.Models;
using AnalystAI.Api.Query;
using AnalystAI.Api.Security;
using AnalystAI.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AnalystAI.Api.Endpoints;

public static class ChatEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static RouteGroupBuilder MapChatEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/chat").WithTags("Chat");

        group.MapGet("/sessions", async (AppDbContext db, CancellationToken ct) =>
        {
            var sessions = await db.ChatSessions.AsNoTracking()
                .OrderByDescending(s => s.UpdatedAt)
                .Select(s => new ChatSessionDto(s.Id, s.Title, s.Subtitle, s.Messages.Count, s.UpdatedAt))
                .ToListAsync(ct);

            return Results.Ok(sessions);
        })
        .WithName("ListSessions")
        .WithSummary("Every stored conversation, most recent first.");

        group.MapPost("/sessions", async (AppDbContext db, CreateSessionRequest? body, CancellationToken ct) =>
        {
            var session = new ChatSession
            {
                Title = string.IsNullOrWhiteSpace(body?.Title)
                    ? "New session"
                    : InputLimits.Clip(body.Title.Trim(), InputLimits.TitleLength),
                Subtitle = InputLimits.Clip(body?.Subtitle ?? "No questions yet", InputLimits.SubtitleLength),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            db.ChatSessions.Add(session);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/chat/sessions/{session.Id}",
                new ChatSessionDto(session.Id, session.Title, session.Subtitle, 0, session.UpdatedAt));
        })
        .WithName("CreateSession")
        .WithSummary("Start a conversation.");

        group.MapGet("/sessions/{id:int}", async (AppDbContext db, int id, CancellationToken ct) =>
        {
            var session = await db.ChatSessions.AsNoTracking()
                .Include(s => s.Messages)
                .FirstOrDefaultAsync(s => s.Id == id, ct);

            if (session is null) return SessionNotFound(id);

            return Results.Ok(session.Messages
                .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
                .Select(ToDto).ToList());
        })
        .WithName("GetSessionMessages")
        .WithSummary("Every turn in one conversation, with the spec and figures behind each answer.");

        // Saving a conversation is naming it: every turn is already stored, so
        // what a name adds is the ability to find it again in History.
        group.MapPut("/sessions/{id:int}", async (
            AppDbContext db, int id, RenameSessionRequest body, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body?.Title))
                return Problems.BadRequest(
                    "The title is empty",
                    "Send { \"title\": \"...\" } in the body. A saved conversation needs a name to be found by.");

            var session = await db.ChatSessions.Include(s => s.Messages)
                .FirstOrDefaultAsync(s => s.Id == id, ct);
            if (session is null) return SessionNotFound(id);

            session.Title = InputLimits.Clip(body.Title.Trim(), InputLimits.TitleLength);
            if (!string.IsNullOrWhiteSpace(body.Subtitle))
                session.Subtitle = InputLimits.Clip(body.Subtitle.Trim(), InputLimits.SubtitleLength);
            session.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);

            return Results.Ok(new ChatSessionDto(
                session.Id, session.Title, session.Subtitle, session.Messages.Count, session.UpdatedAt));
        })
        .WithName("SaveSession")
        .WithSummary("Save a conversation under a name.");

        group.MapDelete("/sessions/{id:int}", async (AppDbContext db, int id, CancellationToken ct) =>
        {
            var session = await db.ChatSessions.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (session is null) return SessionNotFound(id);

            db.ChatSessions.Remove(session);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("DeleteSession")
        .WithSummary("Delete a conversation and its turns.");

        group.MapPost("/sessions/{id:int}/ask", async (
            AppDbContext db, QueryEngine engine, IPlannerResolver planners, IDatasetContext context,
            SchemaSummary schema, int id, AskRequest body, int? datasetId, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body?.Question))
                return Problems.BadRequest("The question is empty", "Send { \"question\": \"...\" } in the body.");

            if (body.Question.Length > InputLimits.QuestionLength)
                return Problems.BadRequest(
                    "The question is too long",
                    $"Keep a question under {InputLimits.QuestionLength:N0} characters.");

            var dataset = await context.ResolveAsync(datasetId, ct);
            if (dataset is null) return Problems.NoDataset(datasetId);

            var session = await db.ChatSessions.Include(s => s.Messages)
                .FirstOrDefaultAsync(s => s.Id == id, ct);
            if (session is null) return SessionNotFound(id);

            if (!await db.SalesRows.AnyAsync(r => r.DatasetId == dataset, ct))
                return Problems.NoRows(dataset.Value);

            var question = body.Question.Trim();

            // Stage 1 — plan. Prior turns go in so follow-ups resolve.
            var planSw = Stopwatch.StartNew();
            var priorTurns = session.Messages
                .OrderBy(m => m.Id).TakeLast(6).Select(m => m.Content).ToList();
            var planner = await planners.ResolveAsync(ct);
            var planned = await planner.PlanAsync(
                question, priorTurns, await schema.DescribeAsync(dataset.Value, ct), ct);
            var spec = planned.Spec;
            planSw.Stop();

            // Stage 2 — execute. Every figure originates here.
            var result = await engine.RunAsync(spec, dataset.Value, ct);

            // Stage 3 — explain, from the computed figures only.
            var prose = await planner.ExplainAsync(question, result, ct);

            var now = DateTime.UtcNow;
            var userMessage = new ChatMessage
            {
                SessionId = id, Role = "user", Content = question, CreatedAt = now,
            };
            var assistantMessage = new ChatMessage
            {
                SessionId = id,
                Role = "assistant",
                Content = prose,
                SpecJson = JsonSerializer.Serialize(result.Spec, Json),
                ResultJson = JsonSerializer.Serialize(result.Figures, Json),
                RowsScanned = result.RowsScanned,
                LatencyMs = (int)planSw.ElapsedMilliseconds + result.DurationMs,
                CreatedAt = now.AddMilliseconds(1),
            };

            db.ChatMessages.AddRange(userMessage, assistantMessage);
            session.UpdatedAt = now;
            if (session.Messages.Count == 0) session.Subtitle = Summarise(question);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new AskResponse(
                ToDto(userMessage), ToDto(assistantMessage),
                result.Spec, result.Figures, result.RowsScanned, result.RowsMatched,
                (int)planSw.ElapsedMilliseconds, result.DurationMs,
                result.Spec.Chart, result.Spec.Title, planned.Attribution));
        })
        .RequireRateLimiting(RateLimits.Assistant)
        .WithName("Ask")
        .WithSummary("Plan a QuerySpec, compute it against the rows, and explain the figures.");

        // Server-sent events: the plan and the figures arrive immediately, then
        // the sentence streams word by word — the client can render the working
        // before the prose finishes.
        group.MapGet("/sessions/{id:int}/stream", async (
            HttpContext http, AppDbContext db, QueryEngine engine, IPlannerResolver planners,
            IDatasetContext context, SchemaSummary schema, int id, string question, int? datasetId,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(question) || question.Length > InputLimits.QuestionLength)
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                await http.Response.WriteAsJsonAsync(new
                {
                    title = string.IsNullOrWhiteSpace(question) ? "The question is empty" : "The question is too long",
                }, ct);
                return;
            }

            var session = await db.ChatSessions.Include(s => s.Messages)
                .FirstOrDefaultAsync(s => s.Id == id, ct);
            if (session is null)
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                await http.Response.WriteAsJsonAsync(new { title = "Session not found", id }, ct);
                return;
            }

            var dataset = await context.ResolveAsync(datasetId, ct);
            if (dataset is null)
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                await http.Response.WriteAsJsonAsync(
                    new { title = "There is no dataset to answer from", datasetId }, ct);
                return;
            }

            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            var planSw = Stopwatch.StartNew();
            var priorTurns = session.Messages.OrderBy(m => m.Id).TakeLast(6).Select(m => m.Content).ToList();
            var planner = await planners.ResolveAsync(ct);
            var planned = await planner.PlanAsync(
                question.Trim(), priorTurns, await schema.DescribeAsync(dataset.Value, ct), ct);
            var spec = planned.Spec;
            planSw.Stop();

            await Send(http, "plan",
                new { spec, planMs = planSw.ElapsedMilliseconds, planner = planned.Attribution }, ct);

            var result = await engine.RunAsync(spec, dataset.Value, ct);
            await Send(http, "figures", new
            {
                figures = result.Figures,
                rowsScanned = result.RowsScanned,
                rowsMatched = result.RowsMatched,
                computeMs = result.DurationMs,
                chart = spec.Chart,
                title = spec.Title,
            }, ct);

            var prose = await planner.ExplainAsync(question, result, ct);
            foreach (var word in prose.Split(' '))
            {
                if (ct.IsCancellationRequested) break;
                await Send(http, "token", new { text = word + " " }, ct);
                await Task.Delay(18, ct);
            }

            var now = DateTime.UtcNow;
            db.ChatMessages.AddRange(
                new ChatMessage { SessionId = id, Role = "user", Content = question.Trim(), CreatedAt = now },
                new ChatMessage
                {
                    SessionId = id, Role = "assistant", Content = prose,
                    SpecJson = JsonSerializer.Serialize(result.Spec, Json),
                    ResultJson = JsonSerializer.Serialize(result.Figures, Json),
                    RowsScanned = result.RowsScanned,
                    LatencyMs = (int)planSw.ElapsedMilliseconds + result.DurationMs,
                    CreatedAt = now.AddMilliseconds(1),
                });
            session.UpdatedAt = now;
            await db.SaveChangesAsync(CancellationToken.None);

            await Send(http, "done", new { ok = true }, ct);
        })
        .RequireRateLimiting(RateLimits.Assistant)
        .WithName("AskStream")
        .WithSummary("Same three stages as /ask, delivered as server-sent events.");

        api.MapPost("/query/run", async (
            QueryEngine engine, IDatasetContext context, RunSpecRequest body, CancellationToken ct) =>
        {
            if (body?.Spec is null)
                return Problems.BadRequest("No spec supplied", "Send { \"spec\": { ... } } in the body.");

            if (body.Spec.GroupBy is not null && !QueryEngine.IsColumn(body.Spec.GroupBy))
                return Problems.UnknownColumn("groupBy column", body.Spec.GroupBy, QueryEngine.Columns);

            if (body.Spec.Metric is not null && !QueryEngine.IsColumn(body.Spec.Metric))
                return Problems.UnknownColumn("metric column", body.Spec.Metric, QueryEngine.Columns);

            if (!QueryEngine.IsAggregate(body.Spec.Aggregate))
                return Problems.BadRequest(
                    "Unknown aggregate",
                    $"'{body.Spec.Aggregate}' is not supported. Use one of: {string.Join(", ", QueryEngine.Aggregates)}.");

            var dataset = await context.ResolveAsync(body.DatasetId, ct);
            if (dataset is null) return Problems.NoDataset(body.DatasetId);

            var result = await engine.RunAsync(body.Spec, dataset.Value, ct);
            return Results.Ok(result);
        })
        .WithTags("Chat")
        .WithName("RunSpec")
        .WithSummary("Run an edited QuerySpec directly — the working is inspectable and re-runnable.");

        return api;
    }

    private record CreateSessionRequest(string? Title, string? Subtitle);

    private static IResult SessionNotFound(int id) => Results.Problem(
        title: "Session not found",
        detail: $"No chat session with id {id}.",
        statusCode: StatusCodes.Status404NotFound);

    private static async Task Send(HttpContext http, string @event, object payload, CancellationToken ct)
    {
        await http.Response.WriteAsync($"event: {@event}\n", ct);
        await http.Response.WriteAsync($"data: {JsonSerializer.Serialize(payload, Json)}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }

    private static ChatMessageDto ToDto(ChatMessage m) => new(
        m.Id, m.Role, m.Content,
        m.SpecJson is null ? null : JsonSerializer.Deserialize<QuerySpec>(m.SpecJson, Json),
        m.ResultJson is null ? null : JsonSerializer.Deserialize<List<Figure>>(m.ResultJson, Json),
        m.RowsScanned, m.LatencyMs, m.CreatedAt);

    private static string Summarise(string question) =>
        question.Length <= 48 ? question : question[..45].TrimEnd() + "...";
}
