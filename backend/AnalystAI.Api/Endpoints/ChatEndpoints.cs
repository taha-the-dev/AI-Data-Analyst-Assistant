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
                .Select(Assistant.ToDto).ToList());
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
            Assistant assistant, int id, AskRequest body, int? datasetId, CancellationToken ct) =>
        {
            var (turn, problem) = await assistant.PrepareAsync(id, datasetId, body?.Question, ct);
            if (problem is not null) return problem;

            var answer = await assistant.AskAsync(turn!, null, ct);
            var result = answer.Result;

            return Results.Ok(new AskResponse(
                Assistant.ToDto(answer.UserMessage), Assistant.ToDto(answer.AssistantMessage),
                result.Spec, result.Figures, result.RowsScanned, result.RowsMatched,
                answer.PlanMs, result.DurationMs, result.Spec.Chart, result.Spec.Title, result.Unit, answer.Planner));
        })
        .RequireRateLimiting(RateLimits.Assistant)
        .WithName("Ask")
        .WithSummary("Plan a query over the file's own columns, compute it, and explain the figures.");

        // Server-sent events: the plan and the figures arrive immediately, then
        // the sentence streams word by word — the client can render the working
        // before the prose finishes. The same module answers as /ask; only the
        // delivery differs.
        group.MapGet("/sessions/{id:int}/stream", async (
            HttpContext http, Assistant assistant, int id, string? question, int? datasetId, CancellationToken ct) =>
        {
            var (turn, problem) = await assistant.PrepareAsync(id, datasetId, question, ct);
            if (problem is not null)
            {
                await problem.ExecuteAsync(http);
                return;
            }

            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            var answer = await assistant.AskAsync(turn!, new StreamProgress(http, ct), ct);

            foreach (var word in answer.AssistantMessage.Content.Split(' '))
            {
                if (ct.IsCancellationRequested) break;
                await Send(http, "token", new { text = word + " " }, ct);
                await Task.Delay(18, ct);
            }

            await Send(http, "done", new { ok = true }, ct);
        })
        .RequireRateLimiting(RateLimits.Assistant)
        .WithName("AskStream")
        .WithSummary("Same answer as /ask, delivered as server-sent events: plan, figures, tokens, done.");

        return api;
    }

    private record CreateSessionRequest(string? Title, string? Subtitle);

    private static IResult SessionNotFound(int id) => Results.Problem(
        title: "Session not found",
        detail: $"No chat session with id {id}.",
        statusCode: StatusCodes.Status404NotFound);

    private sealed class StreamProgress(HttpContext http, CancellationToken ct) : Assistant.IProgress
    {
        public Task PlannedAsync(QuerySpec spec, string planner, int planMs) =>
            Send(http, "plan", new { spec, planMs, planner }, ct);

        public Task ComputedAsync(QueryResult result) => Send(http, "figures", new
        {
            figures = result.Figures,
            unit = result.Unit,
            rowsScanned = result.RowsScanned,
            rowsMatched = result.RowsMatched,
            computeMs = result.DurationMs,
            chart = result.Spec.Chart,
            title = result.Spec.Title,
        }, ct);
    }

    private static async Task Send(HttpContext http, string @event, object payload, CancellationToken ct)
    {
        await http.Response.WriteAsync($"event: {@event}\n", ct);
        await http.Response.WriteAsync($"data: {JsonSerializer.Serialize(payload, Json)}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }
}
