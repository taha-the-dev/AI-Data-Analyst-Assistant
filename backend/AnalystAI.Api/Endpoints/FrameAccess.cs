using AnalystAI.Api.Analysis;
using AnalystAI.Api.Contracts;
using AnalystAI.Api.Data;
using AnalystAI.Api.Models;
using AnalystAI.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AnalystAI.Api.Endpoints;

/// <summary>
/// What every screen that reads the file's own columns does first: resolve the
/// dataset, load its frame, and say plainly why when either is missing.
/// </summary>
internal static class FrameAccess
{
    public sealed record Loaded(Dataset Dataset, Frame Frame);

    public static async Task<(Loaded? Loaded, IResult? Problem)> LoadAsync(
        AppDbContext db, SourceStore sources, IDatasetContext context, int? datasetId, CancellationToken ct)
    {
        var id = await context.ResolveAsync(datasetId, ct);
        if (id is null) return (null, Problems.NoDataset(datasetId));

        var dataset = await db.Datasets.AsNoTracking().FirstAsync(d => d.Id == id, ct);
        var frame = await sources.LoadAsync(dataset, ct);
        return frame is null ? (null, Problems.NoSource(dataset.Name)) : (new Loaded(dataset, frame), null);
    }

    /// <summary>Parses repeated <c>filter=c3:gt:50</c> rules, or explains the first one that is wrong.</summary>
    public static (List<CellFilter> Filters, IResult? Problem) ParseFilters(Frame frame, IEnumerable<string?> raw)
    {
        var filters = new List<CellFilter>();
        foreach (var rule in raw)
        {
            if (string.IsNullOrWhiteSpace(rule)) continue;

            var parts = rule.Split(':', 3);
            if (parts.Length != 3 || !FrameQuery.Ops.Contains(parts[1]))
                return ([], Problems.BadRequest(
                    "Malformed filter",
                    $"'{rule}' is not valid. Use filter=column:op:value with op one of {string.Join(", ", FrameQuery.Ops)}, for example filter=c3:gt:50."));

            if (FrameQuery.Find(frame, parts[0]) is not { } column)
                return ([], UnknownColumn(frame, "filter column", parts[0]));

            filters.Add(new CellFilter(column, parts[1], parts[2]));
        }
        return (filters, null);
    }

    public static IResult UnknownColumn(Frame frame, string what, string value) =>
        Problems.UnknownColumn(what, value, frame.Columns.Select(c => $"{FrameQuery.Key(c)} ({c.Name})"));

    public static string Role(Column c) =>
        c.Identifier ? "identifier" : c.IsNumber ? "number" : c.IsDate ? "date" : c.IsGroup(50) ? "group" : "text";

    public static ColumnUnitDto Unit(Column c)
    {
        var unit = Analysis.Unit.For(c);
        return new ColumnUnitDto(unit.Prefix, unit.Suffix, unit.Decimals);
    }

    public static ExplorerColumnDto Describe(Column c)
    {
        var role = Role(c);
        IReadOnlyList<string>? options = role == "group" || (c.IsNumber && !c.Identifier && c.Distinct <= 12)
            ? c.Values.Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => Cells.TryNumber(v, out var n) ? n : double.MaxValue)
                .ThenBy(v => v, StringComparer.OrdinalIgnoreCase)
                .Take(50).ToList()
            : null;

        return new ExplorerColumnDto(FrameQuery.Key(c), c.Name, c.Kind, role, c.IsNumber ? "right" : "left", c.IsNumber,
            c.Distinct, c.Missing, Unit(c), options);
    }
}
