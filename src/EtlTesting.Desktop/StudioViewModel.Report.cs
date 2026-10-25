using System.Collections.ObjectModel;
using EtlTesting.Core;
using EtlTesting.Reporting;

namespace EtlTesting.Desktop;

public sealed record ReportTestItem(TestResult Test)
{
    public string Name => Test.Name;
    public string Status => Test.Status;
    public string Severity => Test.Severity;
    public string Outcome => ReportPresentation.Outcome(Test);
    public string Counts => $"{Test.Violations:N0} violations · {Test.Evidence.Count:N0} saved examples";
    public string Scope => $"Source {Test.SourceRecords:N0} · Target {Test.TargetRecords:N0} · {Test.DurationSeconds:F2} s";
}
public sealed record ReportEvidenceItem(Evidence Evidence, TestResult? Test)
{
    public string TestName => Test?.Name ?? Evidence.TestId;
    public string Table => Evidence.Table;
    public string BusinessKey => ReportPresentation.DisplayKey(Evidence);
    public string Column => string.IsNullOrWhiteSpace(Evidence.Column) ? "—" : Evidence.Column;
    public string Category => ReportPresentation.Category(Evidence.Category);
    public string Expected => ReportPresentation.Expected(Evidence);
    public string Actual => ReportPresentation.Actual(Evidence);
    public string Explanation => ReportPresentation.Explanation(Evidence);
    public string Rule => string.IsNullOrWhiteSpace(Evidence.Rule) ? "Rule not recorded" : Evidence.Rule;
    public string RawDetails => $"Table: {Evidence.Table}\nTest ID: {Evidence.TestId}\nRule: {Evidence.Rule}\nReason: {Evidence.Reason}\nBusiness key: {Evidence.Key}";
}
public sealed partial class StudioViewModel
{
    private string reportTableFilter = "All tables", reportCategoryFilter = "All categories";
    private ReportEvidenceItem? selectedReportEvidence;
    public ObservableCollection<ReportTestItem> ReportTests { get; } = [];
    public ObservableCollection<ReportEvidenceItem> ReportEvidenceRows { get; } = [];
    public ObservableCollection<string> ReportTables { get; } = ["All tables"];
    public ObservableCollection<string> ReportCategories { get; } = ["All categories"];
    public string ReportTableFilter { get => reportTableFilter; set { if (Set(ref reportTableFilter, value)) { page = 0; RefreshEvidence(); } } }
    public string ReportCategoryFilter { get => reportCategoryFilter; set { if (Set(ref reportCategoryFilter, value)) { page = 0; RefreshEvidence(); } } }
    public ReportEvidenceItem? SelectedReportEvidence { get => selectedReportEvidence; set => Set(ref selectedReportEvidence, value); }
    public string ReportSuiteTitle => SelectedRun == null ? "Run report" : $"{SelectedRun.SuiteName} / Run report";
    public string ReportRunId => SelectedRun?.RunId ?? "—";
    public string ReportEnvironment => SelectedRun?.Environment ?? "—";
    public string ReportStatus => SelectedRun?.Status ?? "No run";
    public string ReportStarted => SelectedRun?.Started.ToLocalTime().ToString("dd MMM yyyy · HH:mm") ?? "—";
    public string ReportDuration => SelectedRun == null ? "—" : (SelectedRun.Ended - SelectedRun.Started).ToString(@"mm\:ss\.fff");
    public string ReportTestCount => SelectedRun?.Tests.Count.ToString("N0") ?? "0";
    public string ReportPassedCount => SelectedRun?.Tests.Count(t => t.Status == "Passed").ToString("N0") ?? "0";
    public string ReportFailedCount => SelectedRun?.Tests.Count(t => t.Status is "Failed" or "Error").ToString("N0") ?? "0";
    public string ReportMismatchCount => SelectedRun?.Tests.Sum(t => t.MismatchedRecords + t.MissingRecords + t.UnexpectedRecords).ToString("N0") ?? "0";
    public string ReportViolationCount => SelectedRun?.Tests.Sum(t => t.Violations).ToString("N0") ?? "0";
    public string ReportScope => SelectedRun?.Scope ?? "—";
    public string ReportNarrative => SelectedRun switch
    {
        null => "Run a suite or select a saved execution to inspect its results.",
        { Complete: false } => "This run is incomplete. Review errors or cancellation before relying on the results.",
        { Status: "Passed" } => "All required validations passed. Review warnings and the scope before sharing.",
        _ => $"{SelectedRun.Tests.Count(t => t.Status is "Failed" or "Error"):N0} validations need attention. Open a result or evidence row to see what happened."
    };
    public string ReportExecutionNotes => SelectedRun == null ? "No run selected." : $"Run ID: {SelectedRun.RunId}\nSuite: {SelectedRun.SuiteName}\nEnvironment: {SelectedRun.Environment}\nStarted: {SelectedRun.Started:O}\nEnded: {SelectedRun.Ended:O}\nScope: {SelectedRun.Scope}\nStrategy: {SelectedRun.Strategy}\nSuite SHA256: {SelectedRun.SuiteHash}\nComplete: {SelectedRun.Complete}\nRuntime parameters: {StudioJson.Write(SelectedRun.RuntimeParameters)}";
    private void RefreshReportRun()
    {
        ReportTests.Clear();
        foreach (var test in SelectedRun?.Tests ?? []) ReportTests.Add(new(test));
        ReportTables.Clear(); ReportTables.Add("All tables");
        ReportCategories.Clear(); ReportCategories.Add("All categories");
        foreach (var table in SelectedRun is null ? Enumerable.Empty<string>() : SelectedRun.Tests.SelectMany(t => t.Evidence).Select(e => e.Table).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)) ReportTables.Add(table);
        foreach (var category in SelectedRun is null ? Enumerable.Empty<string>() : SelectedRun.Tests.SelectMany(t => t.Evidence).Select(e => ReportPresentation.Category(e.Category)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)) ReportCategories.Add(category);
        reportTableFilter = "All tables"; reportCategoryFilter = "All categories";
        Notify(nameof(ReportTableFilter)); Notify(nameof(ReportCategoryFilter));
        foreach (var name in new[] { nameof(ReportSuiteTitle), nameof(ReportRunId), nameof(ReportEnvironment), nameof(ReportStatus), nameof(ReportStarted), nameof(ReportDuration), nameof(ReportTestCount), nameof(ReportPassedCount), nameof(ReportFailedCount), nameof(ReportMismatchCount), nameof(ReportViolationCount), nameof(ReportScope), nameof(ReportNarrative), nameof(ReportExecutionNotes) }) Notify(name);
    }
}
