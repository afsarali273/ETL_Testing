using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using EtlTesting.Core;
using EtlTesting.Persistence;
using EtlTesting.Reporting;
using EtlTesting.SqlServer;
using Microsoft.Win32;

namespace EtlTesting.Desktop;

public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Notify(name); return true; }
}
public sealed class ActionCommand(Func<Task> action) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public async void Execute(object? parameter) => await action();
}
public sealed class SectionVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value.ToString() == parameter.ToString() ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
public sealed class MappingEditor
{
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";
    public bool Trim { get; set; }
    public bool IgnoreCase { get; set; }
    public bool EmptyAsNull { get; set; }
    public decimal AbsoluteTolerance { get; set; }
    public int DateTimeToleranceMilliseconds { get; set; }
    public ColumnMapping ToMapping() => new() { Source = Source, Target = Target, Comparison = new() { Trim = Trim, IgnoreCase = IgnoreCase, EmptyAsNull = EmptyAsNull, AbsoluteTolerance = AbsoluteTolerance, DateTimeToleranceMilliseconds = DateTimeToleranceMilliseconds } };
    public static MappingEditor From(ColumnMapping m) => new() { Source = m.Source, Target = m.Target, Trim = m.Comparison.Trim, IgnoreCase = m.Comparison.IgnoreCase, EmptyAsNull = m.Comparison.EmptyAsNull, AbsoluteTolerance = m.Comparison.AbsoluteTolerance, DateTimeToleranceMilliseconds = m.Comparison.DateTimeToleranceMilliseconds };
}
public sealed partial class StudioViewModel : Observable
{
    private readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EtlTestStudio");
    private readonly IResultStore history;
    private Suite suite = new();
    private EnvironmentDefinition environment = new();
    private CancellationTokenSource? cancellation;
    private int section;
    private bool busy, demo = true;
    private string jsonText = "", environmentJson = "", status = "Ready", suitePath = "", parametersText = "batchId=B-20261024", logs = "", search = "";
    private TestDefinition? selectedTest;
    private RunResult? selectedRun;
    private Evidence? selectedEvidence;
    private int page;
    public StudioViewModel(string? stateDirectory = null)
    {
        if (stateDirectory != null) root = Path.GetFullPath(stateDirectory);
        history = new FileResultStore(Path.Combine(root, "history"));
        Connections = new(Path.Combine(root, "connections", "profiles-v1.json"), ApplyConnectionAsync, message => Status = message);
        Connections.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Connections.IsWorking)) { Notify(nameof(CanEdit)); designer?.RefreshHostState(); } if (e.PropertyName == nameof(Connections.ActiveConnectionSummary)) Notify(nameof(ConnectionStatus)); if (e.PropertyName is nameof(Connections.WorkspaceProfile) or nameof(Connections.WorkspaceDatabase) or nameof(Connections.WorkspaceEnvironment)) { Notify(nameof(ModeBadge)); Notify(nameof(ConnectionStatus)); } };
        OpenCommand = Command(OpenAsync); SaveCommand = Command(SaveAsync); ValidateCommand = Command(ValidateAsync); RunCommand = Command(RunAsync);
        CancelCommand = Command(() => { Cancel(); return Task.CompletedTask; });
        ApplyMappingsCommand = Command(ApplyMappingsAsync); AddComparisonCommand = Command(() => AddTestAsync(false)); AddAssertionCommand = Command(() => AddTestAsync(true));
        ExportCommand = Command(ExportAsync); OpenReportCommand = Command(OpenReportAsync); NextPageCommand = Command(() => { if ((page + 1) * 200 < FilteredEvidence().Count()) page++; RefreshEvidence(); return Task.CompletedTask; }); PreviousPageCommand = Command(() => { page = Math.Max(0, page - 1); RefreshEvidence(); return Task.CompletedTask; });
        SavePresetCommand = Command(SavePresetAsync); LoadPresetCommand = Command(LoadPresetAsync);
    }
    private ActionCommand Command(Func<Task> action) => new(async () => { try { await action(); } catch (Exception e) { Status = e is InvalidDataException or System.Text.Json.JsonException ? e.Message : $"Action failed: {e.GetType().Name}. Verify file permissions, SQL configuration and inputs."; } });
    public string ProductName => Product.Name;
    public StudioNavigationItem[] Navigation { get; } = [new("Overview", "home"), new("Test Suites", "suites"), new("Connections", "database"), new("SQL Workspace", "sql"), new("Run History", "history"), new("Reports", "reports"), new("Settings", "settings")];
    public string[] Headings { get; } = ["Execution overview", "Test suites", "Connection manager", "SQL workspace", "Execution history", "Run report", "CLI & pipeline configuration"];
    public int Section { get => section; set { Set(ref section, value); Notify(nameof(Heading)); Notify(nameof(Subtitle)); Notify(nameof(ModeBadge)); Notify(nameof(ConnectionStatus)); } }
    public string Heading => Section == 5 ? ReportSuiteTitle : Headings[Section];
    public string Subtitle => Section switch { 1 => "Organize suites, design tests, and configure every validation.", 2 => "Save, organize and verify your database connection profiles.", 3 => "Explore databases and run SQL using any saved connection.", _ => "Validate your ETL. Inspect every difference. Share the evidence." };
    public bool IsBusy { get => busy; set { Set(ref busy, value); Connections.HostBusy = value; designer?.RefreshHostState(); Notify(nameof(CanEdit)); } }
    public bool CanEdit => !IsBusy && !Connections.IsWorking && !(designer?.IsWorking ?? false);
    public bool IsDemo { get => demo; set { Set(ref demo, value); Notify(nameof(ModeBadge)); Notify(nameof(ConnectionStatus)); Notify(nameof(CliCommand)); Notify(nameof(OverviewEnvironment)); } }
    public string ModeBadge => Section switch { 2 => "SAVED CONNECTION PROFILES", 3 => Connections.HasWorkspaceProfile ? $"SQL SERVER · {Connections.WorkspaceEnvironment}" : "SQL WORKSPACE · Select a profile", _ => IsDemo ? "DEMO DATA · synthetic fixtures" : $"SQL SERVER · {environment.Name}" };
    public ConnectionManagerViewModel Connections { get; }
    public string ConnectionStatus => Section == 3 ? $"SQL workspace · {Connections.WorkspaceServer} / {Connections.WorkspaceDatabase}" : IsDemo ? "Demo mode · synthetic suite runs" : Connections.ActiveConnectionSummary;
    public string Status { get => status; set => Set(ref status, value); }
    public string JsonText { get => jsonText; set => Set(ref jsonText, value); }
    public string EnvironmentJson { get => environmentJson; set => Set(ref environmentJson, value); }
    public string SuitePath { get => suitePath; set { Set(ref suitePath, value); Notify(nameof(CliCommand)); } }
    public string ParametersText { get => parametersText; set => Set(ref parametersText, value); }
    public string Logs { get => logs; set => Set(ref logs, value); }
    public int Completed { get; private set; }
    public int Total { get; private set; }
    public string ProgressLabel => IsBusy ? $"{Completed} / {Total} tests complete · partial results" : "Ready to run";
    public string SuiteName => suite.Name;
    public int SuiteCount => string.IsNullOrEmpty(suite.Id) ? 0 : 1;
    public int TestCount => suite.TestCases.Count > 0 ? suite.TestCases.Count : suite.Tests.Count;
    public string PassedCount => SelectedRun?.Tests.Count(t => t.Status == "Passed").ToString() ?? "—";
    public string FailedCount => SelectedRun?.Tests.Count(t => t.Status == "Failed").ToString() ?? "—";
    public string LastDuration => SelectedRun == null ? "—" : (SelectedRun.Ended - SelectedRun.Started).ToString(@"mm\:ss\.fff");
    public string RunSummary => SelectedRun == null ? "No run selected. Run a suite or choose a saved execution." : $"{(SelectedRun.Demo ? "DEMO · " : "")}{SelectedRun.Status}   |   {SelectedRun.Environment}   |   {SelectedRun.Started.LocalDateTime:g}   |   {SelectedRun.RunId}";
    public ObservableCollection<TestDefinition> Tests { get; } = [];
    public ObservableCollection<MappingEditor> Keys { get; } = [];
    public ObservableCollection<MappingEditor> Mappings { get; } = [];
    public ObservableCollection<RunResult> Runs { get; } = [];
    public ObservableCollection<TestResult> Results { get; } = [];
    public ObservableCollection<Evidence> EvidenceRows { get; } = [];
    public string TestName { get; set; } = "";
    public string SourceSchema { get; set; } = "staging";
    public string SourceTable { get; set; } = "";
    public string TargetSchema { get; set; } = "dbo";
    public string TargetTable { get; set; } = "";
    public string SourceFilter { get; set; } = "";
    public string TargetFilter { get; set; } = "";
    public string Query { get; set; } = "";
    public long ExpectedRowCount { get; set; }
    public bool MissingRows { get; set; } = true;
    public bool UnexpectedRows { get; set; } = true;
    public TestDefinition? SelectedTest
    {
        get => selectedTest;
        set
        {
            Set(ref selectedTest, value);
            Keys.Clear(); Mappings.Clear();
            if (value == null) return;
            TestName = value.Name; SourceSchema = value.Source?.Schema ?? "staging"; SourceTable = value.Source?.Table ?? ""; TargetSchema = value.Target?.Schema ?? "dbo"; TargetTable = value.Target?.Table ?? "";
            SourceFilter = value.Source?.Filter ?? ""; TargetFilter = value.Target?.Filter ?? ""; Query = value.Query ?? ""; ExpectedRowCount = value.Expect?.RowCount ?? 0;
            MissingRows = value.Checks.MissingTargetRows; UnexpectedRows = value.Checks.UnexpectedTargetRows;
            foreach (var key in value.Keys) Keys.Add(MappingEditor.From(key)); foreach (var mapping in value.Columns) Mappings.Add(MappingEditor.From(mapping));
            foreach (var name in new[] { nameof(TestName), nameof(SourceSchema), nameof(SourceTable), nameof(TargetSchema), nameof(TargetTable), nameof(SourceFilter), nameof(TargetFilter), nameof(Query), nameof(ExpectedRowCount), nameof(MissingRows), nameof(UnexpectedRows) }) Notify(name);
        }
    }
    public RunResult? SelectedRun { get => selectedRun; set { Set(ref selectedRun, value); Results.Clear(); foreach (var test in value?.Tests ?? []) Results.Add(test); RefreshReportRun(); page = 0; RefreshEvidence(); foreach (var name in new[] { nameof(RunSummary), nameof(PassedCount), nameof(FailedCount), nameof(LastDuration), nameof(Heading) }) Notify(name); } }
    public Evidence? SelectedEvidence { get => selectedEvidence; set => Set(ref selectedEvidence, value); }
    public string Search { get => search; set { Set(ref search, value); page = 0; RefreshEvidence(); } }
    public string EvidenceSummary { get; private set; } = "No evidence";
    public string CliCommand => $".\\etltest.exe run --suite \"{SuitePath}\" {(IsDemo ? "--demo" : "--environment \"qa.json\"")} --parameter batchId=B-20261024 --output \"artifacts\\etl-results\" --formats html,json,csv,junit";
    public ICommand OpenCommand { get; } public ICommand SaveCommand { get; } public ICommand ValidateCommand { get; } public ICommand RunCommand { get; } public ICommand CancelCommand { get; }
    public ICommand ApplyMappingsCommand { get; } public ICommand AddComparisonCommand { get; } public ICommand AddAssertionCommand { get; }
    public ICommand ExportCommand { get; } public ICommand OpenReportCommand { get; } public ICommand NextPageCommand { get; } public ICommand PreviousPageCommand { get; } public ICommand SavePresetCommand { get; } public ICommand LoadPresetCommand { get; }
    public async Task InitializeAsync()
    {
        try
        {
            Directory.CreateDirectory(root);
            var sample = Path.Combine(AppContext.BaseDirectory, "samples", "suites", "reservations.json");
            SuitePath = Path.Combine(root, "suites", "reservations.json");
            if (!File.Exists(SuitePath)) await AtomicFile.WriteAsync(SuitePath, await File.ReadAllTextAsync(sample));
            LoadSuite(await File.ReadAllTextAsync(SuitePath)); savedSuiteText = JsonText;
            await RefreshLibraryAsync();
            var env = Path.Combine(root, "qa.json");
            var hasSavedEnvironment = File.Exists(env);
            EnvironmentJson = hasSavedEnvironment ? await File.ReadAllTextAsync(env) : await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "samples", "environments", "qa.json"));
            environment = StudioJson.Read<EnvironmentDefinition>(EnvironmentJson);
            await Connections.InitializeAsync(environment, hasSavedEnvironment);
            IsDemo = !hasSavedEnvironment;
            await ReloadHistoryAsync(); Status = "Suite and saved connection profiles loaded.";
        }
        catch (Exception e) { Status = $"Startup failed: {e.Message}"; }
    }
    private void LoadSuite(string text)
    {
        suite = SuiteHierarchy.ForEditing(DefinitionLoader.Parse(text)); JsonText = StudioJson.Write(suite); Tests.Clear(); foreach (var test in suite.Tests) Tests.Add(test); SelectedTest = Tests.FirstOrDefault();
        Notify(nameof(SuiteName)); Notify(nameof(SuiteCount)); Notify(nameof(TestCount));
        designer?.LoadSuite(suite);
        RefreshTestCases();
    }
    private async Task ApplyConnectionAsync(EnvironmentDefinition selectedEnvironment, string? defaultConnection)
    {
        Suite? changed = defaultConnection == null ? null : DefinitionLoader.Parse(JsonText);
        if (changed != null) changed = changed with { Defaults = changed.Defaults with { Connection = defaultConnection! } };
        await AtomicFile.WriteAsync(Path.Combine(root, "qa.json"), StudioJson.Write(selectedEnvironment));
        environment = selectedEnvironment; EnvironmentJson = StudioJson.Write(selectedEnvironment);
        if (changed != null) { suite = changed; JsonText = StudioJson.Write(changed); IsDemo = false; }
        Notify(nameof(ModeBadge)); Notify(nameof(ConnectionStatus)); Notify(nameof(CliCommand)); Notify(nameof(OverviewEnvironment));
    }
    private Dictionary<string, string> Overrides()
    {
        var values = new Dictionary<string, string>();
        foreach (var line in ParametersText.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var index = line.IndexOf('='); if (index < 1) throw new InvalidDataException("Runtime parameters: one name=value per line required");
            values[line[..index]] = line[(index + 1)..];
        }
        return values;
    }
    private async Task OpenAsync()
    {
        if (IsBusy || !await LeaveSuiteAsync()) return;
        var dialog = new OpenFileDialog { Filter = "Suite JSON|*.json" };
        if (dialog.ShowDialog() != true) return;
        var text = await File.ReadAllTextAsync(dialog.FileName); LoadSuite(text); SuitePath = dialog.FileName; savedSuiteText = JsonText; Designer.EditorReset(); Designer.OpenTest(SelectedTestCase?.Id); ShowingLibrary = false; await SuiteLibrary.RegisterAsync([dialog.FileName]); await RefreshLibraryAsync(); Section = 1; Status = "Suite loaded";
    }
    private Task ValidateAsync() { designer?.ApplyInlineDraft(); LoadSuite(JsonText); _ = DefinitionLoader.Bind(suite, Overrides()); Status = $"Configuration valid · {suite.TestCases.Count} tests · {suite.Tests.Count} validations · no queries executed"; return Task.CompletedTask; }
    private async Task SaveAsync()
    {
        if (IsBusy) return;
        designer?.ApplyInlineDraft();
        if (designer != null && !designer.ConfirmDiscard()) return;
        designer?.EditorReset();
        designer?.OpenTest(SelectedTestCase?.Id);
        var validated = DefinitionLoader.Parse(JsonText); _ = DefinitionLoader.Bind(validated, Overrides());
        var bundled = SuitePath.StartsWith(Path.Combine(AppContext.BaseDirectory, "samples") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        var dialog = new SaveFileDialog { Filter = "Suite JSON|*.json", FileName = Path.GetFileName(SuitePath), InitialDirectory = bundled ? SuiteLibrary.SuitesDirectory : Path.GetDirectoryName(SuitePath) };
        if (dialog.ShowDialog() != true) return;
        await AtomicFile.WriteAsync(dialog.FileName, StudioJson.Write(validated)); SuitePath = dialog.FileName; LoadSuite(StudioJson.Write(validated)); savedSuiteText = JsonText; Designer.OpenTest(SelectedTestCase?.Id); await SuiteLibrary.RegisterAsync([dialog.FileName]); await RefreshLibraryAsync(); Status = "Suite saved";
    }
    private Task ApplyMappingsAsync()
    {
        if (IsBusy) return Task.CompletedTask;
        // Parse JSON first so advanced settings are retained and JSON edits are never overwritten silently.
        var parsed = DefinitionLoader.Parse(JsonText);
        var current = parsed.Tests.SingleOrDefault(t => t.Id == SelectedTest?.Id) ?? throw new InvalidDataException("Select a test present in JSON before applying form edits");
        var changed = current.Type == "dataComparison" ? current with
        {
            Name = TestName,
            Source = current.Source! with { Schema = SourceSchema, Table = SourceTable, Filter = string.IsNullOrWhiteSpace(SourceFilter) ? null : SourceFilter },
            Target = current.Target! with { Schema = TargetSchema, Table = TargetTable, Filter = string.IsNullOrWhiteSpace(TargetFilter) ? null : TargetFilter },
            Keys = Keys.Select(k => k.ToMapping()).ToList(), Columns = Mappings.Select(m => m.ToMapping()).ToList(), Checks = current.Checks with { MissingTargetRows = MissingRows, UnexpectedTargetRows = UnexpectedRows }
        } : current with { Name = TestName, Query = Query, Expect = current.Expect?.Scalar != null ? current.Expect : new() { RowCount = ExpectedRowCount } };
        parsed.Tests[parsed.Tests.FindIndex(t => t.Id == current.Id)] = changed;
        LoadSuite(StudioJson.Write(parsed)); SelectedTest = Tests.First(t => t.Id == changed.Id); Status = "Form applied to JSON. Save to persist."; return Task.CompletedTask;
    }
    private Task AddTestAsync(bool assertion)
    {
        if (IsBusy) return Task.CompletedTask;
        var parsed = DefinitionLoader.Parse(JsonText);
        var template = assertion ? new TestDefinition { Id = "assert-" + Guid.NewGuid().ToString("N")[..8], Name = "New SQL assertion", Type = "sqlAssertion", Query = "SELECT 1 AS Violation WHERE 1 = 0", Expect = new() { RowCount = 0 } }
            : new TestDefinition { Id = "compare-" + Guid.NewGuid().ToString("N")[..8], Name = "New comparison — configure table names", Source = new() { Schema = "staging", Table = "Reservation" }, Target = new() { Table = "Reservation" }, Keys = [new() { Source = "ReservationId", Target = "ReservationId" }], Columns = [new() { Source = "GuestName", Target = "GuestName" }] };
        parsed.Tests.Add(template with { TestCaseId = selectedTestCase?.Id }); LoadSuite(StudioJson.Write(parsed)); SelectedTest = Tests.Last(); ShowingLibrary = false; Section = 1; Status = "Template added as an editable copy. Verify scope and mappings before running."; return Task.CompletedTask;
    }
    private Task RunAsync() => RunSuiteAsync();
    private async Task RunSuiteAsync(string[]? testIds = null)
    {
        if (!CanEdit) return;
        designer?.ApplyInlineDraft();
        if (designer?.Editor?.IsDirty == true) throw new InvalidDataException("Save the validation draft before running the suite. Preview validation can run an unsaved draft.");
        var parsed = DefinitionLoader.Parse(JsonText); var parameters = DefinitionLoader.Bind(parsed, Overrides());
        IDataConnector connector;
        if (IsDemo) connector = new DemoConnector();
        else { environment = StudioJson.Read<EnvironmentDefinition>(EnvironmentJson); var sql = new SqlServerConnector(environment, new EnvironmentCredentialResolver()); sql.Preflight(parsed, parameters); connector = sql; }
        IsBusy = true; cancellation = new(); Logs = ""; Total = parsed.Tests.Count; Completed = 0; Notify(nameof(Total)); Notify(nameof(Completed)); Notify(nameof(ProgressLabel));
        Section = 4; Status = "Running · partial results";
        try
        {
            var progress = new Progress<RunProgress>(p => { Completed = p.Completed; Notify(nameof(Completed)); Notify(nameof(ProgressLabel)); Logs += $"{DateTimeOffset.Now:HH:mm:ss}  {p.Message}\n"; });
            var runner = new SuiteRunner([new ComparisonHandler(new SqliteRowIndexFactory()), new SqlAssertionHandler()]);
            var options = new RunOptions(Path.Combine(root, "work"), IsDemo, TestIds: testIds);
            var run = await Task.Run(() => runner.RunAsync(parsed, IsDemo ? "DEMO" : environment.Name, connector, parameters, options, progress, cancellation.Token));
            var output = Path.Combine(root, "artifacts", run.RunId);
            await new ReportWriter().WriteAsync(run, output, ReportWriter.Supported);
            await history.SaveAsync(run); await ReloadHistoryAsync(); SelectedRun = Runs.First(r => r.RunId == run.RunId);
            designer?.RecordRun(run);
            Status = $"{(run.Demo ? "DEMO · " : "")}{run.Status} · reports saved · {output}"; Section = 5;
        }
        finally { IsBusy = false; cancellation.Dispose(); cancellation = null; Notify(nameof(ProgressLabel)); }
    }
    public void Cancel() { cancellation?.Cancel(); Connections.Cancel(); designer?.Cancel(); }
    private async Task ReloadHistoryAsync() { Runs.Clear(); foreach (var run in await history.ListAsync()) Runs.Add(run); if (SelectedRun == null) SelectedRun = Runs.FirstOrDefault(); RebuildOverview(); }
    private IEnumerable<Evidence> FilteredEvidence() => (SelectedRun?.Tests.SelectMany(t => t.Evidence) ?? []).Where(e =>
        (ReportTableFilter == "All tables" || string.Equals(e.Table, ReportTableFilter, StringComparison.OrdinalIgnoreCase)) &&
        (ReportCategoryFilter == "All categories" || string.Equals(ReportPresentation.Category(e.Category), ReportCategoryFilter, StringComparison.OrdinalIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(Search) || StudioJson.Write(e).Contains(Search, StringComparison.OrdinalIgnoreCase) || (SelectedRun?.Tests.FirstOrDefault(t => t.Id == e.TestId)?.Name.Contains(Search, StringComparison.OrdinalIgnoreCase) ?? false)));
    private void RefreshEvidence()
    {
        EvidenceRows.Clear(); ReportEvidenceRows.Clear(); var all = FilteredEvidence().ToArray(); foreach (var e in all.Skip(page * 200).Take(200)) { EvidenceRows.Add(e); ReportEvidenceRows.Add(new(e, SelectedRun?.Tests.FirstOrDefault(t => t.Id == e.TestId))); } SelectedEvidence = EvidenceRows.FirstOrDefault(); SelectedReportEvidence = ReportEvidenceRows.FirstOrDefault();
        EvidenceSummary = $"Page {page + 1} · {EvidenceRows.Count} of {all.Length} retained examples · {SelectedRun?.Tests.Sum(t => t.Violations) ?? 0} total violations · {(SelectedRun?.Tests.Any(t => t.EvidenceTruncated) == true ? "Evidence capped; full scope checked" : "All evidence retained")}"; Notify(nameof(EvidenceSummary));
    }
    private async Task ExportAsync()
    {
        var run = SelectedRun ?? throw new InvalidDataException("Select a run first");
        var dialog = new OpenFolderDialog { Title = "Export run reports" }; if (dialog.ShowDialog() != true) return;
        await new ReportWriter().WriteAsync(run, dialog.FolderName, ReportWriter.Supported); Status = "Exported HTML, PDF, JSON, CSV and JUnit XML to " + dialog.FolderName;
    }
    private async Task OpenReportAsync()
    {
        var run = SelectedRun ?? throw new InvalidDataException("Select a run first");
        var directory = Path.Combine(root, "artifacts", run.RunId);
        await new ReportWriter().WriteAsync(run, directory, ["html", "pdf"]);
        // A fresh local filename avoids a browser reusing an earlier file-not-found tab.
        var preview = Path.Combine(directory, $"report-preview-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.html");
        File.Copy(Path.Combine(directory, "report.html"), preview);
        Process.Start(new ProcessStartInfo(preview) { UseShellExecute = true });
    }
    private async Task SavePresetAsync()
    {
        var parsed = DefinitionLoader.Parse(JsonText); var test = parsed.Tests.SingleOrDefault(t => t.Id == SelectedTest?.Id) ?? throw new InvalidDataException("Select a configured test");
        var dialog = new SaveFileDialog { Filter = "Versioned suite preset|*.json", FileName = test.Id + "-preset.json" }; if (dialog.ShowDialog() != true) return;
        await AtomicFile.WriteAsync(dialog.FileName, StudioJson.Write(parsed with { Id = "preset-" + test.Id, Name = test.Name + " preset", Tests = [test] })); Status = "Versioned preset saved";
    }
    private async Task LoadPresetAsync()
    {
        var dialog = new OpenFileDialog { Filter = "Versioned suite preset|*.json" }; if (dialog.ShowDialog() != true) return;
        var preset = DefinitionLoader.Parse(await File.ReadAllTextAsync(dialog.FileName)); var parsed = DefinitionLoader.Parse(JsonText);
        foreach (var (key, value) in preset.Parameters) { if (parsed.Parameters.TryGetValue(key, out var existing) && existing != value) throw new InvalidDataException("Preset parameter declaration conflicts: " + key); parsed.Parameters[key] = value; }
        parsed.Tests.AddRange(preset.Tests.Select(t => t with { Id = t.Id + "-" + Guid.NewGuid().ToString("N")[..6], TestCaseId = selectedTestCase?.Id })); LoadSuite(StudioJson.Write(parsed)); Status = "Preset added as editable copies; verify connections and scope";
    }
}
