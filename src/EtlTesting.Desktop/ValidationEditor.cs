using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using EtlTesting.Core;
using EtlTesting.SqlServer;

namespace EtlTesting.Desktop;

public sealed record ValidationTemplateChoice(string Kind, string Name, string Category, string Description, bool Available = true)
{
    public string Availability => Available ? "Ready to configure" : "Planned";
    public static IReadOnlyList<ValidationTemplateChoice> Catalog { get; } =
    [
        new("dataComparison", "Mapped data comparison", "Reconciliation", "Match business keys and compare mapped source and target values, including different servers."),
        new("requiredValues", "Required values", "Data quality", "Find nulls and optionally empty or whitespace values in selected columns."),
        new("uniqueKeys", "Unique business key", "Constraints", "Detect duplicate single or composite keys and optionally null business keys."),
        new("allowedValues", "Allowed values", "Data quality", "Validate a column against a typed list of permitted values."),
        new("numericRange", "Numeric range", "Data quality", "Configure numeric boundaries, inclusivity and null handling."),
        new("dateRange", "Date / time range", "Data quality", "Validate SQL datetime values against explicit wall-clock boundaries."),
        new("lengthRange", "Text length", "Data quality", "Validate character length including trailing spaces."),
        new("referentialIntegrity", "Referential integrity", "Constraints", "Find child rows without a matching parent, using one or several key columns."),
        new("rowCount", "Row count reconciliation", "Reconciliation", "Compare source and target counts with an absolute tolerance."),
        new("aggregate", "Aggregate reconciliation", "Reconciliation", "Compare sum, count, min or max totals, optionally grouped by mapped columns."),
        new("sqlAssertion", "Custom SQL assertion", "Advanced SQL", "Write SQL with suite parameters and configure an exact count or typed scalar expectation."),
        new("incremental", "Incremental load validation", "Incremental", "Baseline, bounded change window, expected changes and checkpoint policy.", false),
        new("historical", "SCD Type 2 history", "Historical", "Version overlap, current-row uniqueness and correct version closure.", false),
        new("schema", "Schema comparison", "Schema", "Expected columns, types, nullability, length and precision.", false)
    ];
}

public sealed class ValidationMappingRow : Observable
{
    private new bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!base.Set(ref field, value, name)) return false;
        Notify(nameof(RuleSummary)); return true;
    }
    public string RuleSummary => string.Join(" · ", new[] { Trim ? "Trim" : null, IgnoreCase ? "Ignore case" : null, EmptyAsNull ? "Empty = null" : null, Tolerance != "0" ? "±" + Tolerance : null, DateTolerance != "0" ? "±" + DateTolerance + " ms" : null }.Where(s => s != null)) is { Length: > 0 } text ? text : "Exact match";
    private string source = "", target = "", tolerance = "0", dateTolerance = "0";
    private bool trim, ignoreCase, emptyAsNull;
    public string Source { get => source; set => Set(ref source, value); }
    public string Target { get => target; set => Set(ref target, value); }
    public bool Trim { get => trim; set => Set(ref trim, value); }
    public bool IgnoreCase { get => ignoreCase; set => Set(ref ignoreCase, value); }
    public bool EmptyAsNull { get => emptyAsNull; set => Set(ref emptyAsNull, value); }
    public string Tolerance { get => tolerance; set => Set(ref tolerance, value); }
    public string DateTolerance { get => dateTolerance; set => Set(ref dateTolerance, value); }
    public ColumnMapping Build(bool key = false) => new()
    {
        Source = Source.Trim(), Target = Target.Trim(), Comparison = key ? new() : new()
        {
            Trim = Trim, IgnoreCase = IgnoreCase, EmptyAsNull = EmptyAsNull,
            AbsoluteTolerance = ValidationEditor.Decimal(Tolerance, "Decimal tolerance"),
            DateTimeToleranceMilliseconds = checked((int)ValidationEditor.Integer(DateTolerance, "Date tolerance", 0, int.MaxValue))
        }
    };
    public static ValidationMappingRow From(ColumnMapping mapping) => new()
    {
        Source = mapping.Source, Target = mapping.Target, Trim = mapping.Comparison.Trim,
        IgnoreCase = mapping.Comparison.IgnoreCase, EmptyAsNull = mapping.Comparison.EmptyAsNull,
        Tolerance = mapping.Comparison.AbsoluteTolerance.ToString(CultureInfo.InvariantCulture),
        DateTolerance = mapping.Comparison.DateTimeToleranceMilliseconds.ToString(CultureInfo.InvariantCulture)
    };
}

public sealed class ValidationColumnOption(string name, string type, bool selected) : Observable
{
    private bool selected = selected;
    public string Name { get; } = name;
    public string Type { get; } = type;
    public bool Selected { get => selected; set => Set(ref selected, value); }
}

public sealed class ValidationEditor : Observable
{
    private readonly TestDefinition original;
    private bool loading = true, dirty;
    private string name = "", connection = "", sourceSchema = "dbo", sourceTable = "", targetSchema = "dbo", targetTable = "", sourceFilter = "", targetFilter = "", sourceConnection = "", targetConnection = "";
    private string columnText = "", valueType = "string", allowedValues = "", minimum = "", maximum = "", aggregate = "sum", sourceMeasure = "", targetMeasure = "", tolerance = "0", allowedViolations = "0";
    private string severity = "error", evidenceLimit = "1000", timeout = "", tags = "", query = "", expectationMode = "zero", rowCount = "0", scalarKind = "decimal", scalarValue = "";
    private bool enabled = true, failOnNull = true, whitespace = true, includeMin = true, includeMax = true, missingRows = true, extraRows = true, allowEmptyString;
    private DatabaseObject? sourceObject, targetObject;

    public ValidationEditor(TestDefinition test, string defaultConnection, string? kind = null)
    {
        original = test; Kind = kind ?? test.Template?.Kind ?? test.Type;
        var template = test.Template;
        name = test.Name; connection = test.Connection ?? defaultConnection; enabled = test.Enabled; severity = test.Severity;
        evidenceLimit = test.EvidenceLimit.ToString(CultureInfo.InvariantCulture); timeout = test.TimeoutSeconds?.ToString(CultureInfo.InvariantCulture) ?? ""; tags = string.Join(", ", test.Tags);
        var source = test.Source ?? template?.Source; var target = test.Target ?? template?.Target;
        sourceSchema = source?.Schema ?? "dbo"; sourceTable = source?.Table ?? ""; sourceFilter = source?.Filter ?? ""; sourceConnection = source?.Connection ?? "";
        targetSchema = target?.Schema ?? "dbo"; targetTable = target?.Table ?? ""; targetFilter = target?.Filter ?? ""; targetConnection = target?.Connection ?? "";
        missingRows = test.Checks.MissingTargetRows; extraRows = test.Checks.UnexpectedTargetRows;
        foreach (var key in test.Keys.Count > 0 ? test.Keys : template?.Keys ?? []) Keys.Add(ValidationMappingRow.From(key));
        foreach (var mapping in test.Columns) Mappings.Add(ValidationMappingRow.From(mapping));
        if (template != null)
        {
            columnText = string.Join(Environment.NewLine, template.Columns); failOnNull = template.FailOnNull; whitespace = template.CheckWhitespace;
            valueType = template.ValueType; allowEmptyString = template.AllowedValues.Contains(""); allowedValues = string.Join(Environment.NewLine, template.AllowedValues.Where(v => v != "")); minimum = template.Minimum ?? ""; maximum = template.Maximum ?? "";
            includeMin = template.IncludeMinimum; includeMax = template.IncludeMaximum; aggregate = template.Aggregate;
            sourceMeasure = template.SourceMeasure ?? ""; targetMeasure = template.TargetMeasure ?? ""; tolerance = template.AbsoluteTolerance.ToString(CultureInfo.InvariantCulture); allowedViolations = template.AllowedViolations.ToString(CultureInfo.InvariantCulture);
        }
        query = test.Query ?? "";
        expectationMode = test.Expect?.Scalar != null ? "scalar" : test.Expect?.RowCount == 0 ? "zero" : "rowCount";
        rowCount = test.Expect?.RowCount?.ToString(CultureInfo.InvariantCulture) ?? "0"; scalarKind = test.Expect?.Scalar?.Kind ?? "decimal"; scalarValue = test.Expect?.Scalar?.Value ?? "";
        foreach (var collection in new[] { Keys, Mappings })
        {
            foreach (var row in collection) row.PropertyChanged += (_, _) => MarkDirty();
            collection.CollectionChanged += (_, args) => { if (args.NewItems != null) foreach (ValidationMappingRow row in args.NewItems) row.PropertyChanged += (_, _) => MarkDirty(); MarkDirty(); };
        }
        loading = false;
    }
    private new bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (!base.Set(ref field, value, property)) return false;
        if (!loading) MarkDirty();
        if (property == nameof(ColumnText)) Notify(nameof(SingleColumn));
        if (property == nameof(ValueType)) Notify(nameof(IsStringValue));
        if (property is nameof(ExpectationMode) or nameof(ScalarKind) or nameof(Aggregate)) { Notify(nameof(UseScalar)); Notify(nameof(UseRowCount)); Notify(nameof(NeedsMeasure)); }
        return true;
    }
    public void MarkDirty() { if (!loading) { dirty = true; Notify(nameof(IsDirty)); Notify(nameof(DraftLabel)); } }
    public bool IsDirty => dirty;
    public string DraftLabel => IsDirty ? "Unsaved validation changes" : "Configuration loaded";
    public string Id => original.Id;
    public string Kind { get; }
    public string TemplateName => ValidationTemplateChoice.Catalog.First(t => t.Kind == Kind).Name;
    public string TemplateDescription => ValidationTemplateChoice.Catalog.First(t => t.Kind == Kind).Description;
    public bool IsComparison => Kind == "dataComparison";
    public bool IsSql => Kind == "sqlAssertion";
    public bool IsRecipe => !IsComparison && !IsSql;
    public bool HasScope => !IsSql;
    public bool HasTarget => IsComparison || Kind is "referentialIntegrity" or "rowCount" or "aggregate";
    public bool HasKeys => IsComparison || Kind is "referentialIntegrity" or "aggregate";
    public bool HasColumnList => Kind is "requiredValues" or "uniqueKeys";
    public string ColumnListTitle => IsUnique ? "Business key columns *" : "Required columns *";
    public bool HasSingleColumn => Kind is "allowedValues" or "numericRange" or "dateRange" or "lengthRange";
    public bool IsRequired => Kind == "requiredValues";
    public bool IsUnique => Kind == "uniqueKeys";
    public bool IsAllowed => Kind == "allowedValues";
    public bool IsStringValue => ValueType == "string";
    public bool IsRange => Kind is "numericRange" or "dateRange" or "lengthRange";
    public bool IsDateRange => Kind == "dateRange";
    public bool IsAggregate => Kind == "aggregate";
    public bool HasTolerance => Kind is "rowCount" or "aggregate";
    public bool HasNullPolicy => Kind is "uniqueKeys" or "allowedValues" or "numericRange" or "dateRange" or "lengthRange" or "referentialIntegrity";
    public bool NeedsMeasure => IsAggregate && Aggregate != "count";
    public bool UseScalar => ExpectationMode == "scalar";
    public bool UseRowCount => ExpectationMode == "rowCount";
    public string ScopeTitle => Kind == "referentialIntegrity" ? "Child table" : HasTarget ? "Source table" : "Validation scope";
    public string TargetTitle => Kind == "referentialIntegrity" ? "Parent table" : "Target table";
    public string KeyTitle => Kind == "referentialIntegrity" ? "Child → parent key mappings" : IsAggregate ? "Grouping keys (optional)" : "Business key mappings";
    public string RangeHelp => Kind == "dateRange" ? "Use yyyy-MM-ddTHH:mm:ss, without a timezone offset. SQL wall-clock values are preserved." : Kind == "lengthRange" ? "Character counts include trailing spaces. Enter whole, non-negative numbers." : "Use decimal numbers with a dot. Leave a boundary empty for an open end.";
    public string Name { get => name; set => Set(ref name, value); }
    public string Connection { get => connection; set => Set(ref connection, value); }
    public bool Enabled { get => enabled; set => Set(ref enabled, value); }
    public string Severity { get => severity; set => Set(ref severity, value); }
    public string EvidenceLimit { get => evidenceLimit; set => Set(ref evidenceLimit, value); }
    public string Timeout { get => timeout; set => Set(ref timeout, value); }
    public string Tags { get => tags; set => Set(ref tags, value); }
    public string SourceSchema { get => sourceSchema; set => Set(ref sourceSchema, value); }
    public string SourceTable { get => sourceTable; set => Set(ref sourceTable, value); }
    public string TargetSchema { get => targetSchema; set => Set(ref targetSchema, value); }
    public string TargetTable { get => targetTable; set => Set(ref targetTable, value); }
    public string SourceFilter { get => sourceFilter; set => Set(ref sourceFilter, value); }
    public string TargetFilter { get => targetFilter; set => Set(ref targetFilter, value); }
    public string SourceConnection { get => sourceConnection; set => Set(ref sourceConnection, value); }
    public string TargetConnection { get => targetConnection; set => Set(ref targetConnection, value); }
    public string ColumnText { get => columnText; set => Set(ref columnText, value); }
    public string SingleColumn { get => ColumnText.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? ""; set => ColumnText = value; }
    public string ValueType { get => valueType; set => Set(ref valueType, value); }
    public string AllowedValues { get => allowedValues; set => Set(ref allowedValues, value); }
    public bool AllowEmptyString { get => allowEmptyString; set => Set(ref allowEmptyString, value); }
    public bool FailOnNull { get => failOnNull; set => Set(ref failOnNull, value); }
    public bool CheckWhitespace { get => whitespace; set => Set(ref whitespace, value); }
    public string Minimum { get => minimum; set => Set(ref minimum, value); }
    public string Maximum { get => maximum; set => Set(ref maximum, value); }
    public bool IncludeMinimum { get => includeMin; set => Set(ref includeMin, value); }
    public bool IncludeMaximum { get => includeMax; set => Set(ref includeMax, value); }
    public string Aggregate { get => aggregate; set => Set(ref aggregate, value); }
    public string SourceMeasure { get => sourceMeasure; set => Set(ref sourceMeasure, value); }
    public string TargetMeasure { get => targetMeasure; set => Set(ref targetMeasure, value); }
    public string Tolerance { get => tolerance; set => Set(ref tolerance, value); }
    public string AllowedViolations { get => allowedViolations; set => Set(ref allowedViolations, value); }
    public string Query { get => query; set => Set(ref query, value); }
    public string ExpectationMode { get => expectationMode; set => Set(ref expectationMode, value); }
    public string RowCount { get => rowCount; set => Set(ref rowCount, value); }
    public string ScalarKind { get => scalarKind; set => Set(ref scalarKind, value); }
    public string ScalarValue { get => scalarValue; set => Set(ref scalarValue, value); }
    public DatabaseObject? SourceObject { get => sourceObject; set { if (base.Set(ref sourceObject, value) && value != null) { SourceSchema = value.Schema; SourceTable = value.Name; } } }
    public DatabaseObject? TargetObject { get => targetObject; set { if (base.Set(ref targetObject, value) && value != null) { TargetSchema = value.Schema; TargetTable = value.Name; } } }
    public ObservableCollection<ValidationMappingRow> Keys { get; } = [];
    public ObservableCollection<ValidationMappingRow> Mappings { get; } = [];

    public TestDefinition Build()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new System.IO.InvalidDataException("Enter a validation name.");
        if (string.IsNullOrWhiteSpace(Connection)) throw new System.IO.InvalidDataException("Select a logical connection.");
        var result = original with
        {
            Name = Name.Trim(), Enabled = Enabled, Severity = Severity, Connection = Connection.Trim(),
            EvidenceLimit = (int)Integer(EvidenceLimit, "Evidence limit", 0, 10000),
            TimeoutSeconds = string.IsNullOrWhiteSpace(Timeout) ? null : (int)Integer(Timeout, "Query timeout", 1, 86400),
            Tags = Tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray()
        };
        var source = new TableScope { Schema = SourceSchema.Trim(), Table = SourceTable.Trim(), Filter = Empty(SourceFilter), Connection = Empty(SourceConnection) };
        var target = new TableScope { Schema = TargetSchema.Trim(), Table = TargetTable.Trim(), Filter = Empty(TargetFilter), Connection = Empty(TargetConnection) };
        if (IsComparison) return result with { Type = "dataComparison", Source = source, Target = target, Keys = Keys.Select(k => k.Build(true)).ToList(), Columns = Mappings.Select(m => m.Build()).ToList(), Checks = original.Checks with { MissingTargetRows = MissingRows, UnexpectedTargetRows = UnexpectedRows }, Query = null, Expect = null, Template = null };
        if (IsSql) return result with { Type = "sqlAssertion", Source = null, Target = null, Keys = [], Columns = [], Template = null, Query = Query, Expect = UseScalar ? new() { Scalar = new(ScalarKind, ScalarKind == "null" ? null : ScalarValue) } : new() { RowCount = ExpectationMode == "zero" ? 0 : Integer(RowCount, "Expected row count", 0, long.MaxValue) } };
        var template = new ValidationTemplateConfiguration
        {
            Kind = Kind, Source = source with { Connection = null }, Target = HasTarget ? target with { Connection = null } : null,
            Columns = ColumnText.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray(),
            Keys = Keys.Select(k => k.Build(true)).ToList(), FailOnNull = FailOnNull, CheckWhitespace = CheckWhitespace,
            ValueType = ValueType, AllowedValues = AllowedValues.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Concat(AllowEmptyString && IsStringValue ? [""] : Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray(),
            Minimum = Empty(Minimum), Maximum = Empty(Maximum), IncludeMinimum = IncludeMinimum, IncludeMaximum = IncludeMaximum,
            Aggregate = Aggregate, SourceMeasure = Empty(SourceMeasure), TargetMeasure = Empty(TargetMeasure),
            AbsoluteTolerance = Decimal(Tolerance, "Absolute tolerance"), AllowedViolations = Integer(AllowedViolations, "Allowed violations", 0, long.MaxValue)
        };
        var compiled = ValidationTemplateCompiler.Compile(template);
        return result with { Type = "sqlAssertion", Source = null, Target = null, Keys = [], Columns = [], Template = template, Query = compiled.Query, Expect = new() { RowCount = 0 } };
    }
    public bool MissingRows { get => missingRows; set => Set(ref missingRows, value); }
    public bool UnexpectedRows { get => extraRows; set => Set(ref extraRows, value); }
    public static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    public static decimal Decimal(string value, string label)
    {
        if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) || parsed < 0) throw new System.IO.InvalidDataException(label + ": enter a non-negative decimal number.");
        return parsed;
    }
    public static long Integer(string value, string label, long min, long max)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < min || parsed > max) throw new System.IO.InvalidDataException($"{label}: enter an integer between {min} and {max}.");
        return parsed;
    }
}
