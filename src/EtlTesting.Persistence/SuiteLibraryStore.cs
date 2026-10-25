using EtlTesting.Core;

namespace EtlTesting.Persistence;

public sealed record SuiteLibrarySettings
{
    public int FormatVersion { get; init; } = 1;
    public List<string> Files { get; init; } = [];
    public List<string> Folders { get; init; } = [];
}
public sealed record SuiteLibraryEntry(string Path, Suite? Definition, string? Error, DateTimeOffset ModifiedAt, bool Bundled);

public sealed class SuiteLibraryStore(string stateDirectory, string samplesDirectory)
{
    private SuiteLibrarySettings settings = new();
    public string SuitesDirectory => System.IO.Path.Combine(stateDirectory, "suites");
    private string SettingsPath => System.IO.Path.Combine(stateDirectory, "suite-library", "index-v1.json");

    public async Task<IReadOnlyList<SuiteLibraryEntry>> DiscoverAsync(CancellationToken token = default)
    {
        Directory.CreateDirectory(SuitesDirectory);
        if (File.Exists(SettingsPath))
        {
            settings = StudioJson.Read<SuiteLibrarySettings>(await File.ReadAllTextAsync(SettingsPath, token));
            if (settings.FormatVersion != 1) throw new InvalidDataException("Unsupported suite-library version.");
        }
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<SuiteLibraryEntry>();
        foreach (var folder in new[] { SuitesDirectory, samplesDirectory }.Concat(settings.Folders).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(folder)) continue;
            try { foreach (var file in Directory.EnumerateFiles(folder, "*.json", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true })) files.Add(System.IO.Path.GetFullPath(file)); }
            catch (IOException) { /* Individually registered files are still loaded below. */ }
        }
        foreach (var file in settings.Files) files.Add(System.IO.Path.GetFullPath(file));
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var bundled = file.StartsWith(System.IO.Path.GetFullPath(samplesDirectory) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            try { entries.Add(new(file, DefinitionLoader.Parse(await File.ReadAllTextAsync(file, token)), null, File.GetLastWriteTimeUtc(file), bundled)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
            { entries.Add(new(file, null, e is InvalidDataException or System.Text.Json.JsonException ? e.Message : "File is unavailable. Check its location and read permissions.", File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTimeOffset.MinValue, bundled)); }
        }
        // A local editable copy supersedes its bundled starter. Independent imported copies stay visible.
        var editableIds = entries.Where(e => !e.Bundled && e.Definition != null).Select(e => e.Definition!.Id).ToHashSet(StringComparer.Ordinal);
        return entries.Where(e => !e.Bundled || e.Definition == null || !editableIds.Contains(e.Definition.Id)).OrderBy(e => e.Definition?.Name ?? System.IO.Path.GetFileName(e.Path), StringComparer.OrdinalIgnoreCase).ToList();
    }
    public async Task RegisterAsync(IEnumerable<string> files, string? folder = null)
    {
        foreach (var file in files.Select(System.IO.Path.GetFullPath)) if (!settings.Files.Contains(file, StringComparer.OrdinalIgnoreCase)) settings.Files.Add(file);
        if (folder != null && !settings.Folders.Contains(System.IO.Path.GetFullPath(folder), StringComparer.OrdinalIgnoreCase)) settings.Folders.Add(System.IO.Path.GetFullPath(folder));
        await AtomicFile.WriteAsync(SettingsPath, StudioJson.Write(settings));
    }
    public async Task<string> SaveNewAsync(Suite suite)
    {
        _ = DefinitionLoader.Parse(StudioJson.Write(suite));
        var path = System.IO.Path.Combine(SuitesDirectory, suite.Id + ".json");
        if (File.Exists(path)) throw new InvalidDataException("A suite file already uses this ID. Duplicate it to create an independent copy.");
        await AtomicFile.WriteAsync(path, StudioJson.Write(suite));
        return path;
    }
}
