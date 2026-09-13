using System.Globalization;

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
    /// <summary>The columns as written when nothing has described the loaded file.</summary>
    private const string DefaultSchema = """
          date      date       the order date
          orderId   text
          customer  category   the buying organisation
          product   category
          category  category   the product category
          qty       number
          price     number
          revenue   number     qty multiplied by price
          region    category
          status    category
        """;

    /// <summary>Stage one: read the question, choose what to compute. No arithmetic.</summary>
    public static string Plan(string question, IReadOnlyList<string> priorTurns, string? schema = null)
    {
        var history = priorTurns.Count == 0
            ? "(none)"
            : string.Join("\n", priorTurns.TakeLast(6).Select(t => "- " + t));

        var columns = string.IsNullOrWhiteSpace(schema) ? DefaultSchema : schema.TrimEnd();

        return $"""
            You plan database queries for a sales dataset. You never calculate anything.

            Columns and their types, with the values each category column actually holds:
            {columns}

            Earlier questions in this conversation:
            {history}

            The question now: "{question}"

            Rules for the plan you return:
            - groupBy must be one of the column names above.
            - Use timeBucket (day/week/month/quarter/year) with groupBy "date" only when the question
              asks about change over time. Set intent to "trend" then, otherwise "aggregate".
            - aggregate is one of sum, avg, count, min, max. Use count for "how many".
            - metric is revenue, qty or price. Leave it empty when aggregate is count.
            - filters carry only what this question asks to narrow by, and a filter value must be
              spelled exactly as listed above. op is one of > >= < <= = !=
            - If the question names a value that appears in one of the category columns above —
              a status, a region, a category, a product, a customer — filter on it.
            - sort is "value desc" normally, "label asc" for a trend.
            - limit is 10 normally, the number asked for in "top N", or 24 for a trend.
            - chart is line for a trend, donut for region, otherwise bar.
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
        var unit = result.Unit == "currency" ? "US dollars" : "a count of orders";
        var figures = string.Join("\n", result.Figures.Select(f =>
            "- " + f.Label + ": " + f.Value.ToString("N2", CultureInfo.InvariantCulture)));

        return $"""
            A database query has already run. These are its results.

            Question asked: "{question}"
            Measured: {result.Spec.Aggregate} of {result.Spec.Metric ?? "orders"},
            grouped by {result.Spec.TimeBucket ?? result.Spec.GroupBy}, in {unit}.
            Rows scanned: {result.RowsScanned}
            Rows matching the filters: {result.RowsMatched}
            Total across the groups shown: {result.Total.ToString("N2", CultureInfo.InvariantCulture)}

            Figures:
            {figures}

            Write two to four sentences answering the question from these figures.

            Absolute rules:
            - Use ONLY the numbers above. Never calculate a new one. State a percentage only if it
              is exactly derivable from the total given; otherwise leave it out.
            - Do not speculate about causes. The data does not say why.
            - Round money to whole dollars, with a dollar sign and thousands separators.
            - Finish by saying how many rows the figures came from.
            - Plain sentences. No bullet points, no headings, no markdown.
            """;
    }
}
