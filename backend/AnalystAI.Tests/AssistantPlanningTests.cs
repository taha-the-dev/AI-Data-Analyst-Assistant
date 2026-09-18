using System.Text.Json.Nodes;
using AnalystAI.Api.Analysis;
using AnalystAI.Api.Query;

namespace AnalystAI.Tests;

/// <summary>
/// The assistant's path from a question to figures, without HTTP: schema →
/// keyword plan → validation → engine. The keyword planner is the built-in
/// adapter at the planner seam, so these are the answers anyone without a model
/// key gets.
/// </summary>
public class AssistantPlanningTests
{
    private static (Frame Frame, DatasetSchema Schema) Load(string csv)
    {
        var frame = Files.Read(csv);
        return (frame, DatasetSchema.From(frame, DatasetAnalyzer.Analyze(frame)));
    }

    private static QueryResult Ask(string csv, string question, params string[] earlier)
    {
        var (frame, schema) = Load(csv);
        var spec = SpecValidator.Normalize(new KeywordPlanner().Plan(question, earlier, schema), schema);
        Assert.NotNull(spec);
        return FrameEngine.Run(frame, schema, spec);
    }

    [Fact]
    public void The_schema_says_what_the_file_is_about()
    {
        var (_, schema) = Load(Files.Students());

        Assert.Equal("marks", schema.DefaultMetric);
        Assert.Equal("subject", schema.DefaultDimension);
        Assert.Contains("Mathematics, Physics", schema.Describe().Replace("English, ", ""));
    }

    [Fact]
    public void Average_marks_by_subject()
    {
        var result = Ask(Files.Students(), "What is the average marks by subject?");

        Assert.Equal("subject", result.Spec.GroupBy);
        Assert.Equal("avg", result.Spec.Aggregate);
        Assert.Equal("marks", result.MetricLabel);
        Assert.Equal("English", result.Figures[0].Label);
        Assert.Equal("", result.Unit.Prefix);
    }

    [Fact]
    public void A_count_by_another_column()
    {
        var result = Ask(Files.Students(), "How many records per grade?");

        Assert.Equal("grade", result.Spec.GroupBy);
        Assert.Equal("count", result.Spec.Aggregate);
        Assert.Null(result.MetricLabel);
        Assert.Equal(12, result.Figures.Sum(f => f.Value));
    }

    [Fact]
    public void A_value_named_in_the_question_becomes_a_filter()
    {
        var result = Ask(Files.Students(), "Average marks in Physics by name");

        var filter = Assert.Single(result.Spec.Filters);
        Assert.Equal(("subject", "Physics"), (filter.Column, filter.Value));
        Assert.Equal(4, result.RowsMatched);
    }

    [Fact]
    public void Short_values_only_filter_next_to_their_column()
    {
        // "a" here is an article, not grade A.
        var loose = Ask(Files.Students(), "Show me a breakdown of marks by subject");
        Assert.Empty(loose.Spec.Filters);

        var named = Ask(Files.Students(), "marks by subject for grade A");
        Assert.Equal(("grade", "A"), (named.Spec.Filters[0].Column, named.Spec.Filters[0].Value));
    }

    [Fact]
    public void Weakest_subjects_rank_lowest_first()
    {
        var result = Ask(Files.Students(), "Which subject is weakest on average marks?");

        Assert.Equal("value asc", result.Spec.Sort);
        Assert.Equal("Physics", result.Figures[0].Label);
    }

    [Fact]
    public void A_follow_up_keeps_the_grouping_and_adds_the_filter()
    {
        var result = Ask(Files.Students(), "what about Ayesha Noor?", "average marks by subject");

        Assert.Equal("subject", result.Spec.GroupBy);
        Assert.Contains(result.Spec.Filters, f => f.Column == "name" && f.Value == "Ayesha Noor");
    }

    [Fact]
    public void Sales_questions_use_the_derived_revenue_in_the_files_currency()
    {
        var result = Ask(Files.Sales(), "revenue by region");

        Assert.Equal("Region", result.Spec.GroupBy);
        Assert.Equal("Revenue", result.MetricLabel);
        Assert.Equal("$", result.Unit.Prefix);
        Assert.Equal(620, result.Total);
    }

    [Fact]
    public void A_trend_groups_by_the_date_column()
    {
        var result = Ask(Files.Sales(), "revenue by month");

        Assert.Equal("trend", result.Spec.Intent);
        Assert.Equal("Order Date", result.Spec.GroupBy);
        Assert.Equal(["2026-01", "2026-02", "2026-03"], result.Figures.Select(f => f.Label));
    }

    [Fact]
    public void The_explanation_only_states_computed_figures_in_the_right_unit()
    {
        var result = Ask(Files.Sales(), "revenue by region");
        var prose = new KeywordPlanner().Explain("revenue by region", result);

        Assert.Contains("$", prose);
        Assert.Contains("Computed from 6 of 6 rows", prose);

        var marks = Ask(Files.Students(), "average marks by subject");
        Assert.DoesNotContain("$", new KeywordPlanner().Explain("average marks by subject", marks));
    }

    [Fact]
    public void A_models_plan_naming_a_column_the_file_lacks_is_rejected()
    {
        var (_, schema) = Load(Files.Students());
        var raw = JsonNode.Parse("""{"groupBy":"region","metric":"revenue","aggregate":"sum"}""")!.AsObject();

        Assert.Null(SpecValidator.TryBuild(raw, schema));
    }

    [Fact]
    public void A_models_plan_is_repaired_where_it_can_be()
    {
        var (_, schema) = Load(Files.Students());
        var raw = JsonNode.Parse("""
            {"groupBy":"subject","metric":"revenue","aggregate":"mode","limit":500,
             "filters":[{"column":"grade","op":"~","value":"A"},{"column":"nope","op":"=","value":"x"}]}
            """)!.AsObject();

        var spec = SpecValidator.TryBuild(raw, schema);

        Assert.NotNull(spec);
        Assert.Equal("marks", spec.Metric);
        Assert.Equal("avg", spec.Aggregate);
        Assert.Equal(100, spec.Limit);
        var filter = Assert.Single(spec.Filters);
        Assert.Equal(("grade", "="), (filter.Column, filter.Op));
    }
}
