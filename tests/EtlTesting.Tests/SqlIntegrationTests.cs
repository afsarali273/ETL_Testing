using EtlTesting.Core;
using EtlTesting.SqlServer;
using Xunit;

namespace EtlTesting.Tests;

public sealed class LocalDbFactAttribute : FactAttribute
{
    public LocalDbFactAttribute() { if (Environment.GetEnvironmentVariable("ETL_TEST_LOCALDB") != "1") Skip = "Opt in with ETL_TEST_LOCALDB=1 after samples/sql/setup.sql"; }
}
public sealed class SqlIntegrationTests
{
    internal static EnvironmentDefinition TestEnvironment() => StudioJson.Read<EnvironmentDefinition>(File.ReadAllText(Environment.GetEnvironmentVariable("ETL_TEST_SQL_ENVIRONMENT") ?? Path.Combine(EngineTests.Root, "samples/environments/localdb.json")));
    private static SqlServerConnector Connector() => new(TestEnvironment(), new EnvironmentCredentialResolver());
    [LocalDbFact] public async Task RealSqlFixtureValidatesDuplicateNullToleranceAndOrphanSemantics()
    {
        var suite = EngineTests.Sample(); var run = await EngineTests.Run(suite, Connector());
        Assert.Equal(1, run.ExitCode);
        var reservations = run.Tests[0]; Assert.Equal("Failed", reservations.Status); Assert.Equal(7, reservations.SourceRecords); Assert.Equal(7, reservations.TargetRecords);
        Assert.Equal(1, reservations.DuplicateGroups); Assert.Equal(2, reservations.NullKeyRecords); Assert.Equal(1, reservations.MismatchedRecords); Assert.Equal(3, reservations.FieldMismatches); Assert.Equal(1, reservations.MissingRecords); Assert.Equal(1, reservations.UnexpectedRecords);
        Assert.Equal("Passed", run.Tests[1].Status); Assert.Equal(1, run.Tests[2].FieldMismatches); Assert.Equal("Failed", run.Tests[3].Status); Assert.Equal(1, run.Tests[3].SourceRecords);
    }
    [LocalDbFact] public async Task BoundParameterDoesNotExecuteInjectedSql()
    {
        var connector = Connector();
        var value = "x'; DROP TABLE dbo.Reservation;--";
        var result = await connector.QueryAsync("warehouse", "SELECT @value AS BoundValue", [new("value", "string", value)], 10, 1, CancellationToken.None);
        Assert.Equal(value, result.Scalar!.Value);
        Assert.Contains("dbo.Reservation", await connector.MetadataAsync("warehouse", CancellationToken.None));
    }
    [LocalDbFact] public async Task RealScalarAndMissingMetadataAreHandled()
    {
        var connector = Connector(); var query = await connector.QueryAsync("warehouse", "SELECT CAST(123.45 AS decimal(18,2)) AS Amount", [], 10, 1, CancellationToken.None);
        Assert.Equal(Cell.From(123.45m), query.Scalar);
        await Assert.ThrowsAsync<InvalidDataException>(async () => { await foreach (var row in connector.ReadAsync(new() { Table = "Reservation" }, "warehouse", ["does-not-exist"], [], 10, CancellationToken.None)) _ = row; });
    }
}
