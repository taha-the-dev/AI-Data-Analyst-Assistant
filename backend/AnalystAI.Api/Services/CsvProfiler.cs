using System.Globalization;
using AnalystAI.Api.Models;

namespace AnalystAI.Api.Services;

/// <summary>
/// Reads an uploaded delimited file and profiles every column: what kind of
/// value it holds, how much of it is missing, and the summary statistics for
/// anything numeric.
/// </summary>
public static class CsvProfiler
{
    public sealed record ParseResult(List<string> Headers, List<string[]> Rows);

    public static ParseResult Parse(TextReader reader, int maxRows = 100_000)
    {
        var headers = new List<string>();
        var rows = new List<string[]>();

        string? line;
        var first = true;
        while ((line = reader.ReadLine()) is not null && rows.Count < maxRows)
        {
            if (line.Length == 0) continue;
            var fields = SplitLine(line);

            if (first)
            {
                headers.AddRange(fields.Select((h, i) => string.IsNullOrWhiteSpace(h) ? $"column_{i + 1}" : h.Trim()));
                first = false;
                continue;
            }

            rows.Add(fields);
        }

        return new ParseResult(headers, rows);
    }

    /// <summary>Splits one CSV line, honouring quoted fields and doubled quotes.</summary>
    public static string[] SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else inQuotes = false;
                }
                else current.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }

        fields.Add(current.ToString());
        return [.. fields];
    }

    public static List<DatasetColumn> Profile(ParseResult parsed)
    {
        var columns = new List<DatasetColumn>();

        for (var c = 0; c < parsed.Headers.Count; c++)
        {
            var values = parsed.Rows
                .Select(r => c < r.Length ? r[c].Trim() : "")
                .ToList();

            var present = values.Where(v => v.Length > 0).ToList();
            var numbers = new List<double>();
            var dates = 0;
            var bools = 0;

            foreach (var v in present)
            {
                if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)) numbers.Add(n);
                else if (DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) dates++;
                else if (bool.TryParse(v, out _)) bools++;
            }

            var distinct = present.Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var kind = Classify(present.Count, numbers.Count, dates, bools, distinct);

            var column = new DatasetColumn
            {
                Name = parsed.Headers[c],
                Ordinal = c + 1,
                Kind = kind,
                Missing = values.Count - present.Count,
                Distinct = distinct,
                SampleValues = string.Join(",", present.Take(3)),
            };

            if (kind == "number" && numbers.Count > 0)
            {
                column.Min = numbers.Min();
                column.Max = numbers.Max();
                column.Mean = Math.Round(numbers.Average(), 4);
                var mean = numbers.Average();
                column.StdDev = Math.Round(
                    Math.Sqrt(numbers.Sum(x => (x - mean) * (x - mean)) / numbers.Count), 4);
            }

            columns.Add(column);
        }

        return columns;
    }

    private static string Classify(int present, int numeric, int dates, int bools, int distinct)
    {
        if (present == 0) return "text";
        if (bools == present) return "boolean";
        if (numeric >= present * 0.9) return "number";
        if (dates >= present * 0.9) return "date";
        // A handful of repeated values across many rows is a category, not free text.
        return distinct <= Math.Max(12, present / 20) ? "category" : "text";
    }

    /// <summary>Fraction of cells present, expressed the way the UI shows it.</summary>
    public static string QualityFor(IReadOnlyList<DatasetColumn> columns, int rowCount)
    {
        if (rowCount == 0 || columns.Count == 0) return "Low";
        var cells = (long)rowCount * columns.Count;
        var missing = columns.Sum(c => (long)c.Missing);
        var completeness = 1d - (double)missing / cells;

        return completeness switch
        {
            >= 0.98 => "High",
            >= 0.90 => "Medium",
            _ => "Low",
        };
    }
}
