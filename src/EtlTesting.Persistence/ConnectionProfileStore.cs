using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using EtlTesting.Core;

namespace EtlTesting.Persistence;

public sealed record SavedConnectionProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public string EnvironmentName { get; init; } = "Development";
    public string ConnectionKey { get; init; } = "warehouse";
    public ConnectionProfile Connection { get; init; } = new();
    public DateTimeOffset? LastCheckedAt { get; init; }
    public string VerificationStatus { get; init; } = "Not tested";
    public string VerificationMessage { get; init; } = "Test this connection to verify access.";
    public string? ServerVersion { get; init; }
    public int? VisibleTables { get; init; }
    public long? LatencyMilliseconds { get; init; }
}

public sealed record ConnectionLibrary
{
    public int FormatVersion { get; init; } = 1;
    public string? ActiveProfileId { get; init; }
    public List<SavedConnectionProfile> Profiles { get; init; } = [];

    public EnvironmentDefinition EnvironmentFor(string profileId)
    {
        var profile = Profiles.SingleOrDefault(p => p.Id == profileId) ?? throw new InvalidDataException("Saved connection profile not found.");
        return new()
        {
            Name = profile.EnvironmentName,
            Connections = Profiles.Where(p => string.Equals(p.EnvironmentName, profile.EnvironmentName, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(p => p.ConnectionKey, p => p.Connection, StringComparer.Ordinal)
        };
    }
}

public sealed class ConnectionProfileStore(string path)
{
    private string? expectedHash;
    public string FilePath => Path.GetFullPath(path);

    public async Task<ConnectionLibrary> LoadAsync(EnvironmentDefinition legacy, CancellationToken cancellationToken = default, bool importLegacyChanges = false)
    {
        if (File.Exists(FilePath))
        {
            var text = await File.ReadAllTextAsync(FilePath, cancellationToken);
            var saved = StudioJson.Read<ConnectionLibrary>(text);
            Validate(saved);
            expectedHash = Hash(text);
            if (importLegacyChanges && legacy.Connections.Count > 0)
            {
                bool Matches(EnvironmentDefinition candidate) => candidate.Name.Equals(legacy.Name, StringComparison.OrdinalIgnoreCase) && candidate.Connections.Count == legacy.Connections.Count && candidate.Connections.All(p => legacy.Connections.TryGetValue(p.Key, out var connection) && connection == p.Value);
                if (saved.ActiveProfileId == null || !Matches(saved.EnvironmentFor(saved.ActiveProfileId)))
                {
                    var matching = saved.Profiles.FirstOrDefault(p => Matches(saved.EnvironmentFor(p.Id)));
                    if (matching != null) saved = saved with { ActiveProfileId = matching.Id };
                    else
                    {
                        var environmentName = legacy.Name; var suffix = 2;
                        while (saved.Profiles.Any(p => p.EnvironmentName.Equals(environmentName, StringComparison.OrdinalIgnoreCase))) environmentName = legacy.Name + " (imported " + suffix++ + ")";
                        var imported = legacy.Connections.Select(p => new SavedConnectionProfile { Name = p.Value.Database, EnvironmentName = environmentName, ConnectionKey = p.Key, Connection = p.Value }).ToList();
                        saved = saved with { Profiles = saved.Profiles.Concat(imported).ToList(), ActiveProfileId = imported.FirstOrDefault(p => p.ConnectionKey == "warehouse")?.Id ?? imported[0].Id };
                    }
                    await SaveAsync(saved, cancellationToken);
                }
            }
            return saved;
        }
        var profiles = legacy.Connections.Select(p => new SavedConnectionProfile
        {
            Name = p.Value.Database.Length > 0 ? p.Value.Database : p.Key,
            EnvironmentName = legacy.Name,
            ConnectionKey = p.Key,
            Connection = p.Value
        }).ToList();
        var migrated = new ConnectionLibrary { Profiles = profiles, ActiveProfileId = profiles.FirstOrDefault()?.Id };
        await SaveAsync(migrated, cancellationToken);
        return migrated;
    }

    public async Task SaveAsync(ConnectionLibrary library, CancellationToken cancellationToken = default)
    {
        Validate(library);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        await using var writerLock = new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        var currentHash = File.Exists(FilePath) ? Hash(await File.ReadAllTextAsync(FilePath, cancellationToken)) : null;
        if (currentHash != expectedHash) throw new InvalidDataException("Saved connections changed in another window. Reopen the application before saving, so those profiles are preserved.");
        var text = StudioJson.Write(library);
        await AtomicFile.WriteAsync(FilePath, text, cancellationToken);
        expectedHash = Hash(text);
    }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static void Validate(ConnectionLibrary library)
    {
        if (library.FormatVersion != 1) throw new InvalidDataException("This connection library version is not supported.");
        if (library.Profiles == null) throw new InvalidDataException("A connection library must contain a profiles array.");
        if (library.Profiles.Count > 1000) throw new InvalidDataException("A library can contain at most 1,000 profiles.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in library.Profiles)
        {
            if (profile == null || profile.Connection == null) throw new InvalidDataException("Connection profile entries cannot be null.");
            if (!Guid.TryParseExact(profile.Id, "N", out _) || !ids.Add(profile.Id)) throw new InvalidDataException("Connection profiles require unique stable IDs.");
            if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 120) throw new InvalidDataException("Profile name must be 1–120 characters.");
            if (string.IsNullOrWhiteSpace(profile.EnvironmentName) || profile.EnvironmentName.Length > 80) throw new InvalidDataException($"{profile.Name}: environment is required (maximum 80 characters).");
            if (string.IsNullOrWhiteSpace(profile.ConnectionKey) || !Regex.IsMatch(profile.ConnectionKey, "^[A-Za-z0-9][A-Za-z0-9_.-]{0,79}$")) throw new InvalidDataException($"{profile.Name}: connection key must use letters, digits, dots, underscores or hyphens.");
            if (!keys.Add(profile.EnvironmentName.Trim() + "\0" + profile.ConnectionKey)) throw new InvalidDataException($"{profile.Name}: connection key '{profile.ConnectionKey}' is already used in {profile.EnvironmentName}. Choose another key to use both databases in a suite.");
            ValidateConnection(profile.Connection);
        }
        if (library.ActiveProfileId != null && !ids.Contains(library.ActiveProfileId)) throw new InvalidDataException("The active connection profile does not exist in this library.");
    }

    public static void ValidateConnection(ConnectionProfile connection)
    {
        if (string.IsNullOrWhiteSpace(connection.Server) || connection.Server.Length > 255 || connection.Server.Any(char.IsControl)) throw new InvalidDataException("Enter a server name, named instance or local SQL Server address.");
        if (string.IsNullOrWhiteSpace(connection.Database) || connection.Database.Length > 128 || connection.Database.Any(char.IsControl)) throw new InvalidDataException("Enter a database name (maximum 128 characters).");
        if (connection.ConnectionTimeoutSeconds is < 1 or > 300) throw new InvalidDataException("Connection timeout must be between 1 and 300 seconds.");
        if (connection.Authentication is not ("integrated" or "sql")) throw new InvalidDataException("Choose Windows or SQL Server authentication.");
        if (connection.Authentication == "sql")
        {
            if (connection.CredentialTarget != null)
            {
                if (string.IsNullOrWhiteSpace(connection.CredentialTarget) || connection.UserEnvironmentVariable != null || connection.PasswordEnvironmentVariable != null) throw new InvalidDataException("Choose one SQL credential source: Windows Credential Manager or environment variables.");
            }
            else if (string.IsNullOrWhiteSpace(connection.UserEnvironmentVariable) || string.IsNullOrWhiteSpace(connection.PasswordEnvironmentVariable)) throw new InvalidDataException("Both SQL username and password environment-variable names are required.");
        }
        else if (connection.CredentialTarget != null || connection.UserEnvironmentVariable != null || connection.PasswordEnvironmentVariable != null) throw new InvalidDataException("Windows authentication does not use SQL credential references.");
    }

    public static List<SavedConnectionProfile> ParseImport(string text)
    {
        using var document = System.Text.Json.JsonDocument.Parse(text);
        if (document.RootElement.TryGetProperty("formatVersion", out _))
        {
            var library = StudioJson.Read<ConnectionLibrary>(text); Validate(library);
            // Verification belongs to this machine and identity, never to an imported claim.
            return library.Profiles.Select(ClearVerification).ToList();
        }
        var environment = StudioJson.Read<EnvironmentDefinition>(text);
        var profiles = environment.Connections.Select(p => new SavedConnectionProfile { Name = p.Value.Database, EnvironmentName = environment.Name, ConnectionKey = p.Key, Connection = p.Value }).ToList();
        Validate(new() { Profiles = profiles });
        return profiles;
    }

    public static SavedConnectionProfile ClearVerification(SavedConnectionProfile profile) => profile with { LastCheckedAt = null, VerificationStatus = "Not tested", VerificationMessage = "Imported profile. Test access on this computer.", ServerVersion = null, VisibleTables = null, LatencyMilliseconds = null };
}
