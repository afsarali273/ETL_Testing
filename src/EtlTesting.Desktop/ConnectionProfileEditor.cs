using System.Globalization;
using System.IO;
using EtlTesting.Core;
using EtlTesting.Persistence;
using EtlTesting.SqlServer;

namespace EtlTesting.Desktop;

public sealed class ConnectionProfileEditor : Observable
{
    private string name = "", environmentName = "Development", connectionKey = "", server = "", port = "", database = "", authentication = "integrated", credentialMode = "windows", userName = "", userVariable = "", passwordVariable = "", timeout = "15", pendingPassword = "";
    private bool encrypt = true, trustCertificate, dirty;
    private string? pendingCredentialTarget;
    private SavedConnectionProfile current;
    public SavedConnectionProfile? Saved { get; private set; }
    public string Id => current.Id;
    public ConnectionProfileEditor(SavedConnectionProfile profile, bool saved = true) { current = profile; Saved = saved ? profile : null; Load(profile); }
    public string Name { get => name; set => Change(ref name, value); }
    public string EnvironmentName { get => environmentName; set => Change(ref environmentName, value); }
    public string ConnectionKey { get => connectionKey; set => Change(ref connectionKey, value); }
    public string Server { get => server; set => Change(ref server, value); }
    public string Port { get => port; set => Change(ref port, value); }
    public string Database { get => database; set => Change(ref database, value); }
    public string Authentication { get => authentication; set { Change(ref authentication, value); Notify(nameof(IsSqlAuthentication)); Notify(nameof(IsWindowsAuthentication)); } }
    public string CredentialMode { get => credentialMode; set { Change(ref credentialMode, value); Notify(nameof(UseWindowsCredentials)); Notify(nameof(UseEnvironmentCredentials)); } }
    public string UserName { get => userName; set => Change(ref userName, value); }
    public string UserVariable { get => userVariable; set => Change(ref userVariable, value); }
    public string PasswordVariable { get => passwordVariable; set => Change(ref passwordVariable, value); }
    public string Timeout { get => timeout; set => Change(ref timeout, value); }
    public bool Encrypt { get => encrypt; set => Change(ref encrypt, value); }
    public bool TrustCertificate { get => trustCertificate; set => Change(ref trustCertificate, value); }
    // This field is transient and is never included in a serialized profile.
    public string PendingPassword { get => pendingPassword; set => Change(ref pendingPassword, value); }
    public bool IsSqlAuthentication => Authentication == "sql";
    public bool IsWindowsAuthentication => !IsSqlAuthentication;
    public bool UseWindowsCredentials => IsSqlAuthentication && CredentialMode == "windows";
    public bool UseEnvironmentCredentials => IsSqlAuthentication && CredentialMode == "environment";
    public bool KeyReadOnly => Saved != null;
    public bool IsDirty => dirty || Saved == null;
    public string SaveState => IsDirty ? "Unsaved changes" : "Saved on this device";
    public string VerificationStatus => current.VerificationStatus;
    public string VerificationMessage => current.VerificationMessage;
    public string CheckedAt => current.LastCheckedAt?.ToLocalTime().ToString("dd MMM yyyy · HH:mm:ss", CultureInfo.InvariantCulture) ?? "Not checked yet";
    public string ServerVersion => current.ServerVersion ?? "—";
    public string VisibleTables => current.VisibleTables?.ToString("N0") ?? "—";
    public string Latency => current.LatencyMilliseconds is long value ? $"{value:N0} ms" : "—";
    public string PasswordHint => current.Connection.CredentialTarget != null && WindowsCredentialStore.TryRead(current.Connection.CredentialTarget, out _) ? "Password saved securely. Leave blank to keep it." : "Password is saved in Windows Credential Manager.";
    public string EndpointSummary => string.IsNullOrWhiteSpace(Server) ? "Server not configured" : Server + (string.IsNullOrWhiteSpace(Port) ? "" : "," + Port);

    private void Change<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        if (property == nameof(PendingPassword) && !string.IsNullOrEmpty(pendingPassword)) pendingCredentialTarget ??= WindowsCredentialStore.TargetPrefix + Id + "/" + Guid.NewGuid().ToString("N");
        dirty = true;
        current = current with { LastCheckedAt = null, VerificationStatus = "Not tested", VerificationMessage = "Settings changed. Test this connection before use.", ServerVersion = null, VisibleTables = null, LatencyMilliseconds = null };
        Notify(property); Notify(nameof(IsDirty)); Notify(nameof(SaveState)); Notify(nameof(EndpointSummary)); VerificationChanged();
    }

    public ConnectionProfile BuildConnection(bool requireDatabase = true)
    {
        if (!int.TryParse(Timeout, out var seconds) || seconds is < 1 or > 300) throw new InvalidDataException("Connection timeout must be between 1 and 300 seconds.");
        var endpoint = Server.Trim();
        if (!string.IsNullOrWhiteSpace(Port))
        {
            if (!int.TryParse(Port, out var portNumber) || portNumber is < 1 or > 65535 || endpoint.Contains(',')) throw new InvalidDataException("Enter a port between 1 and 65535, with no port in the server field.");
            endpoint += "," + portNumber.ToString(CultureInfo.InvariantCulture);
        }
        var connection = new ConnectionProfile
        {
            Server = endpoint, Database = !requireDatabase && string.IsNullOrWhiteSpace(Database) ? "master" : Database.Trim(), Authentication = Authentication,
            Encrypt = Encrypt, TrustServerCertificate = TrustCertificate, ConnectionTimeoutSeconds = seconds,
            CredentialTarget = UseWindowsCredentials ? (string.IsNullOrEmpty(PendingPassword) ? current.Connection.CredentialTarget : pendingCredentialTarget) ?? WindowsCredentialStore.TargetPrefix + Id : null,
            UserEnvironmentVariable = UseEnvironmentCredentials ? UserVariable.Trim() : null,
            PasswordEnvironmentVariable = UseEnvironmentCredentials ? PasswordVariable.Trim() : null
        };
        ConnectionProfileStore.ValidateConnection(connection);
        if (UseWindowsCredentials)
        {
            if (!string.IsNullOrEmpty(PendingPassword) && string.IsNullOrWhiteSpace(UserName)) throw new InvalidDataException("Enter the SQL Server username.");
            if (string.IsNullOrEmpty(PendingPassword))
            {
                if (!WindowsCredentialStore.TryRead(connection.CredentialTarget!, out var stored)) throw new InvalidDataException("Enter the SQL Server username and password to save or test this profile.");
                if (!string.Equals(stored!.UserName, UserName, StringComparison.Ordinal)) throw new InvalidDataException("Enter the password when changing the SQL Server username.");
            }
        }
        return connection;
    }

    public SavedConnectionProfile ToDefinition() => current with { Name = Name.Trim(), EnvironmentName = EnvironmentName.Trim(), ConnectionKey = ConnectionKey.Trim(), Connection = BuildConnection() };

    public ICredentialResolver Credentials(ConnectionProfile connection) => new DraftCredentialResolver(connection.CredentialTarget, !string.IsNullOrEmpty(PendingPassword) && UseWindowsCredentials ? new(UserName.Trim(), PendingPassword) : null);

    public void Verify(string status, string message, ConnectionInspection? inspection, long? milliseconds)
    {
        current = current with { VerificationStatus = status, VerificationMessage = message, LastCheckedAt = DateTimeOffset.Now, ServerVersion = inspection?.ServerVersion, VisibleTables = inspection?.Tables.Count, LatencyMilliseconds = milliseconds };
        VerificationChanged();
    }

    public void MarkSaved(SavedConnectionProfile definition)
    {
        current = definition; Saved = definition; pendingPassword = ""; pendingCredentialTarget = null; dirty = false;
        Notify(nameof(Saved)); Notify(nameof(PendingPassword)); Notify(nameof(PasswordHint)); Notify(nameof(KeyReadOnly)); Notify(nameof(IsDirty)); Notify(nameof(SaveState)); VerificationChanged();
    }

    public void Revert() { if (Saved is not null) Load(Saved); }

    private void Load(SavedConnectionProfile profile)
    {
        current = profile;
        name = profile.Name; environmentName = profile.EnvironmentName; connectionKey = profile.ConnectionKey;
        server = profile.Connection.Server; port = "";
        var comma = server.LastIndexOf(',');
        if (comma > 0 && int.TryParse(server[(comma + 1)..], out _)) { port = server[(comma + 1)..]; server = server[..comma]; }
        database = profile.Connection.Database; authentication = profile.Connection.Authentication;
        credentialMode = profile.Connection.CredentialTarget != null || authentication == "integrated" ? "windows" : "environment";
        userName = profile.Connection.CredentialTarget is string target && WindowsCredentialStore.TryRead(target, out var stored) ? stored!.UserName : "";
        userVariable = profile.Connection.UserEnvironmentVariable ?? ""; passwordVariable = profile.Connection.PasswordEnvironmentVariable ?? "";
        timeout = profile.Connection.ConnectionTimeoutSeconds.ToString(CultureInfo.InvariantCulture); encrypt = profile.Connection.Encrypt; trustCertificate = profile.Connection.TrustServerCertificate;
        pendingPassword = ""; pendingCredentialTarget = null; dirty = false; Notify(null);
    }

    private void VerificationChanged()
    {
        foreach (var property in new[] { nameof(VerificationStatus), nameof(VerificationMessage), nameof(CheckedAt), nameof(ServerVersion), nameof(VisibleTables), nameof(Latency) }) Notify(property);
    }

    private sealed class DraftCredentialResolver(string? target, SqlCredentials? pending) : ICredentialResolver
    {
        public string Resolve(string variable) => new EnvironmentCredentialResolver().Resolve(variable);
        public SqlCredentials ResolveStored(string requested) => pending != null && requested == target ? pending : WindowsCredentialStore.Read(requested);
    }
}
