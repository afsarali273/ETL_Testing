using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EtlTesting.Core;

public static class Product { public const string Name = "ETL Test Studio"; }
public static class StudioJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidDataException("Empty JSON document");
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
}
public sealed record Suite
{
    public string SchemaVersion { get; init; } = "1.0";
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string[] Tags { get; init; } = [];
    public SuiteDefaults Defaults { get; init; } = new();
    public Dictionary<string, ParameterDefinition> Parameters { get; init; } = [];
    public List<TestDefinition> Tests { get; init; } = [];
    public List<TestCaseDefinition> TestCases { get; init; } = [];
}
public sealed record TestCaseDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Enabled { get; init; } = true;
}
public sealed record SuiteDefaults { public string Connection { get; init; } = "warehouse"; public int TimeoutSeconds { get; init; } = 120; }
public sealed record ParameterDefinition { public string Type { get; init; } = "string"; public bool Required { get; init; } public string? Default { get; init; } }
public sealed record TestDefinition
{
    public string? TestCaseId { get; init; }
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "dataComparison";
    public bool Enabled { get; init; } = true;
    public string Severity { get; init; } = "error";
    public string? Connection { get; init; }
    public string[] Tags { get; init; } = [];
    public TableScope? Source { get; init; }
    public TableScope? Target { get; init; }
    public List<ColumnMapping> Keys { get; init; } = [];
    public List<ColumnMapping> Columns { get; init; } = [];
    public RecordChecks Checks { get; init; } = new();
    public int EvidenceLimit { get; init; } = 1000;
    public string? Query { get; init; }
    public Expectation? Expect { get; init; }
    public ValidationTemplateConfiguration? Template { get; init; }
    public int? TimeoutSeconds { get; init; }
}
public sealed record TableScope { public string Schema { get; init; } = "dbo"; public string Table { get; init; } = ""; public string? Connection { get; init; } public string? Filter { get; init; } }
public sealed record ColumnMapping { public string Source { get; init; } = ""; public string Target { get; init; } = ""; public ComparisonRule Comparison { get; init; } = new(); }
public sealed record ComparisonRule { public bool Trim { get; init; } public bool IgnoreCase { get; init; } public bool EmptyAsNull { get; init; } public decimal AbsoluteTolerance { get; init; } public int DateTimeToleranceMilliseconds { get; init; } }
public sealed record RecordChecks { public bool UniqueKeys { get; init; } = true; public bool MissingTargetRows { get; init; } = true; public bool UnexpectedTargetRows { get; init; } = true; }
public sealed record Expectation { public long? RowCount { get; init; } public Cell? Scalar { get; init; } }
public sealed record EnvironmentDefinition { public string Name { get; init; } = "QA"; public Dictionary<string, ConnectionProfile> Connections { get; init; } = []; }
public sealed record ConnectionProfile
{
    public string Server { get; init; } = "";
    public string Database { get; init; } = "";
    public string Authentication { get; init; } = "integrated";
    public string? UserEnvironmentVariable { get; init; }
    public string? PasswordEnvironmentVariable { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CredentialTarget { get; init; }
    public bool Encrypt { get; init; } = true;
    public bool TrustServerCertificate { get; init; }
    public int ConnectionTimeoutSeconds { get; init; } = 15;
}
public sealed record BoundParameter(string Name, string Type, object Value);
public sealed record Cell(string Kind, string? Value)
{
    public static Cell Null => new("null", null);
    public static Cell From(object? value) => value switch
    {
        null or DBNull => Null,
        string s => new("string", s),
        decimal d => new("decimal", d.ToString("G29", CultureInfo.InvariantCulture)),
        byte or short or int or long => new("decimal", Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("G29", CultureInfo.InvariantCulture)),
        double or float => new("float", Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture)),
        DateTime dt => new("datetime", DateTime.SpecifyKind(dt, DateTimeKind.Unspecified).ToString("O", CultureInfo.InvariantCulture)),
        DateTimeOffset dt => new("datetimeoffset", dt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
        bool b => new("boolean", b ? "true" : "false"),
        Guid g => new("guid", g.ToString("D")),
        byte[] bytes => new("binary", Convert.ToBase64String(bytes)),
        _ => throw new InvalidDataException($"Unsupported SQL type {value.GetType().Name}")
    };
    public override string ToString() => Value ?? "NULL";
}
public sealed record DataRow(Dictionary<string, Cell> Values);
public sealed record IndexedPair(string Key, long SourceCount, DataRow? Source, long TargetCount, DataRow? Target, bool NullKey);
public sealed record Evidence(string TestId, string Table, string Category, string Key, string Column, Cell Expected, Cell Actual, string Rule, string Reason);
public sealed class TestResult
{
    public string? TestCaseId { get; set; }
    public string? TestCaseName { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Severity { get; set; } = "error";
    public string Status { get; set; } = "Queued";
    public long SourceRecords { get; set; }
    public long TargetRecords { get; set; }
    public long MismatchedRecords { get; set; }
    public long FieldMismatches { get; set; }
    public long MissingRecords { get; set; }
    public long UnexpectedRecords { get; set; }
    public long DuplicateGroups { get; set; }
    public long NullKeyRecords { get; set; }
    public long Violations { get; set; }
    public string? Error { get; set; }
    public double DurationSeconds { get; set; }
    public List<Evidence> Evidence { get; set; } = [];
    public bool EvidenceTruncated => Violations > Evidence.Count;
}
public sealed class RunResult
{
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public string SuiteId { get; set; } = "";
    public string SuiteName { get; set; } = "";
    public string SuiteHash { get; set; } = "";
    public string Environment { get; set; } = "";
    public bool Demo { get; set; }
    public DateTimeOffset Started { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Ended { get; set; }
    public string Strategy { get; set; } = "Disk-indexed streaming; ordinal typed keys; sequential read-committed reads. Run against a quiescent ETL boundary.";
    public string Scope { get; set; } = "Full suite";
    public Dictionary<string, Cell> RuntimeParameters { get; set; } = [];
    public List<TestResult> Tests { get; set; } = [];
    public bool Complete => Tests.All(t => t.Status is "Passed" or "Failed" or "Skipped");
    public int ExitCode => Tests.Any(t => t.Status == "Cancelled") ? 3 : Tests.Any(t => t.Status == "Error") ? 2 : Tests.Any(t => t.Status == "Failed" && t.Severity == "error") ? 1 : 0;
    public string Status => ExitCode switch { 3 => "Cancelled", 2 => "Error", 1 => "Failed", _ => "Passed" };
}
public sealed record RunProgress(string TestId, string Status, int Completed, int Total, string Message);
public sealed record RunOptions(string WorkDirectory, bool Demo = false, string[]? TestIds = null, string[]? Tags = null, int? TimeoutSeconds = null);
public interface IDataConnector
{
    IAsyncEnumerable<DataRow> ReadAsync(TableScope scope, string connection, IReadOnlyList<string> columns, IReadOnlyList<BoundParameter> parameters, int timeout, CancellationToken cancellationToken);
    Task<(long Count, Cell? Scalar, List<DataRow> Evidence)> QueryAsync(string connection, string query, IReadOnlyList<BoundParameter> parameters, int timeout, int evidenceLimit, CancellationToken cancellationToken);
}
public interface IRowIndex : IAsyncDisposable
{
    Task AddAsync(int side, string key, bool nullKey, DataRow row, CancellationToken cancellationToken);
    IAsyncEnumerable<IndexedPair> PairsAsync(CancellationToken cancellationToken);
}
public interface IRowIndexFactory { Task<IRowIndex> CreateAsync(string directory, CancellationToken cancellationToken); }
public interface ISuiteRunner { Task<RunResult> RunAsync(Suite suite, string environment, IDataConnector connector, IReadOnlyList<BoundParameter> parameters, RunOptions options, IProgress<RunProgress>? progress, CancellationToken cancellationToken); }
public interface IValidationHandler { string Type { get; } Task ExecuteAsync(TestDefinition test, Suite suite, IDataConnector connector, IReadOnlyList<BoundParameter> parameters, RunOptions options, TestResult result, CancellationToken cancellationToken); }
public interface IResultStore { Task SaveAsync(RunResult run, CancellationToken cancellationToken = default); Task<IReadOnlyList<RunResult>> ListAsync(CancellationToken cancellationToken = default); }
public interface IReportWriter { Task WriteAsync(RunResult result, string directory, IEnumerable<string> formats, CancellationToken cancellationToken = default); }
