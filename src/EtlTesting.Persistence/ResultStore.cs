using EtlTesting.Core;

namespace EtlTesting.Persistence;

public static class AtomicFile
{
    public static async Task WriteAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, content, cancellationToken); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
public sealed class FileResultStore(string directory) : IResultStore
{
    public Task SaveAsync(RunResult run, CancellationToken cancellationToken = default) => AtomicFile.WriteAsync(Path.Combine(directory, run.RunId + ".json"), StudioJson.Write(run), cancellationToken);
    public async Task<IReadOnlyList<RunResult>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory)) return [];
        var results = new List<RunResult>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(200))
        {
            try { results.Add(StudioJson.Read<RunResult>(await File.ReadAllTextAsync(file, cancellationToken))); }
            catch (System.Text.Json.JsonException) { /* A corrupt history artifact does not block opening other runs. */ }
        }
        return results.OrderByDescending(r => r.Started).ToList();
    }
}
