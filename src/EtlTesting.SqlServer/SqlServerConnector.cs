using System.Data;
using System.Runtime.CompilerServices;
using EtlTesting.Core;
using Microsoft.Data.SqlClient;
using DataRow = EtlTesting.Core.DataRow;

namespace EtlTesting.SqlServer;

public interface ICredentialResolver
{
    string Resolve(string environmentVariable);
    SqlCredentials ResolveStored(string target) => WindowsCredentialStore.Read(target);
}
public sealed class EnvironmentCredentialResolver : ICredentialResolver
{
    public string Resolve(string environmentVariable) => Environment.GetEnvironmentVariable(environmentVariable) ?? throw new InvalidDataException($"Credential environment variable '{environmentVariable}' is missing");
}
public sealed partial class SqlServerConnector(EnvironmentDefinition environment, ICredentialResolver credentials) : IDataConnector
{
    public static string Quote(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Length > 128 || identifier.Contains('\0')) throw new InvalidDataException("Invalid SQL identifier");
        return "[" + identifier.Replace("]", "]]") + "]";
    }
    public static string SelectSql(TableScope scope, IEnumerable<string> columns) => $"SELECT {string.Join(",", columns.Select(Quote))} FROM {Quote(scope.Schema)}.{Quote(scope.Table)}" + (string.IsNullOrWhiteSpace(scope.Filter) ? "" : " WHERE " + scope.Filter);
    public static SqlParameter Parameter(BoundParameter p)
    {
        var type = p.Type switch { "string" => SqlDbType.NVarChar, "int64" => SqlDbType.BigInt, "decimal" => SqlDbType.Decimal, "boolean" => SqlDbType.Bit, "datetime" => SqlDbType.DateTime2, "datetimeoffset" => SqlDbType.DateTimeOffset, "guid" => SqlDbType.UniqueIdentifier, _ => throw new InvalidDataException("Unsupported parameter type") };
        var result = new SqlParameter("@" + p.Name, type) { Value = p.Value };
        if (type == SqlDbType.NVarChar) result.Size = -1;
        if (type == SqlDbType.Decimal) { result.Precision = 38; result.Scale = (byte)((decimal.GetBits((decimal)p.Value)[3] >> 16) & 0x7f); }
        return result;
    }
    private string ConnectionString(string name)
    {
        if (!environment.Connections.TryGetValue(name, out var profile)) throw new InvalidDataException($"connections.{name}: logical connection not found");
        if (profile.ConnectionTimeoutSeconds < 1 || string.IsNullOrWhiteSpace(profile.Server) || string.IsNullOrWhiteSpace(profile.Database)) throw new InvalidDataException($"connections.{name}: server, database and positive connection timeout required");
        var builder = new SqlConnectionStringBuilder { DataSource = profile.Server, InitialCatalog = profile.Database, IntegratedSecurity = profile.Authentication == "integrated", Encrypt = profile.Encrypt, TrustServerCertificate = profile.TrustServerCertificate, ConnectTimeout = profile.ConnectionTimeoutSeconds, ApplicationName = Product.Name, PersistSecurityInfo = false };
        if (profile.Authentication == "sql")
        {
            if (profile.CredentialTarget is string target)
            {
                var stored = credentials.ResolveStored(target); builder.UserID = stored.UserName; builder.Password = stored.Password;
            }
            else
            {
                builder.UserID = credentials.Resolve(profile.UserEnvironmentVariable ?? throw new InvalidDataException("User credential reference required"));
                builder.Password = credentials.Resolve(profile.PasswordEnvironmentVariable ?? throw new InvalidDataException("Password credential reference required"));
            }
        }
        else if (profile.Authentication != "integrated") throw new InvalidDataException("Authentication must be integrated or sql");
        return builder.ConnectionString;
    }
    public void Preflight(Suite suite, IReadOnlyList<BoundParameter> parameters)
    {
        foreach (var test in suite.Tests.Where(t => t.Enabled))
        {
            var name = test.Connection ?? suite.Defaults.Connection;
            _ = ConnectionString(name);
            if (test.Source != null) _ = ConnectionString(test.Source.Connection ?? name);
            if (test.Target != null) _ = ConnectionString(test.Target.Connection ?? name);
        }
        foreach (var p in parameters) _ = Parameter(p);
    }
    private async Task<SqlConnection> OpenAsync(string name, CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(ConnectionString(name));
        try { await connection.OpenAsync(cancellationToken); return connection; }
        catch (SqlException e)
        {
            await connection.DisposeAsync();
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw new InvalidDataException($"Connection '{name}' failed (SQL {e.Number}). Verify server, database, identity, network and TLS certificate. Server certificate validation is enabled unless explicitly configured otherwise.");
        }
    }
    private static SqlCommand Command(SqlConnection db, string query, IReadOnlyList<BoundParameter> parameters, int timeout)
    {
        var command = db.CreateCommand(); command.CommandText = query; command.CommandTimeout = timeout;
        foreach (var p in parameters) command.Parameters.Add(Parameter(p));
        return command;
    }
    public async Task<IReadOnlyList<string>> MetadataAsync(string name, CancellationToken cancellationToken)
    {
        await using var db = await OpenAsync(name, cancellationToken);
        using var command = db.CreateCommand(); command.CommandTimeout = 15;
        command.CommandText = "SELECT s.name+'.'+t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id ORDER BY s.name,t.name";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<string>(); while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetString(0));
        return result;
    }
    public async IAsyncEnumerable<DataRow> ReadAsync(TableScope scope, string connection, IReadOnlyList<string> columns, IReadOnlyList<BoundParameter> parameters, int timeout, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var db = await OpenAsync(connection, cancellationToken);
        using (var metadata = db.CreateCommand())
        {
            metadata.CommandTimeout = timeout;
            metadata.CommandText = "SELECT c.name FROM sys.columns c JOIN sys.tables t ON c.object_id=t.object_id JOIN sys.schemas s ON t.schema_id=s.schema_id WHERE s.name=@schema AND t.name=@table";
            metadata.Parameters.Add(new SqlParameter("@schema", SqlDbType.NVarChar, 128) { Value = scope.Schema });
            metadata.Parameters.Add(new SqlParameter("@table", SqlDbType.NVarChar, 128) { Value = scope.Table });
            using var reader = await metadata.ExecuteReaderAsync(cancellationToken);
            var available = new HashSet<string>(StringComparer.Ordinal);
            while (await reader.ReadAsync(cancellationToken)) available.Add(reader.GetString(0));
            foreach (var column in columns) if (!available.Contains(column)) throw new InvalidDataException($"{scope.Schema}.{scope.Table}.{column}: column unavailable; verify spelling and metadata permissions");
        }
        using var command = Command(db, SelectSql(scope, columns), parameters, timeout);
        using var data = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        while (await data.ReadAsync(cancellationToken)) yield return ReadRow(data);
    }
    private static DataRow ReadRow(SqlDataReader reader)
    {
        var values = new Dictionary<string, Cell>(StringComparer.Ordinal);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (!values.TryAdd(reader.GetName(i), Cell.From(reader.GetValue(i)))) throw new InvalidDataException("Query columns require unique names");
        }
        return new(values);
    }
    public async Task<(long Count, Cell? Scalar, List<DataRow> Evidence)> QueryAsync(string connection, string query, IReadOnlyList<BoundParameter> parameters, int timeout, int evidenceLimit, CancellationToken cancellationToken)
    {
        await using var db = await OpenAsync(connection, cancellationToken);
        using var command = Command(db, query, parameters, timeout);
        using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        long count = 0; Cell? scalar = null; var evidence = new List<DataRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (evidence.Count < evidenceLimit || count == 0 && reader.FieldCount == 1)
            {
                var row = ReadRow(reader);
                if (count == 0 && reader.FieldCount == 1) scalar = row.Values.Values.Single();
                if (evidence.Count < evidenceLimit) evidence.Add(row);
            }
            count++;
        }
        if (await reader.NextResultAsync(cancellationToken)) throw new InvalidDataException("SQL assertions must return exactly one result set");
        return (count, scalar, evidence);
    }
}

