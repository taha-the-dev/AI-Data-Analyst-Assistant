using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AnalystAI.Api.Query;

/// <summary>
/// Maps a question to a <see cref="QuerySpec"/> with keyword rules.
///
/// This is deliberately not a language model. It is the seam where one goes:
/// register a different <see cref="IQuestionPlanner"/> and the rest of the
/// application is unchanged, because the engine only ever receives a spec.
///
/// <para>Explanations are templated from the computed figures, so the prose can
/// only ever state numbers the engine produced.</para>
/// </summary>
public partial class KeywordPlanner : IQuestionPlanner
{
    /// <summary>
    /// Pulls the values a category column holds out of the schema description
    /// the caller passes in.
    ///
    /// These used to be hard-coded as "North, South, East, West" and a list of
    /// product categories — invented knowledge about one particular file. A
    /// question naming a region in someone else's data went unrecognised, and a
    /// question naming "North" against a file without one produced a filter
    /// matching nothing. Reading them from the file makes the rules work on
    /// whatever was uploaded.
    /// </summary>
    private static IEnumerable<string> ValuesOf(string column, string? schema)
    {
        if (string.IsNullOrWhiteSpace(schema)) yield break;

        foreach (var line in schema.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(column + " ", StringComparison.OrdinalIgnoreCase)) continue;

            var listed = trimmed[column.Length..].Trim();
            var marker = listed.IndexOf("category", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) yield break;

            listed = listed[(marker + "category".Length)..].Trim();
            // A column with too many values to list is described by its size
            // instead, and there is nothing to match against.
            if (listed.Contains("distinct values", StringComparison.OrdinalIgnoreCase)) yield break;

            foreach (var value in listed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (value.Length > 0) yield return value;

            yield break;
        }
    }

    [GeneratedRegex(@"(?:over|above|more than|greater than|>)\s*\$?\s*([\d,]+)", RegexOptions.IgnoreCase)]
    private static partial Regex OverRegex();

    [GeneratedRegex(@"(?:under|below|less than|fewer than|<)\s*\$?\s*([\d,]+)", RegexOptions.IgnoreCase)]
    private static partial Regex UnderRegex();

    [GeneratedRegex(@"top\s+(\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex TopNRegex();

    public string Id => "keyword";

    public string Model => "rules-v1";

    public Task<PlannedSpec> PlanAsync(
        string question, IReadOnlyList<string> priorTurns, string? schema = null, CancellationToken ct = default)
        => Task.FromResult(new PlannedSpec(Plan(question, priorTurns, schema), Id, Model));

    public Task<string> ExplainAsync(string question, QueryResult result, CancellationToken ct = default)
        => Task.FromResult(Explain(question, result));

    public QuerySpec Plan(string question, IReadOnlyList<string> priorTurns, string? schema = null)
    {
        // The question being asked now decides the shape of the answer. Earlier
        // turns only fill gaps — otherwise a conversation that once mentioned
        // "trend" would turn every later question into a trend.
        var self = question.ToLowerInvariant();
        var context = string.Join(' ', priorTurns.TakeLast(4)).ToLowerInvariant();
        var q = $"{context} {self}";

        var selfBucket = BucketOf(self);
        var selfDimension = DimensionOf(self);

        string? timeBucket;
        string groupBy;

        if (selfBucket is not null)
        {
            timeBucket = selfBucket;
            groupBy = "date";
        }
        else if (selfDimension is not null)
        {
            timeBucket = null;
            groupBy = selfDimension;
        }
        else
        {
            // Nothing in the question names a dimension or a period, so this is
            // a bare follow-up ("what about the North?") — inherit.
            var inheritedBucket = BucketOf(context);
            timeBucket = inheritedBucket;
            groupBy = inheritedBucket is not null ? "date" : DimensionOf(context) ?? "category";
        }

        var intent = timeBucket is not null ? "trend" : "aggregate";
        var isFollowUp = selfBucket is null && selfDimension is null;

        // Measure words are read from the question first, then from context.
        var measureText = Contains(self, "how many", "count", "average", "avg", "mean",
            "typical", "quantity", "units", "qty", "unit price", "revenue", "sales") ? self : q;

        var aggregate = Contains(measureText, "how many", "count", "number of orders", "order count") ? "count"
            : Contains(measureText, "average", "avg", "mean", "typical") ? "avg"
            : Contains(measureText, "largest single", "biggest single") ? "max"
            : "sum";

        var metric = Contains(measureText, "quantity", "units", "qty") ? "qty"
            : Contains(measureText, "unit price", "price per") ? "price"
            : "revenue";

        // Filters come from the question. A bare follow-up also inherits the
        // filters already in play, so "and by month?" keeps the region.
        var filterText = isFollowUp ? q : self;
        var filters = new List<QueryFilter>();

        foreach (var region in ValuesOf("region", schema))
            if (filterText.Contains(region.ToLowerInvariant()))
                filters.Add(new QueryFilter("region", "=", region));

        foreach (var category in ValuesOf("category", schema))
            if (filterText.Contains(category.ToLowerInvariant()) && groupBy != "category")
                filters.Add(new QueryFilter("category", "=", category));

        var over = OverRegex().Match(filterText);
        if (over.Success && double.TryParse(over.Groups[1].Value.Replace(",", ""),
                NumberStyles.Any, CultureInfo.InvariantCulture, out var overValue))
            filters.Add(new QueryFilter("revenue", ">", overValue.ToString(CultureInfo.InvariantCulture)));

        var under = UnderRegex().Match(filterText);
        if (under.Success && double.TryParse(under.Groups[1].Value.Replace(",", ""),
                NumberStyles.Any, CultureInfo.InvariantCulture, out var underValue))
            filters.Add(new QueryFilter("revenue", "<", underValue.ToString(CultureInfo.InvariantCulture)));

        var limit = 10;
        var topN = TopNRegex().Match(self);
        if (topN.Success && int.TryParse(topN.Groups[1].Value, out var n)) limit = Math.Clamp(n, 1, 100);
        if (intent == "trend") limit = 24;

        var chart = intent == "trend" ? "line" : groupBy == "region" ? "donut" : "bar";

        return new QuerySpec
        {
            Intent = intent,
            GroupBy = groupBy,
            Metric = aggregate == "count" ? null : metric,
            Aggregate = aggregate,
            Filters = filters,
            Sort = intent == "trend" ? "label asc" : "value desc",
            Limit = limit,
            TimeBucket = timeBucket,
            Chart = chart,
            Title = BuildTitle(aggregate, metric, groupBy, timeBucket, filters),
        };
    }

    public string Explain(string question, QueryResult result)
    {
        var spec = result.Spec;
        var figures = result.Figures;

        if (figures.Count == 0)
        {
            return "No rows matched that question. " +
                   (spec.Filters.Count > 0
                       ? "The filters applied are shown below — removing one will widen the result."
                       : "The dataset may not carry the column this question needs.");
        }

        var money = result.Unit == "currency";
        var sb = new StringBuilder();
        var noun = spec.Aggregate == "count" ? "orders" : spec.Metric == "qty" ? "units" : "revenue";
        var dimension = spec.TimeBucket is not null ? spec.TimeBucket : spec.GroupBy;

        if (spec.Intent == "trend")
        {
            var first = figures[0];
            var last = figures[^1];
            var change = first.Value == 0 ? 0 : (last.Value - first.Value) / first.Value * 100;
            var peak = figures.MaxBy(f => f.Value)!;

            sb.Append(CultureInfo.InvariantCulture,
                $"Across {figures.Count} {Plural(dimension, figures.Count)}, {noun} moved from {Fmt(first.Value, money)} in {first.Label} to {Fmt(last.Value, money)} in {last.Label}");
            sb.Append(CultureInfo.InvariantCulture, $" — a change of {change:0.0}%. ");
            sb.Append(CultureInfo.InvariantCulture,
                $"The peak was {Fmt(peak.Value, money)} in {peak.Label}. ");
        }
        else
        {
            var top = figures[0];
            // A share of the total only means something when the parts add up.
            // Summing averages, minimums or maximums does not, so no percentage.
            var totalIsMeaningful = spec.Aggregate is "sum" or "count";

            if (figures.Count == 1)
            {
                sb.Append(CultureInfo.InvariantCulture,
                    $"{top.Label} accounts for {Fmt(top.Value, money)} of {noun}, the only {Singular(dimension)} matching this question. ");
            }
            else
            {
                sb.Append(CultureInfo.InvariantCulture,
                    $"{top.Label} leads on {noun} with {Fmt(top.Value, money)}");

                if (totalIsMeaningful)
                {
                    var share = result.Total == 0 ? 0 : top.Value / result.Total * 100;
                    sb.Append(CultureInfo.InvariantCulture,
                        $", {share:0.0}% of the {Fmt(result.Total, money)} across the {figures.Count} {Plural(dimension, figures.Count)} returned. ");
                }
                else
                {
                    sb.Append(CultureInfo.InvariantCulture,
                        $", across the {figures.Count} {Plural(dimension, figures.Count)} returned. ");
                }

                sb.Append(CultureInfo.InvariantCulture,
                    $"{figures[1].Label} follows at {Fmt(figures[1].Value, money)}");
                if (figures.Count > 2)
                    sb.Append(CultureInfo.InvariantCulture,
                        $", then {figures[2].Label} at {Fmt(figures[2].Value, money)}");
                sb.Append(". ");
            }
        }

        sb.Append(CultureInfo.InvariantCulture,
            $"Computed from {result.RowsMatched:N0} of {result.RowsScanned:N0} rows");
        sb.Append(spec.Filters.Count > 0 ? " after filtering." : ".");

        return sb.ToString();
    }

    /// <summary>"category" → "categories", not "categorys".</summary>
    private static string Plural(string? word, int count)
    {
        var w = Singular(word);
        if (count == 1) return w;
        if (w.EndsWith('y') && w.Length > 1 && !"aeiou".Contains(w[^2])) return w[..^1] + "ies";
        if (w.EndsWith('s') || w.EndsWith("ch") || w.EndsWith("sh")) return w + "es";
        return w + "s";
    }

    private static string Singular(string? word) => string.IsNullOrWhiteSpace(word) ? "group" : word;

    private static string Fmt(double v, bool money) =>
        money ? "$" + v.ToString("N0", CultureInfo.InvariantCulture)
              : v.ToString("N0", CultureInfo.InvariantCulture);

    private static bool Contains(string haystack, params string[] needles) =>
        needles.Any(n => haystack.Contains(n, StringComparison.OrdinalIgnoreCase));

    /// <summary>The time bucket a piece of text asks for, if any.</summary>
    private static string? BucketOf(string text) =>
        Contains(text, "by month", "monthly", "each month", "per month") ? "month"
        : Contains(text, "by week", "weekly", "per week") ? "week"
        : Contains(text, "by quarter", "quarterly", "per quarter") ? "quarter"
        : Contains(text, "by year", "yearly", "annually", "per year") ? "year"
        : Contains(text, "trend", "over time") ? "month"
        : null;

    /// <summary>The dimension a piece of text asks to group by, if any.</summary>
    private static string? DimensionOf(string text) =>
        Contains(text, "region") ? "region"
        : Contains(text, "customer", "client", "account") ? "customer"
        : Contains(text, "product", "sku", "item") ? "product"
        : Contains(text, "status") ? "status"
        : Contains(text, "category", "categories") ? "category"
        : null;

    private static string BuildTitle(string aggregate, string metric, string groupBy, string? bucket, List<QueryFilter> filters)
    {
        var measure = aggregate switch
        {
            "count" => "Orders",
            "avg" => $"Average {metric}",
            "min" => $"Lowest {metric}",
            "max" => $"Highest {metric}",
            _ => metric == "revenue" ? "Revenue" : $"Total {metric}",
        };

        var by = bucket is not null ? $"by {bucket}" : $"by {groupBy}";
        var where = filters.Count > 0
            ? " (" + string.Join(", ", filters.Select(f => $"{f.Column} {f.Op} {f.Value}")) + ")"
            : "";

        return $"{measure} {by}{where}";
    }
}
