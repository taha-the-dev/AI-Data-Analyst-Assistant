using AnalystAI.Api.Contracts;
using AnalystAI.Api.Query;

namespace AnalystAI.Api.Analysis;

/// <summary>
/// The dashboard as it is being assembled. Every add is guarded: a chart with
/// nothing to draw, a tile past the sixth, or a second chart of the same figures
/// is refused here, so no recipe can put an empty or repeated panel on screen.
/// </summary>
internal sealed class Board(Frame frame)
{
    public const int MaxKpis = 6;
    public const int MaxCharts = 4;
    public const int MaxInsights = 5;

    public Frame Frame { get; } = frame;
    public List<KpiDto> Kpis { get; } = [];
    public List<ChartDto> Charts { get; } = [];
    public DataTableDto? Table { get; set; }
    public List<FieldRoleDto> Fields { get; } = [];

    private readonly List<(int Priority, int Order, InsightDto Insight)> _insights = [];
    private readonly HashSet<Column> _claimed = [];

    public bool Claimed(Column column) => _claimed.Contains(column);

    /// <summary>Records what a column was taken to mean, so no other slot reuses it.</summary>
    public Column? Claim(Column? column, string role)
    {
        if (column is null || !_claimed.Add(column)) return column;
        Fields.Add(new FieldRoleDto(column.Name, role));
        return column;
    }

    /// <summary>The unclaimed column whose header best matches, among those <paramref name="accept"/> allows.</summary>
    public Column? Find(string[] words, Func<Column, bool> accept) =>
        Frame.Columns
            .Where(c => !_claimed.Contains(c) && c.Present > 0 && accept(c))
            .Select(c => (Column: c, Score: c.Match(words)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Column.Index)
            .Select(x => x.Column)
            .FirstOrDefault();

    public bool Full => Kpis.Count >= MaxKpis;

    public void Kpi(string label, string value, string icon, string? hint = null,
        (string Text, string Tone)? delta = null, string? iconTone = null)
    {
        if (Full || Kpis.Any(k => k.Label.Equals(label, StringComparison.OrdinalIgnoreCase))) return;
        Kpis.Add(new KpiDto(label, value, icon, delta?.Text, delta?.Tone, iconTone, hint));
    }

    public bool HasChart(string id) => Charts.Any(c => c.Id == id);

    public void Chart(string id, string title, string kind, IReadOnlyList<Figure> figures, Unit unit, string? caption = null)
    {
        if (Charts.Count >= MaxCharts || HasChart(id)) return;
        // One bar, or every bar at zero, is not a chart.
        if (figures.Count < 2 || figures.All(f => Math.Abs(f.Value) < 1e-12)) return;
        Charts.Add(new ChartDto(id, title, kind, caption, figures, new ValueUnitDto(unit.Prefix, unit.Suffix, unit.Decimals)));
    }

    /// <summary>Lower priority numbers are shown first.</summary>
    public void Insight(int priority, string tone, string icon, string title, string body)
    {
        if (_insights.Any(i => i.Insight.Title == title)) return;
        _insights.Add((priority, _insights.Count, new InsightDto(tone, icon, title, body)));
    }

    public int InsightCount => _insights.Count;

    public IReadOnlyList<InsightDto> Insights(InsightDto? quality)
    {
        var chosen = _insights
            .OrderBy(i => i.Priority).ThenBy(i => i.Order)
            .Take(quality is null ? MaxInsights : MaxInsights - 1)
            .Select(i => i.Insight)
            .ToList();
        if (quality is not null) chosen.Add(quality);
        return chosen;
    }
}
