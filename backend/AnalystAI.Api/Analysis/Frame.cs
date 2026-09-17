using System.Globalization;
using System.Text;
using AnalystAI.Api.Models;
using AnalystAI.Api.Query;
using AnalystAI.Api.Services;

namespace AnalystAI.Api.Analysis;

/// <summary>One column of the uploaded file, as the analyser reads it.</summary>
internal sealed class Column
{
    public required int Index { get; init; }
    public required string Name { get; init; }
    /// <summary>The header split into lower-case words: "StudentName" → student, name.</summary>
    public required string[] Tokens { get; init; }
    /// <summary>The header lower-cased with everything but letters and digits removed.</summary>
    public required string Compact { get; init; }
    public required string Kind { get; init; }
    /// <summary>Trimmed cell text; an empty string wherever the cell is missing.</summary>
    public required string[] Values { get; init; }
    public required int Present { get; init; }
    public required int Distinct { get; init; }
    public double?[] Numbers { get; init; } = [];
    public DateOnly?[] Dates { get; init; } = [];
    /// <summary>The currency symbol most values were written with, if any.</summary>
    public string Prefix { get; init; } = "";
    /// <summary>Most values were written with a percent sign.</summary>
    public bool Percent { get; init; }
    public bool Identifier { get; set; }

    public bool IsNumber => Kind == "number";
    public bool IsDate => Kind == "date";
    public int Missing => Values.Length - Present;

    /// <summary>
    /// Something rows can be grouped by: a handful of repeated values rather
    /// than a measurement, a date or a unique key.
    /// </summary>
    public bool IsGroup(int maxDistinct = 30) =>
        !IsNumber && !IsDate && !Identifier && Distinct >= 2 && Distinct <= maxDistinct && Distinct < Math.Max(3, Present);

    public bool IsLabel => !IsNumber && !IsDate;

    /// <summary>How strongly the header reads as one of <paramref name="words"/>: 3 for a whole word, 1 for a fragment.</summary>
    public int Match(IEnumerable<string> words)
    {
        var best = 0;
        foreach (var word in words)
        {
            if (Tokens.Contains(word) || Compact == word) return 3;
            if (word.Length >= 4 && Compact.Contains(word)) best = 1;
        }
        return best;
    }

    public override string ToString() => Name;
}

/// <summary>A series of numbers to aggregate — a column, or one derived from two.</summary>
internal sealed record Measure(string Name, double?[] Values, Unit Unit)
{
    public IEnumerable<double> Present => Values.Where(v => v.HasValue).Select(v => v!.Value);
    public string Lower => Fmt.Lower(Name);
    public string Title => Fmt.Title(Name);

    public static Measure Of(Column column) => new(column.Name, column.Numbers, Unit.For(column));
}

/// <summary>How a figure is written: "$", "%", and how many decimals.</summary>
internal sealed record Unit(string Prefix, string Suffix, int Decimals)
{
    public static readonly Unit Count = new("", "", 0);
    public static readonly Unit Percent = new("", "%", 1);

    public static Unit For(Column column)
    {
        var integers = column.Numbers.All(n => n is null || Math.Abs(n.Value % 1) < 1e-9);
        return new Unit(column.Prefix, column.Percent ? "%" : "", integers ? 0 : column.Prefix.Length > 0 ? 2 : 1);
    }

    /// <summary>An average of whole numbers still needs a decimal place to be honest.</summary>
    public Unit ForAverage => this with { Decimals = Math.Max(Decimals, Prefix.Length > 0 ? 2 : 1) };
}

internal enum Agg { Count, Sum, Mean }

/// <summary>
/// The uploaded file held column by column, with the statistics every recipe
/// draws on. Nothing here knows what the data is about.
/// </summary>
internal sealed class Frame
{
    public required int RowCount { get; init; }
    public required List<Column> Columns { get; init; }
    public required int DuplicateRows { get; init; }

    public static Frame From(CsvProfiler.ParseResult parsed, IReadOnlyList<DatasetColumn> profile)
    {
        var rows = parsed.Rows;
        var columns = new List<Column>(parsed.Headers.Count);

        for (var c = 0; c < parsed.Headers.Count; c++)
        {
            var kind = profile.FirstOrDefault(p => p.Ordinal == c + 1)?.Kind ?? "text";
            var values = new string[rows.Count];
            var present = 0;
            var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var r = 0; r < rows.Count; r++)
            {
                var raw = c < rows[r].Length ? rows[r][c].Trim() : "";
                if (Cells.IsMissing(raw)) raw = "";
                else
                {
                    present++;
                    distinct.Add(raw);
                }
                values[r] = raw;
            }

            double?[] numbers = [];
            DateOnly?[] dates = [];
            var prefix = "";
            var percent = false;

            if (kind == "number")
            {
                numbers = new double?[rows.Count];
                var symbols = new Dictionary<char, int>();
                var percents = 0;

                for (var r = 0; r < rows.Count; r++)
                {
                    if (values[r].Length == 0 || !Cells.TryNumber(values[r], out var n, out var symbol, out var pct)) continue;
                    numbers[r] = n;
                    if (symbol is { } s) symbols[s] = symbols.GetValueOrDefault(s) + 1;
                    if (pct) percents++;
                }

                var top = symbols.OrderByDescending(kv => kv.Value).FirstOrDefault();
                if (top.Value * 2 >= present && top.Value > 0) prefix = top.Key == '₨' ? "Rs " : top.Key.ToString();
                percent = percents * 2 >= present && percents > 0;
            }
            else if (kind == "date")
            {
                dates = new DateOnly?[rows.Count];
                for (var r = 0; r < rows.Count; r++)
                    if (values[r].Length > 0 && Cells.TryDate(values[r], out var d)) dates[r] = d;
            }

            var tokens = Tokenise(parsed.Headers[c]);
            var column = new Column
            {
                Index = c,
                Name = parsed.Headers[c],
                Tokens = tokens,
                Compact = string.Concat(tokens),
                Kind = kind,
                Values = values,
                Present = present,
                Distinct = distinct.Count,
                Numbers = numbers,
                Dates = dates,
                Prefix = prefix,
                Percent = percent,
            };
            column.Identifier = LooksLikeIdentifier(column, rows.Count);
            columns.Add(column);
        }

        return new Frame { RowCount = rows.Count, Columns = columns, DuplicateRows = CountDuplicates(rows) };
    }

    /// <summary>"student_id", "StudentName", "Marks (Out of 100)" → words.</summary>
    internal static string[] Tokenise(string header)
    {
        var spaced = new StringBuilder();
        for (var i = 0; i < header.Length; i++)
        {
            var ch = header[i];
            if (i > 0 && char.IsUpper(ch) && char.IsLower(header[i - 1])) spaced.Append(' ');
            spaced.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        return spaced.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static readonly string[] KeyWords = ["id", "uuid", "guid", "roll", "code", "no", "sno", "srno", "serial", "key", "ref", "reference", "number", "num"];
    private static readonly string[] MeasureWords = ["marks", "score", "amount", "total", "price", "salary", "age", "qty", "quantity", "count"];

    /// <summary>
    /// A key identifies a row; it is never summed, averaged or charted. A header
    /// that says so is enough; a number column counts only if every value is
    /// unique and they run in sequence.
    /// </summary>
    private static bool LooksLikeIdentifier(Column column, int rows)
    {
        if (column.IsDate || column.Match(MeasureWords) == 3) return false;
        if (column.Tokens.Length > 0 && KeyWords.Contains(column.Tokens[^1])) return true;
        if (column.Compact.EndsWith("id") && column.Compact.Length <= 12 && !column.IsNumber) return true;

        if (!column.IsNumber || rows < 10 || column.Distinct != column.Present) return false;
        var ordered = column.Numbers.Where(n => n.HasValue).Select(n => n!.Value).Order().ToList();
        return ordered.All(n => n % 1 == 0) && ordered.Zip(ordered.Skip(1), (a, b) => b - a).All(d => Math.Abs(d - 1) < 1e-9);
    }

    /// <summary>Rows identical to an earlier row, compared on their trimmed cells.</summary>
    private static int CountDuplicates(List<string[]> rows)
    {
        var seen = new HashSet<ulong>();
        var duplicates = 0;

        foreach (var row in rows)
        {
            // FNV-1a over every cell. 64 bits keeps an accidental collision out
            // of reach at the row counts an upload allows.
            var hash = 14695981039346656037UL;
            foreach (var cell in row)
            {
                foreach (var ch in cell.AsSpan().Trim())
                {
                    hash ^= ch;
                    hash *= 1099511628211UL;
                }
                hash ^= 0x1F;
                hash *= 1099511628211UL;
            }
            if (!seen.Add(hash)) duplicates++;
        }

        return duplicates;
    }

    public Column? Date => Columns.Where(c => c.IsDate && c.Present > 0).MaxBy(c => c.Present);

    // ── Aggregation ─────────────────────────────────────────────────────────

    public static int DistinctCount(Column column) => column.Distinct;

    /// <summary>Rows grouped by a column's values, largest first.</summary>
    public List<Figure> Group(Column group, Measure? measure, Agg agg, int limit = 10)
    {
        var buckets = new Dictionary<string, (double Sum, int Count)>(StringComparer.OrdinalIgnoreCase);

        for (var r = 0; r < RowCount; r++)
        {
            var key = group.Values[r].Length == 0 ? "Unspecified" : group.Values[r];
            if (agg == Agg.Count)
            {
                var b = buckets.GetValueOrDefault(key);
                buckets[key] = (b.Sum + 1, b.Count + 1);
                continue;
            }

            if (measure?.Values[r] is not { } value) continue;
            var bucket = buckets.GetValueOrDefault(key);
            buckets[key] = (bucket.Sum + value, bucket.Count + 1);
        }

        return buckets
            .Where(kv => kv.Value.Count > 0)
            .Select(kv => new Figure(kv.Key, agg == Agg.Mean ? Round(kv.Value.Sum / kv.Value.Count) : Round(kv.Value.Sum)))
            .OrderByDescending(f => f.Value)
            .ThenBy(f => f.Label, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    /// <summary>A measure (or a row count) over time, oldest first, at a grain that suits the span.</summary>
    public List<Figure> Series(Column date, Measure? measure, Agg agg, int limit = 36)
    {
        var present = date.Dates.Where(d => d.HasValue).Select(d => d!.Value).ToList();
        if (present.Count == 0) return [];

        var span = present.Max().DayNumber - present.Min().DayNumber;
        Func<DateOnly, string> key = span switch
        {
            <= 45 => d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            <= 365 * 3 => d => d.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            _ => d => d.Year.ToString(CultureInfo.InvariantCulture),
        };

        var buckets = new SortedDictionary<string, (double Sum, int Count)>(StringComparer.Ordinal);
        for (var r = 0; r < RowCount; r++)
        {
            if (date.Dates[r] is not { } d) continue;
            double value;
            if (agg == Agg.Count) value = 1;
            else if (measure?.Values[r] is { } v) value = v;
            else continue;

            var k = key(d);
            var b = buckets.GetValueOrDefault(k);
            buckets[k] = (b.Sum + value, b.Count + 1);
        }

        return buckets
            .Select(kv => new Figure(kv.Key, agg == Agg.Mean ? Round(kv.Value.Sum / kv.Value.Count) : Round(kv.Value.Sum)))
            .TakeLast(limit)
            .ToList();
    }

    /// <summary>How the values of a measure are spread, in evenly sized bands.</summary>
    public static List<Figure> Histogram(Measure measure, int target = 8)
    {
        var values = measure.Present.ToList();
        if (values.Count < 2) return [];

        var min = values.Min();
        var max = values.Max();
        if (max - min < 1e-9) return [];

        var integers = values.All(v => Math.Abs(v % 1) < 1e-9);
        if (integers && max - min <= 10)
        {
            return Enumerable.Range((int)min, (int)(max - min) + 1)
                .Select(v => new Figure(Fmt.Short(v, measure.Unit), values.Count(x => (int)x == v)))
                .ToList();
        }

        var step = Nice((max - min) / target);
        var start = Math.Floor(min / step) * step;
        var bins = Math.Max(1, (int)Math.Ceiling((max - start) / step - 1e-9));
        var counts = new int[bins];

        foreach (var v in values)
            counts[Math.Clamp((int)Math.Floor((v - start) / step), 0, bins - 1)]++;

        return counts
            .Select((count, i) => new Figure(Fmt.Band(start + i * step, start + (i + 1) * step, measure.Unit), count))
            .ToList();
    }

    /// <summary>Pearson's r over the rows where both are present, or null with too few.</summary>
    public double? Correlation(Measure a, Measure b)
    {
        double n = 0, sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
        for (var r = 0; r < RowCount; r++)
        {
            if (a.Values[r] is not { } x || b.Values[r] is not { } y) continue;
            n++; sx += x; sy += y; sxx += x * x; syy += y * y; sxy += x * y;
        }
        if (n < 8) return null;

        var cov = sxy - sx * sy / n;
        var vx = sxx - sx * sx / n;
        var vy = syy - sy * sy / n;
        return vx <= 0 || vy <= 0 ? null : cov / Math.Sqrt(vx * vy);
    }

    public static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        var rank = (sorted.Count - 1) * p;
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
    }

    private static double Nice(double raw)
    {
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        foreach (var f in new[] { 1, 2, 2.5, 5, 10 })
            if (f * magnitude >= raw) return f * magnitude;
        return 10 * magnitude;
    }

    public static double Round(double value) => Math.Round(value, 4);
}

/// <summary>Writing figures the way the dashboard shows them.</summary>
internal static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Value(double value, Unit unit) =>
        unit.Prefix + value.ToString("N" + unit.Decimals, Inv) + unit.Suffix;

    /// <summary>12.4K, 1.25M — for tiles and chart labels where space is short.</summary>
    public static string Compact(double value, Unit unit)
    {
        var abs = Math.Abs(value);
        var body = abs switch
        {
            >= 1e9 => (value / 1e9).ToString("0.##", Inv) + "B",
            >= 1e6 => (value / 1e6).ToString("0.##", Inv) + "M",
            >= 1e4 => (value / 1e3).ToString("0.#", Inv) + "K",
            _ => value.ToString("N" + unit.Decimals, Inv),
        };
        return unit.Prefix + body + unit.Suffix;
    }

    /// <summary>A band edge: no trailing zeros, thousands shortened.</summary>
    public static string Short(double value, Unit unit)
    {
        var abs = Math.Abs(value);
        var body = abs switch
        {
            >= 1e6 => (value / 1e6).ToString("0.#", Inv) + "M",
            >= 1e4 => (value / 1e3).ToString("0.#", Inv) + "K",
            _ => value.ToString("0.##", Inv),
        };
        return unit.Prefix + body;
    }

    /// <summary>"$25–50K": the scale is written once, after the upper edge, when both edges share it.</summary>
    public static string Band(double lo, double hi, Unit unit)
    {
        foreach (var (size, mark) in new[] { (1e6, "M"), (1e3, "K") })
        {
            if (Math.Abs(hi) < (size == 1e3 ? 1e4 : size) || (lo != 0 && Math.Abs(lo) < size)) continue;
            return $"{unit.Prefix}{(lo / size).ToString("0.#", Inv)}–{(hi / size).ToString("0.#", Inv)}{mark}";
        }
        return $"{Short(lo, unit)}–{Short(hi, unit).Substring(unit.Prefix.Length)}";
    }

    public static string Int(double value) => value.ToString("N0", Inv);

    public static string Pct(double fraction) => (fraction * 100).ToString("0.#", Inv) + "%";

    /// <summary>Unit words headers end with, and how a title writes them.</summary>
    private static readonly Dictionary<string, string> Units = new()
    {
        ["c"] = "°C", ["f"] = "°F", ["kmh"] = "km/h", ["mph"] = "mph", ["kg"] = "kg", ["cm"] = "cm", ["mm"] = "mm",
        ["km"] = "km", ["usd"] = "USD", ["pkr"] = "PKR", ["eur"] = "EUR", ["inr"] = "INR", ["gbp"] = "GBP",
        ["pct"] = "%", ["hrs"] = "hrs", ["mins"] = "mins", ["sec"] = "sec", ["ms"] = "ms",
    };

    private static readonly HashSet<string> Acronyms = ["gpa", "cgpa", "id", "aov", "kpi", "ctc", "hr", "sku", "gmv", "doj"];

    /// <summary>"marks_obtained" → "Marks Obtained"; "temperature_c" → "Temperature (°C)".</summary>
    public static string Title(string header)
    {
        var words = Frame.Tokenise(header);
        if (words.Length == 0) return header;

        var unit = words.Length > 1 && Units.TryGetValue(words[^1], out var u) ? u : null;
        var body = string.Join(' ', (unit is null ? words : words[..^1])
            .Select(t => Acronyms.Contains(t) ? t.ToUpperInvariant() : char.ToUpperInvariant(t[0]) + t[1..]));
        return unit is null ? body : $"{body} ({unit})";
    }

    /// <summary>"MarksObtained" → "marks obtained", for use inside a sentence; a trailing unit is dropped.</summary>
    public static string Lower(string header)
    {
        var words = Frame.Tokenise(header);
        if (words.Length > 1 && Units.ContainsKey(words[^1])) words = words[..^1];
        return words.Length == 0 ? header : string.Join(' ', words.Select(t => Acronyms.Contains(t) ? t.ToUpperInvariant() : t));
    }

    /// <summary>"subject" → "subjects". Only what headers need.</summary>
    public static string Plural(string noun) =>
        noun.EndsWith('s') ? noun : noun.EndsWith('y') && noun.Length > 2 && !"aeiou".Contains(noun[^2]) ? noun[..^1] + "ies" : noun + "s";

    /// <summary>"2026-03" → "Mar 2026", matching the frontend's bucket labels.</summary>
    public static string Period(string label)
    {
        if (DateOnly.TryParseExact(label, "yyyy-MM", Inv, DateTimeStyles.None, out var month))
            return month.ToString("MMM yyyy", Inv);
        if (DateOnly.TryParseExact(label, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var day))
            return day.ToString("d MMM yyyy", Inv);
        return label;
    }
}
