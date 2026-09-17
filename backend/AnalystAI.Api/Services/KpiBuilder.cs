using System.Globalization;
using AnalystAI.Api.Contracts;
using AnalystAI.Api.Models;
using AnalystAI.Api.Query;

namespace AnalystAI.Api.Services;

/// <summary>
/// Builds the Analytics KPI strip from computed figures.
///
/// The deltas used to be constants baked into the endpoint, which meant the
/// only invented numbers in the whole service sat on its most prominent screen.
/// Every delta here is the last complete period against the one before it,
/// taken from the same series the chart underneath is drawn from, and a series
/// too short to support a comparison carries no delta at all.
/// </summary>
public static class KpiBuilder
{
    public static IReadOnlyList<KpiDto> ForRevenue(
        double totalRevenue,
        int orderCount,
        int inProcessing,
        IReadOnlyList<Figure> revenueByMonth,
        IReadOnlyList<Figure> ordersByMonth)
    {
        var revenueChange = Change(revenueByMonth);
        var orderChange = Change(ordersByMonth);
        var aovChange = Change(PerOrder(revenueByMonth, ordersByMonth));
        var average = orderCount == 0 ? 0 : totalRevenue / orderCount;

        return
        [
            new("Revenue", Compact(totalRevenue), "payments", revenueChange?.Text, revenueChange?.Tone),
            new("Orders", orderCount.ToString("N0", CultureInfo.InvariantCulture), "receipt_long",
                orderChange?.Text, orderChange?.Tone),
            new("Avg Order Value", "$" + average.ToString("N2", CultureInfo.InvariantCulture), "trending_up",
                aovChange?.Text, aovChange?.Tone),
            new("In Processing",
                orderCount == 0 ? "0%" : $"{inProcessing / (double)orderCount * 100:0.0}%", "pending"),
        ];
    }

    /// <summary>Revenue per order, month by month — the series behind the AOV delta.</summary>
    private static List<Figure> PerOrder(IReadOnlyList<Figure> revenue, IReadOnlyList<Figure> orders)
    {
        var byLabel = orders.ToDictionary(o => o.Label, o => o.Value);
        return revenue
            .Where(r => byLabel.TryGetValue(r.Label, out var count) && count > 0)
            .Select(r => new Figure(r.Label, r.Value / byLabel[r.Label]))
            .ToList();
    }

    /// <summary>The last period against the one before it, or null if that cannot be said.</summary>
    private static (string Text, string Tone)? Change(IReadOnlyList<Figure> series)
    {
        if (series.Count < 2) return null;

        var last = series[^1].Value;
        var previous = series[^2].Value;
        if (previous == 0) return null;

        var percent = (last - previous) / Math.Abs(previous) * 100;
        return ($"{(percent >= 0 ? "+" : "")}{percent.ToString("0.0", CultureInfo.InvariantCulture)}%",
                percent >= 0 ? "up" : "down");
    }

    public static string Compact(double value) => value switch
    {
        >= 1_000_000 => "$" + (value / 1_000_000).ToString("0.00", CultureInfo.InvariantCulture) + "M",
        >= 1_000 => "$" + (value / 1_000).ToString("0.0", CultureInfo.InvariantCulture) + "K",
        _ => "$" + value.ToString("0", CultureInfo.InvariantCulture),
    };
}
