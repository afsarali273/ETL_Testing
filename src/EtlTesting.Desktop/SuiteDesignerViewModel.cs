using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using EtlTesting.Core;
using EtlTesting.SqlServer;

namespace EtlTesting.Desktop;

public sealed class DesignerCommand(Func<object?, Task> action) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public async void Execute(object? parameter) => await action(parameter);
}

public sealed class SuiteValidationRow(TestDefinition test, Action<SuiteValidationRow, bool> toggle) : Observable
{
    private bool selected;
    private string lastRun = "Ready";
    public TestDefinition Definition { get; } = test;
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string Kind => Definition.Template?.Kind ?? Definition.Type;
    public string TypeName => ValidationTemplateChoice.Catalog.FirstOrDefault(t => t.Kind == Kind)?.Name ?? Kind;
    public string Scope => Definition.Source is TableScope source ? $"{source.Schema}.{source.Table} → {Definition.Target?.Schema}.{Definition.Target?.Table}" : Definition.Template is { } template ? $"{template.Source.Schema}.{template.Source.Table}" + (template.Target == null ? "" : $" → {template.Target.Schema}.{template.Target.Table}") : "Custom SQL";
    public string Severity => Definition.Severity == "warning" ? "Warning" : "Error";
    public bool Enabled { get => Definition.Enabled; set { if (value != Definition.Enabled) toggle(this, value); } }
    public bool Selected { get => selected; set => Set(ref selected, value); }
    public string Status => Enabled ? LastRun : "Disabled";
    public string LastRun { get => lastRun; set { Set(ref lastRun, value); Notify(nameof(Status)); } }
}

public sealed class SuiteParameterEditor : Observable
{
    private string name = "", type = "string", defaultValue = "";
    private bool required;
    public string Name { get => name; set => Set(ref name, value); }
    public string Type { get => type; set => Set(ref type, value); }
    public string DefaultValue { get => defaultValue; set => Set(ref defaultValue, value); }
    public bool Required { get => required; set => Set(ref required, value); }
}

public sealed record DesignerPreviewView(string Name, DataView Rows);

public sealed partial class SuiteDesignerViewModel : Observable
{
    private readonly Func<string> readSuite;
    private readonly Action<string> applySuite;
    private readonly Func<EnvironmentDefinition> readEnvironment;
    private readonly Func<bool> hostBusy;
    private readonly Func<bool> isDemo;
    private readonly Func<Dictionary<string, ParameterDefinition>, IReadOnlyList<BoundParameter>> bindParameters;
    private readonly Func<string?, Task> run;
    private readonly Func<TestDefinition, Dictionary<string, ParameterDefinition>, CancellationToken, Task<RunResult>> previewValidation;
    private readonly Func<Task> saveSuite;
    private readonly Action<string> status;
    private readonly Stack<List<(int Index, TestDefinition Test)>> deleted = new();
    private Suite current = new();
    private string search = "", typeFilter = "All types", enabledFilter = "All validations", templateSearch = "", category = "All categories", feedback = "Select a validation to edit, or add a template.", jsonText = "", generatedSql = "", previewMessage = "Preview the source scope or run this validation to inspect its evidence.";
    private bool working, feedbackError, newValidation, readingMetadata, hasPreview, loadingParameters;
    private int stage;
    private CancellationTokenSource? cancellation;
    private ValidationEditor? editor;
    private SuiteValidationRow? selectedRow;
    private ValidationTemplateChoice? selectedTemplate;
    private DesignerPreviewView? selectedPreview;

    public SuiteDesignerViewModel(Func<string> readSuite, Action<string> applySuite, Func<EnvironmentDefinition> readEnvironment, Func<bool> hostBusy, Func<bool> isDemo, Func<Dictionary<string, ParameterDefinition>, IReadOnlyList<BoundParameter>> bindParameters, Func<string?, Task> run, Func<TestDefinition, Dictionary<string, ParameterDefinition>, CancellationToken, Task<RunResult>> previewValidation, Func<Task> saveSuite, Action<string> status)
    {
        this.readSuite = readSuite; this.applySuite = applySuite; this.readEnvironment = readEnvironment; this.hostBusy = hostBusy; this.isDemo = isDemo; this.bindParameters = bindParameters; this.run = run; this.previewValidation = previewValidation; this.saveSuite = saveSuite; this.status = status;
        RowsView = CollectionViewSource.GetDefaultView(Rows); RowsView.Filter = item => item is SuiteValidationRow row && (ActiveTestCaseId == null || row.Definition.TestCaseId == ActiveTestCaseId) && Matches(Search, row.Name, row.Scope, row.TypeName, row.Id) && (TypeFilter == "All types" || row.TypeName == TypeFilter) && (EnabledFilter == "All validations" || row.Enabled == (EnabledFilter == "Enabled"));
        TemplatesView = CollectionViewSource.GetDefaultView(Templates); TemplatesView.Filter = item => item is ValidationTemplateChoice t && Matches(TemplateSearch, t.Name, t.Category, t.Description) && (Category == "All categories" || t.Category == Category);
        AddCommand = Command(_ => { if (!ConfirmDiscard()) return Task.CompletedTask; BeginBatch(); return Task.CompletedTask; });
        ChooseTemplateCommand = Command(p => BeginTemplateAsync(p as ValidationTemplateChoice ?? SelectedTemplate));
        EditCommand = Command(p => { var row = Row(p); if (ConfirmDiscard()) OpenEditor(row.Definition, false); return Task.CompletedTask; });
        DuplicateCommand = Command(p => { var row = Row(p); if (ConfirmDiscard()) OpenEditor(row.Definition with { Id = row.Id + "-" + Guid.NewGuid().ToString("N")[..6], Name = row.Name + " copy" }, true); return Task.CompletedTask; });
        DeleteCommand = Command(p => { var row = Row(p); if (!ConfirmDiscard()) return Task.CompletedTask; DeleteRows([row]); Editor = null; EnsureInlineEditor(); return Task.CompletedTask; });
        DeleteSelectedCommand = Command(_ => { DeleteRows(Rows.Where(r => r.Selected).ToArray()); return Task.CompletedTask; });
        UndoCommand = Command(_ => { UndoDelete(); return Task.CompletedTask; });
        EnableSelectedCommand = Command(_ => { ToggleSelected(true); return Task.CompletedTask; }); DisableSelectedCommand = Command(_ => { ToggleSelected(false); return Task.CompletedTask; });
        ToggleOneCommand = Command(p => { var row = Row(p); Toggle(row, !row.Enabled); return Task.CompletedTask; });
        SaveValidationCommand = Command(_ => { SaveValidation(); return Task.CompletedTask; });
        BackCommand = Command(_ => { if (ConfirmDiscard()) { Editor = null; Stage = 0; EnsureInlineEditor(); } return Task.CompletedTask; });
        RunOneCommand = Command(async p => await run(Row(p).Id)); RunSuiteCommand = Command(async _ => await run(null));
        SaveSuiteCommand = Command(async _ => { if (!ConfirmDiscard()) return; if (Stage == 2) { Editor = null; Stage = 0; } await saveSuite(); });
        JsonCommand = Command(_ => { if (!ConfirmDiscard()) return Task.CompletedTask; JsonText = readSuite(); Stage = 3; return Task.CompletedTask; });
        ApplyJsonCommand = Command(_ => { Commit(DefinitionLoader.Parse(JsonText)); Stage = 0; Feedback = "Suite JSON applied. Save the suite to persist it."; return Task.CompletedTask; });
        LoadMetadataCommand = Command(async _ => await LoadMetadataAsync());
        PreviewScopeCommand = Command(async _ => await PreviewScopeAsync());
        PreviewValidationCommand = Command(async _ => await PreviewAsync());
        ShowSqlCommand = Command(_ => { var test = BuildValidated(); GeneratedSql = test.Template == null ? test.Query ?? "Mapped comparisons use the shared comparison engine; source and target can use different connections." : ValidationTemplateCompiler.Compile(test.Template).Query; return Task.CompletedTask; });
        SelectAllColumnsCommand = Command(_ => { if (Editor != null) Editor.ColumnText = string.Join(Environment.NewLine, SourceColumns.Select(c => c.Name)); SyncColumnChoices(); return Task.CompletedTask; });
        ClearColumnsCommand = Command(_ => { if (Editor != null) Editor.ColumnText = ""; SyncColumnChoices(); return Task.CompletedTask; });
        CancelCommand = new DesignerCommand(_ => { cancellation?.Cancel(); Feedback = "Cancelling preview…"; return Task.CompletedTask; });
        Parameters.CollectionChanged += (_, args) => { if (args.NewItems != null) foreach (SuiteParameterEditor row in args.NewItems) row.PropertyChanged += (_, _) => { if (!loadingParameters) Editor?.MarkDirty(); }; if (!loadingParameters) Editor?.MarkDirty(); };
    }

    private DesignerCommand Command(Func<object?, Task> action) => new(async parameter =>
    {
        if (!CanInteract) return;
        try { FeedbackError = false; await action(parameter); }
        catch (OperationCanceledException) { Feedback = "Preview cancelled. The validation draft is retained."; }
        catch (Exception e) { FeedbackError = true; Feedback = e is InvalidDataException or System.Text.Json.JsonException ? e.Message : $"Unable to complete the action ({e.GetType().Name}). Check inputs and database access."; status(Feedback); }
    });
    public ObservableCollection<SuiteValidationRow> Rows { get; } = [];
    public ICollectionView RowsView { get; }
    public ObservableCollection<ValidationTemplateChoice> Templates { get; } = new(ValidationTemplateChoice.Catalog);
    public ICollectionView TemplatesView { get; }
    public ObservableCollection<string> ConnectionNames { get; } = [];
    public ObservableCollection<DatabaseObject> SourceObjects { get; } = [];
    public ObservableCollection<DatabaseObject> TargetObjects { get; } = [];
    public ObservableCollection<DatabaseColumn> SourceColumns { get; } = [];
    public ObservableCollection<DatabaseColumn> TargetColumns { get; } = [];
    public ObservableCollection<ValidationColumnOption> ColumnOptions { get; } = [];
    public ObservableCollection<SuiteParameterEditor> Parameters { get; } = [];
    public ObservableCollection<DesignerPreviewView> Previews { get; } = [];
    public ObservableCollection<Evidence> PreviewEvidence { get; } = [];
    public string[] Categories { get; } = ["All categories", .. ValidationTemplateChoice.Catalog.Select(t => t.Category).Distinct()];
    public string[] TypeNames { get; } = ["All types", .. ValidationTemplateChoice.Catalog.Where(t => t.Available).Select(t => t.Name)];
    public string[] EnabledFilters { get; } = ["All validations", "Enabled", "Disabled"];
    public static string[] ParameterTypes { get; } = ["string", "int64", "decimal", "boolean", "datetime", "datetimeoffset", "guid"];
    public bool IsWorking { get => working; private set { Set(ref working, value); Notify(nameof(CanInteract)); Notify(nameof(CanAddBatch)); } }
    public bool CanInteract => !IsWorking && !hostBusy();
    public int Stage { get => stage; set => Set(ref stage, value); }
    public string SuiteName => current.Name;
    public string SuiteContext => $"{current.TestCases.FirstOrDefault(c => c.Id == ActiveTestCaseId)?.Name ?? current.Name} · {Rows.Count(r => ActiveTestCaseId == null || r.Definition.TestCaseId == ActiveTestCaseId)} validations · {current.Defaults.Connection}";
    public string SelectionSummary => $"{Rows.Count(r => r.Selected)} selected";
    public string EmptyMessage => Rows.Count == 0 ? "No validations yet. Choose a template to build your first check." : "Search by name, table or validation type.";
    public string Search { get => search; set { Set(ref search, value); RowsView.Refresh(); } }
    public string TypeFilter { get => typeFilter; set { Set(ref typeFilter, value); RowsView.Refresh(); } }
    public string EnabledFilter { get => enabledFilter; set { Set(ref enabledFilter, value); RowsView.Refresh(); } }
    public string TemplateSearch { get => templateSearch; set { Set(ref templateSearch, value); TemplatesView.Refresh(); } }
    public string Category { get => category; set { Set(ref category, value); TemplatesView.Refresh(); } }
    public string Feedback { get => feedback; private set => Set(ref feedback, value); }
    public bool FeedbackError { get => feedbackError; private set => Set(ref feedbackError, value); }
    public string JsonText { get => jsonText; set => Set(ref jsonText, value); }
    public string GeneratedSql { get => generatedSql; private set { Set(ref generatedSql, value); Notify(nameof(HasGeneratedSql)); } }
    public bool HasGeneratedSql => GeneratedSql.Length > 0;
    public bool HasPreview => hasPreview;
    public string PreviewMessage { get => previewMessage; private set { Set(ref previewMessage, value); hasPreview = true; Notify(nameof(HasPreview)); } }
    public SuiteValidationRow? SelectedRow
    {
        get => selectedRow;
        set
        {
            if (value != null && Stage == 0 && Editor != null && Editor.Id != value.Id)
            {
                if (!ConfirmDiscard()) { Notify(); return; }
                Set(ref selectedRow, value); OpenEditor(value.Definition, false); Stage = 0;
            }
            else Set(ref selectedRow, value);
            Notify(nameof(InlineJson));
        }
    }
    public ValidationTemplateChoice? SelectedTemplate { get => selectedTemplate; set { Set(ref selectedTemplate, value); Notify(nameof(CanChooseTemplate)); } }
    public bool CanChooseTemplate => SelectedTemplate?.Available == true && CanInteract;
    public ValidationEditor? Editor { get => editor; private set { Set(ref editor, value); SelectedMapping = null; Notify(nameof(HasEditor)); NotifyScopeProfiles(); } }
    public bool HasEditor => Editor != null;
    public DesignerPreviewView? SelectedPreview { get => selectedPreview; set => Set(ref selectedPreview, value); }
    public ICommand AddCommand { get; } public ICommand ChooseTemplateCommand { get; } public ICommand EditCommand { get; } public ICommand DuplicateCommand { get; } public ICommand DeleteCommand { get; } public ICommand DeleteSelectedCommand { get; } public ICommand UndoCommand { get; }
    public ICommand EnableSelectedCommand { get; } public ICommand DisableSelectedCommand { get; } public ICommand ToggleOneCommand { get; } public ICommand SaveValidationCommand { get; } public ICommand BackCommand { get; } public ICommand RunOneCommand { get; } public ICommand RunSuiteCommand { get; } public ICommand SaveSuiteCommand { get; }
    public ICommand JsonCommand { get; } public ICommand ApplyJsonCommand { get; } public ICommand LoadMetadataCommand { get; } public ICommand PreviewScopeCommand { get; } public ICommand PreviewValidationCommand { get; } public ICommand ShowSqlCommand { get; } public ICommand SelectAllColumnsCommand { get; } public ICommand ClearColumnsCommand { get; } public ICommand CancelCommand { get; }

    public void LoadSuite(Suite suite)
    {
        var selectedId = selectedRow?.Id ?? editor?.Id;
        if (current.Id != suite.Id) deleted.Clear();
        var previous = Rows.ToDictionary(r => r.Id);
        current = suite; Rows.Clear();
        foreach (var test in suite.Tests)
        {
            var row = new SuiteValidationRow(test, Toggle);
            if (previous.TryGetValue(test.Id, out var old)) { row.Selected = old.Selected; if (StudioJson.Write(old.Definition) == StudioJson.Write(test)) row.LastRun = old.LastRun; }
            row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SuiteValidationRow.Selected)) Notify(nameof(SelectionSummary)); };
            Rows.Add(row);
        }
        selectedRow = Rows.FirstOrDefault(r => r.Id == selectedId);
        Notify(nameof(SelectedRow)); Notify(nameof(InlineJson));
        Notify(nameof(SuiteName)); Notify(nameof(SuiteContext)); Notify(nameof(EmptyMessage)); Notify(nameof(SelectionSummary));
        RefreshConnectionNames();
    }
    public void SyncFromHost()
    {
        if (Stage != 0 || string.IsNullOrWhiteSpace(readSuite())) return;
        try { LoadSuite(DefinitionLoader.Parse(readSuite())); } catch (InvalidDataException e) { Feedback = e.Message; FeedbackError = true; }
    }
    public void RefreshHostState() { Notify(nameof(CanInteract)); Notify(nameof(CanChooseTemplate)); Notify(nameof(CanAddBatch)); }
    public void RecordRun(RunResult result)
    {
        foreach (var test in result.Tests.Where(t => t.Status != "Skipped")) { var row = Rows.FirstOrDefault(r => r.Id == test.Id); if (row != null) row.LastRun = test.Status; }
    }
    private SuiteValidationRow Row(object? parameter) => parameter as SuiteValidationRow ?? SelectedRow ?? throw new InvalidDataException("Select a validation first.");
    private static bool Matches(string text, params string[] fields) => string.IsNullOrWhiteSpace(text) || fields.Any(f => f.Contains(text, StringComparison.OrdinalIgnoreCase));
    public bool ConfirmDiscard()
    {
        if (Stage == 3 && JsonText != readSuite())
        {
            if (MessageBox.Show("Discard the unapplied suite JSON changes?", "Unsaved suite JSON", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
            JsonText = readSuite();
        }
        if (Stage == 1 && BatchItems.Any(i => i.Selected))
        {
            if (MessageBox.Show("Discard the selected template drafts?", "Unsaved templates", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
            BatchItems.Clear(); Editor = null; return true;
        }
        if (Editor?.IsDirty != true) return true;
        if (MessageBox.Show("Discard the unsaved validation changes? Saved suite validations are retained.", "Unsaved validation", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
        Editor = null; return true;
    }
    private Task BeginTemplateAsync(ValidationTemplateChoice? template)
    {
        if (template == null || !template.Available) throw new InvalidDataException("Choose an available validation template.");
        if (!ConfirmDiscard()) return Task.CompletedTask;
        var scope = SelectedRow?.Definition.Target ?? SelectedRow?.Definition.Template?.Source ?? current.Tests.FirstOrDefault()?.Target ?? new TableScope();
        var definition = new TestDefinition { Id = "validation-" + Guid.NewGuid().ToString("N")[..10], Name = template.Name, Type = template.Kind == "dataComparison" ? "dataComparison" : "sqlAssertion", Source = template.Kind == "dataComparison" ? scope with { Schema = "staging" } : null, Target = template.Kind == "dataComparison" ? scope : null, Template = template.Kind is "dataComparison" or "sqlAssertion" ? null : new() { Kind = template.Kind, Source = scope with { Connection = null } }, Query = template.Kind == "sqlAssertion" ? "SELECT 1 AS [Violation] WHERE 1 = 0;" : null, Expect = template.Kind == "dataComparison" ? null : new() { RowCount = 0 } };
        OpenEditor(definition with { TestCaseId = ActiveTestCaseId }, true, template.Kind); return Task.CompletedTask;
    }
    private void OpenEditor(TestDefinition test, bool adding, string? kind = null)
    {
        newValidation = adding; Editor = new(test, current.Defaults.Connection, kind); SourceObjects.Clear(); TargetObjects.Clear(); SourceColumns.Clear(); TargetColumns.Clear(); ColumnOptions.Clear(); Previews.Clear(); PreviewEvidence.Clear(); GeneratedSql = "";
        hasPreview = false; Notify(nameof(HasPreview));
        SetParameters(current.Parameters);
        Editor.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName is nameof(ValidationEditor.Connection) or nameof(ValidationEditor.SourceConnection) or nameof(ValidationEditor.TargetConnection)) NotifyScopeProfiles();
            if (e.PropertyName == nameof(ValidationEditor.ColumnText)) SyncColumnChoices();
            if (readingMetadata || IsWorking || e.PropertyName is not (nameof(ValidationEditor.SourceObject) or nameof(ValidationEditor.TargetObject))) return;
            try { await ReadColumnsAsync(e.PropertyName == nameof(ValidationEditor.TargetObject)); }
            catch (Exception exception) { Feedback = exception is InvalidDataException ? exception.Message : "Column metadata could not be loaded. Check the selected profile and permissions."; FeedbackError = true; }
        };
        RefreshConnectionNames(); FeedbackError = false; Feedback = adding ? "New validation draft. Configure the rule, then save it to the suite." : "Edit the configuration for this validation. Other validation types stay out of the way.";
        Stage = 2;
    }
    private void RefreshConnectionNames()
    {
        var names = new[] { "" }.Concat(readEnvironment().Connections.Keys.Order()).ToArray();
        foreach (var name in ConnectionNames.Where(n => !names.Contains(n)).ToArray()) ConnectionNames.Remove(name);
        foreach (var name in names) if (!ConnectionNames.Contains(name)) ConnectionNames.Add(name);
    }
    private Dictionary<string, ParameterDefinition> BuildParameters()
    {
        var result = new Dictionary<string, ParameterDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Parameters)
        {
            if (!result.TryAdd(row.Name.Trim(), new() { Type = row.Type, Required = row.Required, Default = string.IsNullOrEmpty(row.DefaultValue) ? null : row.DefaultValue })) throw new InvalidDataException("Parameter names must be unique: " + row.Name);
        }
        return result;
    }
    private void SetParameters(Dictionary<string, ParameterDefinition> declarations)
    {
        loadingParameters = true;
        try { Parameters.Clear(); foreach (var (name, p) in declarations) Parameters.Add(new() { Name = name, Type = p.Type, Required = p.Required, DefaultValue = p.Default ?? "" }); }
        finally { loadingParameters = false; }
    }
    private TestDefinition BuildValidated()
    {
        var draft = Editor?.Build() ?? throw new InvalidDataException("Open a validation to configure it.");
        var sample = DefinitionLoader.Parse(readSuite()) with { Tests = [draft], Parameters = BuildParameters() };
        return DefinitionLoader.Parse(StudioJson.Write(sample)).Tests.Single();
    }
    private void SaveValidation()
    {
        var draft = BuildValidated(); var suite = DefinitionLoader.Parse(readSuite()) with { Parameters = BuildParameters() };
        var index = suite.Tests.FindIndex(t => t.Id == draft.Id);
        if (newValidation) { if (index >= 0) throw new InvalidDataException("A validation already has this ID."); suite.Tests.Add(draft); }
        else { if (index < 0) throw new InvalidDataException("This validation was removed from the suite. Add it as a new copy instead."); suite.Tests[index] = draft; }
        Commit(suite); Editor = null; Stage = 0; EnsureInlineEditor(); Feedback = $"Applied {draft.Name}. Save the suite file to persist your changes.";
    }
    private void Commit(Suite suite) { var text = StudioJson.Write(suite); _ = DefinitionLoader.Parse(text); applySuite(text); LoadSuite(suite); }
    private void Toggle(SuiteValidationRow row, bool enabled)
    {
        try { if (!CanInteract) return; ApplyInlineDraft(); var suite = DefinitionLoader.Parse(readSuite()); var index = suite.Tests.FindIndex(t => t.Id == row.Id); suite.Tests[index] = suite.Tests[index] with { Enabled = enabled }; Commit(suite); if (Editor?.Id == row.Id) { Editor = null; EnsureInlineEditor(); } }
        catch (Exception e) { Feedback = e.Message; FeedbackError = true; LoadSuite(current); }
    }
    private void ToggleSelected(bool enabled)
    {
        var ids = Rows.Where(r => r.Selected).Select(r => r.Id).ToHashSet(); if (ids.Count == 0) throw new InvalidDataException("Select validations using the first column.");
        var suite = DefinitionLoader.Parse(readSuite()); for (var i = 0; i < suite.Tests.Count; i++) if (ids.Contains(suite.Tests[i].Id)) suite.Tests[i] = suite.Tests[i] with { Enabled = enabled }; Commit(suite);
        Feedback = $"{ids.Count} validations {(enabled ? "enabled" : "disabled")}.";
    }
    private void DeleteRows(SuiteValidationRow[] rows)
    {
        if (rows.Length == 0) throw new InvalidDataException("Select at least one validation to delete.");
        var ids = rows.Select(r => r.Id).ToHashSet(); var suite = DefinitionLoader.Parse(readSuite());
        var removed = suite.Tests.Select((t, i) => (i, t)).Where(x => ids.Contains(x.t.Id)).ToList(); suite.Tests.RemoveAll(t => ids.Contains(t.Id)); Commit(suite); deleted.Push(removed);
        Feedback = $"Deleted {removed.Count} validation(s). Use Undo delete to restore them before closing the app.";
    }
    private void UndoDelete()
    {
        if (deleted.Count == 0) { Feedback = "No validation deletion to undo."; return; }
        var removed = deleted.Peek(); var suite = DefinitionLoader.Parse(readSuite());
        if (removed.Any(r => suite.Tests.Any(t => t.Id == r.Test.Id))) throw new InvalidDataException("A deleted validation ID is already in this suite.");
        foreach (var (index, test) in removed.OrderBy(r => r.Index)) suite.Tests.Insert(Math.Min(index, suite.Tests.Count), test);
        Commit(suite); deleted.Pop(); Feedback = "Deleted validations restored.";
    }
    private async Task WithWorkAsync(Func<CancellationToken, Task> action)
    {
        IsWorking = true; cancellation = new();
        try { await action(cancellation.Token); }
        finally { IsWorking = false; cancellation.Dispose(); cancellation = null; }
    }
    private SqlServerConnector Connector() => new(readEnvironment(), new EnvironmentCredentialResolver());
    private string ScopeConnection(bool target) => string.IsNullOrWhiteSpace(target ? Editor?.TargetConnection : Editor?.SourceConnection) ? Editor!.Connection : target ? Editor!.TargetConnection : Editor!.SourceConnection;
    private async Task LoadMetadataAsync()
    {
        if (Editor == null) return;
        RefreshConnectionNames();
        await WithWorkAsync(async token =>
        {
            readingMetadata = true;
            try
            {
                SourceObjects.Clear(); foreach (var item in await Connector().CatalogAsync(ScopeConnection(false), token)) SourceObjects.Add(item);
                Editor.SourceObject = SourceObjects.FirstOrDefault(t => t.Schema == Editor.SourceSchema && t.Name == Editor.SourceTable);
                if (Editor.HasTarget)
                {
                    TargetObjects.Clear(); foreach (var item in await Connector().CatalogAsync(ScopeConnection(true), token)) TargetObjects.Add(item);
                    Editor.TargetObject = TargetObjects.FirstOrDefault(t => t.Schema == Editor.TargetSchema && t.Name == Editor.TargetTable);
                }
                await ReadColumnsInternalAsync(false, token); if (Editor.HasTarget) await ReadColumnsInternalAsync(true, token);
                Feedback = $"Loaded {SourceObjects.Count} source tables/views" + (Editor.HasTarget ? $" and {TargetObjects.Count} target tables/views." : ".");
            }
            finally { readingMetadata = false; }
        });
    }
    private Task ReadColumnsAsync(bool target) => WithWorkAsync(token => ReadColumnsInternalAsync(target, token));
    private async Task ReadColumnsInternalAsync(bool target, CancellationToken token)
    {
        var item = target ? Editor?.TargetObject : Editor?.SourceObject; var collection = target ? TargetColumns : SourceColumns; collection.Clear();
        if (item == null) return;
        foreach (var column in await Connector().ColumnsAsync(ScopeConnection(target), item, token)) collection.Add(column);
        if (!target)
        {
            ColumnOptions.Clear(); var selected = Editor!.ColumnText.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            foreach (var column in SourceColumns)
            {
                var option = new ValidationColumnOption(column.Name, column.SqlType, selected.Contains(column.Name));
                option.PropertyChanged += (_, _) => { if (readingMetadata) return; Editor!.ColumnText = string.Join(Environment.NewLine, ColumnOptions.Where(c => c.Selected).Select(c => c.Name)); };
                ColumnOptions.Add(option);
            }
        }
    }
    private void SyncColumnChoices()
    {
        if (Editor == null || readingMetadata) return;
        readingMetadata = true;
        try { var selected = Editor.ColumnText.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal); foreach (var option in ColumnOptions) option.Selected = selected.Contains(option.Name); }
        finally { readingMetadata = false; }
    }
    private async Task PreviewScopeAsync()
    {
        if (Editor == null || Editor.IsSql) throw new InvalidDataException("Scope preview is available for table-based validations. Use Preview validation for SQL.");
        await WithWorkAsync(async token =>
        {
            Previews.Clear(); PreviewEvidence.Clear();
            var scopes = new List<(string Label, string Connection, string Schema, string Table, string Filter)> { ("Source scope", ScopeConnection(false), Editor.SourceSchema, Editor.SourceTable, Editor.SourceFilter) };
            if (Editor.HasTarget) scopes.Add(("Target scope", ScopeConnection(true), Editor.TargetSchema, Editor.TargetTable, Editor.TargetFilter));
            foreach (var scope in scopes)
            {
                var query = $"SELECT TOP (50) * FROM {SqlServerConnector.Quote(scope.Schema)}.{SqlServerConnector.Quote(scope.Table)}" + (string.IsNullOrWhiteSpace(scope.Filter) ? "" : $" WHERE ({scope.Filter})");
                var data = await Connector().ExecuteSqlAsync(scope.Connection, query, 30, 50, bindParameters(BuildParameters()), token);
                if (data.Cancelled) throw new OperationCanceledException(token);
                if (data.Error != null) throw new InvalidDataException(data.Error);
                foreach (var result in data.Results) Previews.Add(new(scope.Label, ToView(result)));
            }
            SelectedPreview = Previews.FirstOrDefault(); PreviewMessage = "Scope preview: up to 50 rows per table. This is a display sample, not a validation result.";
        });
    }
    private async Task PreviewAsync()
    {
        var draft = BuildValidated();
        if (isDemo() && draft.Template != null) throw new InvalidDataException("Choose a SQL connection and turn off demo mode to preview a configured template. Synthetic demo SQL uses fixed fixtures.");
        Previews.Clear(); PreviewEvidence.Clear();
        await WithWorkAsync(async token =>
        {
            var result = await previewValidation(draft, BuildParameters(), token); var test = result.Tests.Single();
            foreach (var evidence in test.Evidence.Take(200)) PreviewEvidence.Add(evidence);
            PreviewMessage = $"{(result.Demo ? "DEMO · " : "")}{test.Status} · {test.Violations} violations · {test.Evidence.Count} retained examples · {test.DurationSeconds:F2}s" + (test.Error == null ? "" : " · " + test.Error);
            Feedback = "Validation preview completed. The draft has not been added to run history or saved to the suite.";
        });
    }
    private static DataView ToView(SqlResultSet result)
    {
        var table = new DataTable();
        for (var i = 0; i < result.Columns.Count; i++) { var name = string.IsNullOrWhiteSpace(result.Columns[i]) ? $"Column {i + 1}" : result.Columns[i]; var unique = name; var suffix = 2; while (table.Columns.Contains(unique)) unique = name + " (" + suffix++ + ")"; table.Columns.Add(unique, typeof(object)); }
        foreach (var row in result.Rows) table.Rows.Add(row.Select(value => value ?? DBNull.Value).ToArray());
        return table.DefaultView;
    }
    public void Cancel() => cancellation?.Cancel();
}
