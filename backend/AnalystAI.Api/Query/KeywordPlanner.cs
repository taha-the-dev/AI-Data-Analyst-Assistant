using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AnalystAI.Api.Analysis;

namespace AnalystAI.Api.Query;

/// <summary>
/// Maps a question to a <see cref="QuerySpec"/> with keyword rules.
///
/// This is deliberately not a language model — it is the built-in adapter at
/// the planner seam, and the one every hosted planner falls back to. It knows no
/// vocabulary of its own: the columns it can group by, the measures it can
/// compute and the values it can filter on all come from the file's schema, so
/// "average marks by subject" works on a mark sheet and "revenue by region" on
/// an order export.
///
/// <para>Explanations are templated from the computed figures, so the prose can
/// only ever state numbers the engine produced.</para>
/// </summary>
public partial class KeywordPlanner : IQuestionPlanner
{
    [GeneratedRegex(@"(?:over|above|more than|greater than|at least|>)\s*\$?\s*([\d,]+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex OverRegex();

    [GeneratedRegex(@"(?:under|below|less than|fewer than|at most|<)\s*\$?\s*([\d,]+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex UnderRegex();

    [GeneratedRegex(@"(?:top|best|highest)\s+(\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex TopNRegex();

    [GeneratedRegex(@"(?:bottom|worst|lowest)\s+(\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex BottomNRegex();

    public string Id => "keyword";

    public string Model => "rules-v1";

    public Task<PlannedSpec> PlanAsync(
        string question, IReadOnlyList<string> priorTurns, DatasetSchema schema, CancellationToken ct = default)
        => Task.FromResult(new PlannedSpec(Plan(question, priorTurns, schema), Id, Model));

    public Task<string> ExplainAsync(string question, QueryResult result, DatasetSchema schema, CancellationToken ct = default)
        => Task.FromResult(Explain(question, result));

    public QuerySpec Plan(string question, IReadOnlyList<string> priorTurns, DatasetSchema schema)
    {
        // The question being asked now decides the shape of the answer. Earlier
        // turns only fill gaps — otherwise a conversation that once mentioned
        // "trend" would turn every later question into a trend.
        var self = question.ToLowerInvariant();
        var context = string.Join(' ', priorTurns.TakeLast(4)).ToLowerInvariant();
        var q = $"{context} {self}";

        var selfBucket = schema.DateColumn is null ? null : BucketOf(self);
        var selfDimension = DimensionOf(self, schema);
        var isFollowUp = selfBucket is null && selfDimension is null;

        string? bucket;
        string? groupBy;
        if (selfBucket is not null)
        {
            bucket = selfBucket;
            groupBy = schema.DateColumn;
        }
        else if (selfDimension is not null)
        {
            bucket = null;
            groupBy = selfDimension.Name;
        }
        else
        {
            // Nothing in the question names a grouping or a period, so this is a
            // bare follow-up ("what about Physics?") — inherit.
            bucket = schema.DateColumn is null ? null : BucketOf(context);
            groupBy = bucket is not null ? schema.DateColumn : DimensionOf(context, schema)?.Name ?? schema.DefaultDimension;
        }

        // Measure words are read from the question first, then from context.
        var metric = MeasureOf(self, schema, groupBy) ?? MeasureOf(context, schema, groupBy);
        var measureText = metric is not null || HasAggregateWord(self) ? self : q;

        var aggregate = Contains(measureText, "how many", "count", "number of") ? "count"
            : Contains(measureText, "median") ? "median"
            : Contains(measureText, "average", "avg", "mean", "typical") ? "avg"
            : Contains(measureText, "highest", "maximum", "max ", "largest single", "best score", "top score") && !TopNRegex().IsMatch(self) ? "max"
            : Contains(measureText, "lowest", "minimum", "min ", "smallest") && !BottomNRegex().IsMatch(self) ? "min"
            : Contains(measureText, "total", "sum", "overall") ? "sum"
            : "";

        metric ??= schema.DefaultMetric;
        var measure = schema.Find(metric);
        if (aggregate == "") aggregate = measure is null ? "count" : measure.Additive ? "sum" : "avg";

        // Filters come from the question. A bare follow-up also inherits the
        // filters already in play, so "and by month?" keeps the subject.
        var filterText = isFollowUp ? q : self;
        var filters = new List<QueryFilter>();

        foreach (var column in schema.Columns.Where(c => c.Kind == "category" && c.Name != groupBy))
            foreach (var value in column.Values)
                if (Mentions(filterText, value, column.Name))
                    filters.Add(new QueryFilter(column.Name, "=", value));

        if (measure is not null && measure.Expression is null)
        {
            var over = OverRegex().Match(filterText);
            if (over.Success && TryNumber(over.Groups[1].Value, out var overValue))
                filters.Add(new QueryFilter(measure.Name, ">", overValue));

            var under = UnderRegex().Match(filterText);
            if (under.Success && TryNumber(under.Groups[1].Value, out var underValue))
                filters.Add(new QueryFilter(measure.Name, "<", underValue));
        }

        var limit = 10;
        var sort = bucket is not null ? "label asc" : "value desc";
        var topN = TopNRegex().Match(self);
        var bottomN = BottomNRegex().Match(self);
        if (topN.Success && int.TryParse(topN.Groups[1].Value, out var n)) limit = Math.Clamp(n, 1, 100);
        if (bottomN.Success && int.TryParse(bottomN.Groups[1].Value, out var m))
        {
            limit = Math.Clamp(m, 1, 100);
            sort = "value asc";
        }
        else if (bucket is null && Contains(self, "weakest", "worst", "lowest", "least", "bottom")) sort = "value asc";

        return new QuerySpec
        {
            Intent = bucket is not null ? "trend" : "aggregate",
            GroupBy = groupBy,
            Metric = aggregate == "count" ? null : metric,
            Aggregate = aggregate,
            Filters = filters,
            Sort = sort,
            Limit = limit,
            // "auto" lets the engine choose the grain that suits the file's span.
            TimeBucket = bucket == "auto" ? null : bucket,
            Chart = bucket is not null ? "line" : "bar",
        };
    }

    public string Explain(string question, QueryResult result)
    {
        var spec = result.Spec;
        var figures = result.Figures;
        var unit = result.Unit;

        if (figures.Count == 0)
        {
            return "No rows matched that question. " +
                   (spec.Filters.Count > 0
                       ? "Removing a filter will widen the result: " + string.Join(", ", spec.Filters.Select(f => $"{f.Column} {f.Op} {f.Value}")) + "."
                       : "The file may not carry the column this question needs.");
        }

        var sb = new StringBuilder();
        var noun = result.MetricLabel is null
            ? "row count"
            : spec.Aggregate switch
            {
                "avg" => $"average {Fmt.Lower(result.MetricLabel)}",
                "median" => $"median {Fmt.Lower(result.MetricLabel)}",
                "min" => $"lowest {Fmt.Lower(result.MetricLabel)}",
                "max" => $"highest {Fmt.Lower(result.MetricLabel)}",
                _ => $"total {Fmt.Lower(result.MetricLabel)}",
            };
        var dimension = spec.TimeBucket ?? Fmt.Lower(spec.GroupBy ?? "group");
        string V(double v) => unit.Prefix + v.ToString("N" + unit.Decimals, CultureInfo.InvariantCulture) + unit.Suffix;

        if (spec.Intent == "trend")
        {
            var first = figures[0];
            var last = figures[^1];
            var peak = figures.MaxBy(f => f.Value)!;

            sb.Append(CultureInfo.InvariantCulture,
                $"Across {figures.Count} {Plural(dimension, figures.Count)}, {noun} moved from {V(first.Value)} in {Fmt.Period(first.Label)} to {V(last.Value)} in {Fmt.Period(last.Label)}");
            if (first.Value != 0)
                sb.Append(CultureInfo.InvariantCulture, $" — a change of {(last.Value - first.Value) / Math.Abs(first.Value) * 100:0.0}%");
            sb.Append(CultureInfo.InvariantCulture, $". The peak was {V(peak.Value)} in {Fmt.Period(peak.Label)}. ");
        }
        else
        {
            var top = figures[0];
            var ascending = spec.Sort == "value asc";
            // A share of the total only means something when the parts add up.
            var totalIsMeaningful = spec.Aggregate is "sum" or "count" && result.Total > 0;

            if (figures.Count == 1)
            {
                sb.Append(CultureInfo.InvariantCulture,
                    $"{top.Label} has {noun} of {V(top.Value)}, the only {dimension} matching this question. ");
            }
            else
            {
                sb.Append(CultureInfo.InvariantCulture,
                    $"{top.Label} {(ascending ? "is lowest" : "leads")} on {noun} with {V(top.Value)}");
                sb.Append(totalIsMeaningful
                    ? string.Create(CultureInfo.InvariantCulture, $", {top.Value / result.Total * 100:0.0}% of the {V(result.Total)} across the {figures.Count} {Plural(dimension, figures.Count)} shown. ")
                    : string.Create(CultureInfo.InvariantCulture, $", across the {figures.Count} {Plural(dimension, figures.Count)} shown. "));

                sb.Append(CultureInfo.InvariantCulture, $"{figures[1].Label} follows at {V(figures[1].Value)}");
                if (figures.Count > 2)
                    sb.Append(CultureInfo.InvariantCulture, $", then {figures[2].Label} at {V(figures[2].Value)}");
                sb.Append(". ");
            }
        }

        sb.Append(CultureInfo.InvariantCulture,
            $"Computed from {result.RowsMatched:N0} of {result.RowsScanned:N0} rows");
        sb.Append(spec.Filters.Count > 0 ? " after filtering." : ".");

        return sb.ToString();
    }

    // ── Reading the question against the schema ─────────────────────────────

    /// <summary>
    /// The grouping a piece of text asks for: a groupable column named after
    /// "by", "per", "each" or "across", or failing that, named anywhere.
    /// </summary>
    private static SchemaColumn? DimensionOf(string text, DatasetSchema schema)
    {
        var candidates = schema.Groupings.Where(c => c.Kind != "date").ToList();
        foreach (var lead in new[] { "by ", "per ", "each ", "across ", "for each ", "in each " })
            foreach (var column in candidates)
                if (Phrases(column.Name).Any(p => text.Contains(lead + p, StringComparison.Ordinal)))
                    return column;

        return candidates.FirstOrDefault(c => !c.CanMeasure && Phrases(c.Name).Any(p => ContainsWord(text, p)));
    }

    /// <summary>A measure the text names, other than the grouping column.</summary>
    private static string? MeasureOf(string text, DatasetSchema schema, string? groupBy) =>
        schema.Measures
            .Where(c => c.Name != groupBy)
            .OrderByDescending(c => c.Name.Length)
            .FirstOrDefault(c => Phrases(c.Name).Any(p => ContainsWord(text, p)))?.Name;

    /// <summary>"YearsAtCompany" → "years at company", "yearsatcompany"; plus simple plurals.</summary>
    private static IEnumerable<string> Phrases(string header)
    {
        var words = Frame.Tokenise(header);
        if (words.Length == 0) yield break;
        var spaced = string.Join(' ', words);
        yield return spaced;
        if (words.Length > 1) yield return string.Concat(words);
        yield return Fmt.Plural(spaced);
        // "student_name" is asked about as "student"; "marks_obtained" as "marks".
        if (words.Length > 1 && words[^1] is "name" or "id" or "no" or "obtained" or "score" or "value" or "amount")
            yield return words[0];
    }

    /// <summary>
    /// A category value the text mentions as a word. Short values ("A", "B")
    /// only count next to their column's name — "grade A" — or every "a" in a
    /// question would become a filter.
    /// </summary>
    private static bool Mentions(string text, string value, string column)
    {
        var v = value.ToLowerInvariant();
        if (v.Length >= 3) return ContainsWord(text, v);
        return Phrases(column).Any(p => ContainsWord(text, $"{p} {v}"));
    }

    private static bool ContainsWord(string text, string phrase) =>
        Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(phrase)}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase);

    private static bool HasAggregateWord(string text) =>
        Contains(text, "how many", "count", "average", "avg", "mean", "median", "total", "sum", "highest", "lowest", "maximum", "minimum");

    private static bool TryNumber(string raw, out string value)
    {
        value = "";
        if (!double.TryParse(raw.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var n)) return false;
        value = n.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>"category" → "categories", not "categorys".</summary>
    private static string Plural(string word, int count) => count == 1 ? word : Fmt.Plural(word);

    private static bool Contains(string haystack, params string[] needles) =>
        needles.Any(n => haystack.Contains(n, StringComparison.OrdinalIgnoreCase));

    /// <summary>The time bucket a piece of text asks for, if any.</summary>
    private static string? BucketOf(string text) =>
        Contains(text, "by day", "daily", "per day", "each day") ? "day"
        : Contains(text, "by month", "monthly", "each month", "per month") ? "month"
        : Contains(text, "by week", "weekly", "per week") ? "week"
        : Contains(text, "by quarter", "quarterly", "per quarter") ? "quarter"
        : Contains(text, "by year", "yearly", "annually", "per year") ? "year"
        : Contains(text, "trend", "over time") ? "auto"
        : null;
}
