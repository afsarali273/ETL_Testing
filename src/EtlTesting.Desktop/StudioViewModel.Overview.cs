using System.Collections.ObjectModel;
using System.Windows.Input;
using EtlTesting.Core;

namespace EtlTesting.Desktop;

public sealed record OverviewRunRow(RunResult Run)
{
    public string Started => Run.Started.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    public string Suite => Run.SuiteName;
    public string Environment => Run.Demo ? "DEMO" : Run.Environment.Length > 17 ? Run.Environment[..16] + "…" : Run.Environment;
    public string Status => Run.Status;
    public string StatusLabel => "● " + Status;
    public string StatusColor => Status == "Passed" ? "#07945B" : Status == "Failed" ? "#D52A2A" : "#D47800";
    public int Passed => Run.Tests.Count(t => t.Status == "Passed");
    public int Failed => Run.Tests.Count(t => t.Status is "Failed" or "Error");
    public string Duration => (Run.Ended - Run.Started).ToString(@"hh\:mm\:ss");
}
public sealed record OverviewSuiteRow(SuiteCard Card, RunResult? LastRun)
{
    public string Name => Card.Name;
    public int Tests => Card.Entry.Definition?.TestCases.Count > 0 ? Card.Entry.Definition.TestCases.Count : Card.Entry.Definition?.Tests.Count ?? 0;
    public string LastRunAt => LastRun?.Started.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? "Never";
    public string Status => LastRun?.Status ?? "Not run";
    public string StatusLabel => "● " + Status;
    public string StatusColor => Status == "Passed" ? "#07945B" : Status == "Not run" ? "#73839A" : "#D52A2A";
}
public sealed record OverviewFailureRow(RunResult Run, TestResult Test)
{
    public string Time => Run.Started.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    public string Suite => Run.SuiteName;
    public string TestName => Test.TestCaseName ?? Test.Name;
    public string Message => Test.Error ?? Test.Evidence.FirstOrDefault()?.Reason ?? $"{Test.Violations:N0} validation violations";
}
public sealed partial class StudioViewModel
{
    public ObservableCollection<OverviewRunRow> OverviewRuns { get; } = [];
    public ObservableCollection<OverviewSuiteRow> OverviewSuites { get; } = [];
    public ObservableCollection<OverviewFailureRow> OverviewFailures { get; } = [];
    public int OverviewSuiteCount => SuiteCards.Count(c => c.Entry.Definition != null);
    public int OverviewTestCount => SuiteCards.Where(c => c.Entry.Definition != null).Sum(c => c.Entry.Definition!.TestCases.Count > 0 ? c.Entry.Definition.TestCases.Count : c.Entry.Definition.Tests.Count);
    public string OverviewPassed => Runs.FirstOrDefault()?.Tests.Count(t => t.Status == "Passed").ToString() ?? "—";
    public string OverviewFailed => Runs.FirstOrDefault()?.Tests.Count(t => t.Status is "Failed" or "Error").ToString() ?? "—";
    public string OverviewLatestScope => Runs.FirstOrDefault() is { } latest ? $"Out of {latest.Tests.Count} validations" : "No executions yet";
    public string OverviewDuration => Runs.FirstOrDefault() is { } latest ? (latest.Ended - latest.Started).ToString(@"mm\:ss") : "—";
    public string OverviewEnvironment => IsDemo ? "Demo data" : environment.Name;
    public string OverviewUpdatedAt => "Last refresh: " + DateTime.Now.ToString("HH:mm");
    public ICommand ViewAllRunsCommand => Command(() => { Section = 4; return Task.CompletedTask; });
    public ICommand ViewAllSuitesCommand => LibraryCommand(async _ => { if (!await LeaveSuiteAsync()) return; ShowingLibrary = true; Section = 1; });
    public ICommand ViewAllFailuresCommand => Command(() => { SelectedRun = OverviewFailures.FirstOrDefault()?.Run ?? SelectedRun; Section = 4; return Task.CompletedTask; });
    public ICommand OverviewOpenSuiteCommand => LibraryCommand(async item =>
    {
        if (item is OverviewSuiteRow row && await OpenLibrarySuiteAsync(row.Card)) Section = 1;
    });
    public ICommand OverviewOpenRunCommand => LibraryCommand(item =>
    {
        if (item is OverviewRunRow row) { SelectedRun = row.Run; Section = 4; }
        else if (item is OverviewFailureRow failure) { SelectedRun = failure.Run; Section = 4; }
        return Task.CompletedTask;
    });
    public ICommand RefreshOverviewCommand => Command(async () => { await RefreshLibraryAsync(); await ReloadHistoryAsync(); Status = "Overview refreshed"; });
    private void RebuildOverview()
    {
        OverviewRuns.Clear();
        foreach (var run in Runs.Take(6)) OverviewRuns.Add(new(run));
        OverviewSuites.Clear();
        foreach (var card in SuiteCards.Where(c => c.Entry.Definition != null).Take(8))
            OverviewSuites.Add(new(card, Runs.FirstOrDefault(r => r.SuiteId == card.Entry.Definition!.Id)));
        OverviewFailures.Clear();
        foreach (var run in Runs)
            foreach (var test in run.Tests.Where(t => t.Status is "Failed" or "Error"))
            {
                if (OverviewFailures.Count >= 8) break;
                OverviewFailures.Add(new(run, test));
            }
        foreach (var name in new[] { nameof(OverviewSuiteCount), nameof(OverviewTestCount), nameof(OverviewPassed), nameof(OverviewFailed), nameof(OverviewLatestScope), nameof(OverviewDuration), nameof(OverviewEnvironment), nameof(OverviewUpdatedAt) }) Notify(name);
    }
}
