using System.Globalization;
using AnalystAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AnalystAI.Api.Query;

/// <summary>
/// One implementation of "narrow these rows" and "order these rows", shared by
/// the query engine and the explorer grid. Both screens must agree on what a
/// filter means, so both call the same code rather than keeping a copy each.
/// </summary>
public static class RowFilters
{
    /// <summary>A row whose date could not be read keeps the default date.</summary>
    public static readonly DateOnly UndatedValue = default;

    public const string UndatedLabel = "Undated";

    /// <summary>Accepts both symbolic and URL-safe operator names.</summary>
    public static string ParseOp(string op) => op.Trim().ToLowerInvariant() switch
    {
        "gt" => ">",
        "gte" or "ge" => ">=",
        "lt" => "<",
        "lte" or "le" => "<=",
        "eq" => "=",
        "ne" or "neq" => "!=",
        _ => op.Trim(),
    };

    /// <summary>
    /// Translates one filter to SQL. An unknown column or an unparseable value
    /// narrows nothing rather than throwing: a bad filter must not take the
    /// request down, and the spec returned to the caller shows what actually ran.
    /// </summary>
    public static IQueryable<SalesRow> Apply(IQueryable<SalesRow> q, QueryFilter filter)
    {
        var column = filter.Column.Trim().ToLowerInvariant();
        var op = ParseOp(filter.Op);
        var raw = filter.Value.Trim().Trim('"');

        if (column is "qty" or "price" or "revenue")
        {
            if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var n))
                return q;

            return column switch
            {
                "qty" => op switch
                {
                    ">" => q.Where(r => r.Qty > n),
                    ">=" => q.Where(r => r.Qty >= n),
                    "<" => q.Where(r => r.Qty < n),
                    "<=" => q.Where(r => r.Qty <= n),
                    "!=" => q.Where(r => r.Qty != n),
                    _ => q.Where(r => r.Qty == n),
                },
                "price" => op switch
                {
                    ">" => q.Where(r => r.Price > n),
                    ">=" => q.Where(r => r.Price >= n),
                    "<" => q.Where(r => r.Price < n),
                    "<=" => q.Where(r => r.Price <= n),
                    "!=" => q.Where(r => r.Price != n),
                    _ => q.Where(r => r.Price == n),
                },
                _ => op switch
                {
                    ">" => q.Where(r => r.Revenue > n),
                    ">=" => q.Where(r => r.Revenue >= n),
                    "<" => q.Where(r => r.Revenue < n),
                    "<=" => q.Where(r => r.Revenue <= n),
                    "!=" => q.Where(r => r.Revenue != n),
                    _ => q.Where(r => r.Revenue == n),
                },
            };
        }

        if (column == "date")
        {
            if (!DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return q;

            return op switch
            {
                ">" => q.Where(r => r.Date > d),
                ">=" => q.Where(r => r.Date >= d),
                "<" => q.Where(r => r.Date < d),
                "<=" => q.Where(r => r.Date <= d),
                "!=" => q.Where(r => r.Date != d),
                _ => q.Where(r => r.Date == d),
            };
        }

        // Text comparisons are case-insensitive. "shipped" and "Shipped" are the
        // same request as far as anyone typing — or any model planning — is
        // concerned, and a filter that silently matches nothing because of a
        // capital letter is indistinguishable from having no data.
        //
        // Both sides are lowered rather than relying on LIKE: SQLite's LIKE
        // ignores case, PostgreSQL's does not, and the same filter has to match
        // the same rows on either.
        var negate = op == "!=";
        var pattern = Escape(raw.ToLowerInvariant());

        return column switch
        {
            "orderid" => negate
                ? q.Where(r => !EF.Functions.Like(r.OrderId.ToLower(), pattern, Escapes))
                : q.Where(r => EF.Functions.Like(r.OrderId.ToLower(), pattern, Escapes)),
            "customer" => negate
                ? q.Where(r => !EF.Functions.Like(r.Customer.ToLower(), pattern, Escapes))
                : q.Where(r => EF.Functions.Like(r.Customer.ToLower(), pattern, Escapes)),
            "product" => negate
                ? q.Where(r => !EF.Functions.Like(r.Product.ToLower(), pattern, Escapes))
                : q.Where(r => EF.Functions.Like(r.Product.ToLower(), pattern, Escapes)),
            "category" => negate
                ? q.Where(r => !EF.Functions.Like(r.Category.ToLower(), pattern, Escapes))
                : q.Where(r => EF.Functions.Like(r.Category.ToLower(), pattern, Escapes)),
            "region" => negate
                ? q.Where(r => !EF.Functions.Like(r.Region.ToLower(), pattern, Escapes))
                : q.Where(r => EF.Functions.Like(r.Region.ToLower(), pattern, Escapes)),
            "status" => negate
                ? q.Where(r => !EF.Functions.Like(r.Status.ToLower(), pattern, Escapes))
                : q.Where(r => EF.Functions.Like(r.Status.ToLower(), pattern, Escapes)),
            _ => q,
        };
    }

    private const string Escapes = "\\";

    /// <summary>
    /// LIKE without wildcards is an equality test that ignores case in SQLite,
    /// which is exactly what is wanted here — so anything in the value that LIKE
    /// would read as a wildcard is escaped first.
    /// </summary>
    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public static IQueryable<SalesRow> Order(IQueryable<SalesRow> q, string sort, bool descending) =>
        sort.Trim().ToLowerInvariant() switch
        {
            "date" => descending ? q.OrderByDescending(r => r.Date) : q.OrderBy(r => r.Date),
            "orderid" => descending ? q.OrderByDescending(r => r.OrderId) : q.OrderBy(r => r.OrderId),
            "customer" => descending ? q.OrderByDescending(r => r.Customer) : q.OrderBy(r => r.Customer),
            "product" => descending ? q.OrderByDescending(r => r.Product) : q.OrderBy(r => r.Product),
            "category" => descending ? q.OrderByDescending(r => r.Category) : q.OrderBy(r => r.Category),
            "qty" => descending ? q.OrderByDescending(r => r.Qty) : q.OrderBy(r => r.Qty),
            "price" => descending ? q.OrderByDescending(r => r.Price) : q.OrderBy(r => r.Price),
            "region" => descending ? q.OrderByDescending(r => r.Region) : q.OrderBy(r => r.Region),
            "status" => descending ? q.OrderByDescending(r => r.Status) : q.OrderBy(r => r.Status),
            _ => descending ? q.OrderByDescending(r => r.Revenue) : q.OrderBy(r => r.Revenue),
        };
}
