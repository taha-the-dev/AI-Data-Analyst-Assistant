using System.Globalization;
using AnalystAI.Api.Contracts;
using AnalystAI.Api.Data;
using AnalystAI.Api.Query;
using Microsoft.EntityFrameworkCore;

namespace AnalystAI.Api.Endpoints;

/// <summary>The planner an account uses, the providers behind it, and liveness.</summary>
public static class SettingsEndpoints
{
    /// <summary>
    /// Status is read from configuration rather than hard-coded, so the Settings
    /// screen tells the truth about which providers can actually answer.
    /// </summary>
    private static ProviderDto[] BuildProviders(IConfiguration configuration) =>
    [
        new("keyword", "Built-in planner", "No model. Keyword rules pick the query; answers are templated.",
            ["rules-v1"], "Connected"),
        new("gemini", "Google Gemini",
            "Free tier at aistudio.google.com/apikey. Questions and column names are sent to Google; your rows are not.",
            ["gemini-2.5-flash", "gemini-2.5-pro", "gemini-2.0-flash"],
            GeminiPlanner.IsConfigured(configuration) ? "Connected" : "Needs key"),
        new("openrouter", "OpenRouter",
            "One key, many models. The question and your column names are sent to OpenRouter; your rows are not.",
            OpenRouterPlanner.Models,
            OpenRouterPlanner.IsConfigured(configuration) ? "Connected" : "Needs key"),
    ];

    /// <summary>
    /// The saved choice, corrected to one this build can honour. A provider it
    /// no longer offers — Ollama was listed here as "not implemented yet" — reads
    /// back as the built-in planner, and a model the provider does not serve
    /// reads back as its first model, so the Settings screen always opens on a
    /// selection that can be saved.
    /// </summary>
    private static (ProviderDto Provider, string Model) Effective(
        Models.UserSettings settings, ProviderDto[] providers)
    {
        var provider = providers.FirstOrDefault(p => p.Id == settings.ProviderId)
                       ?? providers.First(p => p.Id == "keyword");

        var model = provider.Id == settings.ProviderId && provider.Models.Contains(settings.ModelName)
            ? settings.ModelName
            : provider.Models[0];

        return (provider, model);
    }

    /// <summary>
    /// Settings, providers and status. Mapped on the signed-in group: each
    /// account has its own planner choice, and what it has loaded is its own.
    /// </summary>
    public static RouteGroupBuilder MapSettingsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/settings", async (AppDbContext db, IConfiguration configuration, CancellationToken ct) =>
        {
            var s = await db.UserSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new Models.UserSettings();
            var (provider, model) = Effective(s, BuildProviders(configuration));

            return Results.Ok(new SettingsDto(provider.Id, model));
        })
        .WithTags("Settings")
        .WithName("GetSettings")
        .WithSummary("The planner that answers this account's questions, and its model.");

        api.MapPut("/settings", async (
            AppDbContext db, IConfiguration configuration, SettingsDto body, CancellationToken ct) =>
        {
            if (body is null)
                return Problems.BadRequest("No settings supplied", "Send { \"providerId\": \"...\", \"modelName\": \"...\" }.");

            var providers = BuildProviders(configuration);
            var provider = providers.FirstOrDefault(p => p.Id == body.ProviderId);
            if (provider is null)
                return Problems.BadRequest(
                    "Unknown provider",
                    $"'{body.ProviderId}' is not a provider. Choose one of: {string.Join(", ", providers.Select(p => p.Id))}.");

            // The model picker must actually mean something: reject a model the
            // selected provider does not serve rather than storing it silently.
            if (!provider.Models.Contains(body.ModelName))
                return Problems.BadRequest(
                    "That model is not available from this provider",
                    $"{provider.Name} serves: {string.Join(", ", provider.Models)}.");

            var s = await db.UserSettings.FirstOrDefaultAsync(ct);
            if (s is null)
            {
                s = new Models.UserSettings();
                db.UserSettings.Add(s);
            }

            s.ProviderId = body.ProviderId;
            s.ModelName = body.ModelName;

            await db.SaveChangesAsync(ct);

            return Results.Ok(new SettingsDto(s.ProviderId, s.ModelName));
        })
        .WithTags("Settings")
        .WithName("UpdateSettings")
        .WithSummary("Choose the planner and model. Rejects a model the chosen provider does not serve.");

        api.MapGet("/providers", (IConfiguration configuration) => Results.Ok(BuildProviders(configuration)))
            .WithTags("Settings")
            .WithName("ListProviders")
            .WithSummary("Model providers and the models each one serves.");

        api.MapGet("/status", async (AppDbContext db, IConfiguration configuration, CancellationToken ct) =>
        {
            var settings = await db.UserSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new Models.UserSettings();
            var (selected, model) = Effective(settings, BuildProviders(configuration));

            return Results.Ok(new
            {
                status = "ok",
                datasets = await db.Datasets.CountAsync(ct),
                rows = await db.SalesRows.LongCountAsync(ct),
                gemini = GeminiPlanner.IsConfigured(configuration) ? "configured" : "no key",
                openrouter = OpenRouterPlanner.IsConfigured(configuration) ? "configured" : "no key",
                // What the assistant will actually use for this account's next
                // question, which is the only planner fact worth surfacing.
                assistant = new
                {
                    provider = selected.Name,
                    model,
                    // A provider without a key still answers — the built-in
                    // planner takes over — so this reports the model, not health.
                    ready = selected.Status == "Connected",
                },
            });
        })
        .WithTags("Health")
        .WithName("Status")
        .WithSummary("What this account has loaded, and which planner will answer its next question.");

        return api;
    }

    /// <summary>
    /// Liveness, for load balancers, uptime monitors and the development
    /// preflight. It is public, so it says the service is up and nothing about
    /// what any account holds. HEAD is answered too: uptime monitors such as
    /// UptimeRobot check with HEAD, and a GET-only route told them 405.
    /// </summary>
    public static RouteGroupBuilder MapHealthEndpoint(this RouteGroupBuilder api)
    {
        api.MapMethods("/health", [HttpMethods.Get, HttpMethods.Head], () => Results.Ok(new
            {
                status = "ok",
                utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            }))
            .AllowAnonymous()
            .WithTags("Health")
            .WithName("Health")
            .WithSummary("Liveness only. Needs no account and reveals nothing stored.");

        return api;
    }
}
