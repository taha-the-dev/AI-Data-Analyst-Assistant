using System.Globalization;
using AnalystAI.Api.Analysis;

namespace AnalystAI.Api.Query;

/// <summary>
/// The two prompts every model-backed planner sends: plan the query, then
/// describe the figures the engine computed. They live here rather than inside
/// one provider so a second provider cannot quietly drift into asking for
/// something different — the wording is the contract, and both sides of it are
/// validated afterwards regardless.
/// </summary>
public static class Prompts
{
    /// <summary>Stage one: read the question, choose what to compute. No arithmetic.</summary>
    public static string Plan(string question, IReadOnlyList<string> priorTurns, DatasetSchema schema)
    {
        var history = priorTurns.Count == 0
            ? "(none)"
            : string.Join("\n", priorTurns.TakeLast(6).Select(t => "- " + t));

        return $"""
            You plan queries over an uploaded table of {schema.Rows:N0} rows, recognised as
            {schema.DomainLabel.ToLowerInvariant()} data. You never calculate anything.

            Its columns, their kinds, and the values each category column actually holds
            (a role in brackets is what the column was recognised as):
            {schema.Describe()}

            Earlier questions in this conversation:
            {history}

            The question now: "{question}"

            Rules for the plan you return:
            - groupBy is a column name above whose kind is category, date, or a number with few values.
              With no better choice use "{schema.DefaultDimension}".
            - Use timeBucket (day/week/month/quarter/year) with groupBy "{schema.DateColumn ?? "(no date column)"}"
              only when the question asks about change over time. Set intent to "trend" then,
              otherwise "aggregate".
            - metric is a column of kind number (or a derived one). With no better choice use
              "{schema.DefaultMetric}". Leave it empty when aggregate is count.
            - aggregate is one of sum, avg, count, min, max, median. Use count for "how many",
              avg for "average", sum for "total".
            - filters carry only what this question asks to narrow by. A category value must be
              spelled exactly as listed above. op is one of > >= < <= = != contains
            - If the question names a value that appears in one of the category columns above,
              filter on it.
            - sort is "value desc" normally, "value asc" for "lowest" or "bottom", "label asc" for a trend.
            - limit is 10 normally, the number asked for in "top N", or 36 for a trend.
            - chart is line for a trend, otherwise bar.
            - title is a short human title in sentence case.
            - The question being asked now outweighs the history. Use the history only to resolve
              references such as "that" or "those".

            Return a single JSON object with the keys: intent, groupBy, metric, aggregate,
            timeBucket, sort, limit, chart, title, and filters — an array whose items each have
            a column, an op and a value.
            """;
    }

    /// <summary>Stage three: write the sentence, using only figures already computed.</summary>
    public static string Explain(string question, QueryResult result)
    {
        var unit = result.Unit;
        var measured = result.MetricLabel is null
            ? "a count of rows"
            : $"{result.Spec.Aggregate} of {result.MetricLabel}";
        var written = unit.Prefix.Length > 0 || unit.Suffix.Length > 0
            ? $"Values are written with {(unit.Prefix.Length > 0 ? $"the prefix \"{unit.Prefix}\"" : "")}"
              + $"{(unit.Prefix.Length > 0 && unit.Suffix.Length > 0 ? " and " : "")}"
              + $"{(unit.Suffix.Length > 0 ? $"the suffix \"{unit.Suffix}\"" : "")}."
            : "Values carry no currency or unit sign; do not add one.";
        var figures = string.Join("\n", result.Figures.Select(f =>
            "- " + f.Label + ": " + f.Value.ToString("N" + unit.Decimals, CultureInfo.InvariantCulture)));

        return $"""
            A query over an uploaded table has already run. These are its results.

            Question asked: "{question}"
            Measured: {measured}, grouped by {result.Spec.TimeBucket ?? result.Spec.GroupBy}.
            {written}
            Rows in the file: {result.RowsScanned}
            Rows matching the filters: {result.RowsMatched}
            {(result.Total > 0 ? $"Total across the groups shown: {result.Total.ToString("N" + unit.Decimals, CultureInfo.InvariantCulture)}" : "")}

            Figures:
            {figures}

            Write two to four sentences answering the question from these figures.

            Absolute rules:
            - Use ONLY the numbers above. Never calculate a new one. State a percentage only if it
              is exactly derivable from the total given; otherwise leave it out.
            - Do not speculate about causes. The data does not say why.
            - Write values exactly as they appear above, with their unit sign if any.
            - Finish by saying how many rows the figures came from.
            - Plain sentences. No bullet points, no headings, no markdown.
            """;
    }
}
