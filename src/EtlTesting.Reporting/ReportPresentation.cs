using System.Text.Json;
using EtlTesting.Core;

namespace EtlTesting.Reporting;

public static class ReportPresentation
{
    public static string Category(string category) => category.ToLowerInvariant() switch
    {
        "datacomparison" or "fieldmismatch" => "Value mismatch",
        "missingtarget" or "missingtargetrows" or "missingrecord" => "Missing target record",
        "unexpectedtarget" or "unexpectedtargetrows" or "unexpectedrecord" => "Unexpected target record",
        "duplicatekey" or "uniquekeys" or "duplicatekeys" or "nullkey" => "Duplicate or invalid key",
        "requiredvalues" or "requiredvalue" => "Required value missing",
        "allowedvalues" => "Value outside allowed list",
        "numericrange" => "Number outside range",
        "daterange" => "Date outside range",
        "lengthrange" => "Text length outside range",
        "referentialintegrity" => "Missing related record",
        "rowcount" => "Row count mismatch",
        "aggregate" => "Aggregate mismatch",
        _ => SplitWords(category)
    };
    public static string Outcome(TestResult test) => test.Status switch
    {
        "Passed" => "The configured validation completed with no violations.",
        "Failed" when test.Severity == "warning" => $"{test.Violations:N0} warning-level violation{(test.Violations == 1 ? "" : "s")} found. The run can still pass.",
        "Failed" => $"{test.Violations:N0} violation{(test.Violations == 1 ? "" : "s")} found. Review the examples and rule below.",
        "Error" => "The validation could not complete. Review the technical error before trusting its counts.",
        "Cancelled" => "Execution was cancelled. Results for this validation may be incomplete.",
        "Skipped" => "This validation was skipped and did not check data.",
        _ => "This validation has not produced a final result."
    };
    public static string Explanation(Evidence evidence) => evidence.Category.ToLowerInvariant() switch
    {
        "requiredvalues" or "requiredvalue" => "A required field was missing or empty in this record.",
        "uniquekeys" or "duplicatekey" or "duplicatekeys" or "nullkey" => "The business key is duplicated or contains a null component.",
        "allowedvalues" => "A field contains a value outside the configured allowed list.",
        "referentialintegrity" => "This record has no matching related record.",
        "datacomparison" or "fieldmismatch" => "The source and target values differ under the configured comparison rule.",
        "missingrecord" or "missingtarget" => "A source record has no matching target record.",
        "unexpectedrecord" or "unexpectedtarget" => "A target record has no matching source record.",
        "rowcount" => "The observed row count differs from the configured expectation.",
        _ => "This record did not meet the configured validation rule."
    };
    public static string DisplayKey(Evidence evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence.Key)) return "No business key recorded";
        try
        {
            using var json = JsonDocument.Parse(evidence.Key);
            if (json.RootElement.ValueKind == JsonValueKind.Array)
                return string.Join(" · ", json.RootElement.EnumerateArray().Select(CellValue));
            if (json.RootElement.ValueKind != JsonValueKind.Object) return evidence.Key;
            var priority = new[] { "PropertyCode", "ReservationId", "CustomerId", "PaymentId", "Id", "Key" };
            var values = json.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
            var parts = priority.Where(values.ContainsKey).Take(3).Select(name => $"{name}: {CellValue(values[name])}").ToArray();
            if (parts.Length > 0) return string.Join(" · ", parts);
            return string.Join(" · ", values.Take(2).Select(p => $"{p.Key}: {CellValue(p.Value)}"));
        }
        catch (JsonException) { return evidence.Key.Length <= 100 ? evidence.Key : evidence.Key[..97] + "..."; }
    }
    public static string Expected(Evidence evidence) => IsComparison(evidence) ? evidence.Expected.ToString() : "See rule";
    public static string Actual(Evidence evidence) => IsComparison(evidence) ? evidence.Actual.ToString() : "See captured row";
    private static bool IsComparison(Evidence evidence) => evidence.Category.ToLowerInvariant() is "datacomparison" or "fieldmismatch" or "missingtarget" or "unexpectedtarget" || evidence.Expected.Value != null || evidence.Actual.Value != null;
    private static string CellValue(JsonElement value) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out var item) ? item.ValueKind == JsonValueKind.Null ? "NULL" : item.ToString() : value.ToString();
    private static string SplitWords(string value) => string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString())).Replace("_", " ");
}
