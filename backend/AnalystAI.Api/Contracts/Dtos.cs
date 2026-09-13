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

/// <summary>Which uploaded header fed each queryable field. A null header means nothing matched.</summary>
public record FieldMappingDto(string Field, string? Header);

public record UploadResultDto(
    DatasetDto Dataset,
    IReadOnlyList<ColumnProfileDto> Columns,
    int RowsImported,
    IReadOnlyList<FieldMappingDto> Mapping);

public record KpiDto(string Label, string Value, string Icon, string? Delta = null, string? DeltaTone = null, string? IconTone = null);

public record InsightDto(string Tone, string Icon, string Title, string Body);

public record DashboardDto(
    IReadOnlyList<KpiDto> Kpis,
    IReadOnlyList<Figure> RevenueTrend,
    IReadOnlyList<Figure> RegionalPerformance,
    IReadOnlyList<Figure> CategoryBars,
    IReadOnlyList<InsightDto> Insights);

public record AnalyticsDto(
    IReadOnlyList<KpiDto> Kpis,
    IReadOnlyList<Figure> RevenueByCategory,
    IReadOnlyList<Figure> RevenueByRegion,
    IReadOnlyList<Figure> TopCustomers,
    IReadOnlyList<Figure> RevenueTrend);

public record ExplorerColumnDto(string Key, string Label, string Align, bool Numeric);

public record ExplorerRowDto(
    int Id, string Date, string OrderId, string Customer, string Product,
    string Category, int Qty, double Price, double Revenue, string Region, string Status);

public record ChatSessionDto(int Id, string Title, string Subtitle, int MessageCount, DateTime UpdatedAt);

public record ChatMessageDto(
    int Id, string Role, string Content, QuerySpec? Spec, IReadOnlyList<Figure>? Figures,
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

public record ReportFigureDto(int Number, string Caption, string Kind, IReadOnlyList<Figure> Data, QuerySpec Spec, long RowsScanned);

public record ReportDetailDto(ReportDto Report, string Meta, IReadOnlyList<ReportSectionDto> Sections);

/// <summary>
/// Which planner answers questions, and with which model. The only preference
/// the service acts on; the rows-per-page, date-format, profile-on-upload,
/// show-the-working and email toggles it used to carry were stored and read back
/// but never consulted anywhere.
/// </summary>
public record SettingsDto(string ProviderId, string ModelName);

public record ProviderDto(string Id, string Name, string Detail, string[] Models, string Status);

public record RunSpecRequest(QuerySpec Spec, int? DatasetId);

/// <summary>Sign-up and sign-in both take an email address and a password.</summary>
public record CredentialsRequest(string Email, string Password);

public record AccountDto(string Email);

/// <summary>Deleting an account asks for its password again.</summary>
public record DeleteAccountRequest(string Password);
