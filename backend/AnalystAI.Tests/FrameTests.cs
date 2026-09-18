using AnalystAI.Api.Analysis;
using AnalystAI.Api.Services;

namespace AnalystAI.Tests;

public class CellTests
{
    [Theory]
    [InlineData("1,250", 1250)]
    [InlineData("$1,250.50", 1250.5)]
    [InlineData("87%", 87)]
    [InlineData("Rs 400", 400)]
    [InlineData("-$5", -5)]
    public void Reads_numbers_as_spreadsheets_write_them(string raw, double expected)
    {
        Assert.True(Cells.TryNumber(raw, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("2026-01-05")]
    [InlineData("S001")]
    [InlineData("")]
    public void Does_not_read_other_things_as_numbers(string raw) => Assert.False(Cells.TryNumber(raw, out _));

    [Theory]
    [InlineData("")]
    [InlineData("N/A")]
    [InlineData("null")]
    [InlineData(" - ")]
    public void Treats_placeholders_as_missing(string raw) => Assert.True(Cells.IsMissing(raw));
}

public class ColumnTests
{
    private readonly Frame _students = Files.Read(Files.Students());

    private Column Col(string name) => _students.Columns.Single(c => c.Name == name);

    [Fact]
    public void An_id_column_is_an_identifier_and_is_never_measured_or_grouped()
    {
        var id = Col("student_id");
        Assert.True(id.Identifier);
        Assert.Equal("identifier", id.Role);
        Assert.False(id.CanMeasure);
        Assert.False(id.CanGroupBy);
    }

    [Fact]
    public void Marks_are_a_measure_and_subjects_a_grouping()
    {
        Assert.True(Col("marks").CanMeasure);
        Assert.Equal("group", Col("subject").Role);
        Assert.True(Col("subject").CanGroupBy);
        Assert.Equal(["English", "Mathematics", "Physics"], Col("subject").FilterOptions());
    }

    [Fact]
    public void Currency_columns_keep_their_symbol()
    {
        var sales = Files.Read(Files.Sales());
        var price = sales.Columns.Single(c => c.Name == "Unit Price");
        Assert.True(price.IsNumber);
        Assert.Equal("$", Unit.For(price).Prefix);
    }
}

public class FrameQueryTests
{
    private readonly Frame _students = Files.Read(Files.Students());

    private Column Col(string name) => _students.Columns.Single(c => c.Name == name);

    [Fact]
    public void Filters_compare_numbers_by_value_and_text_ignoring_case()
    {
        var rows = FrameQuery.Filter(_students,
        [
            new CellFilter(Col("marks"), "lt", "50"),
            new CellFilter(Col("subject"), "eq", "physics"),
        ]);

        // Physics marks are 45, 50, 55, 60: only the first is below 50.
        Assert.Single(rows);
        Assert.Equal("Ali Khan", Col("name").Values[rows[0]]);
    }

    [Fact]
    public void Sorting_by_a_number_column_is_numeric_with_blanks_last()
    {
        var frame = Files.Read("name,score\na,9\nb,\nc,88\nd,10\n");
        var rows = Enumerable.Range(0, frame.RowCount).ToList();
        FrameQuery.Sort(rows, frame.Columns[1], descending: true);

        Assert.Equal(["c", "d", "a", "b"], rows.Select(r => frame.Columns[0].Values[r]));
    }

    [Fact]
    public void Grouping_averages_by_subject_and_ranks_largest_first()
    {
        var all = Enumerable.Range(0, _students.RowCount).ToList();
        var grouped = FrameQuery.Group(_students, all, Col("subject"), null, Col("marks"), "avg", 10);

        Assert.Equal(["English", "Mathematics", "Physics"], grouped.Figures.Select(f => f.Label));
        Assert.Equal(52.5, grouped.Figures[^1].Value);
        Assert.Equal(3, grouped.GroupCount);
    }

    [Fact]
    public void Dates_group_into_iso_weeks()
    {
        var frame = Files.Read(Files.Sales());
        var date = frame.Columns.Single(c => c.Name == "Order Date");
        var grouped = FrameQuery.Group(frame, Enumerable.Range(0, frame.RowCount).ToList(), date, "week", null, "count", 50);

        Assert.Equal("2026-W02", grouped.Figures[0].Label);
        Assert.Equal(6, grouped.Figures.Sum(f => f.Value));
    }
}
