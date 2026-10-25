using System.Runtime.CompilerServices;
using EtlTesting.Core;
using Microsoft.Data.Sqlite;

namespace EtlTesting.Persistence;

public sealed class SqliteRowIndexFactory : IRowIndexFactory
{
    public async Task<IRowIndex> CreateAsync(string directory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"index-{Guid.NewGuid():N}.db");
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        await db.OpenAsync(cancellationToken);
        using var command = db.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA cache_size=-4096; CREATE TABLE entries(side INTEGER, key TEXT COLLATE BINARY, nullkey INTEGER, payload TEXT);";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new SqliteRowIndex(db, path);
    }
}
internal sealed class SqliteRowIndex(SqliteConnection db, string path) : IRowIndex
{
    private SqliteTransaction? transaction;
    private int pending;
    public async Task AddAsync(int side, string key, bool nullKey, DataRow row, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        transaction ??= db.BeginTransaction();
        using var command = db.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO entries VALUES($side,$key,$nullkey,$payload)";
        command.Parameters.AddWithValue("$side", side); command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$nullkey", nullKey ? 1 : 0); command.Parameters.AddWithValue("$payload", StudioJson.Write(row));
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (++pending >= 1000) { transaction.Commit(); transaction.Dispose(); transaction = null; pending = 0; }
    }
    public async IAsyncEnumerable<IndexedPair> PairsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        transaction?.Commit(); transaction?.Dispose(); transaction = null;
        using var command = db.CreateCommand();
        command.CommandText = "CREATE INDEX bykey ON entries(key,side)";
        await command.ExecuteNonQueryAsync(cancellationToken);
        command.CommandText = """
            SELECT key, SUM(CASE WHEN side=0 THEN 1 ELSE 0 END), MAX(CASE WHEN side=0 THEN payload END),
            SUM(CASE WHEN side=1 THEN 1 ELSE 0 END), MAX(CASE WHEN side=1 THEN payload END), MAX(nullkey)
            FROM entries GROUP BY key ORDER BY key COLLATE BINARY
            """;
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new(reader.GetString(0), reader.GetInt64(1), reader.IsDBNull(2) ? null : StudioJson.Read<DataRow>(reader.GetString(2)), reader.GetInt64(3), reader.IsDBNull(4) ? null : StudioJson.Read<DataRow>(reader.GetString(4)), reader.GetInt64(5) != 0);
        }
    }
    public async ValueTask DisposeAsync()
    {
        transaction?.Dispose(); await db.DisposeAsync();
        File.Delete(path);
    }
}
