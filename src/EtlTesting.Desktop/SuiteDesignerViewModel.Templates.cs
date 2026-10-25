using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using EtlTesting.Core;
using EtlTesting.Persistence;
using Microsoft.Win32;

namespace EtlTesting.Desktop;

public sealed record TemplateCategoryChoice(string Name, int Count)
{
    public string Icon => Name switch { "All categories" => "suites", "Reconciliation" => "arrow", "Data quality" => "check", "Constraints" => "test", "Advanced SQL" => "sql", "Incremental" => "history", "Historical" => "history", _ => "database" };
    public string Accent => Name switch { "Reconciliation" => "#008F80", "Data quality" => "#1670DB", "Constraints" => "#7955CD", "Advanced SQL" => "#AA6819", "Incremental" => "#C47618", "Historical" => "#4D739C", "Schema" => "#697B94", _ => "#1670DB" };
    public string Tint => Name switch { "Reconciliation" => "#DBF3EC", "Data quality" => "#E4F0FF", "Constraints" => "#EDE6FF", "Advanced SQL" => "#FFF0DE", "Incremental" => "#FFF0DE", "Historical" => "#E8EFF8", "Schema" => "#EEF2F6", _ => "#E4F0FF" };
}

public sealed class BatchTemplateItem(ValidationTemplateChoice template, Action changed) : Observable
{
    private bool selected, ready;
    public ValidationTemplateChoice Template { get; } = template;
    public string Name => Template.Name;
    public string Description => Template.Description;
    public string Category => Template.Category;
    public bool Available => Template.Available;
    public string Accent => Category switch { "Constraints" => "#7955CD", "Reconciliation" => "#008F80", "Data quality" => "#1670DB", _ => "#C07924" };
    public string Tint => Category switch { "Constraints" => "#EBE3FF", "Reconciliation" => "#D9F2EB", "Data quality" => "#E0EDFF", _ => "#FFF0DE" };
    public bool Selected { get => selected; set { if (Set(ref selected, value)) { Notify(nameof(Status)); changed(); } } }
    public bool Ready { get => ready; set { Set(ref ready, value); Notify(nameof(Status)); changed(); } }
    public string Status => !Available ? "Coming later" : !Selected ? "Select to add" : Ready ? "✓ Configured" : "● Needs configuration";
    public ValidationEditor? DraftEditor { get; set; }
    public TestDefinition? Configured { get; set; }
    public Dictionary<string, ParameterDefinition> Declarations { get; set; } = [];
}

public sealed partial class SuiteDesignerViewModel
{
    public void EditorReset() { Editor = null; SelectedRow = null; BatchItems.Clear(); activeBatchItem = null; Stage = 0; }
    public void OpenTest(string? id)
    {
        ActiveTestCaseId = id; EnsureInlineEditor();
    }
    public void ApplyInlineDraft() { if (Stage == 0 && Editor?.IsDirty == true) SaveValidation(); }
    public string SourceServer => Profile(false)?.Server ?? "Choose a connection";
    public string SourceDatabase => Profile(false)?.Database ?? "—";
    public string TargetServer => Profile(true)?.Server ?? "Choose a connection";
    public string TargetDatabase => Profile(true)?.Database ?? "—";
    private ConnectionProfile? Profile(bool target)
    {
        if (Editor == null) return null;
        var key = ScopeConnection(target);
        return string.IsNullOrWhiteSpace(key) ? null : readEnvironment().Connections.GetValueOrDefault(key);
    }
    private void NotifyScopeProfiles() { Notify(nameof(SourceServer)); Notify(nameof(SourceDatabase)); Notify(nameof(TargetServer)); Notify(nameof(TargetDatabase)); }
    private void EnsureInlineEditor()
    {
        var row = Rows.FirstOrDefault(r => (ActiveTestCaseId == null || r.Definition.TestCaseId == ActiveTestCaseId) && r.Definition.Type == "dataComparison") ?? Rows.FirstOrDefault(r => ActiveTestCaseId == null || r.Definition.TestCaseId == ActiveTestCaseId);
        if (row == null) { Editor = null; Stage = 0; return; }
        SelectedRow = row; OpenEditor(row.Definition, false); Stage = 0; Notify(nameof(InlineJson));
    }
    public string InlineJson => SelectedRow == null ? "No validation selected." : StudioJson.Write(SelectedRow.Definition);
    public ICommand AddKeyCommand => Command(_ => { Editor?.Keys.Add(new()); return Task.CompletedTask; });
    public ICommand RemoveKeyCommand => Command(p => { if (p is ValidationMappingRow row) Editor?.Keys.Remove(row); return Task.CompletedTask; });
    public ICommand AddMappingCommand => Command(_ => { Editor?.Mappings.Add(new()); return Task.CompletedTask; });
    private ValidationMappingRow? selectedMapping;
    public ValidationMappingRow? SelectedMapping { get => selectedMapping; set { Set(ref selectedMapping, value); Notify(nameof(HasSelectedMapping)); } }
    public bool HasSelectedMapping => SelectedMapping != null;
    public ICommand RemoveMappingCommand => Command(p => { var row = p as ValidationMappingRow ?? SelectedMapping; if (row != null) Editor?.Mappings.Remove(row); SelectedMapping = null; return Task.CompletedTask; });
    public ICommand MoveMappingUpCommand => Command(_ => { MoveMapping(-1); return Task.CompletedTask; });
    public ICommand MoveMappingDownCommand => Command(_ => { MoveMapping(1); return Task.CompletedTask; });
    private void MoveMapping(int offset)
    {
        if (Editor == null || SelectedMapping == null) throw new System.IO.InvalidDataException("Select a column mapping first.");
        var index = Editor.Mappings.IndexOf(SelectedMapping); var next = index + offset;
        if (index >= 0 && next >= 0 && next < Editor.Mappings.Count) Editor.Mappings.Move(index, next);
    }
    private string? activeTestCaseId;
    public string? ActiveTestCaseId
    {
        get => activeTestCaseId;
        set { if (!Set(ref activeTestCaseId, value)) return; foreach (var row in Rows) row.Selected = false; SelectedRow = null; RowsView.Refresh(); Notify(nameof(SuiteContext)); }
    }
    public ObservableCollection<BatchTemplateItem> BatchItems { get; } = [];
    private ICollectionView? batchView;
    public ICollectionView BatchView => batchView ??= CreateBatchView();
    private ICollectionView CreateBatchView()
    {
        var view = CollectionViewSource.GetDefaultView(BatchItems);
        view.Filter = value => value is BatchTemplateItem i && Matches(BatchSearch, i.Name, i.Description, i.Category) && (BatchCategory == "All categories" || i.Category == BatchCategory) && (CatalogTab == 0 || i.Available && i.Template.Kind is "requiredValues" or "uniqueKeys" or "referentialIntegrity" or "dataComparison" or "rowCount" or "aggregate");
        return view;
    }
    private string batchSearch = "", batchCategory = "All categories";
    private int catalogTab;
    public int CatalogTab { get => catalogTab; set { Set(ref catalogTab, value); BatchView.Refresh(); } }
    public IReadOnlyList<TemplateCategoryChoice> BatchCategories { get; } = [new("All categories", ValidationTemplateChoice.Catalog.Count), .. ValidationTemplateChoice.Catalog.GroupBy(t => t.Category).Select(g => new TemplateCategoryChoice(g.Key, g.Count()))];
    public string BatchSearch { get => batchSearch; set { Set(ref batchSearch, value); BatchView.Refresh(); } }
    public string BatchCategory { get => batchCategory; set { Set(ref batchCategory, value); BatchView.Refresh(); } }
    private BatchTemplateItem? activeBatchItem;
    public BatchTemplateItem? ActiveBatchItem
    {
        get => activeBatchItem;
        set
        {
            if (value == activeBatchItem) return;
            if (!CanInteract) { Notify(nameof(ActiveBatchItem)); return; }
            if (activeBatchItem != null && Editor != null)
            {
                try { activeBatchItem.Declarations = BuildParameters(); activeBatchItem.DraftEditor = Editor; }
                catch (System.IO.InvalidDataException e) { Feedback = e.Message; FeedbackError = true; Notify(nameof(ActiveBatchItem)); return; }
            }
            Set(ref activeBatchItem, value);
            if (value == null || !value.Available) { Editor = null; return; }
            if (value.DraftEditor == null)
            {
                // Reuse the focused editor and its metadata behavior; drafts stay separate until batch commit.
                var scope = current.Tests.FirstOrDefault(t => t.TestCaseId == ActiveTestCaseId)?.Target ?? current.Tests.FirstOrDefault()?.Target ?? new TableScope();
                var kind = value.Template.Kind;
                OpenEditor(new() { Id = "check-" + Guid.NewGuid().ToString("N")[..12], TestCaseId = ActiveTestCaseId, Name = value.Name, Type = kind == "dataComparison" ? kind : "sqlAssertion", Source = kind == "dataComparison" ? scope with { Schema = "staging" } : null, Target = kind == "dataComparison" ? scope : null, Template = kind is "dataComparison" or "sqlAssertion" ? null : new() { Kind = kind, Source = scope with { Connection = null } }, Query = kind == "sqlAssertion" ? "SELECT 1 AS [Violation] WHERE 1 = 0;" : null, Expect = kind == "dataComparison" ? null : new() { RowCount = 0 } }, true, kind);
                value.DraftEditor = Editor;
                value.Declarations = BuildParameters();
                Editor!.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ValidationEditor.IsDirty)) value.Ready = false; };
            }
            else
            {
                Editor = value.DraftEditor;
                SetParameters(value.Declarations);
                SourceObjects.Clear(); TargetObjects.Clear(); SourceColumns.Clear(); TargetColumns.Clear(); ColumnOptions.Clear();
            }
            Previews.Clear(); PreviewEvidence.Clear(); GeneratedSql = ""; hasPreview = false; Notify(nameof(HasPreview));
            Stage = 1;
        }
    }
    public string BatchSummary => $"{BatchItems.Count(i => i.Selected)} selected · {BatchItems.Count(i => i.Selected && !i.Ready)} need configuration";
    public string AddBatchLabel => $"Add {BatchItems.Count(i => i.Selected)} checks to test";
    public bool CanAddBatch => CanInteract && BatchItems.Any(i => i.Selected) && BatchItems.Where(i => i.Selected).All(i => i.Ready);
    private void BatchChanged() { Notify(nameof(BatchSummary)); Notify(nameof(AddBatchLabel)); Notify(nameof(CanAddBatch)); }
    private void BeginBatch()
    {
        activeBatchItem = null; Editor = null; BatchItems.Clear();
        foreach (var template in ValidationTemplateChoice.Catalog) BatchItems.Add(new(template, BatchChanged));
        BatchSearch = ""; BatchCategory = "All categories"; Stage = 1; BatchChanged();
        ActiveBatchItem = BatchItems.First(i => i.Available);
        Feedback = "Select checks, configure each one, then add them together. Preview runs the current draft against the selected connection.";
    }
    private void ConfigureBatchItem()
    {
        var item = ActiveBatchItem ?? throw new System.IO.InvalidDataException("Select a template to configure.");
        item.Configured = BuildValidated(); item.Declarations = BuildParameters(); item.Selected = true; item.Ready = true;
        Feedback = $"{item.Name} configured. Select another check or add the selected checks to this test.";
    }
    private Suite BatchSuite()
    {
        if (!CanAddBatch) throw new System.IO.InvalidDataException("Configure every selected check before adding or saving a preset.");
        var suite = DefinitionLoader.Parse(readSuite());
        var parameters = new Dictionary<string, ParameterDefinition>(suite.Parameters);
        var selectedDeclarations = new Dictionary<string, ParameterDefinition>();
        foreach (var item in BatchItems.Where(i => i.Selected)) foreach (var (name, declaration) in item.Declarations)
        {
            if (selectedDeclarations.TryGetValue(name, out var previous) && previous != declaration) throw new System.IO.InvalidDataException($"Parameter {name} differs between checks. Use one declaration across the selected checks.");
            selectedDeclarations[name] = declaration;
            parameters[name] = declaration;
        }
        return suite with { Tests = BatchItems.Where(i => i.Selected).Select(i => i.Configured!).ToList(), Parameters = parameters };
    }
    public ICommand ConfigureBatchCommand => Command(_ => { ConfigureBatchItem(); return Task.CompletedTask; });
    public ICommand AddBatchCommand => Command(_ =>
    {
        var batch = BatchSuite(); var suite = DefinitionLoader.Parse(readSuite());
        Commit(suite with { Tests = [.. suite.Tests, .. batch.Tests], Parameters = batch.Parameters });
        BatchItems.Clear(); Editor = null; activeBatchItem = null; Stage = 0; EnsureInlineEditor(); Feedback = $"Added {batch.Tests.Count} validations. Save the suite to persist your changes.";
        return Task.CompletedTask;
    });
    public ICommand SaveBatchPresetCommand => Command(async _ =>
    {
        var preset = BatchSuite();
        var dialog = new SaveFileDialog { Filter = "Validation preset JSON|*.json", FileName = "validation-preset.json" };
        if (dialog.ShowDialog() != true) return;
        await AtomicFile.WriteAsync(dialog.FileName, StudioJson.Write(preset with { Name = "Validation preset", Id = "preset-" + Guid.NewGuid().ToString("N")[..10] }));
        Feedback = "Preset saved. Use Load preset to add editable copies to another test.";
    });
    public ICommand LoadBatchPresetCommand => Command(async _ =>
    {
        var dialog = new OpenFileDialog { Filter = "Validation preset JSON|*.json" };
        if (dialog.ShowDialog() != true) return;
        var preset = DefinitionLoader.Parse(await System.IO.File.ReadAllTextAsync(dialog.FileName));
        var suite = DefinitionLoader.Parse(readSuite()); var parameters = new Dictionary<string, ParameterDefinition>(suite.Parameters);
        foreach (var (name, p) in preset.Parameters) { if (parameters.TryGetValue(name, out var old) && old != p) throw new System.IO.InvalidDataException("Conflicting preset parameter: " + name); parameters[name] = p; }
        Commit(suite with { Parameters = parameters, Tests = [.. suite.Tests, .. preset.Tests.Select(t => t with { Id = "check-" + Guid.NewGuid().ToString("N")[..12], TestCaseId = ActiveTestCaseId })] });
        Feedback = $"Loaded {preset.Tests.Count} editable validations. Verify scope and connections before running.";
    });
}
