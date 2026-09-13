using System.Text;
using AnalystAI.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AnalystAI.Api.Services;

/// <summary>
/// Describes the file in context to a planner: the columns, and the values the
/// low-cardinality ones actually hold.
///
/// Without this the planner has to guess how a question's words map onto stored
/// values — "orders in processing" produced a plan with no filter on status,
/// because nothing told it that `status` contains "Processing". The values are
/// read from the rows themselves, so the description is true of whichever file
/// is loaded rather than of the file the prompt was written against.
/// </summary>
public class SchemaSummary(AppDbContext db)
{
    /// <summary>Above this a column is an identifier, not a category worth listing.</summary>
    private const int CategoryCeiling = 25;

    public async Task<string> DescribeAsync(int datasetId, CancellationToken ct = default)
    {
        var rows = db.SalesRows.AsNoTracking().Where(r => r.DatasetId == datasetId);

        var categories = await Values(rows.Select(r => r.Category), ct);
        var regions = await Values(rows.Select(r => r.Region), ct);
        var statuses = await Values(rows.Select(r => r.Status), ct);
        var products = await Values(rows.Select(r => r.Product), ct);
        var customers = await Values(rows.Select(r => r.Customer), ct);

        var first = await rows.OrderBy(r => r.Date).Select(r => r.Date).FirstOrDefaultAsync(ct);
        var last = await rows.OrderByDescending(r => r.Date).Select(r => r.Date).FirstOrDefaultAsync(ct);

        var text = new StringBuilder();
        text.AppendLine("  date      date       the order date"
            + (first == default ? "" : $", from {first:yyyy-MM-dd} to {last:yyyy-MM-dd}"));
        text.AppendLine("  orderId   text");
        text.AppendLine(Line("customer", customers));
        text.AppendLine(Line("product", products));
        text.AppendLine(Line("category", categories));
        text.AppendLine("  qty       number");
        text.AppendLine("  price     number");
        text.AppendLine("  revenue   number     qty multiplied by price");
        text.AppendLine(Line("region", regions));
        text.Append(Line("status", statuses));

        return text.ToString();
    }

    private static async Task<List<string>> Values(IQueryable<string> column, CancellationToken ct) =>
        await column.Distinct().OrderBy(v => v).Take(CategoryCeiling + 1).ToListAsync(ct);

    /// <summary>
    /// Lists the values only when there are few enough to be a category. A
    /// truncated list would invite the planner to filter on a value that is not
    /// in it, so a long one is described by its size instead.
    /// </summary>
    private static string Line(string name, IReadOnlyList<string> values)
    {
        var padded = name.PadRight(9);
        return values.Count > CategoryCeiling
            ? $"  {padded} category   {values.Count}+ distinct values"
            : $"  {padded} category   {string.Join(", ", values)}";
    }
}
