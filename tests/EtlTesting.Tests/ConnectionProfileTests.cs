using EtlTesting.Core;
using EtlTesting.Persistence;
using Xunit;

namespace EtlTesting.Tests;

public sealed class ConnectionProfileTests
{
    private static SavedConnectionProfile Profile(string name, string server, string database, string key = "warehouse", string environment = "QA") => new() { Name = name, EnvironmentName = environment, ConnectionKey = key, Connection = new() { Server = server, Database = database } };

    [Fact] public async Task LegacyEnvironmentMigratesAllServersWithoutChangingTheirConnectionSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "etl-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConnectionProfileStore(Path.Combine(directory, "profiles-v1.json"));
            var legacy = new EnvironmentDefinition { Name = "Development", Connections = new() { ["source"] = new() { Server = "server-a", Database = "Source", TrustServerCertificate = true }, ["warehouse"] = new() { Server = "server-b,1444", Database = "Target" } } };
            var library = await store.LoadAsync(legacy);
            Assert.Equal(2, library.Profiles.Count); Assert.NotNull(library.ActiveProfileId);
            var restored = await store.LoadAsync(new());
            var environment = restored.EnvironmentFor(restored.ActiveProfileId!);
            Assert.Equal(legacy.Connections["source"], environment.Connections["source"]); Assert.Equal(legacy.Connections["warehouse"], environment.Connections["warehouse"]);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact] public async Task MultipleProfilesAndActiveSelectionRoundTripAndKeepSeparateEnvironments()
    {
        var directory = Path.Combine(Path.GetTempPath(), "etl-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var qa = Profile("QA warehouse", "sql-qa", "Warehouse"); var source = Profile("QA source", "sql-source", "Staging", "source"); var production = Profile("Production warehouse", "sql-prod", "Warehouse", environment: "Production");
            var store = new ConnectionProfileStore(Path.Combine(directory, "profiles-v1.json"));
            await store.SaveAsync(new() { ActiveProfileId = production.Id, Profiles = [qa, source, production] });
            var loaded = await store.LoadAsync(new()); Assert.Equal(production.Id, loaded.ActiveProfileId);
            Assert.Single(loaded.EnvironmentFor(production.Id).Connections); Assert.Equal(2, loaded.EnvironmentFor(qa.Id).Connections.Count);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact] public void AmbiguousLogicalKeysAndUnsupportedVersionsAreRejected()
    {
        var profile = Profile("Warehouse", "sql", "DB");
        Assert.Throws<InvalidDataException>(() => ConnectionProfileStore.Validate(new() { Profiles = [profile, profile with { Id = Guid.NewGuid().ToString("N"), Name = "Duplicate" }] }));
        Assert.Throws<InvalidDataException>(() => ConnectionProfileStore.Validate(new() { FormatVersion = 2 }));
        Assert.Throws<InvalidDataException>(() => ConnectionProfileStore.Validate(new() { Profiles = [profile], ActiveProfileId = Guid.NewGuid().ToString("N") }));
    }
    [Fact] public void ImportNeverClaimsAnotherMachinesVerification()
    {
        var profile = Profile("Verified on another PC", "sql", "DB") with { VerificationStatus = "Verified", LastCheckedAt = DateTimeOffset.Now, LatencyMilliseconds = 1, VisibleTables = 40 };
        var imported = Assert.Single(ConnectionProfileStore.ParseImport(StudioJson.Write(new ConnectionLibrary { ActiveProfileId = profile.Id, Profiles = [profile] })));
        Assert.Equal("Not tested", imported.VerificationStatus); Assert.Null(imported.LastCheckedAt); Assert.Null(imported.VisibleTables);
    }
    [Fact] public void SqlCredentialReferencesAreValidatedAndPlaintextPasswordFieldsAreRejected()
    {
        var profile = Profile("SQL auth", "sql", "DB") with { Connection = new() { Server = "sql", Database = "DB", Authentication = "sql", CredentialTarget = "EtlTestStudio/profile/123" } };
        var serialized = StudioJson.Write(new ConnectionLibrary { Profiles = [profile] });
        Assert.DoesNotContain("\"password\":", serialized, StringComparison.OrdinalIgnoreCase);
        ConnectionProfileStore.Validate(new() { Profiles = [profile] });
        Assert.Throws<InvalidDataException>(() => ConnectionProfileStore.ValidateConnection(profile.Connection with { UserEnvironmentVariable = "ETL_USER" }));
        Assert.Throws<System.Text.Json.JsonException>(() => ConnectionProfileStore.ParseImport(serialized.Replace("\"authentication\": \"sql\"", "\"authentication\": \"sql\", \"password\": \"secret\"")));
    }
    [Fact] public void TablePreviewQuotesNamesAndOrdersByTheCompositePrimaryKey()
    {
        var table = new EtlTesting.SqlServer.DatabaseObject("a]b", "T", "TABLE", 10);
        var sql = EtlTesting.SqlServer.SqlServerConnector.PreviewSql(table, [new("Key1", "int", false, true, 1), new("Key2", "int", false, true, 2), new("Name", "nvarchar(20)", true, false, 3)]);
        Assert.Contains("[a]]b].[T]", sql); Assert.Contains("ORDER BY [Key1], [Key2]", sql); Assert.Contains("OFFSET @offset ROWS FETCH NEXT @pageSize", sql);
    }
    [Fact] public async Task AStaleWindowCannotOverwriteProfilesSavedByAnotherWindow()
    {
        var directory = Path.Combine(Path.GetTempPath(), "etl-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "profiles-v1.json"); var first = new ConnectionProfileStore(path);
            var original = await first.LoadAsync(new()); var second = new ConnectionProfileStore(path); await second.LoadAsync(new());
            var profile = Profile("Added by first window", "sql", "DB"); await first.SaveAsync(original with { Profiles = [profile] });
            await Assert.ThrowsAsync<InvalidDataException>(() => second.SaveAsync(original));
            Assert.Equal(profile.Id, Assert.Single((await new ConnectionProfileStore(path).LoadAsync(new())).Profiles).Id);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact] public async Task AChangedLegacyEnvironmentBecomesAnotherProfileInsteadOfOverwritingSavedServers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "etl-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConnectionProfileStore(Path.Combine(directory, "profiles-v1.json"));
            var legacy = new EnvironmentDefinition { Name = "Development", Connections = new() { ["warehouse"] = new() { Server = "old-server", Database = "DB" } } };
            var first = await store.LoadAsync(legacy);
            var changed = legacy with { Connections = new() { ["warehouse"] = legacy.Connections["warehouse"] with { Server = "new-server" } } };
            var loaded = await store.LoadAsync(changed, importLegacyChanges: true);
            Assert.Equal(2, loaded.Profiles.Count); Assert.Contains(loaded.Profiles, p => p.Id == first.Profiles[0].Id && p.Connection.Server == "old-server");
            Assert.Equal("new-server", loaded.EnvironmentFor(loaded.ActiveProfileId!).Connections["warehouse"].Server);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
