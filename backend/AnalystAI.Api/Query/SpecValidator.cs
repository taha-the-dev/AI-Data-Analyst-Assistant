using System.Text.Json.Nodes;
using AnalystAI.Api.Analysis;

namespace AnalystAI.Api.Query;

/// <summary>
/// The one gate every plan passes before it runs, whichever planner wrote it.
///
/// Nothing a model returns is trusted, and the keyword rules are held to the
/// same standard. Columns must exist in the file and suit their slot, unknown
/// operators and aggregates are replaced, numbers are clamped, and a plan that
/// cannot be repaired is rejected so the caller can fall back rather than
/// handing the engine something it should never run.
/// </summary>
public static class SpecValidator
{
    public static readonly string[] Aggregates = ["sum", "avg", "count", "min", "max", "median"];
    private static readonly string[] Buckets = ["day", "week", "month", "quarter", "year"];
    private static readonly string[] Charts = ["bar", "line", "donut", "table"];
    private static readonly string[] Sorts = ["value desc", "value asc", "label asc", "label desc"];

    /// <summary>Reads a model's JSON reply into a spec, then normalises it. Null when unusable.</summary>
    public static QuerySpec? TryBuild(JsonObject raw, DatasetSchema schema)
    {
        string? Str(string key)
        {
            var v = raw[key]?.ToString();
            return string.IsNullOrWhiteSpace(v) || v == "null" ? null : v.Trim();
        }

        var filters = new List<QueryFilter>();
        if (raw["filters"] is JsonArray array)
            foreach (var node in array)
                filters.Add(new QueryFilter(
                    node?["column"]?.ToString() ?? "", node?["op"]?.ToString() ?? "=", node?["value"]?.ToString() ?? ""));

        var limit = 10;
        if (raw["limit"] is { } limitNode && int.TryParse(limitNode.ToString(), out var parsed)) limit = parsed;

        return Normalize(new QuerySpec
        {
            Intent = Str("intent") ?? "aggregate",
            GroupBy = Str("groupBy"),
            Metric = Str("metric"),
            Aggregate = Str("aggregate") ?? "",
            Filters = filters,
            Sort = Str("sort") ?? "",
            Limit = limit,
            TimeBucket = Str("timeBucket"),
            Chart = Str("chart") ?? "",
            Title = Str("title") ?? "",
        }, schema, strict: true);
    }

    /// <summary>
    /// Repairs a spec against the file's schema. With <paramref name="strict"/>
    /// a grouping column that does not exist rejects the plan (a model named
    /// something the file lacks); without it the file's default grouping stands in.
    /// </summary>
    public static QuerySpec? Normalize(QuerySpec spec, DatasetSchema schema, bool strict = false)
    {
        var bucket = spec.TimeBucket?.ToLowerInvariant();
        if (bucket is not null && !Buckets.Contains(bucket)) bucket = null;

        // Grouping: a groupable column; a period means the date column.
        var group = schema.Find(spec.GroupBy);
        if (group is not null && !group.CanGroupBy) group = null;
        if (group is null && spec.GroupBy is not null && strict && bucket is null) return null;
        if ((group is null || bucket is not null) && bucket is not null && schema.DateColumn is not null)
            group = schema.Find(schema.DateColumn);
        group ??= schema.Find(schema.DefaultDimension) ?? schema.Groupings.FirstOrDefault();
        if (group is null) return null;

        var isDate = group.Kind == "date";
        if (!isDate) bucket = null;

        // Measure: a numeric column; otherwise the file's own, otherwise a count.
        var aggregate = spec.Aggregate?.ToLowerInvariant() ?? "";
        var metric = schema.Find(spec.Metric);
        if (metric is not null && !metric.CanMeasure) metric = null;
        if (aggregate != "count") metric ??= schema.Find(schema.DefaultMetric);
        if (!Aggregates.Contains(aggregate)) aggregate = metric is null ? "count" : metric.Additive ? "sum" : "avg";
        if (metric is null) aggregate = "count";
        if (aggregate == "count") metric = null;

        var filters = new List<QueryFilter>();
        foreach (var f in spec.Filters)
        {
            var column = schema.Find(f.Column);
            if (column is null || column.Expression is not null || string.IsNullOrWhiteSpace(f.Value)) continue;
            var op = f.Op?.Trim() switch
            {
                ">" or ">=" or "<" or "<=" or "=" or "!=" or "contains" => f.Op.Trim(),
                _ => "=",
            };
            filters.Add(new QueryFilter(column.Name, op, f.Value.Trim()));
        }

        var intent = isDate ? "trend" : "aggregate";
        var sort = Sorts.Contains(spec.Sort) ? spec.Sort : isDate ? "label asc" : "value desc";
        var limit = spec.Limit <= 0 ? 10 : Math.Clamp(spec.Limit, 1, 100);
        if (isDate) limit = Math.Max(limit, 36);

        var chart = spec.Chart?.ToLowerInvariant() ?? "";
        if (!Charts.Contains(chart)) chart = isDate ? "line" : group.Values.Count is > 1 and <= 4 && aggregate is "sum" or "count" ? "donut" : "bar";
        if (isDate && chart == "donut") chart = "line";

        return new QuerySpec
        {
            Intent = intent,
            GroupBy = group.Name,
            Metric = metric?.Name,
            Aggregate = aggregate,
            Filters = filters,
            Sort = sort,
            Limit = limit,
            TimeBucket = bucket,
            Chart = chart,
            Title = string.IsNullOrWhiteSpace(spec.Title) ? TitleFor(aggregate, metric?.Name, group.Name, bucket, filters) : spec.Title.Trim(),
        };
    }

    public static string TitleFor(string aggregate, string? metric, string groupBy, string? bucket, IReadOnlyList<QueryFilter> filters)
    {
        var measure = metric is null ? "Rows" : aggregate switch
        {
            "avg" => $"Average {Fmt.Lower(metric)}",
            "min" => $"Lowest {Fmt.Lower(metric)}",
            "max" => $"Highest {Fmt.Lower(metric)}",
            "median" => $"Median {Fmt.Lower(metric)}",
            _ => $"Total {Fmt.Lower(metric)}",
        };
        var by = $"by {bucket ?? Fmt.Lower(groupBy)}";
        var where = filters.Count > 0
            ? " (" + string.Join(", ", filters.Select(f => $"{f.Column} {f.Op} {f.Value}")) + ")"
            : "";
        return $"{measure} {by}{where}";
    }
}
