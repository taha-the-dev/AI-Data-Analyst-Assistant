using AnalystAI.Api.Query;

namespace AnalystAI.Api.Analysis;

/// <summary>Orders, products, customers and the money they bring in.</summary>
internal static class Sales
{
    private static readonly string[] OrderWords = ["order", "orderid", "invoice", "transaction", "receipt", "bill"];
    private static readonly string[] QtyWords = ["qty", "quantity", "units", "volume", "pieces", "pcs"];
    private static readonly string[] PriceWords = ["price", "unitprice", "rate", "mrp"];
    private static readonly string[] DiscountWords = ["discount"];
    private static readonly string[] MarginWords = ["margin"];
    private static readonly string[] ProfitWords = ["profit", "earnings", "gain"];
    private static readonly string[] RevenueWords = ["revenue", "sales", "sale", "amount", "total", "turnover", "gmv", "gross", "net", "value", "income", "subtotal"];
    private static readonly string[] CustomerWords = ["customer", "client", "buyer", "account", "company", "shopper"];
    private static readonly string[] ProductWords = ["product", "item", "sku", "service", "model"];
    private static readonly string[] CategoryWords = ["category", "segment", "type", "department", "class", "group", "line", "brand", "subcategory"];
    private static readonly string[] RegionWords = ["region", "country", "state", "city", "market", "territory", "area", "store", "branch", "location", "zone"];
    private static readonly string[] StatusWords = ["status", "stage", "fulfilment", "fulfillment", "shipment", "delivery"];
    private static readonly string[] ChannelWords = ["channel", "source", "payment", "method", "platform"];
    private static readonly string[] DateWords = ["order", "date", "invoice", "purchase", "sale", "transaction"];

    public static void Build(Board b)
    {
        var f = b.Frame;

        var order = b.Claim(b.Find(OrderWords, c => !c.IsDate && (c.Identifier || c.IsLabel)), "Order ID");
        var qty = b.Claim(b.Find(QtyWords, c => c.IsNumber && !c.Identifier), "Quantity");
        var price = b.Claim(b.Find(PriceWords, c => c.IsNumber && !c.Identifier), "Unit price");
        b.Claim(b.Find(DiscountWords, c => c.IsNumber), "Discount");
        var margin = b.Claim(b.Find(MarginWords, c => c.IsNumber), "Margin");
        var profit = b.Claim(b.Find(ProfitWords, c => c.IsNumber && !c.Percent), "Profit");
        var revenueColumn = b.Claim(b.Find(RevenueWords, c => c.IsNumber && !c.Identifier && !c.Percent), "Revenue");
        var customer = b.Claim(b.Find(CustomerWords, c => c.IsLabel), "Customer");
        var product = b.Claim(b.Find(ProductWords, c => c.IsLabel && !c.Identifier), "Product");
        var category = b.Claim(b.Find(CategoryWords, c => c.IsGroup(40)), "Category");
        var region = b.Claim(b.Find(RegionWords, c => c.IsGroup(60)), "Region");
        var status = b.Claim(b.Find(StatusWords, c => c.IsGroup(15)), "Status");
        var channel = b.Claim(b.Find(ChannelWords, c => c.IsGroup(15)), "Channel");
        var date = b.Claim(b.Find(DateWords, c => c.IsDate) ?? f.Date, "Date");

        Measure? revenue = revenueColumn is not null ? Measure.Of(revenueColumn) with { Name = revenueColumn.Name } : null;
        var derived = false;
        if (revenue is null && qty is not null && price is not null)
        {
            // No revenue column: the only honest substitute is the product of two columns the file does have.
            var values = new double?[f.RowCount];
            for (var r = 0; r < f.RowCount; r++)
                if (qty.Numbers[r] is { } q && price.Numbers[r] is { } p) values[r] = q * p;
            revenue = new Measure("Revenue", values, Measure.Of(price).Unit with { Decimals = 0 });
            derived = true;
        }

        var units = qty is null ? null : Measure.Of(qty);
        var primary = revenue ?? units;
        if (primary is null) return;

        var total = primary.Present.Sum();
        var orders = order?.Distinct ?? f.RowCount;
        var byMonth = date is null ? [] : f.Series(date, primary, Agg.Sum);
        var change = DatasetAnalyzer.Change(byMonth);
        var productTotals = product is null ? [] : f.Group(product, primary, Agg.Sum, 1000);
        var money = primary.Unit with { Decimals = 0 };

        // ── Tiles ──
        b.Kpi(revenue is null ? $"Total {primary.Title}" : Title(revenue), Fmt.Compact(total, money),
            revenue is null ? "inventory_2" : "payments", derived ? "quantity × price" : null,
            change is null ? null : (change.Value.Text, change.Value.Tone));

        b.Kpi(order is null ? "Transactions" : "Orders", Fmt.Int(orders), "receipt_long", order is null ? "one per row" : null);

        if (revenue is not null && orders > 0)
            b.Kpi(order is null ? "Avg Sale Value" : "Avg Order Value", Fmt.Compact(total / orders, primary.Unit with { Decimals = 2 }), "shopping_cart");

        if (change is { } c && byMonth.Count >= 2)
            b.Kpi("Sales Growth", c.Text, c.Tone == "down" ? "trending_down" : "trending_up",
                $"{Fmt.Period(byMonth[^1].Label)} vs {Fmt.Period(byMonth[^2].Label)}", iconTone: c.Tone == "down" ? "error" : null);

        if (productTotals.Count >= 2)
            b.Kpi($"Top {Fmt.Title(product!.Name)}", productTotals[0].Label, "star",
                $"{DatasetAnalyzer.Share(productTotals[0].Value, total)} of {primary.Lower}");

        if (profit is not null)
        {
            var profitSum = Measure.Of(profit).Present.Sum();
            b.Kpi("Profit", Fmt.Compact(profitSum, Measure.Of(profit).Unit with { Decimals = 0 }), "account_balance_wallet",
                revenue is not null && total > 0 ? $"{Fmt.Pct(profitSum / total)} margin" : null);
        }
        else if (margin is not null)
        {
            var m = Measure.Of(margin);
            var avg = m.Present.Average();
            b.Kpi("Avg Margin", m.Unit.Suffix == "%" || avg > 1 ? Fmt.Value(avg, m.Unit.ForAverage with { Suffix = "%" }) : Fmt.Pct(avg), "percent");
        }

        if (customer is not null) b.Kpi("Customers", Fmt.Int(customer.Distinct), "person");
        if (units is not null && revenue is not null) b.Kpi("Units Sold", Fmt.Compact(units.Present.Sum(), Unit.Count), "inventory_2");

        // ── Charts ──
        if (date is not null)
            b.Chart($"series:{primary.Name}", $"{Title(primary)} over time", "line", byMonth, money, "Total per period");

        if (category is not null)
            b.Chart($"Sum:{primary.Name}:{category.Name}", $"{Title(primary)} by {Fmt.Lower(category.Name)}", "bars", f.Group(category, primary, Agg.Sum, 8), money);

        // The Top Products table already ranks them; the chart earns its place only when there is no category to show instead.
        if (productTotals.Count >= 2 && category is null)
            b.Chart($"Sum:{primary.Name}:{product!.Name}", $"{Fmt.Title(product.Name)} performance", "bars", productTotals.Take(8).ToList(), money, $"Top {Math.Min(8, productTotals.Count)} by {primary.Lower}");

        if (region is not null)
            b.Chart($"Sum:{primary.Name}:{region.Name}", $"{Title(primary)} by {Fmt.Lower(region.Name)}", region.Distinct <= 6 ? "donut" : "bars", f.Group(region, primary, Agg.Sum, 8), money);

        if (date is not null)
            b.Chart($"volume:{date.Name}", order is null ? "Transaction volume" : "Order volume", "columns",
                order is null ? f.Series(date, null, Agg.Count) : DistinctPerPeriod(f, date, order), Unit.Count,
                order is null ? "Rows per period" : "Distinct orders per period");

        if (status is not null)
            b.Chart($"count:{status.Name}", $"{Fmt.Title(order is null ? "Transactions" : "Orders")} by {Fmt.Lower(status.Name)}", "donut", f.Group(status, null, Agg.Count, 6), Unit.Count);

        if (channel is not null)
            b.Chart($"Sum:{primary.Name}:{channel.Name}", $"{Title(primary)} by {Fmt.Lower(channel.Name)}", "bars", f.Group(channel, primary, Agg.Sum, 8), money);

        // ── Table ──
        var lead = product ?? customer ?? category ?? region;
        if (lead is not null)
            b.Table = LeaderTable(f, lead, lead == product ? "Top Products" : lead == customer ? "Top Customers" : $"Top {Fmt.Plural(Fmt.Title(lead.Name))}",
                primary, units == primary ? null : units, order, money, total);

        // ── Insights ──
        if (category is not null)
        {
            var cats = f.Group(category, primary, Agg.Sum, 100);
            if (cats.Count >= 2)
                b.Insight(1, "positive", "leaderboard", $"Leading {Fmt.Lower(category.Name)}",
                    $"{cats[0].Label} leads {primary.Lower} at {Fmt.Compact(cats[0].Value, money)}, {DatasetAnalyzer.Share(cats[0].Value, total)} of the " +
                    $"{Fmt.Compact(total, money)} total; {cats[^1].Label} contributes the least ({DatasetAnalyzer.Share(cats[^1].Value, total)}).");
        }

        if (change is { } move && byMonth.Count >= 2)
        {
            var peak = byMonth.MaxBy(x => x.Value)!;
            b.Insight(2, move.Tone == "down" ? "alert" : "positive", move.Tone == "down" ? "trending_down" : "trending_up", "Direction of travel",
                $"{Title(primary)} {(move.Percent >= 0 ? "rose" : "fell")} {Math.Abs(move.Percent):0.0}% in {Fmt.Period(byMonth[^1].Label)} against " +
                $"{Fmt.Period(byMonth[^2].Label)}. {Fmt.Period(peak.Label)} is the strongest period at {Fmt.Compact(peak.Value, money)}.");
        }

        if (productTotals.Count >= 2)
            b.Insight(3, "neutral", "star", $"Best-selling {Fmt.Lower(product!.Name)}",
                $"{productTotals[0].Label} brings in {Fmt.Compact(productTotals[0].Value, money)} ({DatasetAnalyzer.Share(productTotals[0].Value, total)}), " +
                $"ahead of {productTotals[1].Label} at {Fmt.Compact(productTotals[1].Value, money)}.");

        if (region is not null)
        {
            var regions = f.Group(region, primary, Agg.Sum, 100);
            if (regions.Count >= 2)
                b.Insight(4, "neutral", "public", $"{Fmt.Title(region.Name)} concentration",
                    $"{regions[0].Label} accounts for {DatasetAnalyzer.Share(regions[0].Value, total)} of {primary.Lower}; " +
                    $"{regions[^1].Label} for {DatasetAnalyzer.Share(regions[^1].Value, total)}.");
        }

        if (status is not null)
        {
            var lost = f.Group(status, null, Agg.Count, 50)
                .Where(s => new[] { "cancel", "return", "refund", "fail", "reject" }.Any(w => s.Label.Contains(w, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (lost.Count > 0)
            {
                var count = lost.Sum(s => s.Value);
                b.Insight(5, "alert", "undo", "Lost orders",
                    $"{Fmt.Int(count)} rows ({DatasetAnalyzer.Share(count, f.RowCount)}) are {string.Join(" or ", lost.Select(s => s.Label.ToLowerInvariant()))}.");
            }
        }
    }

    private static string Title(Measure m) => m.Title;

    private static List<Figure> DistinctPerPeriod(Frame f, Column date, Column order)
    {
        var series = f.Series(date, null, Agg.Count);
        if (series.Count == 0) return series;

        // Series decides the grain; re-key each row the same way by matching its prefix.
        var width = series[0].Label.Length;
        var sets = series.ToDictionary(s => s.Label, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        for (var r = 0; r < f.RowCount; r++)
        {
            if (date.Dates[r] is not { } d || order.Values[r].Length == 0) continue;
            var key = d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)[..Math.Min(width, 10)];
            if (sets.TryGetValue(key, out var set)) set.Add(order.Values[r]);
        }
        return series.Select(s => new Figure(s.Label, sets[s.Label].Count)).ToList();
    }

    private static Contracts.DataTableDto LeaderTable(Frame f, Column lead, string title, Measure primary, Measure? units, Column? order, Unit money, double total)
    {
        var rows = new Dictionary<string, (double Value, double Units, HashSet<string> Orders, int Rows)>(StringComparer.OrdinalIgnoreCase);
        for (var r = 0; r < f.RowCount; r++)
        {
            if (lead.Values[r].Length == 0) continue;
            var key = lead.Values[r];
            if (!rows.TryGetValue(key, out var row)) row = (0, 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);
            row.Value += primary.Values[r] ?? 0;
            row.Units += units?.Values[r] ?? 0;
            if (order is not null && order.Values[r].Length > 0) row.Orders.Add(order.Values[r]);
            row.Rows++;
            rows[key] = row;
        }

        var columns = new List<(string, string)> { (Fmt.Title(lead.Name), "left"), (primary.Title, "right") };
        if (units is not null) columns.Add(("Units", "right"));
        columns.Add((order is null ? "Rows" : "Orders", "right"));
        columns.Add(("Share", "right"));

        var top = rows.OrderByDescending(kv => kv.Value.Value).Take(8).ToList();
        return DatasetAnalyzer.Table(title, $"{top.Count} of {Fmt.Int(rows.Count)} by {primary.Lower}", columns,
            top.Select(kv =>
            {
                var cells = new List<string> { kv.Key, Fmt.Value(kv.Value.Value, money) };
                if (units is not null) cells.Add(Fmt.Int(kv.Value.Units));
                cells.Add(Fmt.Int(order is null ? kv.Value.Rows : kv.Value.Orders.Count));
                cells.Add(DatasetAnalyzer.Share(kv.Value.Value, total));
                return cells.ToArray();
            }));
    }
}
