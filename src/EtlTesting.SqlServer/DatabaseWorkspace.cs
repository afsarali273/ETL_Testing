using System.Data;
using System.Diagnostics;
using System.Globalization;
using EtlTesting.Core;
using Microsoft.Data.SqlClient;

namespace EtlTesting.SqlServer;

public sealed record DatabaseObject(string Schema, string Name, string Kind, long? ApproximateRows)
{
    public string QualifiedName => SqlServerConnector.Quote(Schema) + "." + SqlServerConnector.Quote(Name);
    public string Description => Kind == "VIEW" ? "View" : $"Table · {ApproximateRows.GetValueOrDefault():N0} estimated rows";
}
public sealed record DatabaseColumn(string Name, string SqlType, bool Nullable, bool PrimaryKey, int Ordinal);
public sealed record ConnectionInspection(string Server, string Database, string ServerVersion, IReadOnlyList<string> Tables);
public sealed class SqlResultSet
{
    public List<string> Columns { get; } = [];
    public List<object?[]> Rows { get; } = [];
    public bool Truncated { get; set; }
    public int TruncatedCells { get; set; }
}
public sealed class SqlExecutionResult
{
    public List<SqlResultSet> Results { get; } = [];
    public string Message { get; set; } = "";
    public string? Error { get; set; }
    public bool Cancelled { get; set; }
    public bool Capped { get; set; }
    public long RecordsAffected { get; set; } = -1;
    public double DurationSeconds { get; set; }
}

public sealed partial class SqlServerConnector
{
    public async Task<ConnectionInspection> InspectAsync(string name, CancellationToken cancellationToken)
    {
        await using var db = await OpenAsync(name, cancellationToken);
        using var command = db.CreateCommand(); command.CommandTimeout = 15;
        command.CommandText = "SELECT s.name+'.'+t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id ORDER BY s.name,t.name";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var tables = new List<string>(); while (await reader.ReadAsync(cancellationToken)) tables.Add(reader.GetString(0));
        return new(db.DataSource, db.Database, db.ServerVersion, tables);
    }

    public async Task<IReadOnlyList<string>> ListDatabasesAsync(string name, CancellationToken cancellationToken)
    {
        if (!environment.Connections.TryGetValue(name, out var profile)) throw new InvalidDataException("Connection not found.");
        var serverScope = environment with { Connections = new() { [name] = profile with { Database = "master" } } };
        var connector = new SqlServerConnector(serverScope, credentials);
        await using var db = await connector.OpenAsync(name, cancellationToken);
        using var command = db.CreateCommand(); command.CommandTimeout = 15;
        command.CommandText = "SELECT name FROM sys.databases WHERE state=0 AND HAS_DBACCESS(name)=1 ORDER BY name";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<string>(); while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetString(0));
        return result;
    }

    public async Task<IReadOnlyList<DatabaseObject>> CatalogAsync(string name, CancellationToken cancellationToken)
    {
        await using var db = await OpenAsync(name, cancellationToken);
        using var command = db.CreateCommand(); command.CommandTimeout = 30;
        command.CommandText = """
            SELECT s.name,o.name,o.type,CASE WHEN o.type='U' THEN
                (SELECT SUM(p.rows) FROM sys.partitions p WHERE p.object_id=o.object_id AND p.index_id IN(0,1)) END
            FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
            WHERE o.type IN('U','V') AND o.is_ms_shipped=0 ORDER BY s.name,o.name
            """;
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<DatabaseObject>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2).Trim() == "V" ? "VIEW" : "TABLE", reader.IsDBNull(3) ? null : reader.GetInt64(3)));
        return result;
    }

    public async Task<IReadOnlyList<DatabaseColumn>> ColumnsAsync(string name, DatabaseObject table, CancellationToken cancellationToken)
    {
        await using var db = await OpenAsync(name, cancellationToken);
        using var command = db.CreateCommand(); command.CommandTimeout = 30;
        command.CommandText = """
            SELECT c.name,ty.name,c.max_length,c.precision,c.scale,c.is_nullable,c.column_id,
                CAST(CASE WHEN EXISTS(SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id=ic.object_id AND i.index_id=ic.index_id
                WHERE ic.object_id=c.object_id AND ic.column_id=c.column_id AND i.is_primary_key=1) THEN 1 ELSE 0 END AS bit)
            FROM sys.columns c JOIN sys.objects o ON o.object_id=c.object_id JOIN sys.schemas s ON s.schema_id=o.schema_id
            JOIN sys.types ty ON ty.user_type_id=c.user_type_id
            WHERE s.name=@schema AND o.name=@table AND o.type IN('U','V') ORDER BY c.column_id
            """;
        command.Parameters.Add(new SqlParameter("@schema", SqlDbType.NVarChar, 128) { Value = table.Schema });
        command.Parameters.Add(new SqlParameter("@table", SqlDbType.NVarChar, 128) { Value = table.Name });
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<DatabaseColumn>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var type = reader.GetString(1); var length = reader.GetInt16(2);
            var display = type switch
            {
                "nvarchar" or "nchar" => $"{type}({(length == -1 ? "max" : (length / 2).ToString(CultureInfo.InvariantCulture))})",
                "varchar" or "char" or "varbinary" or "binary" => $"{type}({(length == -1 ? "max" : length.ToString(CultureInfo.InvariantCulture))})",
                "decimal" or "numeric" => $"{type}({reader.GetByte(3)},{reader.GetByte(4)})",
                "datetime2" or "datetimeoffset" or "time" => $"{type}({reader.GetByte(4)})",
                _ => type
            };
            result.Add(new(reader.GetString(0), display, reader.GetBoolean(5), reader.GetBoolean(7), reader.GetInt32(6)));
        }
        if (result.Count == 0) throw new InvalidDataException("No columns are visible. Refresh the catalog or check metadata permissions.");
        return result;
    }

    public static string PreviewSql(DatabaseObject table, IReadOnlyList<DatabaseColumn> columns)
    {
        if (columns.Count == 0) throw new InvalidDataException("No visible columns to preview.");
        var keys = columns.Where(c => c.PrimaryKey).OrderBy(c => c.Ordinal).ToList();
        var ordering = keys.Count > 0 ? string.Join(", ", keys.Select(c => Quote(c.Name))) : "(SELECT NULL)";
        return $"SELECT {string.Join(", ", columns.Select(c => Quote(c.Name)))}\nFROM {table.QualifiedName}\nORDER BY {ordering}\nOFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;";
    }

    public Task<SqlExecutionResult> PreviewAsync(string name, DatabaseObject table, IReadOnlyList<DatabaseColumn> columns, long offset, int limit, CancellationToken cancellationToken)
    {
        if (offset < 0) throw new InvalidDataException("Invalid preview page.");
        return ExecuteSqlAsync(name, PreviewSql(table, columns), 60, limit,
            [new("offset", "int64", offset), new("pageSize", "int64", (long)limit + 1)], cancellationToken);
    }

    public async Task<SqlExecutionResult> ExecuteSqlAsync(string name, string query, int timeout, int rowLimit, IReadOnlyList<BoundParameter> parameters, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new InvalidDataException("Enter SQL to execute.");
        if (timeout is < 1 or > 600 || rowLimit is < 1 or > 1000) throw new InvalidDataException("Query timeout must be 1–600 seconds; result limit must be 1–1,000 rows.");
        var watch = Stopwatch.StartNew();
        var result = new SqlExecutionResult();
        var remainingCharacters = 4_000_000;
        var remainingCells = 50_000;
        try
        {
            await using var db = await OpenAsync(name, cancellationToken);
            var messages = new List<string>();
            db.InfoMessage += (_, args) => { if (messages.Sum(m => m.Length) < 16000) messages.Add(args.Message); };
            using var command = Command(db, query, parameters, timeout);
            using var cancellationRegistration = cancellationToken.Register(() => { try { command.Cancel(); } catch (InvalidOperationException) { } });
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
            do
            {
                if (reader.FieldCount == 0) continue;
                var set = new SqlResultSet(); result.Results.Add(set);
                for (var i = 0; i < Math.Min(reader.FieldCount, 200); i++) set.Columns.Add(reader.GetName(i));
                if (reader.FieldCount > 200) messages.Add("Only the first 200 columns are displayed.");
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (set.Rows.Count == rowLimit || remainingCells < set.Columns.Count || remainingCharacters <= 0) { set.Truncated = result.Capped = true; command.Cancel(); break; }
                    var values = new object?[set.Columns.Count];
                    for (var i = 0; i < values.Length; i++)
                    {
                        if (await reader.IsDBNullAsync(i, cancellationToken)) { values[i] = null; continue; }
                        var type = reader.GetFieldType(i);
                        if (type == typeof(string))
                        {
                            using var text = reader.GetTextReader(i); var characterLimit = Math.Min(4096, Math.Max(0, remainingCharacters)); var buffer = new char[characterLimit + 1];
                            var count = await text.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
                            values[i] = new string(buffer, 0, Math.Min(count, characterLimit)) + (count > characterLimit ? " … [cell truncated]" : "");
                            remainingCharacters -= Math.Min(count, characterLimit);
                            if (count > characterLimit) set.TruncatedCells++;
                        }
                        else if (type == typeof(byte[]))
                        {
                            var size = reader.GetBytes(i, 0, null, 0, 0); var buffer = new byte[Math.Min(size, 64)];
                            _ = reader.GetBytes(i, 0, buffer, 0, buffer.Length);
                            values[i] = "0x" + Convert.ToHexString(buffer) + (size > 64 ? $" … [{size:N0} bytes]" : "");
                            if (size > 64) set.TruncatedCells++;
                        }
                        else values[i] = reader.GetValue(i);
                    }
                    set.Rows.Add(values);
                    remainingCells -= values.Length;
                }
                if (result.Capped) break;
                if (result.Results.Count == 10) { result.Capped = true; command.Cancel(); messages.Add("Stopped at 10 result sets."); break; }
            }
            while (await reader.NextResultAsync(cancellationToken));
            result.RecordsAffected = reader.RecordsAffected;
            result.Message = string.Join(Environment.NewLine, messages);
        }
        catch (Exception e) when (cancellationToken.IsCancellationRequested && e is OperationCanceledException or SqlException)
        {
            result.Cancelled = true; result.Message = "Cancelled. Displayed results are partial; earlier statements may already have executed.";
        }
        catch (SqlException e) { result.Error = $"SQL {e.Number}, line {e.LineNumber}: {e.Message}"; }
        finally { result.DurationSeconds = watch.Elapsed.TotalSeconds; }
        return result;
    }
}
