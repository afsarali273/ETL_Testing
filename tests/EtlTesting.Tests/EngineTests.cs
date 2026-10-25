using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using EtlTesting.Core;
using EtlTesting.Persistence;
using EtlTesting.Reporting;
using EtlTesting.SqlServer;
using Xunit;

namespace EtlTesting.Tests;

public sealed class EngineTests
{
    internal static string Root
    {
        get { var directory = new DirectoryInfo(AppContext.BaseDirectory); while (!File.Exists(Path.Combine(directory.FullName, "global.json"))) directory = directory.Parent!; return directory.FullName; }
    }
    internal static Suite Sample() => DefinitionLoader.Parse(File.ReadAllText(Path.Combine(Root, "samples/suites/reservations.json")));
    internal static Suite One() => Sample() with { Tests = [new() { Id = "one", Name = "One", Source = new() { Schema = "staging", Table = "T" }, Target = new() { Table = "T" }, Keys = [new() { Source = "Id", Target = "Id" }], Columns = [new() { Source = "Name", Target = "Name" }, new() { Source = "Amount", Target = "Amount" }] }] };
    internal static DataRow Row(object? key, string? name = "A", decimal amount = 100) => new(new() { ["Id"] = Cell.From(key), ["Name"] = Cell.From(name), ["Amount"] = Cell.From(amount) });
    internal static Task<RunResult> Run(Suite suite, IDataConnector connector, CancellationToken token = default, string[]? selected = null) => new SuiteRunner([new ComparisonHandler(new SqliteRowIndexFactory()), new SqlAssertionHandler()]).RunAsync(suite, "TEST", connector, DefinitionLoader.Bind(suite, new Dictionary<string, string>()), new(Path.Combine(Path.GetTempPath(), "etl-tests"), TestIds: selected), null, token);

    [Fact] public async Task EqualCountsDoNotHideMissingAndExtraKeys()
    {
        var run = await Run(One(), new Rows([Row(1), Row(2)], [Row(1), Row(3)])); var test = Assert.Single(run.Tests);
        Assert.Equal(1, test.MissingRecords); Assert.Equal(1, test.UnexpectedRecords); Assert.Equal(1, run.ExitCode);
    }
    [Fact] public async Task MultipleFieldsCountOneMismatchedRecord()
    {
        var test = Assert.Single((await Run(One(), new Rows([Row(1)], [Row(1, "B", 110)]))).Tests);
        Assert.Equal(1, test.MismatchedRecords); Assert.Equal(2, test.FieldMismatches); Assert.Equal(2, test.Violations);
    }
    [Fact] public async Task DuplicateGroupsAreNeverMatchedArbitrarily()
    {
        var test = Assert.Single((await Run(One(), new Rows([Row(1), Row(1, "B")], [Row(1, "C")]))).Tests);
        Assert.Equal(1, test.DuplicateGroups); Assert.Equal(0, test.FieldMismatches); Assert.Equal(0, test.MismatchedRecords);
    }
    [Fact] public async Task NullKeysAreViolationsAndExcluded()
    {
        var test = Assert.Single((await Run(One(), new Rows([Row(null)], [Row(null)]))).Tests);
        Assert.Equal(2, test.NullKeyRecords); Assert.Equal(0, test.FieldMismatches); Assert.Equal("NullKey", Assert.Single(test.Evidence).Category);
    }
    [Fact] public async Task NullValuesMatchNullAndDifferFromEmpty()
    {
        Assert.Equal(0, (await Run(One(), new Rows([Row(1, null)], [Row(1, null)]))).ExitCode);
        Assert.Equal(1, (await Run(One(), new Rows([Row(1, null)], [Row(1, "")]))).ExitCode);
    }
    [Fact] public async Task EvidenceLimitDoesNotLimitScope()
    {
        var suite = One(); suite.Tests[0] = suite.Tests[0] with { EvidenceLimit = 1 };
        var test = Assert.Single((await Run(suite, new Rows([Row(1), Row(2), Row(3)], []))).Tests);
        Assert.Equal(3, test.MissingRecords); Assert.Single(test.Evidence); Assert.True(test.EvidenceTruncated);
    }
    [Theory] [InlineData("Alice ", "ALICE", true)] [InlineData("Alice", "Bob", false)]
    public void StringRulesUseOrdinalComparison(string left, string right, bool equal) => Assert.Equal(equal, ValueComparison.Equal(Cell.From(left), Cell.From(right), new() { Trim = true, IgnoreCase = true }));
    [Fact] public void DecimalAndDateRulesAreTyped()
    {
        Assert.True(ValueComparison.Equal(Cell.From(100m), Cell.From(100.01m), new() { AbsoluteTolerance = .01m }));
        Assert.False(ValueComparison.Equal(Cell.From(100m), Cell.From(100.02m), new() { AbsoluteTolerance = .01m }));
        var date = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Unspecified);
        Assert.True(ValueComparison.Equal(Cell.From(date), Cell.From(date.AddMilliseconds(2)), new() { DateTimeToleranceMilliseconds = 2 }));
        Assert.DoesNotContain("Z", Cell.From(date).Value!);
        Assert.True(ValueComparison.Equal(new("float", "1.0"), Cell.From(1.0d), new()));
        var id = Guid.NewGuid(); Assert.True(ValueComparison.Equal(new("guid", id.ToString().ToUpperInvariant()), Cell.From(id), new()));
        Assert.True(ValueComparison.Equal(new("binary", "Y Q=="), Cell.From(new byte[] { 97 }), new()));
        Assert.Throws<InvalidDataException>(() => ValueComparison.Equal(Cell.From(1), Cell.From("1"), new()));
    }
    [Fact] public async Task CancellationIsExplicitAndNeverSuccessful()
    {
        using var source = new CancellationTokenSource(); source.Cancel();
        var run = await Run(One(), new Rows([], []), source.Token); Assert.Equal(3, run.ExitCode); Assert.False(run.Complete);
    }
    [Fact] public async Task RuntimeErrorsTakePrecedenceOverFailures()
    {
        var suite = One(); suite.Tests.Add(suite.Tests[0] with { Id = "error", Columns = [new() { Source = "NoSuchColumn", Target = "NoSuchColumn" }] });
        var run = await Run(suite, new Rows([Row(1)], [])); Assert.Equal(2, run.ExitCode); Assert.False(run.Complete);
    }
    [Fact] public async Task WarningsAreVisibleAndNonBlocking()
    {
        var suite = One(); suite.Tests[0] = suite.Tests[0] with { Severity = "warning" };
        var run = await Run(suite, new Rows([Row(1)], [])); Assert.Equal(0, run.ExitCode); Assert.Equal("Failed", run.Tests[0].Status);
        var xml = XDocument.Parse(ReportWriter.JUnit(run)); Assert.Empty(xml.Descendants("failure")); Assert.Single(xml.Descendants("system-out"));
    }
    [Fact] public async Task SelectedTestsAreClearlyPartial()
    {
        var suite = One(); suite.Tests.Add(suite.Tests[0] with { Id = "two" });
        var run = await Run(suite, new Rows([Row(1)], [Row(1)]), selected: ["one"]); Assert.Equal("Skipped", run.Tests[1].Status); Assert.Contains("partial", run.Scope);
    }
    [Fact] public void SchemaRejectsUnknownOptionsAndTypes()
    {
        var text = File.ReadAllText(Path.Combine(Root, "samples/suites/reservations.json"));
        Assert.Throws<InvalidDataException>(() => DefinitionLoader.Parse(text.Replace("\"evidenceLimit\":1000", "\"evdenceLimit\":1000")));
        Assert.Throws<InvalidDataException>(() => DefinitionLoader.Parse(text.Replace("dataComparison", "incremental")));
        var suite = Sample(); suite.Tests.Add(suite.Tests[0]); Assert.Throws<InvalidDataException>(() => DefinitionLoader.Parse(StudioJson.Write(suite)));
    }
    [Fact] public void ParametersHaveTypesAndOverrideDefaults()
    {
        var sample = Sample(); Assert.Equal("override", Assert.Single(DefinitionLoader.Bind(sample, new Dictionary<string, string> { ["batchId"] = "override" })).Value);
        Assert.Throws<InvalidDataException>(() => DefinitionLoader.Bind(sample, new Dictionary<string, string> { ["typo"] = "x" }));
        var typed = sample with { Parameters = new() { ["money"] = new() { Type = "decimal", Required = true } } };
        Assert.Equal(12.34m, Assert.Single(DefinitionLoader.Bind(typed, new Dictionary<string, string> { ["money"] = "12.34" })).Value);
        Assert.Throws<InvalidDataException>(() => DefinitionLoader.Bind(typed, new Dictionary<string, string>()));
        Assert.Throws<InvalidDataException>(() => DefinitionLoader.Bind(typed, new Dictionary<string, string> { ["money"] = "12,34" }));
    }
    [Fact] public void IdentifiersAndValuesHaveSeparateSafePaths()
    {
        Assert.Equal("[a]]b]", SqlServerConnector.Quote("a]b"));
        Assert.Throws<InvalidDataException>(() => SqlServerConnector.Quote(""));
        var parameter = SqlServerConnector.Parameter(new("batchId", "string", "x'; DELETE dbo.T; --"));
        Assert.Equal(System.Data.SqlDbType.NVarChar, parameter.SqlDbType); Assert.Equal("@batchId", parameter.ParameterName);
        Assert.DoesNotContain("DELETE", SqlServerConnector.SelectSql(new() { Table = "T" }, ["Id"]));
    }
    [Fact] public async Task ReportsEscapeHtmlCsvAndHaveValidJUnit()
    {
        var run = await Run(One(), new Rows([Row(1, "<script>alert(1)</script>")], [Row(1, "=cmd()")])) ;
        var html = ReportWriter.Html(run); Assert.DoesNotContain("<script>", html); Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("At a glance", html);
        Assert.Contains("Validation outcomes", html);
        Assert.Contains("Technical rule and captured evidence", html);
        Assert.Contains("\"'=cmd()\"", ReportWriter.Csv(run)); Assert.Equal("\"a,\"\"b\"", ReportWriter.EscapeCsv("a,\"b"));
        Assert.Single(XDocument.Parse(ReportWriter.JUnit(run)).Descendants("failure"));
        run.Tests[0].Status = "Error"; Assert.Single(XDocument.Parse(ReportWriter.JUnit(run)).Descendants("error"));
    }
    [Fact] public async Task PdfReportContainsTheRecordedValidationAndEvidence()
    {
        var run = await Run(One(), new Rows([Row(1, "expected")], [Row(1, "actual")]));
        var directory = Path.Combine(Path.GetTempPath(), "etl-pdf-" + Guid.NewGuid().ToString("N"));
        try
        {
            await new ReportWriter().WriteAsync(run, directory, ["pdf", "html"]);
            var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, "report.pdf"));
            Assert.True(bytes.Length > 1000);
            Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
            Assert.Contains("Expected / source", await File.ReadAllTextAsync(Path.Combine(directory, "report.html")));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact] public async Task HistoryRoundTripsAndAtomicSaveLeavesNoTemporaryFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "etl-history-" + Guid.NewGuid().ToString("N"));
        try { var store = new FileResultStore(directory); var run = await Run(One(), new Rows([], [])); await store.SaveAsync(run); Assert.Equal(run.RunId, Assert.Single(await store.ListAsync()).RunId); Assert.Empty(Directory.GetFiles(directory, "*.tmp")); }
        finally { Directory.Delete(directory, true); }
    }
    [Fact] public async Task QueryIsExecutedOnlyOnceAndScalarCardinalityIsChecked()
    {
        var suite = Sample() with { Tests = [new() { Id = "sql", Name = "SQL", Type = "sqlAssertion", Query = "SELECT 1", Expect = new() { Scalar = Cell.From(1) } }] };
        var connector = new Rows([], []); var run = await Run(suite, connector); Assert.Equal(1, connector.Queries); Assert.Equal(0, run.ExitCode);
        connector.QueryCount = 2; Assert.Equal(2, (await Run(suite, connector)).ExitCode);
    }
    internal sealed class Rows(DataRow[] source, DataRow[] target) : IDataConnector
    {
        public int Queries { get; private set; } public int QueryCount { get; set; } = 1;
        public async IAsyncEnumerable<DataRow> ReadAsync(TableScope scope, string connection, IReadOnlyList<string> columns, IReadOnlyList<BoundParameter> parameters, int timeout, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield(); foreach (var row in scope.Schema == "staging" ? source : target) { cancellationToken.ThrowIfCancellationRequested(); foreach (var c in columns) _ = row.Values[c]; yield return row; }
        }
        public Task<(long Count, Cell? Scalar, List<DataRow> Evidence)> QueryAsync(string connection, string query, IReadOnlyList<BoundParameter> parameters, int timeout, int evidenceLimit, CancellationToken cancellationToken) { Queries++; return Task.FromResult<(long, Cell?, List<DataRow>)>((QueryCount, Cell.From(1), [])); }
    }
}
