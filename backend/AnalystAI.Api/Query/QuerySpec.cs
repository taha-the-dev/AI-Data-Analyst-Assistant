using System.Text.Json.Serialization;

namespace AnalystAI.Api.Query;

/// <summary>
/// The contract the whole application runs on.
///
/// A planner decides *what* to compute and returns one of these. It never
/// computes anything itself — <see cref="QueryEngine"/> runs the spec against
/// the real rows, so every figure a caller sees came from the data.
/// </summary>
public record QuerySpec
{
    /// <summary>aggregate | trend | distribution | list</summary>
    public string Intent { get; init; } = "aggregate";
    public string? GroupBy { get; init; }
    public string? Metric { get; init; }
    /// <summary>sum | avg | count | min | max</summary>
    public string Aggregate { get; init; } = "sum";
    public List<QueryFilter> Filters { get; init; } = [];
    /// <summary>"value desc" | "value asc" | "label asc" | "label desc"</summary>
    public string Sort { get; init; } = "value desc";
    public int Limit { get; init; } = 10;
    /// <summary>day | week | month | quarter | year</summary>
    public string? TimeBucket { get; init; }
    /// <summary>bar | line | donut | table</summary>
    public string Chart { get; init; } = "bar";
    public string Title { get; init; } = "";
}

public record QueryFilter(string Column, string Op, string Value);

/// <summary>A single computed figure. The label/value pairs a chart renders.</summary>
public record Figure(string Label, double Value);

/// <summary>
/// What the engine produced, plus the working: which spec ran, how many rows it
/// touched and how long it took.
/// </summary>
public record QueryResult
{
    public required QuerySpec Spec { get; init; }
    public required List<Figure> Figures { get; init; }
    public long RowsScanned { get; init; }
    public long RowsMatched { get; init; }
    public int DurationMs { get; init; }
    public double Total { get; init; }
    public string Unit { get; init; } = "currency";
}

/// <summary>
/// Turns a natural-language question into a <see cref="QuerySpec"/>.
/// Implemented here by keyword rules; swap in an LLM-backed planner by
/// registering a different implementation.
/// </summary>
public interface IQuestionPlanner
{
    /// <summary>Identifies which planner answered, so the UI can say so.</summary>
    string Id { get; }

    /// <summary>The model this planner plans with, shown beside <see cref="Id"/>.</summary>
    string Model { get; }

    /// <param name="schema">
    /// The columns of the file in context and the values its category columns
    /// hold, or null when nothing described it. A planner that ignores the
    /// question's wording can still be right; one that ignores the data cannot.
    /// </param>
    /// <returns>
    /// The spec and the planner that actually produced it. A hosted planner
    /// falls back to keyword rules on a missing key, a rate limit or an
    /// unusable reply, so the planner asked is not always the planner that
    /// answered — and the screen must say which one did.
    /// </returns>
    Task<PlannedSpec> PlanAsync(
        string question, IReadOnlyList<string> priorTurns, string? schema, CancellationToken ct = default);

    /// <summary>
    /// Writes the answer. Implementations are given the computed figures and
    /// must not produce any number that is not among them.
    /// </summary>
    Task<string> ExplainAsync(string question, QueryResult result, CancellationToken ct = default);
}

/// <summary>
/// A spec with the planner that produced it. Attribution is recorded where the
/// plan is made rather than read back from settings: settings say which planner
/// was asked, and that is a different question from which one answered.
/// </summary>
public record PlannedSpec(QuerySpec Spec, string Planner, string Model)
{
    /// <summary>The one string the UI shows, e.g. "keyword/rules-v1".</summary>
    public string Attribution => string.IsNullOrWhiteSpace(Model) ? Planner : $"{Planner}/{Model}";
}

/// <summary>Chooses the planner for a request from the saved provider setting.</summary>
public interface IPlannerResolver
{
    Task<IQuestionPlanner> ResolveAsync(CancellationToken ct = default);
}

[JsonSerializable(typeof(QuerySpec))]
[JsonSerializable(typeof(QueryResult))]
internal partial class QueryJsonContext : JsonSerializerContext;
