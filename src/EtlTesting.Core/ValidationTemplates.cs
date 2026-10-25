using System.Globalization;

namespace EtlTesting.Core;

// Template configuration is portable suite data, not a second execution format.
public sealed record ValidationTemplateConfiguration
{
    public string Kind { get; init; } = "requiredValues";
    public TableScope Source { get; init; } = new();
    public TableScope? Target { get; init; }
    public string[] Columns { get; init; } = [];
    public List<ColumnMapping> Keys { get; init; } = [];
    public bool FailOnNull { get; init; } = true;
    public bool CheckWhitespace { get; init; } = true;
    public string ValueType { get; init; } = "string";
    public string[] AllowedValues { get; init; } = [];
    public string? Minimum { get; init; }
    public string? Maximum { get; init; }
    public bool IncludeMinimum { get; init; } = true;
    public bool IncludeMaximum { get; init; } = true;
    public string Aggregate { get; init; } = "sum";
    public string? SourceMeasure { get; init; }
    public string? TargetMeasure { get; init; }
    public decimal AbsoluteTolerance { get; init; }
    public long AllowedViolations { get; init; }
}

public sealed record CompiledValidationTemplate(string Query, IReadOnlyList<BoundParameter> Parameters, string EvidenceUnit);

public static class ValidationTemplateCompiler
{
    private const string Prefix = "__etl_template_";
    public static CompiledValidationTemplate Compile(ValidationTemplateConfiguration rule)
    {
        var parameters = new List<BoundParameter>();
        if (rule.AllowedViolations < 0 || rule.AbsoluteTolerance < 0) throw new InvalidDataException("Allowed violations and tolerance must be non-negative.");
        if (rule.Keys.Any(k => k.Comparison != new ComparisonRule())) throw new InvalidDataException("SQL template key mappings use database equality; comparison normalization options are not supported here.");
        if (rule.Source.Connection != null || rule.Target?.Connection != null) throw new InvalidDataException("SQL validation templates use one logical connection. Use a mapped comparison for different servers.");
        string Bind(string type, string value)
        {
            var name = Prefix + parameters.Count;
            var input = new Suite { Parameters = new() { [name] = new() { Type = type, Required = true } } };
            parameters.Add(DefinitionLoader.Bind(input, new Dictionary<string, string> { [name] = value }).Single());
            return "@" + name;
        }
        string Scope(TableScope scope) => $"(SELECT * FROM {Quote(scope.Schema)}.{Quote(scope.Table)}" + (string.IsNullOrWhiteSpace(scope.Filter) ? "" : $" WHERE ({scope.Filter})") + ")";
        string Column()
        {
            if (rule.Columns.Length != 1) throw new InvalidDataException("Select exactly one column for this validation.");
            return "s." + Quote(rule.Columns[0]);
        }
        string WithNull(string column, string predicate) => rule.FailOnNull ? $"({column} IS NULL OR ({predicate}))" : $"({column} IS NOT NULL AND ({predicate}))";
        var source = Scope(rule.Source);
        string query;
        var unit = "rows";
        switch (rule.Kind)
        {
            case "requiredValues":
                if (rule.Columns.Length == 0) throw new InvalidDataException("Select at least one required column.");
                string WhitespaceExpression(string column)
                {
                    var expression = $"CONVERT(nvarchar(max), {column})";
                    foreach (var code in new[] { 9, 10, 11, 12, 13, 160 }) expression = $"REPLACE({expression}, NCHAR({code}), N'')";
                    return $"LTRIM(RTRIM({expression}))";
                }
                var required = rule.Columns.Select(c => "s." + Quote(c)).Select(c => rule.CheckWhitespace ? $"({c} IS NULL OR (TRY_CONVERT(nvarchar(max), {c}) IS NOT NULL AND {WhitespaceExpression(c)} = N''))" : $"{c} IS NULL");
                query = $"SELECT s.* FROM {source} AS s WHERE {string.Join(" OR ", required)}";
                break;
            case "uniqueKeys":
                if (rule.Columns.Length == 0) throw new InvalidDataException("Select at least one business key column.");
                var keys = rule.Columns.Select(c => "s." + Quote(c)).ToArray();
                var nullPredicate = string.Join(" OR ", keys.Select(k => k + " IS NULL"));
                query = $"SELECT {string.Join(", ", keys)}, COUNT_BIG(*) AS [Occurrences] FROM {source} AS s GROUP BY {string.Join(", ", keys)} HAVING COUNT_BIG(*) > 1" + (rule.FailOnNull ? " OR " + nullPredicate : "");
                unit = "key groups";
                break;
            case "allowedValues":
                var allowedColumn = Column();
                if (rule.AllowedValues.Length is < 1 or > 1500) throw new InvalidDataException("Enter between 1 and 1,500 allowed values.");
                var values = rule.AllowedValues.Select(v => Bind(rule.ValueType, v));
                query = $"SELECT s.* FROM {source} AS s WHERE {WithNull(allowedColumn, allowedColumn + " NOT IN (" + string.Join(", ", values) + ")")}";
                break;
            case "numericRange":
            case "dateRange":
            case "lengthRange":
                var rangeColumn = Column();
                if (rule.Minimum == null && rule.Maximum == null) throw new InvalidDataException("Enter at least one range boundary.");
                var rangeType = rule.Kind == "dateRange" ? "datetime" : rule.Kind == "lengthRange" ? "int64" : "decimal";
                if (rule.Kind == "lengthRange" && new[] { rule.Minimum, rule.Maximum }.Where(v => v != null).Any(v => !long.TryParse(v, out var length) || length < 0)) throw new InvalidDataException("Length boundaries must be non-negative integers.");
                var expression = rule.Kind == "lengthRange" ? $"(LEN(CONVERT(nvarchar(max), {rangeColumn}) + N'#') - 1)" : rangeColumn;
                var ranges = new List<string>();
                if (rule.Minimum != null) ranges.Add($"{expression} {(rule.IncludeMinimum ? "<" : "<=")} {Bind(rangeType, rule.Minimum)}");
                if (rule.Maximum != null) ranges.Add($"{expression} {(rule.IncludeMaximum ? ">" : ">=")} {Bind(rangeType, rule.Maximum)}");
                if (parameters.Count == 2 && Compare(parameters[0].Value, parameters[1].Value) > 0) throw new InvalidDataException("Lower boundary must not exceed upper boundary.");
                query = $"SELECT s.* FROM {source} AS s WHERE {WithNull(rangeColumn, string.Join(" OR ", ranges))}";
                break;
            case "referentialIntegrity":
                if (rule.Target == null || rule.Keys.Count == 0) throw new InvalidDataException("Select a parent table and at least one child-to-parent key mapping.");
                var joins = rule.Keys.Select(k => $"s.{Quote(k.Source)} = p.{Quote(k.Target)}");
                var nullKeys = string.Join(" OR ", rule.Keys.Select(k => $"s.{Quote(k.Source)} IS NULL"));
                var nonNullKeys = string.Join(" AND ", rule.Keys.Select(k => $"s.{Quote(k.Source)} IS NOT NULL"));
                var orphan = $"NOT EXISTS (SELECT 1 FROM {Scope(rule.Target)} AS p WHERE {string.Join(" AND ", joins)})";
                query = $"SELECT s.* FROM {source} AS s WHERE " + (rule.FailOnNull ? $"(({nullKeys}) OR {orphan})" : $"(({nonNullKeys}) AND {orphan})");
                break;
            case "rowCount":
                if (rule.Target == null) throw new InvalidDataException("Select a target table for row count reconciliation.");
                query = $"SELECT s.[RowCount] AS [SourceCount], t.[RowCount] AS [TargetCount] FROM (SELECT COUNT_BIG(*) AS [RowCount] FROM {source} AS q) AS s CROSS JOIN (SELECT COUNT_BIG(*) AS [RowCount] FROM {Scope(rule.Target)} AS q) AS t WHERE ABS(CONVERT(decimal(38,0), s.[RowCount]) - t.[RowCount]) > {Bind("decimal", rule.AbsoluteTolerance.ToString(CultureInfo.InvariantCulture))}";
                unit = "count comparisons";
                break;
            case "aggregate":
                if (rule.Target == null) throw new InvalidDataException("Select both tables for aggregate reconciliation.");
                if (rule.Aggregate is not ("sum" or "count" or "min" or "max")) throw new InvalidDataException("Choose sum, count, min or max.");
                string Aggregate(bool isSource)
                {
                    var scope = isSource ? source : Scope(rule.Target!);
                    var group = rule.Keys.Select(k => "q." + Quote(isSource ? k.Source : k.Target)).ToArray();
                    var selectGroups = string.Join(", ", group.Select((c, i) => $"{c} AS [g{i}]"));
                    var measure = rule.Aggregate == "count" ? "COUNT_BIG(*)" : $"{rule.Aggregate.ToUpperInvariant()}(q.{Quote(isSource ? rule.SourceMeasure! : rule.TargetMeasure!)})";
                    return $"SELECT {(group.Length > 0 ? selectGroups + ", " : "")}{measure} AS [Measure], 1 AS [Present] FROM {scope} AS q" + (group.Length > 0 ? " GROUP BY " + string.Join(", ", group) : "");
                }
                var conditions = rule.Keys.Select((_, i) => $"(s.[g{i}] = t.[g{i}] OR (s.[g{i}] IS NULL AND t.[g{i}] IS NULL))").ToArray();
                var tolerance = Bind("decimal", rule.AbsoluteTolerance.ToString(CultureInfo.InvariantCulture));
                query = $"SELECT s.*, t.* FROM ({Aggregate(true)}) AS s FULL OUTER JOIN ({Aggregate(false)}) AS t ON {(conditions.Length == 0 ? "1 = 1" : string.Join(" AND ", conditions))} WHERE s.[Present] IS NULL OR t.[Present] IS NULL OR (s.[Measure] IS NULL AND t.[Measure] IS NOT NULL) OR (s.[Measure] IS NOT NULL AND t.[Measure] IS NULL) OR ABS(CONVERT(decimal(38,10), s.[Measure]) - CONVERT(decimal(38,10), t.[Measure])) > {tolerance}";
                // Unique aliases keep evidence readable and compatible with SQL readers.
                var evidenceGroups = string.Concat(rule.Keys.Select((_, i) => $"COALESCE(s.[g{i}], t.[g{i}]) AS [Group{i + 1}], "));
                query = query.Replace("SELECT s.*, t.*", $"SELECT {evidenceGroups}s.[Measure] AS [SourceMeasure], t.[Measure] AS [TargetMeasure]");
                unit = "aggregate groups";
                break;
            default: throw new InvalidDataException($"Template '{rule.Kind}' is not available.");
        }
        return new(query + ";", parameters, unit);
    }

    public static void EnsureNoParameterConflicts(IEnumerable<BoundParameter> parameters)
    {
        if (parameters.Any(p => p.Name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException($"Parameter names beginning with {Prefix} are reserved for validation templates.");
    }
    private static int Compare(object left, object right) => left is IComparable comparable ? comparable.CompareTo(right) : 0;
    private static string Quote(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Contains('\0')) throw new InvalidDataException("Table, schema and column names must contain 1–128 characters.");
        return "[" + value.Replace("]", "]]") + "]";
    }
}
