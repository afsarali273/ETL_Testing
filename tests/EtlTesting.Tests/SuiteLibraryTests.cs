using EtlTesting.Core;
using EtlTesting.Persistence;
using Xunit;

namespace EtlTesting.Tests;
public sealed class SuiteLibraryTests
{
    private static Suite Flat() => new() { Id = "suite", Name = "Reservations", Tests = [new() { Id = "check", Name = "Check", Type = "sqlAssertion", Query = "SELECT 1 WHERE 1=0", Expect = new() { RowCount = 0 } }] };
    [Fact] public void GroupingPreservesChecksAndCopyRemapsEveryParent()
    {
        var flat = Flat(); var grouped = SuiteHierarchy.ForEditing(flat);
        Assert.Empty(flat.TestCases); Assert.Equal("check", grouped.Tests[0].Id);
        Assert.Equal(grouped.TestCases[0].Id, grouped.Tests[0].TestCaseId);
        var copy = SuiteHierarchy.Duplicate(grouped, "Copy");
        Assert.NotEqual(grouped.Id, copy.Id); Assert.NotEqual(grouped.Tests[0].Id, copy.Tests[0].Id);
        Assert.Equal(copy.TestCases[0].Id, copy.Tests[0].TestCaseId);
        _ = DefinitionLoader.Parse(StudioJson.Write(copy));
        Assert.Throws<InvalidDataException>(() => DefinitionLoader.Parse(StudioJson.Write(grouped with { Tests = [grouped.Tests[0] with { TestCaseId = "missing" }] })));
    }
    [Fact] public async Task DisabledParentSkipsItsChecksAndResultsRetainTestIdentity()
    {
        var grouped = SuiteHierarchy.ForEditing(EngineTests.One());
        grouped = grouped with { TestCases = [grouped.TestCases[0] with { Enabled = false }] };
        var run = await new SuiteRunner([]).RunAsync(grouped, "DEMO", new DemoConnector(), DefinitionLoader.Bind(grouped, new Dictionary<string, string>()), new(Path.GetTempPath(), Demo: true), null, CancellationToken.None);
        var result = Assert.Single(run.Tests); Assert.Equal("Skipped", result.Status);
        Assert.Equal(grouped.TestCases[0].Id, result.TestCaseId); Assert.Equal(grouped.TestCases[0].Name, result.TestCaseName);
    }
    [Fact] public async Task DiscoveryListsAllSuitesImportsAndErrorsWithoutDuplicatingStarter()
    {
        var root = Path.Combine(Path.GetTempPath(), "etl-library-" + Guid.NewGuid().ToString("N"));
        try
        {
            var samples = Path.Combine(root, "samples"); Directory.CreateDirectory(samples);
            await File.WriteAllTextAsync(Path.Combine(samples, "starter.json"), StudioJson.Write(Flat()));
            var store = new SuiteLibraryStore(Path.Combine(root, "state"), samples);
            var local = await store.SaveNewAsync(Flat());
            var imports = Path.Combine(root, "imports"); Directory.CreateDirectory(imports);
            await File.WriteAllTextAsync(Path.Combine(imports, "second.json"), StudioJson.Write(Flat() with { Id = "other", Name = "Other suite" }));
            await File.WriteAllTextAsync(Path.Combine(imports, "broken.json"), "{broken");
            await store.DiscoverAsync(); await store.RegisterAsync([], imports);
            var entries = await new SuiteLibraryStore(Path.Combine(root, "state"), samples).DiscoverAsync();
            Assert.Equal(3, entries.Count); Assert.Single(entries, e => e.Error != null);
            Assert.Equal(local, Assert.Single(entries, e => e.Definition?.Id == "suite").Path);
            Assert.Contains(entries, e => e.Definition?.Id == "other");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
