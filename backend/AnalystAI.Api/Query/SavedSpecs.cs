namespace AnalystAI.Api.Query;

/// <summary>
/// The specs the product ships with. Dashboard, Analytics and the report writer
/// all read from this one list, so a change to what "revenue by month" means
/// lands on every screen at once instead of on whichever endpoint was edited.
/// </summary>
public static class SavedSpecs
{
    public static QuerySpec RevenueByMonth() => new()
    {
        Intent = "trend", GroupBy = "date", Metric = "revenue", Aggregate = "sum",
        TimeBucket = "month", Sort = "label asc", Limit = 24, Chart = "line",
        Title = "Revenue by month",
    };

    public static QuerySpec OrdersByMonth() => new()
    {
        Intent = "trend", GroupBy = "date", Aggregate = "count",
        TimeBucket = "month", Sort = "label asc", Limit = 24, Chart = "line",
        Title = "Orders by month",
    };

    public static QuerySpec RevenueByCategory() => new()
    {
        Intent = "aggregate", GroupBy = "category", Metric = "revenue", Aggregate = "sum",
        Sort = "value desc", Limit = 10, Chart = "bar", Title = "Revenue by category",
    };

    public static QuerySpec RevenueByRegion() => new()
    {
        Intent = "aggregate", GroupBy = "region", Metric = "revenue", Aggregate = "sum",
        Sort = "value desc", Limit = 10, Chart = "donut", Title = "Revenue by region",
    };

    public static QuerySpec TopCustomers(int limit = 5) => new()
    {
        Intent = "aggregate", GroupBy = "customer", Metric = "revenue", Aggregate = "sum",
        Sort = "value desc", Limit = limit, Chart = "bar", Title = "Top customers by revenue",
    };

    public static QuerySpec TopProducts(int limit = 5) => new()
    {
        Intent = "aggregate", GroupBy = "product", Metric = "revenue", Aggregate = "sum",
        Sort = "value desc", Limit = limit, Chart = "bar", Title = "Top products by revenue",
    };
}
