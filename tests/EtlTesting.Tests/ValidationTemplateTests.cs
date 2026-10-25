using EtlTesting.Core;
using EtlTesting.Persistence;
using EtlTesting.SqlServer;
using Xunit;

namespace EtlTesting.Tests;

public sealed class ValidationTemplateTests
{
    private static ValidationTemplateConfiguration Rule(string kind) => new() { Kind = kind, Source = new() { Schema = "dbo", Table = "Reservation", Filter = "LoadBatchId = @batchId" }, Columns = ["GuestName"] };
    private static TestDefinition Test(ValidationTemplateConfiguration rule) => new() { Id = "rule", Name = "Rule", Type = "sqlAssertion", Template = rule, Query = ValidationTemplateCompiler.Compile(rule).Query, Expect = new() { RowCount = 0 } };

    [Fact]
    public void AllowedValuesRemainTypedParametersAndIdentifiersAreQuoted()
    {
        var value = "x'); DROP TABLE dbo.Reservation; --";
        var result = ValidationTemplateCompiler.Compile(Rule("allowedValues") with { Columns = ["Odd]Column"], AllowedValues = [value, "Active"] });
        Assert.DoesNotContain("DROP TABLE", result.Query);
        Assert.Contains("[Odd]]Column]", result.Query);
        Assert.Equal(value, result.Parameters[0].Value);
        Assert.Equal("string", result.Parameters[0].Type);
        Assert.Contains("@__etl_template_0", result.Query);
    }

    [Fact]
    public void NumericAndDateBoundariesAreTypedAndValidateOrdering()
    {
        var result = ValidationTemplateCompiler.Compile(Rule("numericRange") with { Minimum = "1.25", Maximum = "99.5", IncludeMaximum = false });
        Assert.Equal(1.25m, result.Parameters[0].Value);
        Assert.Contains(">= @__etl_template_1", result.Query);
        Assert.Throws<InvalidDataException>(() => ValidationTemplateCompiler.Compile(Rule("numericRange") with { Minimum = "10", Maximum = "1" }));
        var date = ValidationTemplateCompiler.Compile(Rule("dateRange") with { Minimum = "2026-10-24T00:00:00" });
        Assert.IsType<DateTime>(date.Parameters[0].Value);
        Assert.Equal(DateTimeKind.Unspecified, ((DateTime)date.Parameters[0].Value).Kind);
        Assert.Throws<InvalidDataException>(() => ValidationTemplateCompiler.Compile(Rule("lengthRange") with { Minimum = "-1" }));
    }

    [Fact]
    public void CompositeUniquenessAndReferenceRulesHandleNullPolicy()
    {
        var unique = ValidationTemplateCompiler.Compile(Rule("uniqueKeys") with { Columns = ["PropertyCode", "ReservationId"] });
        Assert.Contains("GROUP BY s.[PropertyCode], s.[ReservationId]", unique.Query);
        Assert.Contains("HAVING COUNT_BIG(*) > 1 OR", unique.Query);
        Assert.Equal("key groups", unique.EvidenceUnit);
        var parent = Rule("referentialIntegrity") with { Target = new() { Table = "Customer" }, Keys = [new() { Source = "CustomerId", Target = "Id" }], FailOnNull = false };
        var reference = ValidationTemplateCompiler.Compile(parent);
        Assert.Contains("s.[CustomerId] IS NOT NULL", reference.Query);
        Assert.Contains("NOT EXISTS", reference.Query);
        Assert.Contains("s.[CustomerId] = p.[Id]", reference.Query);
    }

    [Fact]
    public void AggregateGroupsHaveUniqueEvidenceNamesAndValidProjectionSeparators()
    {
        var result = ValidationTemplateCompiler.Compile(Rule("aggregate") with { Target = new() { Table = "Reservation" }, SourceMeasure = "Net", TargetMeasure = "Revenue", Keys = [new() { Source = "Property", Target = "Property" }, new() { Source = "Date", Target = "Date" }] });
        Assert.Contains("AS [Group1], COALESCE", result.Query);
        Assert.DoesNotContain(", ,", result.Query);
        Assert.Contains("AS [SourceMeasure]", result.Query);
        Assert.Contains("AS [TargetMeasure]", result.Query);
        Assert.Contains("FULL OUTER JOIN", result.Query);
    }

    [Fact]
    public void ConfigurationRoundTripsThroughSchemaAndRetainsEditingDetails()
    {
        var rule = Rule("allowedValues") with { AllowedValues = ["Active", "Inactive"], FailOnNull = false, AllowedViolations = 3 };
        var suite = EngineTests.Sample() with { Tests = [Test(rule) with { TimeoutSeconds = 45 }] };
        var loaded = DefinitionLoader.Parse(StudioJson.Write(suite)).Tests.Single();
        Assert.Equal("allowedValues", loaded.Template!.Kind);
        Assert.False(loaded.Template.FailOnNull);
        Assert.Equal(3, loaded.Template.AllowedViolations);
        Assert.Equal(rule.AllowedValues, loaded.Template.AllowedValues);
        Assert.Equal(45, loaded.TimeoutSeconds);
        Assert.Throws<InvalidDataException>(() => DefinitionLoader.Parse(StudioJson.Write(suite).Replace("allowedValues\"", "unavailableRule\"")));
    }

    [Fact]
    public async Task TemplateEvidenceCapDoesNotChangeScopeAndThresholdIsExplicit()
    {
        var rule = Rule("requiredValues") with { AllowedViolations = 2 };
        var connector = new QueryFixture(3);
        var suite = EngineTests.Sample() with { Tests = [Test(rule) with { EvidenceLimit = 1 }] };
        var result = await EngineTests.Run(suite, connector);
        Assert.Equal(1, result.ExitCode); Assert.Equal(3, result.Tests[0].Violations); Assert.Single(result.Tests[0].Evidence); Assert.True(result.Tests[0].EvidenceTruncated);
        Assert.Equal(1, connector.Calls); Assert.Contains("[GuestName]", connector.Sql);
        Assert.Equal(0, (await EngineTests.Run(suite, new QueryFixture(2))).ExitCode);
    }

    [Fact]
    public async Task EmptySuitesCanBeSavedAfterDeletingLastValidationButCannotRun()
    {
        var suite = DefinitionLoader.Parse(StudioJson.Write(EngineTests.Sample() with { Tests = [] }));
        Assert.Empty(suite.Tests);
        await Assert.ThrowsAsync<InvalidDataException>(() => EngineTests.Run(suite, new QueryFixture(0)));
    }

    [Theory]
    [InlineData("requiredValues")]
    [InlineData("uniqueKeys")]
    [InlineData("allowedValues")]
    [InlineData("numericRange")]
    [InlineData("referentialIntegrity")]
    [InlineData("aggregate")]
    public void IncompleteTemplatesAreRejectedBeforeExecution(string kind)
    {
        var invalid = new ValidationTemplateConfiguration { Kind = kind, Source = new() { Table = "T" } };
        Assert.Throws<InvalidDataException>(() => ValidationTemplateCompiler.Compile(invalid));
    }

    [LocalDbFact]
    public async Task RealSqlTemplateQueriesHandleFixtureCountsAndCompositeAggregateGroups()
    {
        var environment = SqlIntegrationTests.TestEnvironment();
        var connector = new SqlServerConnector(environment, new EnvironmentCredentialResolver());
        var batch = DefinitionLoader.Bind(EngineTests.Sample(), new Dictionary<string, string>());
        var rules = new List<(ValidationTemplateConfiguration Rule, long Expected)>
        {
            (Rule("requiredValues"), 1),
            (Rule("uniqueKeys") with { Columns = ["PropertyCode", "ReservationId"] }, 2),
            (Rule("allowedValues") with { Columns = ["Status"], AllowedValues = ["Active", "Inactive"] }, 2),
            (Rule("numericRange") with { Columns = ["RevenueAmount"], Minimum = "0", Maximum = "200" }, 6),
            (Rule("dateRange") with { Columns = ["ModifiedDate"], Minimum = "2026-10-23T00:00:00", Maximum = "2026-10-24T00:00:00", IncludeMaximum = false }, 1),
            (Rule("lengthRange") with { Minimum = "1", Maximum = "100" }, 1),
            (Rule("referentialIntegrity") with { Source = new() { Table = "Payment", Filter = "LoadBatchId = @batchId" }, Target = new() { Table = "Reservation", Filter = "LoadBatchId = @batchId" }, Keys = [new() { Source = "PropertyCode", Target = "PropertyCode" }, new() { Source = "ReservationId", Target = "ReservationId" }] }, 1),
            (Rule("rowCount") with { Source = new() { Schema = "staging", Table = "Reservation", Filter = "BatchId = @batchId" }, Target = new() { Table = "Reservation", Filter = "LoadBatchId = @batchId" } }, 0),
            (Rule("aggregate") with { Source = new() { Schema = "staging", Table = "Payment", Filter = "BatchId = @batchId" }, Target = new() { Table = "Payment", Filter = "LoadBatchId = @batchId" }, SourceMeasure = "Amount", TargetMeasure = "Amount", AbsoluteTolerance = .01m, Keys = [new() { Source = "PropertyCode", Target = "PropertyCode" }, new() { Source = "ReservationId", Target = "ReservationId" }] }, 1)
        };
        foreach (var (rule, expected) in rules)
        {
            var compiled = ValidationTemplateCompiler.Compile(rule);
            var result = await connector.QueryAsync("warehouse", compiled.Query, batch.Concat(compiled.Parameters).ToArray(), 30, 10, CancellationToken.None);
            Assert.True(result.Count == expected, $"{rule.Kind}: expected {expected}, found {result.Count}");
        }
    }

    private sealed class QueryFixture(long count) : IDataConnector
    {
        public int Calls { get; private set; }
        public string Sql { get; private set; } = "";
        public IAsyncEnumerable<DataRow> ReadAsync(TableScope scope, string connection, IReadOnlyList<string> columns, IReadOnlyList<BoundParameter> parameters, int timeout, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(long Count, Cell? Scalar, List<DataRow> Evidence)> QueryAsync(string connection, string query, IReadOnlyList<BoundParameter> parameters, int timeout, int evidenceLimit, CancellationToken cancellationToken)
        {
            Calls++; Sql = query;
            return Task.FromResult<(long, Cell?, List<DataRow>)>((count, null, Enumerable.Range(0, (int)Math.Min(count, evidenceLimit)).Select(i => new DataRow(new() { ["Id"] = Cell.From(i) })).ToList()));
        }
    }
}
