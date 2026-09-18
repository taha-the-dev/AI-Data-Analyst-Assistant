using AnalystAI.Api.Query;

namespace AnalystAI.Api.Contracts;

public record Paged<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total, int PageCount);

public record DatasetDto(
    int Id, string Name, string Type, string Icon,
    long Rows, int Columns, string Quality, DateTime UpdatedAt);

public record ColumnProfileDto(
    string Name, int Ordinal, string Kind, int Missing, int Distinct,
    double? Min, double? Max, double? Mean, double? StdDev, string[] Samples);

public record DatasetDetailDto(DatasetDto Dataset, IReadOnlyList<ColumnProfileDto> Columns);

public record UploadResultDto(
    DatasetDto Dataset,
    IReadOnlyList<ColumnProfileDto> Columns,
    long RowsImported);

/// <summary>
/// One tile. <see cref="Hint"/> says what the figure was computed from or
/// against — "score ≥ 50", "Mar 2026 vs Feb 2026" — when the label alone does not.
/// </summary>
public record KpiDto(string Label, string Value, string Icon, string? Delta = null, string? DeltaTone = null, string? IconTone = null, string? Hint = null);

public record InsightDto(string Tone, string Icon, string Title, string Body);

/// <summary>
/// A dashboard written for one file. Nothing in it is fixed: which tiles,
/// charts, table and insights appear, and what they are called, all follow from
/// the columns the file actually has. A figure that cannot be computed from the
/// file is left out rather than shown as zero.
/// </summary>
public record DashboardDto(
    /// <summary>education | sales | hr | general</summary>
    string Domain,
    string DomainLabel,
    string Summary,
    IReadOnlyList<FieldRoleDto> Fields,
    IReadOnlyList<KpiDto> Kpis,
    IReadOnlyList<ChartDto> Charts,
    DataTableDto? Table,
    IReadOnlyList<InsightDto> Insights,
    DataQualityDto Quality);

/// <summary>A column the analyser recognised, and what it took it to mean.</summary>
public record FieldRoleDto(string Column, string Role);

/// <summary>How a chart's values are written: a prefix such as "$", a suffix such as "%", and decimals.</summary>
public record ValueUnitDto(string Prefix, string Suffix, int Decimals);

/// <summary>Kind is line | bars | columns | donut.</summary>
public record ChartDto(string Id, string Title, string Kind, string? Caption, IReadOnlyList<Figure> Figures, ValueUnitDto Unit);

public record TableColumnDto(string Label, string Align);

public record DataTableDto(string Title, string? Caption, IReadOnlyList<TableColumnDto> Columns, IReadOnlyList<string[]> Rows);

public record ColumnIssueDto(string Column, int Missing, double Completeness);

public record DataQualityDto(
    long Rows,
    int Columns,
    long MissingCells,
    int DuplicateRows,
    double Completeness,
    int Score,
    string Grade,
    IReadOnlyList<ColumnIssueDto> Issues);

/// <summary>
/// One column of the uploaded file. Key is its position ("c0", "c1"…), since
/// headers can repeat or be blank. Role is identifier | number | date | group | text.
/// Options lists the values of a grouping column, for filter pickers.
/// </summary>
public record ExplorerColumnDto(
    string Key, string Label, string Kind, string Role, string Align, bool Numeric,
    int Distinct, int Missing, ValueUnitDto Unit, IReadOnlyList<string>? Options);

/// <summary>The earliest and latest value of the file's main date column.</summary>
public record CoverageDto(string Column, string From, string To);

public record ExplorerSchemaDto(IReadOnlyList<ExplorerColumnDto> Columns, long Rows, CoverageDto? Coverage);

/// <summary>A row as the file has it: one cell per column, in column order; an empty string where the cell is blank.</summary>
public record ExplorerRowDto(int Id, IReadOnlyList<string> Cells);

public record AnalyticsFieldDto(string Key, string Label, string Kind, ValueUnitDto Unit);

/// <summary>
/// What Analytics can compute for a file: its numeric columns as metrics, its
/// groupings and dates as dimensions, and its low-cardinality columns as filters.
/// The defaults follow what the dashboard recognised, so a mark sheet opens on
/// marks by subject rather than on whichever column came first.
/// </summary>
public record AnalyticsFieldsDto(
    IReadOnlyList<AnalyticsFieldDto> Metrics,
    IReadOnlyList<AnalyticsFieldDto> Dimensions,
    IReadOnlyList<ExplorerColumnDto> Filters,
    string? DefaultMetric,
    string? DefaultDimension,
    string DefaultAggregate,
    long Rows);

public record MetricSummaryDto(int Count, double Sum, double Mean, double Min, double Max, double Median);

/// <summary>Total is the sum of every group, for share of total; null when a share would mean nothing (averages, extremes).</summary>
public record AnalyticsResultDto(
    string Title,
    string? MetricLabel,
    string DimensionLabel,
    string Aggregate,
    string? Bucket,
    ValueUnitDto Unit,
    IReadOnlyList<Figure> Figures,
    double? Total,
    int GroupCount,
    int RowsMatched,
    int RowsScanned,
    int DurationMs,
    MetricSummaryDto? Summary);

public record ChatSessionDto(int Id, string Title, string Subtitle, int MessageCount, DateTime UpdatedAt);

public record ChatMessageDto(
    int Id, string Role, string Content, QuerySpec? Spec, IReadOnlyList<Figure>? Figures, ValueUnitDto? Unit,
    long RowsScanned, int LatencyMs, DateTime CreatedAt);

public record AskRequest(string Question);

/// <summary>
/// The three stages, returned together: what the planner chose, what the engine
/// computed, and the sentence written from those figures.
/// </summary>
public record AskResponse(
    ChatMessageDto UserMessage,
    ChatMessageDto AssistantMessage,
    QuerySpec Spec,
    IReadOnlyList<Figure> Figures,
    long RowsScanned,
    long RowsMatched,
    int PlanMs,
    int ComputeMs,
    string Chart,
    string Title,
    ValueUnitDto Unit,
    /// <summary>
    /// The planner that produced the spec, as "provider/model". Not read back
    /// from settings: settings record which planner was asked, and a hosted one
    /// falls back to keyword rules whenever it cannot answer.
    /// </summary>
    string Planner);

public record ReportDto(int Id, string Title, string Dataset, int Figures, string Status, DateTime CreatedAt);

/// <summary>What a caller supplies to write a report; everything else is computed.</summary>
public record CreateReportRequest(string? Title, int? DatasetId);

/// <summary>Renaming a conversation is how it gets saved under a name worth keeping.</summary>
public record RenameSessionRequest(string Title, string? Subtitle);

public record ReportSectionDto(string Heading, string[] Paragraphs, ReportFigureDto? Figure);

/// <summary>Kind is line | bars | columns | donut, as on the dashboard; Unit says how its values are written.</summary>
public record ReportFigureDto(int Number, string Caption, string Kind, IReadOnlyList<Figure> Data, ValueUnitDto Unit, long RowsScanned);

public record ReportDetailDto(ReportDto Report, string Meta, IReadOnlyList<ReportSectionDto> Sections);

/// <summary>
/// Which planner answers questions, and with which model. The only preference
/// the service acts on; the rows-per-page, date-format, profile-on-upload,
/// show-the-working and email toggles it used to carry were stored and read back
/// but never consulted anywhere.
/// </summary>
public record SettingsDto(string ProviderId, string ModelName);

public record ProviderDto(string Id, string Name, string Detail, string[] Models, string Status);

/// <summary>Sign-up and sign-in both take an email address and a password.</summary>
public record CredentialsRequest(string Email, string Password);

public record AccountDto(string Email);

/// <summary>Deleting an account asks for its password again.</summary>
public record DeleteAccountRequest(string Password);
