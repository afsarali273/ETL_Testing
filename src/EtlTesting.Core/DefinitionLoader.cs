using System.Globalization;
using System.Text.Json.Nodes;
using Json.Schema;

namespace EtlTesting.Core;

public static class DefinitionLoader
{
    private static readonly Lazy<JsonSchema> Schema = new(() =>
    {
        using var stream = typeof(DefinitionLoader).Assembly.GetManifestResourceStream("EtlTesting.Core.suite-v1.schema.json")!;
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    });
    public static Suite Parse(string text)
    {
        var evaluation = Schema.Value.Evaluate(JsonNode.Parse(text), new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (!evaluation.IsValid) throw new InvalidDataException(string.Join(Environment.NewLine, Flatten(evaluation)));
        var suite = StudioJson.Read<Suite>(text);
        var errors = Validate(suite);
        if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        return suite;
    }
    private static IEnumerable<string> Flatten(EvaluationResults result)
    {
        if (result.Errors != null) foreach (var error in result.Errors) yield return $"{result.InstanceLocation}: {error.Value}";
        if (result.HasDetails) foreach (var detail in result.Details) foreach (var error in Flatten(detail)) yield return error;
    }
    public static List<string> Validate(Suite suite)
    {
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var cases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var testCase in suite.TestCases)
        {
            if (string.IsNullOrWhiteSpace(testCase.Id) || !cases.Add(testCase.Id)) errors.Add("testCases.id: each test requires a unique stable ID");
            if (string.IsNullOrWhiteSpace(testCase.Name)) errors.Add($"testCases ({testCase.Id}).name: enter a test name");
        }
        for (var i = 0; i < suite.Tests.Count; i++)
        {
            var t = suite.Tests[i];
            void Error(string path, string message) => errors.Add($"tests[{i}] ({t.Id}).{path}: {message}");
            if (!ids.Add(t.Id)) Error("id", "Duplicate stable ID");
            if (t.TestCaseId != null && !cases.Contains(t.TestCaseId)) Error("testCaseId", "The parent test does not exist");
            if (suite.TestCases.Count > 0 && t.TestCaseId == null) Error("testCaseId", "Assign this validation to a test");
            if (t.TimeoutSeconds is <= 0 or > 86400) Error("timeoutSeconds", "Use 1–86400 seconds or inherit the suite timeout");
            if (t.Template != null)
            {
                if (t.Type != "sqlAssertion") Error("template", "SQL templates require sqlAssertion execution");
                try { _ = ValidationTemplateCompiler.Compile(t.Template); }
                catch (InvalidDataException e) { Error("template", e.Message); }
                if (t.Expect is not { RowCount: 0, Scalar: null }) Error("expect", "Templates use a zero-violation expectation; configure their allowed violation threshold in the template");
            }
            if (t.Type == "dataComparison")
            {
                if (t.Source == null || t.Target == null) Error("source/target", "Both table scopes are required");
                if (t.Keys.Count == 0) Error("keys", "At least one key mapping is required");
                if (t.Columns.Count == 0) Error("columns", "Explicit comparison columns are required");
                if (t.Query != null || t.Expect != null) Error("query", "SQL assertion options are invalid for a comparison");
                if (t.Keys.Any(k => k.Comparison != new ComparisonRule())) Error("keys.comparison", "Key rules are fixed: exact typed ordinal equality");
                if (!t.Checks.UniqueKeys) Error("checks.uniqueKeys", "Key uniqueness must be checked for exact matching");
                if (t.Keys.Select(k => k.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count() != t.Keys.Count || t.Keys.Select(k => k.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count() != t.Keys.Count) Error("keys", "Repeated key columns");
            }
            else if (t.Type == "sqlAssertion")
            {
                if (t.Template == null && string.IsNullOrWhiteSpace(t.Query)) Error("query", "SQL is required");
                if (t.Expect == null || (t.Expect.RowCount == null) == (t.Expect.Scalar == null)) Error("expect", "Choose exactly one rowCount or typed scalar expectation");
                if (t.Expect?.Scalar is Cell scalar)
                {
                    try
                    {
                        if (scalar.Kind == "null" && scalar.Value != null || scalar.Kind != "null" && scalar.Value == null) throw new FormatException();
                        if (scalar.Kind == "decimal") _ = decimal.Parse(scalar.Value!, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
                        if (scalar.Kind == "float") _ = double.Parse(scalar.Value!, CultureInfo.InvariantCulture);
                        if (scalar.Kind == "datetime") _ = DateTime.ParseExact(scalar.Value!, "O", CultureInfo.InvariantCulture, DateTimeStyles.None);
                        if (scalar.Kind == "datetimeoffset") _ = DateTimeOffset.ParseExact(scalar.Value!, "O", CultureInfo.InvariantCulture);
                        if (scalar.Kind == "boolean" && scalar.Value is not ("true" or "false")) throw new FormatException();
                        if (scalar.Kind == "guid") _ = Guid.ParseExact(scalar.Value!, "D");
                        if (scalar.Kind == "binary") _ = Convert.FromBase64String(scalar.Value!);
                    }
                    catch (Exception e) when (e is FormatException or OverflowException) { Error("expect.scalar.value", "Invalid typed scalar representation"); }
                }
                if (t.Source != null || t.Target != null || t.Keys.Count != 0 || t.Columns.Count != 0) Error("source", "Table/mapping options are invalid for a SQL assertion");
            }
            else Error("type", $"Unavailable check type: {t.Type}");
        }
        return errors;
    }
    public static List<BoundParameter> Bind(Suite suite, IReadOnlyDictionary<string, string> overrides)
    {
        foreach (var name in overrides.Keys) if (!suite.Parameters.ContainsKey(name)) throw new InvalidDataException($"parameters.{name}: undeclared parameter");
        var result = new List<BoundParameter>();
        foreach (var (name, declaration) in suite.Parameters)
        {
            var value = overrides.GetValueOrDefault(name) ?? declaration.Default;
            if (value == null) { if (declaration.Required) throw new InvalidDataException($"parameters.{name}: required input missing"); continue; }
            try
            {
                object parsed = declaration.Type switch
                {
                    "string" => value,
                    "int64" => long.Parse(value, CultureInfo.InvariantCulture),
                    "decimal" => decimal.Parse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture),
                    "boolean" => bool.Parse(value),
                    "datetime" => DateTime.ParseExact(value, ["yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF"], CultureInfo.InvariantCulture, DateTimeStyles.None),
                    "datetimeoffset" => DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture),
                    "guid" => Guid.Parse(value),
                    _ => throw new FormatException("Unsupported parameter type")
                };
                result.Add(new(name, declaration.Type, parsed));
            }
            catch (Exception e) when (e is FormatException or OverflowException) { throw new InvalidDataException($"parameters.{name}: invalid {declaration.Type} value"); }
        }
        return result;
    }
}
