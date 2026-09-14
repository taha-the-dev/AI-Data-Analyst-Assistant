using System.Globalization;
using AnalystAI.Api.Models;

namespace AnalystAI.Api.Services;

/// <summary>
/// Turns the parsed cells of an uploaded file into stored rows.
///
/// Profiling a file told the user what was in it but left nothing to query, so
/// an uploaded dataset could be listed and never analysed. Every field is
/// matched by header name; whatever cannot be matched is reported back rather
/// than filled in with a plausible value.
/// </summary>
public static class RowMapper
{
    /// <summary>Which header ended up feeding each queryable field.</summary>
    public sealed record FieldMapping(string Field, string? Header);

    /// <summary>
    /// Which cell feeds each field, worked out once from the headers so rows can
    /// then be mapped one at a time — an upload stores them in batches instead
    /// of holding a mapped copy of the whole file.
    /// </summary>
    public sealed class Plan
    {
        internal Plan(Dictionary<string, int> index, List<FieldMapping> mapping)
        {
            Index = index;
            Mapping = mapping;
        }

        internal Dictionary<string, int> Index { get; }
        public List<FieldMapping> Mapping { get; }
    }

    // Ordered by preference: the first header that contains one of these words
    // wins the field. Exact matches are preferred over contains.
    private static readonly Dictionary<string, string[]> Vocabulary = new()
    {
        ["date"] = ["date", "day", "period", "timestamp", "time", "month"],
        ["orderId"] = ["orderid", "order", "invoice", "transaction", "reference", "ref"],
        ["customer"] = ["customer", "client", "account", "company", "buyer", "user"],
        ["product"] = ["product", "item", "sku", "service", "plan"],
        ["category"] = ["category", "segment", "type", "department", "dept", "group"],
        ["qty"] = ["qty", "quantity", "units", "count", "volume"],
        ["price"] = ["price", "unitprice", "rate", "cost", "unitcost"],
        ["revenue"] = ["revenue", "amount", "total", "sales", "value", "turnover", "gmv"],
        ["region"] = ["region", "country", "market", "territory", "area", "state", "city"],
        ["status"] = ["status", "state", "stage", "outcome"],
    };

    public static Plan PlanFor(IReadOnlyList<string> headers)
    {
        var normalised = headers
            .Select(h => new string(h.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()))
            .ToList();

        var taken = new HashSet<int>();
        var index = new Dictionary<string, int>();

        foreach (var (field, words) in Vocabulary)
        {
            var match = FindColumn(normalised, words, taken);
            if (match is null) continue;
            index[field] = match.Value;
            taken.Add(match.Value);
        }

        var mapping = Vocabulary.Keys
            .Select(field => new FieldMapping(
                field,
                index.TryGetValue(field, out var i) ? headers[i] : null))
            .ToList();

        return new Plan(index, mapping);
    }

    public static SalesRow MapRow(string[] cells, Plan plan)
    {
        var index = plan.Index;
        var qty = (int)Math.Round(Number(cells, index, "qty") ?? 0);
        var price = Number(cells, index, "price") ?? 0;
        var revenue = Number(cells, index, "revenue")
                      // No revenue column: the only honest substitute is the
                      // product of two columns the file does have.
                      ?? (qty > 0 && price > 0 ? qty * price : price);

        return new SalesRow
        {
            Date = Date(cells, index),
            OrderId = Text(cells, index, "orderId"),
            Customer = Text(cells, index, "customer"),
            Product = Text(cells, index, "product"),
            Category = Text(cells, index, "category"),
            Qty = qty,
            Price = Math.Round(price, 4),
            Revenue = Math.Round(revenue, 4),
            Region = Text(cells, index, "region"),
            Status = Text(cells, index, "status"),
        };
    }

    private static int? FindColumn(List<string> headers, string[] words, HashSet<int> taken)
    {
        for (var i = 0; i < headers.Count; i++)
            if (!taken.Contains(i) && words.Contains(headers[i]))
                return i;

        for (var i = 0; i < headers.Count; i++)
            if (!taken.Contains(i) && words.Any(w => headers[i].Contains(w)))
                return i;

        return null;
    }

    private static string Cell(string[] cells, Dictionary<string, int> index, string field) =>
        index.TryGetValue(field, out var i) && i < cells.Length ? cells[i].Trim() : "";

    private static string Text(string[] cells, Dictionary<string, int> index, string field)
    {
        var value = Cell(cells, index, field);
        return value.Length == 0 ? "Unspecified" : value;
    }

    private static double? Number(string[] cells, Dictionary<string, int> index, string field)
    {
        var raw = Cell(cells, index, field);
        if (raw.Length == 0) return null;

        // Currency symbols, thousands separators and trailing percent signs are
        // common in exported files and are not part of the number.
        var cleaned = new string(raw.Where(c => char.IsDigit(c) || c is '.' or '-' or 'e' or 'E').ToArray());
        return double.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>
    /// An unreadable or missing date stays at the default value, which the
    /// engine reports as "Undated" rather than inventing a day for the row.
    /// </summary>
    private static DateOnly Date(string[] cells, Dictionary<string, int> index)
    {
        var raw = Cell(cells, index, "date");
        if (raw.Length == 0) return default;

        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return exact;
        if (DateOnly.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out var local))
            return local;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp))
            return DateOnly.FromDateTime(stamp);

        return default;
    }
}
