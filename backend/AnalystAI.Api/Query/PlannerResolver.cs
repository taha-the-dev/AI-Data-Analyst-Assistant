using AnalystAI.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AnalystAI.Api.Query;

/// <summary>
/// Picks the planner for a request from the provider saved in settings, so the
/// choice made on the Settings screen actually changes what answers questions.
///
/// Selecting a model provider whose key is not configured falls back to the
/// keyword planner rather than failing: the app keeps working, the wording is
/// just plainer. The same is true at every later step — see the planners
/// themselves, which swallow their own failures the same way.
/// </summary>
public class PlannerResolver(
    AppDbContext db,
    KeywordPlanner keyword,
    GeminiPlanner gemini,
    OpenRouterPlanner openRouter,
    IConfiguration configuration,
    ILogger<PlannerResolver> logger) : IPlannerResolver
{
    public async Task<IQuestionPlanner> ResolveAsync(CancellationToken ct = default)
    {
        var providerId = await db.UserSettings
            .AsNoTracking()
            .Select(s => s.ProviderId)
            .FirstOrDefaultAsync(ct) ?? "keyword";

        switch (providerId)
        {
            case "gemini" when GeminiPlanner.IsConfigured(configuration):
                return gemini;

            case "gemini":
                logger.LogInformation(
                    "Gemini is selected but no API key is set. Set GEMINI_API_KEY or Gemini:ApiKey to enable it.");
                return keyword;

            case "openrouter" when OpenRouterPlanner.IsConfigured(configuration):
                return openRouter;

            case "openrouter":
                logger.LogInformation(
                    "OpenRouter is selected but no API key is set. Set OPENROUTER_API_KEY or OpenRouter:ApiKey to enable it.");
                return keyword;

            default:
                return keyword;
        }
    }
}
