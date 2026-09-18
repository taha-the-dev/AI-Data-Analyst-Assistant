namespace AnalystAI.Api.Models;

/// <summary>
/// A record that belongs to one account. The database context stamps
/// <see cref="UserId"/> on insert and filters every query by it, so an endpoint
/// cannot read or change another account's data by forgetting a where-clause.
/// </summary>
public interface IOwned
{
    string UserId { get; set; }
}

public class Dataset : IOwned
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "CSV";
    public string Icon { get; set; } = "table_chart";
    public long RowCount { get; set; }
    public int ColumnCount { get; set; }
    public string Quality { get; set; } = "High";
    public DateTime UpdatedAt { get; set; }
    public List<DatasetColumn> Columns { get; set; } = [];
}

/// <summary>Per-column profile produced when a file is uploaded.</summary>
public class DatasetColumn : IOwned
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public int DatasetId { get; set; }
    public Dataset? Dataset { get; set; }

    public string Name { get; set; } = "";
    public int Ordinal { get; set; }
    /// <summary>number | text | category | date | boolean</summary>
    public string Kind { get; set; } = "text";
    public int Missing { get; set; }
    public int Distinct { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Mean { get; set; }
    public double? StdDev { get; set; }
    public string SampleValues { get; set; } = "";
}

/// <summary>
/// The uploaded file itself, gzip-compressed. The stored rows are mapped onto a
/// fixed schema and lose every column that schema has no place for; the
/// dashboard is written from this copy, so it sees the columns the file
/// actually has.
/// </summary>
public class DatasetSource : IOwned
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public int DatasetId { get; set; }
    public byte[] Content { get; set; } = [];
}

public class ChatSession : IOwned
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ChatMessage> Messages { get; set; } = [];
}

public class ChatMessage : IOwned
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public int SessionId { get; set; }
    public ChatSession? Session { get; set; }

    /// <summary>user | assistant</summary>
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    /// <summary>The QuerySpec the planner chose, as JSON. Null for user turns.</summary>
    public string? SpecJson { get; set; }
    /// <summary>The computed figures the answer was written from, as JSON.</summary>
    public string? ResultJson { get; set; }
    public long RowsScanned { get; set; }
    public int LatencyMs { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Report : IOwned
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string Title { get; set; } = "";
    public string DatasetName { get; set; } = "";
    public int Figures { get; set; }
    public string Status { get; set; } = "Ready";
    public DateTime CreatedAt { get; set; }
}

/// <summary>Which planner answers this account's questions, and with which model.</summary>
public class UserSettings : IOwned
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string ProviderId { get; set; } = "keyword";
    public string ModelName { get; set; } = "rules-v1";
}
