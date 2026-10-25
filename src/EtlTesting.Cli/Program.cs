using EtlTesting.Core;
using EtlTesting.Persistence;
using EtlTesting.Reporting;
using EtlTesting.SqlServer;

return await Cli.RunAsync(args);

public static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        try
        {
            var command = args.FirstOrDefault() ?? "help";
            if (command == "help")
            {
                Console.WriteLine("ETL Test Studio\netltest validate --suite <file> [--parameter name=value]\netltest run --suite <file> [--demo | --environment <file>] [--parameter name=value] [--output <dir>] [--formats html,pdf,json,csv,junit] [--test id] [--tag name] [--timeout seconds]\netltest list-checks\nExit: 0 passed; 1 validation failures; 2 configuration/execution/report error; 3 cancelled. Warning failures do not block.");
                return 0;
            }
            if (command == "list-checks") { Console.WriteLine("dataComparison  Exact mapped comparison; composite keys, nulls, duplicates, tolerances\nsqlAssertion    Zero-violation, row-count or typed scalar assertion\nSQL templates   requiredValues, uniqueKeys, allowedValues, numericRange, dateRange, lengthRange, referentialIntegrity, rowCount, aggregate\nTemplates retain editable configuration and use the shared SQL assertion handler.\nIncremental, SCD and schema templates: unavailable in this release."); return 0; }
            if (command is not ("validate" or "run")) throw new InvalidDataException("Unknown command. Use help.");
            var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var known = new[] { "--suite", "--environment", "--parameter", "--output", "--formats", "--test", "--tag", "--timeout", "--demo" };
            for (var i = 1; i < args.Length; i++)
            {
                var key = args[i];
                if (!known.Contains(key)) throw new InvalidDataException($"Unknown option {key}");
                var value = key == "--demo" ? "true" : ++i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal) ? args[i] : throw new InvalidDataException($"Value missing for {key}");
                if (!values.TryGetValue(key, out var list)) values[key] = list = [];
                if (list.Count > 0 && key is not ("--parameter" or "--test" or "--tag")) throw new InvalidDataException($"Repeated option {key}");
                list.Add(value);
            }
            string? Get(string key) => values.GetValueOrDefault(key)?.Last();
            var suiteFile = Path.GetFullPath(Get("--suite") ?? throw new InvalidDataException("--suite is required"));
            var suite = DefinitionLoader.Parse(await File.ReadAllTextAsync(suiteFile, cancellation.Token));
            var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in values.GetValueOrDefault("--parameter") ?? [])
            {
                var split = item.IndexOf('='); if (split < 1) throw new InvalidDataException("Parameter must be name=value");
                overrides[item[..split]] = item[(split + 1)..];
            }
            var parameters = DefinitionLoader.Bind(suite, overrides);
            var demo = Get("--demo") != null;
            if (demo && Get("--environment") != null) throw new InvalidDataException("Choose --demo or --environment");
            var timeout = Get("--timeout") is string raw ? int.TryParse(raw, out var seconds) && seconds is > 0 and <= 86400 ? seconds : throw new InvalidDataException("--timeout must be 1..86400") : (int?)null;
            IDataConnector connector; var environmentName = "DEMO";
            if (demo) connector = new DemoConnector();
            else if (Get("--environment") is string environmentPath)
            {
                // Explicit CLI paths are relative to the process working directory.
                var environment = StudioJson.Read<EnvironmentDefinition>(await File.ReadAllTextAsync(Path.GetFullPath(environmentPath), cancellation.Token));
                var sql = new SqlServerConnector(environment, new EnvironmentCredentialResolver()); sql.Preflight(suite, parameters); connector = sql; environmentName = environment.Name;
            }
            else if (command == "validate") connector = new DemoConnector();
            else throw new InvalidDataException("A real run requires --environment; use --demo for synthetic fixtures");
            if (command == "validate") { Console.WriteLine($"VALID: {suite.Id}; {suite.Tests.Count} tests; {parameters.Count} bound parameters. No database queries executed."); return 0; }
            var formats = (Get("--formats") ?? "json,html,pdf,csv,junit").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (formats.Length == 0 || formats.Except(ReportWriter.Supported).Any()) throw new InvalidDataException("Invalid report formats");
            var output = Path.GetFullPath(Get("--output") ?? "artifacts/etl-results");
            var runner = new SuiteRunner([new ComparisonHandler(new SqliteRowIndexFactory()), new SqlAssertionHandler()]);
            var progress = new InlineProgress(p => Console.WriteLine($"[{p.Completed}/{p.Total}] {p.Message}"));
            var run = await runner.RunAsync(suite, environmentName, connector, parameters, new(Path.Combine(output, ".work"), demo, values.GetValueOrDefault("--test")?.ToArray(), values.GetValueOrDefault("--tag")?.ToArray(), timeout), progress, cancellation.Token);
            // Diagnostic artifacts remain writable after cancellation.
            await new ReportWriter().WriteAsync(run, output, formats, CancellationToken.None);
            Console.WriteLine($"{(demo ? "DEMO · " : "")}{run.Status}: {run.RunId}\nReports: {output}");
            return run.ExitCode;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled"); return 3; }
        catch (Exception e)
        {
            Console.Error.WriteLine(Environment.GetEnvironmentVariable("ETLTEST_DIAGNOSTICS") == "1" ? e.ToString() : e is InvalidDataException or System.Text.Json.JsonException ? e.Message : $"Configuration or artifact failure: {e.GetType().Name}. Verify input paths, permissions and disk space.");
            return 2;
        }
    }
    private sealed class InlineProgress(Action<RunProgress> action) : IProgress<RunProgress> { public void Report(RunProgress value) => action(value); }
}
