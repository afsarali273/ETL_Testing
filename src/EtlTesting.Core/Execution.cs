using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EtlTesting.Core;

public static class ValueComparison
{
    public static bool Equal(Cell left, Cell right, ComparisonRule rule)
    {
        Cell Normalize(Cell c)
        {
            if (c.Kind != "string") return c;
            var s = rule.Trim ? c.Value?.Trim() : c.Value;
            return rule.EmptyAsNull && s == "" ? Cell.Null : new("string", s);
        }
        left = Normalize(left); right = Normalize(right);
        if (left.Kind == "null" || right.Kind == "null") return left.Kind == right.Kind;
        if (left.Kind != right.Kind) throw new InvalidDataException($"Incompatible comparison types: {left.Kind} / {right.Kind}");
        return left.Kind switch
        {
            "null" => true,
            "decimal" => Math.Abs(decimal.Parse(left.Value!, CultureInfo.InvariantCulture) - decimal.Parse(right.Value!, CultureInfo.InvariantCulture)) <= rule.AbsoluteTolerance,
            "float" => double.Parse(left.Value!, CultureInfo.InvariantCulture).Equals(double.Parse(right.Value!, CultureInfo.InvariantCulture)),
            "guid" => Guid.Parse(left.Value!) == Guid.Parse(right.Value!),
            "binary" => Convert.FromBase64String(left.Value!).AsSpan().SequenceEqual(Convert.FromBase64String(right.Value!)),
            "datetime" => Math.Abs((DateTime.Parse(left.Value!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) - DateTime.Parse(right.Value!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)).TotalMilliseconds) <= rule.DateTimeToleranceMilliseconds,
            "datetimeoffset" => Math.Abs((DateTimeOffset.Parse(left.Value!, CultureInfo.InvariantCulture) - DateTimeOffset.Parse(right.Value!, CultureInfo.InvariantCulture)).TotalMilliseconds) <= rule.DateTimeToleranceMilliseconds,
            "string" => string.Equals(left.Value, right.Value, rule.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal),
            _ => left.Value == right.Value
        };
    }
    public static string Key(IEnumerable<Cell> values) => StudioJson.Write(values.ToArray());
}

public sealed class ComparisonHandler(IRowIndexFactory indexes) : IValidationHandler
{
    public string Type => "dataComparison";
    public async Task ExecuteAsync(TestDefinition test, Suite suite, IDataConnector connector, IReadOnlyList<BoundParameter> parameters, RunOptions options, TestResult result, CancellationToken cancellationToken)
    {
        await using var index = await indexes.CreateAsync(options.WorkDirectory, cancellationToken);
        var connection = test.Connection ?? suite.Defaults.Connection;
        var timeout = options.TimeoutSeconds ?? test.TimeoutSeconds ?? suite.Defaults.TimeoutSeconds;
        for (var side = 0; side < 2; side++)
        {
            var scope = side == 0 ? test.Source! : test.Target!;
            string Column(ColumnMapping m) => side == 0 ? m.Source : m.Target;
            var columns = test.Keys.Concat(test.Columns).Select(Column).Distinct(StringComparer.Ordinal).ToArray();
            await foreach (var row in connector.ReadAsync(scope, scope.Connection ?? connection, columns, parameters, timeout, cancellationToken))
            {
                var key = test.Keys.Select(k => row.Values[Column(k)]).ToArray();
                await index.AddAsync(side, ValueComparison.Key(key), key.Any(c => c.Kind == "null"), row, cancellationToken);
                if (side == 0) result.SourceRecords++; else result.TargetRecords++;
            }
        }
        void Add(string category, string key, string column, Cell expected, Cell actual, string rule, string reason)
        {
            result.Violations++;
            if (result.Evidence.Count < test.EvidenceLimit) result.Evidence.Add(new(test.Id, $"{test.Source!.Schema}.{test.Source.Table} → {test.Target!.Schema}.{test.Target.Table}", category, key, column, expected, actual, rule, reason));
        }
        await foreach (var pair in index.PairsAsync(cancellationToken))
        {
            if (pair.NullKey)
            {
                result.NullKeyRecords += pair.SourceCount + pair.TargetCount;
                Add("NullKey", pair.Key, "", Cell.From("non-null business key"), Cell.Null, "All key components required", $"Source {pair.SourceCount}; target {pair.TargetCount}. Excluded from matching.");
                continue;
            }
            if (pair.SourceCount > 1 || pair.TargetCount > 1)
            {
                result.DuplicateGroups++;
                Add("DuplicateKey", pair.Key, "", Cell.From(pair.SourceCount), Cell.From(pair.TargetCount), "One row per business key on each side", "Ambiguous key group excluded from matching");
                continue;
            }
            if (pair.SourceCount == 0)
            {
                if (test.Checks.UnexpectedTargetRows) { result.UnexpectedRecords++; Add("UnexpectedTarget", pair.Key, "", Cell.Null, Cell.From("present"), "No unexpected target keys", "Key absent in source"); }
                continue;
            }
            if (pair.TargetCount == 0)
            {
                if (test.Checks.MissingTargetRows) { result.MissingRecords++; Add("MissingTarget", pair.Key, "", Cell.From("present"), Cell.Null, "Every source key present", "Key absent in target"); }
                continue;
            }
            var mismatched = false;
            foreach (var mapping in test.Columns)
            {
                var expected = pair.Source!.Values[mapping.Source]; var actual = pair.Target!.Values[mapping.Target];
                // Null and a non-null value are a mismatch, not a type error.
                var equal = expected.Kind == "null" || actual.Kind == "null"
                    ? expected.Kind == actual.Kind || mapping.Comparison.EmptyAsNull && (expected.Kind == "null" ? actual.Kind == "string" && (mapping.Comparison.Trim ? actual.Value?.Trim() : actual.Value) == "" : expected.Kind == "string" && (mapping.Comparison.Trim ? expected.Value?.Trim() : expected.Value) == "")
                    : ValueComparison.Equal(expected, actual, mapping.Comparison);
                if (equal) continue;
                mismatched = true; result.FieldMismatches++;
                Add("FieldMismatch", pair.Key, $"{mapping.Source} → {mapping.Target}", expected, actual, StudioJson.Write(mapping.Comparison), "Mapped values differ");
            }
            if (mismatched) result.MismatchedRecords++;
        }
    }
}
public sealed class SqlAssertionHandler : IValidationHandler
{
    public string Type => "sqlAssertion";
    public async Task ExecuteAsync(TestDefinition test, Suite suite, IDataConnector connector, IReadOnlyList<BoundParameter> parameters, RunOptions options, TestResult result, CancellationToken cancellationToken)
    {
        var compiled = test.Template == null ? null : ValidationTemplateCompiler.Compile(test.Template);
        if (compiled != null) ValidationTemplateCompiler.EnsureNoParameterConflicts(parameters);
        var query = await connector.QueryAsync(test.Connection ?? suite.Defaults.Connection, compiled?.Query ?? test.Query!, compiled == null ? parameters : parameters.Concat(compiled.Parameters).ToList(), options.TimeoutSeconds ?? test.TimeoutSeconds ?? suite.Defaults.TimeoutSeconds, test.EvidenceLimit, cancellationToken);
        result.SourceRecords = query.Count;
        if (compiled != null)
        {
            if (query.Count <= test.Template!.AllowedViolations) return;
            result.Violations = query.Count;
            foreach (var row in query.Evidence)
                result.Evidence.Add(new(test.Id, $"{test.Template.Source.Schema}.{test.Template.Source.Table}", test.Template.Kind, StudioJson.Write(row.Values), "", Cell.From($"at most {test.Template.AllowedViolations} violating {compiled.EvidenceUnit}"), Cell.From(query.Count), test.Template.Kind, StudioJson.Write(row)));
            return;
        }
        var expected = test.Expect!.RowCount is long count ? Cell.From(count) : test.Expect.Scalar!;
        var actual = test.Expect.RowCount != null ? Cell.From(query.Count) : query.Scalar ?? Cell.Null;
        if (test.Expect.Scalar != null && (query.Count != 1 || query.Scalar == null)) throw new InvalidDataException("Scalar assertions require exactly one row and one column");
        if (expected.Kind == actual.Kind && ValueComparison.Equal(expected, actual, new())) return;
        result.Violations = 1;
        if (test.EvidenceLimit > 0) result.Evidence.Add(new(test.Id, "SQL assertion", "Assertion", "query", "expect", expected, actual, "Exact typed expectation", $"Returned {query.Count} rows; query executed once"));
        // Include bounded returned rows as provenance in the single violation reason.
        if (result.Evidence.Count > 0) result.Evidence[0] = result.Evidence[0] with { Reason = result.Evidence[0].Reason + "; rows: " + StudioJson.Write(query.Evidence) };
    }
}
public sealed class SuiteRunner(IEnumerable<IValidationHandler> handlers) : ISuiteRunner
{
    private readonly Dictionary<string, IValidationHandler> registry = handlers.ToDictionary(h => h.Type, StringComparer.Ordinal);
    public async Task<RunResult> RunAsync(Suite suite, string environment, IDataConnector connector, IReadOnlyList<BoundParameter> parameters, RunOptions options, IProgress<RunProgress>? progress, CancellationToken cancellationToken)
    {
        var errors = DefinitionLoader.Validate(suite);
        if (suite.Tests.Count == 0) errors.Add("The suite has no validations. Add one before running.");
        if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
        if (connector is DemoConnector && !options.Demo) throw new InvalidDataException("Synthetic fixtures require explicit demo mode");
        if (options.TestIds is { Length: > 0 } && options.TestIds.Except(suite.Tests.Select(t => t.Id)).Any()) throw new InvalidDataException("Unknown test selection");
        var result = new RunResult { SuiteId = suite.Id, SuiteName = suite.Name, SuiteHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(StudioJson.Write(suite)))), Environment = environment, Demo = options.Demo, Scope = options.TestIds?.Length > 0 || options.Tags?.Length > 0 ? "Selected tests/tags (partial suite)" : "Full suite" };
        result.Tests = suite.Tests.Select(t => new TestResult { Id = t.Id, Name = t.Name, Severity = t.Severity, TestCaseId = t.TestCaseId, TestCaseName = suite.TestCases.FirstOrDefault(c => c.Id == t.TestCaseId)?.Name }).ToList();
        result.RuntimeParameters = parameters.ToDictionary(p => p.Name, p => Cell.From(p.Value), StringComparer.Ordinal);
        for (var i = 0; i < suite.Tests.Count; i++)
        {
            var test = suite.Tests[i]; var outcome = result.Tests[i]; var watch = Stopwatch.StartNew();
            if (cancellationToken.IsCancellationRequested) outcome.Status = "Cancelled";
            else if (!test.Enabled || suite.TestCases.Any(c => c.Id == test.TestCaseId && !c.Enabled) || options.TestIds?.Length > 0 && !options.TestIds.Contains(test.Id) || options.Tags?.Length > 0 && !options.Tags.Intersect(test.Tags.Concat(suite.Tags)).Any()) outcome.Status = "Skipped";
            else
            {
                outcome.Status = "Running";
                progress?.Report(new(test.Id, "Running", i, suite.Tests.Count, $"Running {test.Name}"));
                try
                {
                    await registry[test.Type].ExecuteAsync(test, suite, connector, parameters, options, outcome, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    outcome.Status = outcome.Violations == 0 ? "Passed" : "Failed";
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { outcome.Status = "Cancelled"; }
                catch (Exception e) { outcome.Status = "Error"; outcome.Error = e is InvalidDataException ? e.Message : "Execution failed. Verify connection, permissions, TLS, SQL and available disk space. " + e.GetType().Name; }
            }
            outcome.DurationSeconds = watch.Elapsed.TotalSeconds;
            progress?.Report(new(test.Id, outcome.Status, i + 1, suite.Tests.Count, $"{test.Name}: {outcome.Status}; {outcome.SourceRecords + outcome.TargetRecords} rows scanned"));
        }
        result.Ended = DateTimeOffset.UtcNow;
        return result;
    }
}
