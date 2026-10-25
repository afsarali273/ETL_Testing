using System.Diagnostics;
using System.Xml.Linq;
using EtlTesting.Core;
using Xunit;

namespace EtlTesting.Tests;

public sealed class CliTests
{
    private static async Task<(int Code, string Output)> Invoke(params string[] args)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var dll = Path.Combine(EngineTests.Root, $"src/EtlTesting.Cli/bin/{configuration}/net10.0/etltest.dll");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = EngineTests.Root, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add(dll); foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
        return (process.ExitCode, await output + await errors);
    }
    [Fact] public async Task ValidateAndListChecksWorkWithoutDesktopState()
    {
        Assert.Equal(0, (await Invoke("validate", "--suite", "samples/suites/reservations.json")).Code);
        var checks = await Invoke("list-checks"); Assert.Equal(0, checks.Code); Assert.Contains("dataComparison", checks.Output);
    }
    [Fact] public async Task BadInputsAndUnknownOptionsReturnConfigurationError()
    {
        Assert.Equal(2, (await Invoke("run", "--suite", "missing.json", "--demo")).Code);
        Assert.Equal(2, (await Invoke("run", "--suite", "samples/suites/reservations.json", "--typo", "x")).Code);
        Assert.Equal(2, (await Invoke("run", "--suite", "samples/suites/reservations.json")).Code);
    }
    [Fact] public async Task DemoFailureWritesAllReportArtifacts()
    {
        var path = Path.Combine(Path.GetTempPath(), "etl-cli-" + Guid.NewGuid().ToString("N"));
        try
        {
            var run = await Invoke("run", "--suite", "samples/suites/reservations.json", "--demo", "--output", path);
            Assert.Equal(1, run.Code); Assert.Contains("DEMO", run.Output);
            var data = StudioJson.Read<RunResult>(await File.ReadAllTextAsync(Path.Combine(path, "run.json"))); Assert.True(data.Demo); Assert.Equal(4, data.Tests.Count);
            Assert.Equal(4, XDocument.Load(Path.Combine(path, "junit.xml")).Descendants("testcase").Count()); Assert.True(File.Exists(Path.Combine(path, "report.html"))); Assert.True(File.Exists(Path.Combine(path, "mismatches.csv")));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
    [Fact] public async Task ArtifactFailureCannotReportSuccess()
    {
        var path = Path.GetTempFileName();
        try { Assert.Equal(2, (await Invoke("run", "--suite", "samples/suites/reservations.json", "--demo", "--output", path)).Code); }
        finally { File.Delete(path); }
    }
}
