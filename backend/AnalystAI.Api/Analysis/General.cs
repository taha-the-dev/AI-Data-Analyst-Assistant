using AnalystAI.Api.Query;

namespace AnalystAI.Api.Analysis;

/// <summary>
/// The recipe for any file. It knows nothing about subject areas: it takes the
/// file's numeric columns as measures, its low-cardinality columns as groupings
/// and its date column as time, and writes what those support.
///
/// After a subject-area recipe it only fills gaps, so a sales file missing a
/// region still gets four charts, and a file no recipe recognised still gets a
/// full dashboard.
/// </summary>
internal static class General
{
    public static void Build(Board b, bool primary)
    {
        var f = b.Frame;
        if (f.RowCount == 0)
        {
            b.Kpi("Records", "0", "table_rows");
            return;
        }

        var measures = f.Columns
            .Where(c => c.IsNumber && !c.Identifier && !IsYear(c) && Spread(c))
            .OrderBy(c => primary ? 0 : b.Claimed(c) ? 0 : 1)
            .ThenBy(c => c.Missing)
            .ThenBy(c => c.Index)
            .Take(4)
            .Select(c => (Column: c, Measure: Measure.Of(c)))
            .ToList();

        var groups = f.Columns
            .Where(c => c.IsGroup(24))
            .OrderBy(c => primary ? 0 : b.Claimed(c) ? 0 : 1)
            .ThenBy(c => c.Distinct > 12 ? 1 : 0)
            .ThenBy(c => c.Missing)
            .ThenBy(c => c.Index)
            .ToList();

        var label = f.Columns
            .Where(c => c.IsLabel && !c.IsGroup(24) && c.Present > 0)
            .OrderBy(c => c.Identifier ? 1 : 0)
            .ThenByDescending(c => c.Distinct)
            .FirstOrDefault();

        var date = f.Date;
        var m0 = measures.Count > 0 ? measures[0].Measure : null;
        var m1 = measures.Count > 1 ? measures[1].Measure : null;
        var g0 = groups.Count > 0 ? groups[0] : null;
        var g1 = groups.Count > 1 ? groups[1] : null;

        if (primary)
        {
            foreach (var (column, _) in measures) b.Claim(column, "Measure");
            foreach (var group in groups.Take(2)) b.Claim(group, "Grouping");
            b.Claim(date, "Date");
            b.Claim(label, "Label");
        }

        // ── Tiles ──
        if (primary || b.Kpis.Count < 3)
        {
            b.Kpi("Records", Fmt.Int(f.RowCount), "table_rows");
            if (label is not null && label.Distinct < f.RowCount && label.Distinct > 1)
                b.Kpi($"Distinct {Fmt.Plural(Fmt.Title(label.Name))}", Fmt.Int(label.Distinct), "tag");

            foreach (var (column, measure) in measures)
            {
                if (b.Kpis.Count >= Board.MaxKpis - (g0 is null ? 0 : 1)) break;
                var values = measure.Present.ToList();
                if (Additive(column))
                    b.Kpi($"Total {measure.Title}", Fmt.Compact(values.Sum(), measure.Unit), "functions",
                        $"average {Fmt.Compact(values.Average(), measure.Unit.ForAverage)}");
                else
                    b.Kpi($"Avg {measure.Title}", Fmt.Compact(values.Average(), measure.Unit.ForAverage), "functions",
                        $"range {Fmt.Compact(values.Min(), measure.Unit)} – {Fmt.Compact(values.Max(), measure.Unit)}");
            }

            if (g0 is not null) b.Kpi(Fmt.Plural(Fmt.Title(g0.Name)), Fmt.Int(g0.Distinct), "category");
        }

        // ── Charts ──
        if (date is not null)
        {
            if (m0 is not null)
            {
                var agg = Additive(measures[0].Column) ? Agg.Sum : Agg.Mean;
                b.Chart($"series:{m0.Name}", $"{m0.Title} over time", "line", f.Series(date, m0, agg),
                    agg == Agg.Mean ? m0.Unit.ForAverage : m0.Unit, agg == Agg.Mean ? "Average per period" : "Total per period");
            }
            else
            {
                b.Chart($"series:count:{date.Name}", "Records over time", "line", f.Series(date, null, Agg.Count), Unit.Count);
            }
        }

        if (m0 is not null) b.Chart($"hist:{m0.Name}", $"{m0.Title} distribution", "columns", Frame.Histogram(m0), Unit.Count, "Rows in each band");

        if (g0 is not null && m0 is not null)
        {
            var agg = Additive(measures[0].Column) ? Agg.Sum : Agg.Mean;
            b.Chart($"{agg}:{m0.Name}:{g0.Name}", $"{(agg == Agg.Mean ? "Average " + m0.Lower : m0.Title)} by {Fmt.Lower(g0.Name)}", "bars",
                f.Group(g0, m0, agg, 10), agg == Agg.Mean ? m0.Unit.ForAverage : m0.Unit);
        }

        if (g0 is not null)
            b.Chart($"count:{g0.Name}", $"Rows by {Fmt.Lower(g0.Name)}", g0.Distinct <= 6 ? "donut" : "bars", f.Group(g0, null, Agg.Count, 10), Unit.Count);

        if (g0 is not null && m1 is not null)
            b.Chart($"Mean:{m1.Name}:{g0.Name}", $"Average {m1.Lower} by {Fmt.Lower(g0.Name)}", "bars", f.Group(g0, m1, Agg.Mean, 10), m1.Unit.ForAverage);

        if (g1 is not null)
            b.Chart($"count:{g1.Name}", $"Rows by {Fmt.Lower(g1.Name)}", g1.Distinct <= 6 ? "donut" : "bars", f.Group(g1, null, Agg.Count, 10), Unit.Count);

        if (m1 is not null) b.Chart($"hist:{m1.Name}", $"{m1.Title} distribution", "columns", Frame.Histogram(m1), Unit.Count, "Rows in each band");

        // ── Table ──
        b.Table ??= g0 is not null ? GroupTable(f, g0, measures.Select(m => m.Measure).Take(2).ToList())
            : m0 is not null ? TopRows(f, measures[0].Column, m0, label)
            : SampleRows(f);

        // ── Insights ──
        var offset = primary ? 0 : 50;

        if (g0 is not null && m0 is not null)
        {
            var byGroup = f.Group(g0, m0, Agg.Mean, 50);
            if (byGroup.Count >= 2)
                b.Insight(offset + 1, "neutral", "leaderboard", $"{m0.Title} by {Fmt.Lower(g0.Name)}",
                    $"{byGroup[0].Label} has the highest average {m0.Lower} ({Fmt.Value(byGroup[0].Value, m0.Unit.ForAverage)}) " +
                    $"and {byGroup[^1].Label} the lowest ({Fmt.Value(byGroup[^1].Value, m0.Unit.ForAverage)}).");
        }

        if (measures.Count >= 2)
        {
            var strongest = measures
                .SelectMany((a, i) => measures.Skip(i + 1).Select(c => (A: a.Measure, B: c.Measure, R: f.Correlation(a.Measure, c.Measure))))
                .Where(x => x.R is { } r && Math.Abs(r) >= 0.5)
                .OrderByDescending(x => Math.Abs(x.R!.Value))
                .FirstOrDefault();

            if (strongest.A is not null)
                b.Insight(offset + 2, "neutral", "insights", "Related measures",
                    strongest.R > 0
                        ? $"{strongest.A.Title} and {strongest.B.Lower} rise together (r = {strongest.R:0.00})."
                        : $"{strongest.B.Title} falls as {strongest.A.Lower} rises (r = {strongest.R:0.00}).");
        }

        if (m0 is not null)
        {
            var sorted = m0.Present.Order().ToList();
            var q1 = Frame.Percentile(sorted, 0.25);
            var q3 = Frame.Percentile(sorted, 0.75);
            var iqr = q3 - q1;
            if (iqr > 0 && sorted.Count >= 10)
            {
                var lo = q1 - 1.5 * iqr;
                var hi = q3 + 1.5 * iqr;
                var outliers = sorted.Count(v => v < lo || v > hi);
                if (outliers > 0)
                    b.Insight(offset + 3, "alert", "scatter_plot", "Unusual values",
                        $"{Fmt.Int(outliers)} {m0.Lower} {(outliers == 1 ? "value sits" : "values sit")} outside the typical range of " +
                        $"{Fmt.Compact(Math.Max(lo, sorted[0]), m0.Unit)} to {Fmt.Compact(Math.Min(hi, sorted[^1]), m0.Unit)}.");
            }
        }

        if (g0 is not null)
        {
            var counts = f.Group(g0, null, Agg.Count, 50);
            if (counts.Count >= 2)
                b.Insight(offset + 4, "neutral", "pie_chart", $"Largest {Fmt.Lower(g0.Name)}",
                    $"{counts[0].Label} makes up {DatasetAnalyzer.Share(counts[0].Value, f.RowCount)} of the rows, " +
                    $"across {Fmt.Int(g0.Distinct)} {Fmt.Plural(Fmt.Lower(g0.Name))}.");
        }

        if (date is not null && m0 is not null)
        {
            var series = f.Series(date, m0, Additive(measures[0].Column) ? Agg.Sum : Agg.Mean);
            if (DatasetAnalyzer.Change(series) is { } change)
                b.Insight(offset + 5, change.Tone == "down" ? "alert" : "positive", change.Tone == "down" ? "trending_down" : "trending_up",
                    "Latest period",
                    $"{m0.Title} moved {change.Text} in {Fmt.Period(series[^1].Label)} against {Fmt.Period(series[^2].Label)}.");
        }
    }

    private static bool Additive(Column column) => DatasetAnalyzer.Additive(column.Name);

    private static bool IsYear(Column c) =>
        c.Match(["year", "yr"]) == 3 && c.Numbers.All(n => n is null || (n % 1 == 0 && n >= 1900 && n <= 2100));

    /// <summary>A column holding one value throughout says nothing a chart could show.</summary>
    private static bool Spread(Column c)
    {
        double? first = null;
        foreach (var n in c.Numbers)
        {
            if (n is null) continue;
            if (first is null) first = n;
            else if (Math.Abs(n.Value - first.Value) > 1e-12) return true;
        }
        return false;
    }

    private static Contracts.DataTableDto GroupTable(Frame f, Column group, List<Measure> measures)
    {
        var counts = f.Group(group, null, Agg.Count, 8);
        var means = measures.Select(m => f.Group(group, m, Agg.Mean, 1000)
            .ToDictionary(x => x.Label, x => x.Value, StringComparer.OrdinalIgnoreCase)).ToList();

        var columns = new List<(string, string)> { (Fmt.Title(group.Name), "left"), ("Rows", "right"), ("Share", "right") };
        columns.AddRange(measures.Select(m => ($"Avg {m.Title}", "right")));

        var rows = counts.Select(c =>
        {
            var cells = new List<string> { c.Label, Fmt.Int(c.Value), DatasetAnalyzer.Share(c.Value, f.RowCount) };
            cells.AddRange(measures.Select((m, i) => means[i].TryGetValue(c.Label, out var v) ? Fmt.Value(v, m.Unit.ForAverage) : "—"));
            return cells.ToArray();
        });

        return DatasetAnalyzer.Table($"{Fmt.Title(group.Name)} Summary",
            $"{Math.Min(8, group.Distinct)} of {Fmt.Int(group.Distinct)} {Fmt.Plural(Fmt.Lower(group.Name))} by row count", columns, rows);
    }

    private static Contracts.DataTableDto TopRows(Frame f, Column measureColumn, Measure measure, Column? label)
    {
        var shown = new List<Column>();
        if (label is not null) shown.Add(label);
        shown.Add(measureColumn);
        shown.AddRange(f.Columns.Where(c => !shown.Contains(c) && !c.Identifier && c.Present > 0).Take(5 - shown.Count));

        var order = Enumerable.Range(0, f.RowCount)
            .Where(r => measure.Values[r].HasValue)
            .OrderByDescending(r => measure.Values[r])
            .Take(8);

        return DatasetAnalyzer.Table($"Top Records by {measure.Title}", $"Highest 8 of {Fmt.Int(f.RowCount)} rows",
            shown.Select(c => (Fmt.Title(c.Name), c.IsNumber ? "right" : "left")),
            order.Select(r => shown.Select(c => Cell(c, r)).ToArray()));
    }

    private static Contracts.DataTableDto SampleRows(Frame f)
    {
        var shown = f.Columns.Take(5).ToList();
        return DatasetAnalyzer.Table("Sample Records", $"First {Math.Min(8, f.RowCount)} of {Fmt.Int(f.RowCount)} rows",
            shown.Select(c => (Fmt.Title(c.Name), c.IsNumber ? "right" : "left")),
            Enumerable.Range(0, Math.Min(8, f.RowCount)).Select(r => shown.Select(c => Cell(c, r)).ToArray()));
    }

    /// <summary>A cell as the table shows it: numbers in the column's own format, gaps as a dash.</summary>
    internal static string Cell(Column c, int row)
    {
        if (c.Values[row].Length == 0) return "—";
        if (c.IsNumber && c.Numbers[row] is { } n) return Fmt.Value(n, Unit.For(c));
        return c.Values[row];
    }
}
