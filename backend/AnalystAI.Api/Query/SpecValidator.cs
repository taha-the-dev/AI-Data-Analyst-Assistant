using System.Text.Json.Nodes;

namespace AnalystAI.Api.Query;

/// <summary>
/// Nothing a model returns is trusted. Unknown columns, operators and
/// aggregates are dropped, numbers are clamped, and a plan that cannot be
/// repaired is rejected so the caller can fall back rather than handing the
/// engine something it should never run.
///
/// Shared by every model-backed planner: a new provider gets the same guard
/// rails as the first one, automatically.
/// </summary>
public static class SpecValidator
{
    private static readonly string[] Intents = ["aggregate", "trend", "distribution", "list"];
    private static readonly string[] Buckets = ["day", "week", "month", "quarter", "year"];
    private static readonly string[] Charts = ["bar", "line", "donut", "table"];
    private static readonly string[] Ops = [">", ">=", "<", "<=", "=", "!="];

    /// <summary>Returns null when the plan is unusable — the caller then falls back.</summary>
    public static QuerySpec? TryBuild(JsonObject raw)
    {
        string? Str(string key)
        {
            var v = raw[key]?.ToString();
            return string.IsNullOrWhiteSpace(v) || v == "null" ? null : v.Trim();
        }

        var groupBy = Str("groupBy");
        if (groupBy is null || !QueryEngine.IsColumn(groupBy)) return null;

        var aggregate = Str("aggregate")?.ToLowerInvariant();
        if (aggregate is null || !QueryEngine.IsAggregate(aggregate)) aggregate = "sum";

        var metric = Str("metric")?.ToLowerInvariant();
        if (aggregate == "count") metric = null;
        else if (metric is null || !QueryEngine.IsColumn(metric)) metric = "revenue";

        var bucket = Str("timeBucket")?.ToLowerInvariant();
        if (bucket is not null && !Buckets.Contains(bucket)) bucket = null;

        var intent = Str("intent")?.ToLowerInvariant();
        if (intent is null || !Intents.Contains(intent)) intent = bucket is not null ? "trend" : "aggregate";
        if (bucket is not null) intent = "trend";

        var chart = Str("chart")?.ToLowerInvariant();
        if (chart is null || !Charts.Contains(chart))
            chart = intent == "trend" ? "line" : groupBy == "region" ? "donut" : "bar";

        var sort = Str("sort");
        if (sort is not "value desc" and not "value asc" and not "label asc" and not "label desc")
            sort = intent == "trend" ? "label asc" : "value desc";

        var limit = 10;
        if (raw["limit"] is { } limitNode && int.TryParse(limitNode.ToString(), out var parsed))
            limit = Math.Clamp(parsed, 1, 100);
        if (intent == "trend") limit = Math.Max(limit, 24);

        var filters = new List<QueryFilter>();
        if (raw["filters"] is JsonArray array)
        {
            foreach (var node in array)
            {
                var column = node?["column"]?.ToString();
                var op = node?["op"]?.ToString();
                var value = node?["value"]?.ToString();

                if (string.IsNullOrWhiteSpace(column) || string.IsNullOrWhiteSpace(value)) continue;
                if (!QueryEngine.IsColumn(column)) continue;
                if (string.IsNullOrWhiteSpace(op) || !Ops.Contains(op)) op = "=";

                filters.Add(new QueryFilter(column, op, value));
            }
        }

        return new QuerySpec
        {
            Intent = intent,
            GroupBy = bucket is not null ? "date" : groupBy,
            Metric = metric,
            Aggregate = aggregate,
            Filters = filters,
            Sort = sort,
            Limit = limit,
            TimeBucket = bucket,
            Chart = chart,
            Title = Str("title") ?? $"{aggregate} of {metric ?? "orders"} by {bucket ?? groupBy}",
        };
    }
}
