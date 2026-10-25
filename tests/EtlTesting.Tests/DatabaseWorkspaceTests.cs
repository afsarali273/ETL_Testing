using EtlTesting.Core;
using EtlTesting.SqlServer;
using Xunit;

namespace EtlTesting.Tests;

public sealed class DatabaseWorkspaceTests
{
    private static SqlServerConnector Connector() => new(SqlIntegrationTests.TestEnvironment(), new EnvironmentCredentialResolver());

    [LocalDbFact] public async Task DatabaseDiscoveryCatalogColumnsAndActualPreviewWork()
    {
        var connector = Connector();
        Assert.Contains("EtlTestStudioSample", await connector.ListDatabasesAsync("warehouse", CancellationToken.None));
        var inspection = await connector.InspectAsync("warehouse", CancellationToken.None); Assert.Equal("EtlTestStudioSample", inspection.Database); Assert.NotEmpty(inspection.ServerVersion);
        var objects = await connector.CatalogAsync("warehouse", CancellationToken.None);
        var table = Assert.Single(objects, o => o.Schema == "dbo" && o.Name == "Reservation");
        var columns = await connector.ColumnsAsync("warehouse", table, CancellationToken.None); Assert.Contains(columns, c => c.Name == "RevenueAmount" && c.SqlType == "decimal(18,2)");
        var page = await connector.PreviewAsync("warehouse", table, columns, 0, 2, CancellationToken.None);
        Assert.Null(page.Error); Assert.Equal(2, Assert.Single(page.Results).Rows.Count); Assert.True(page.Capped);
    }
    [LocalDbFact] public async Task TypedValuesMultipleResultSetsAndDuplicateColumnNamesAreSupported()
    {
        var result = await Connector().ExecuteSqlAsync("warehouse", "SELECT CAST(12.34 AS decimal(18,2)) AS Amount, CAST(NULL AS nvarchar(10)) AS NullableValue; SELECT @text AS x, 2 AS x;", 10, 200, [new("text", "string", "x'; DROP TABLE dbo.Reservation;--")], CancellationToken.None);
        Assert.Null(result.Error); Assert.Equal(2, result.Results.Count); Assert.Equal(12.34m, result.Results[0].Rows[0][0]); Assert.Null(result.Results[0].Rows[0][1]);
        Assert.Equal("x'; DROP TABLE dbo.Reservation;--", result.Results[1].Rows[0][0]); Assert.Equal(["x", "x"], result.Results[1].Columns);
    }
    [LocalDbFact] public async Task LargeCellsAreBoundedAndRemainingColumnsStillReadCorrectly()
    {
        var result = await Connector().ExecuteSqlAsync("warehouse", "SELECT REPLICATE(CAST(N'x' AS nvarchar(max)),5000) AS Payload, CAST(0x010203 AS varbinary(max)) AS Bytes;", 10, 200, [], CancellationToken.None);
        Assert.Null(result.Error); var set = Assert.Single(result.Results); Assert.Equal(1, set.TruncatedCells); Assert.Contains("[cell truncated]", (string)set.Rows[0][0]!); Assert.Equal("0x010203", set.Rows[0][1]);
    }
    [LocalDbFact] public async Task RowCapLimitsDisplayedResultsAndClearlyMarksPartialExecution()
    {
        var result = await Connector().ExecuteSqlAsync("warehouse", "SELECT 1 AS Id UNION ALL SELECT 2 UNION ALL SELECT 3; SELECT 99 AS ShouldNotRun;", 10, 2, [], CancellationToken.None);
        Assert.Null(result.Error); Assert.True(result.Capped); Assert.Equal(2, Assert.Single(result.Results).Rows.Count);
    }
    [LocalDbFact] public async Task CancellationPreservesEarlierResultsAndSqlErrorsAreReported()
    {
        var connector = Connector(); await connector.InspectAsync("warehouse", CancellationToken.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var cancelled = await connector.ExecuteSqlAsync("warehouse", "SELECT 1 AS Id; RAISERROR(N'First result ready',0,1) WITH NOWAIT; WAITFOR DELAY '00:00:10'; SELECT 2 AS Id;", 15, 200, [], cancellation.Token);
        Assert.True(cancelled.Cancelled); Assert.Null(cancelled.Error); Assert.NotEmpty(cancelled.Results); Assert.Equal(1, cancelled.Results[0].Rows[0][0]);
        var error = await connector.ExecuteSqlAsync("warehouse", "SELECT CAST(N'not-an-int' AS int) AS InvalidValue;", 10, 200, [], CancellationToken.None);
        Assert.NotNull(error.Error); Assert.Contains("245", error.Error);
    }
}
