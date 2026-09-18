using AnalystAI.Api.Analysis;

namespace AnalystAI.Tests;

public class AnalyzerTests
{
    private static readonly string[] SalesWords = ["Revenue", "Order", "Product", "Sales"];

    [Fact]
    public void A_mark_sheet_gets_a_student_dashboard_and_no_sales_figures()
    {
        var dashboard = DatasetAnalyzer.Analyze(Files.Read(Files.Students()));

        Assert.Equal("education", dashboard.Domain);
        Assert.Contains(dashboard.Kpis, k => k.Label == "Total Students" && k.Value == "4");
        Assert.Contains(dashboard.Kpis, k => k.Label == "Average Marks");
        Assert.Contains(dashboard.Kpis, k => k.Label == "Pass Rate");
        Assert.Equal("Student Performance", dashboard.Table?.Title);
        Assert.DoesNotContain(dashboard.Kpis, k => SalesWords.Any(w => k.Label.Contains(w)));
        Assert.Contains(dashboard.Insights, i => i.Body.Contains("Physics has the lowest average marks"));
    }

    [Fact]
    public void An_order_export_derives_revenue_from_quantity_and_price()
    {
        var dashboard = DatasetAnalyzer.Analyze(Files.Read(Files.Sales()));

        Assert.Equal("sales", dashboard.Domain);
        // 2×10 + 1×50 + 3×100 + 5×10 + 1×100 + 2×50 = 620
        var revenue = Assert.Single(dashboard.Kpis, k => k.Label == "Revenue");
        Assert.Equal("$620", revenue.Value);
        Assert.Equal("Top Products", dashboard.Table?.Title);
    }

    [Fact]
    public void An_hr_file_gets_workforce_figures()
    {
        var dashboard = DatasetAnalyzer.Analyze(Files.Read(Files.Employees()));

        Assert.Equal("hr", dashboard.Domain);
        Assert.Contains(dashboard.Kpis, k => k.Label == "Employees" && k.Value == "6");
        Assert.Contains(dashboard.Kpis, k => k.Label == "Attrition Rate" && k.Value == "33.3%");
        Assert.Equal("Employee Overview", dashboard.Table?.Title);
    }

    [Fact]
    public void An_unrecognised_file_is_described_by_its_own_columns()
    {
        var dashboard = DatasetAnalyzer.Analyze(Files.Read(Files.Weather()));

        Assert.Equal("general", dashboard.Domain);
        Assert.Contains(dashboard.Kpis, k => k.Label == "Avg Temperature (°C)");
        Assert.DoesNotContain(dashboard.Kpis, k => k.Value.StartsWith('$'));
    }

    [Fact]
    public void Data_quality_counts_gaps_and_repeats_and_never_rounds_up_to_perfect()
    {
        var quality = DatasetAnalyzer.Analyze(Files.Read(Files.Weather())).Quality;

        Assert.Equal(1, quality.DuplicateRows);
        Assert.True(quality.Score < 100);

        var gappy = DatasetAnalyzer.Analyze(Files.Read("a,b\n1,x\n2,\n3,z\n")).Quality;
        Assert.Equal(1, gappy.MissingCells);
        Assert.Equal("b", Assert.Single(gappy.Issues).Column);
    }

    [Fact]
    public void Every_chart_has_something_to_draw()
    {
        foreach (var csv in new[] { Files.Students(), Files.Sales(), Files.Employees(), Files.Weather() })
            Assert.All(DatasetAnalyzer.Analyze(Files.Read(csv)).Charts, chart =>
            {
                Assert.True(chart.Figures.Count >= 2);
                Assert.Contains(chart.Figures, f => f.Value != 0);
            });
    }
}
