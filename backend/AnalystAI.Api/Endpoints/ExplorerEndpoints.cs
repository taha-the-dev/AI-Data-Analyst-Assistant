using AnalystAI.Api.Contracts;
using AnalystAI.Api.Data;
using AnalystAI.Api.Query;
using AnalystAI.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AnalystAI.Api.Endpoints;

public static class ExplorerEndpoints
{
    private static readonly ExplorerColumnDto[] ColumnSet =
    [
        new("date", "Date", "left", false),
        new("orderId", "Order ID", "left", false),
        new("customer", "Customer", "left", false),
        new("product", "Product", "left", false),
        new("category", "Category", "left", false),
        new("qty", "Qty", "right", true),
        new("price", "Price", "right", true),
        new("revenue", "Revenue", "right", true),
        new("region", "Region", "left", false),
        new("status", "Status", "left", false),
    ];

    public static RouteGroupBuilder MapExplorerEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/explorer").WithTags("Explorer");

        group.MapGet("/columns", () => Results.Ok(ColumnSet))
            .WithName("ExplorerColumns")
            .WithSummary("Column definitions the grid renders from.");

        group.MapGet("/rows", async (
            AppDbContext db,
            HttpRequest request,
            IDatasetContext context,
            int? datasetId,
            int page = 1,
            int pageSize = 25,
            string sort = "revenue",
            string dir = "desc",
            CancellationToken ct = default) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 500);

            var id = await context.ResolveAsync(datasetId, ct);
            if (id is null) return Problems.NoDataset(datasetId);

            if (!QueryEngine.IsColumn(sort))
                return Problems.UnknownColumn("sort column", sort, QueryEngine.Columns);

            // Filters arrive as repeated ?filter=column:op:value
            var filters = new List<QueryFilter>();
            foreach (var raw in request.Query["filter"])
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var parts = raw.Split(':', 3);
                if (parts.Length != 3)
                    return Problems.BadRequest(
                        "Malformed filter",
                        $"'{raw}' is not valid. Use filter=column:op:value, for example filter=revenue:gt:10000.");

                if (!QueryEngine.IsColumn(parts[0]))
                    return Problems.UnknownColumn("filter column", parts[0], QueryEngine.Columns);

                filters.Add(new QueryFilter(parts[0], RowFilters.ParseOp(parts[1]), parts[2]));
            }

            var stored = await db.SalesRows.CountAsync(r => r.DatasetId == id, ct);
            if (stored == 0) return Problems.NoRows(id.Value);

            var q = db.SalesRows.AsNoTracking().Where(r => r.DatasetId == id);
            foreach (var f in filters) q = RowFilters.Apply(q, f);

            var matched = await q.CountAsync(ct);
            q = RowFilters.Order(q, sort, dir.Equals("desc", StringComparison.OrdinalIgnoreCase));

            var items = await q.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(r => new ExplorerRowDto(
                    r.Id,
                    r.Date == RowFilters.UndatedValue ? "" : r.Date.ToString("yyyy-MM-dd"),
                    r.OrderId, r.Customer, r.Product,
                    r.Category, r.Qty, r.Price, r.Revenue, r.Region, r.Status))
                .ToListAsync(ct);

            return Results.Ok(new Paged<ExplorerRowDto>(
                items, page, pageSize, matched, (int)Math.Ceiling(matched / (double)pageSize)));
        })
        .WithName("ExplorerRows")
        .WithSummary("Paged rows with sorting and repeatable filter=column:op:value.");

        return api;
    }
}
