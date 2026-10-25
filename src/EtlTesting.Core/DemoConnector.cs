using System.Runtime.CompilerServices;

namespace EtlTesting.Core;

// Synthetic fixtures always travel with RunOptions.Demo=true in the supplied hosts.
public sealed class DemoConnector : IDataConnector
{
    public async IAsyncEnumerable<DataRow> ReadAsync(TableScope scope, string connection, IReadOnlyList<string> columns, IReadOnlyList<BoundParameter> parameters, int timeout, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var target = scope.Schema == "dbo";
        foreach (var i in Enumerable.Range(1, 8))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(12, cancellationToken);
            var values = new Dictionary<string, Cell>();
            foreach (var column in columns)
            {
                values[column] = column switch
                {
                    "PropertyCode" => Cell.From("SG"),
                    "ReservationId" or "CustomerId" or "PaymentId" => Cell.From($"{scope.Table[0]}{(target && i == 8 ? 9 : i):000}"),
                    "GuestName" or "Name" => Cell.From(target && i == 2 ? "Changed name" : $"Guest {i}"),
                    "NetAmount" or "RevenueAmount" or "Amount" => Cell.From(100m * i + (target && i == 2 ? 25m : 0m)),
                    "Status" => Cell.From("Active"),
                    _ => Cell.From(i)
                };
            }
            yield return new(values);
        }
    }
    public async Task<(long Count, Cell? Scalar, List<DataRow> Evidence)> QueryAsync(string connection, string query, IReadOnlyList<BoundParameter> parameters, int timeout, int evidenceLimit, CancellationToken cancellationToken)
    {
        await Task.Delay(80, cancellationToken);
        // Demo does not interpret arbitrary SQL. Its fixed fixture contains two orphan references.
        return (2, null, Enumerable.Range(1, Math.Min(2, evidenceLimit)).Select(i => new DataRow(new() { ["PaymentId"] = Cell.From($"P90{i}"), ["ReservationId"] = Cell.From($"R99{i}") })).ToList());
    }
}
