using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using EtlTesting.Core;
using EtlTesting.Persistence;
using Microsoft.Win32;

namespace EtlTesting.Desktop;

public sealed record SuiteCard(SuiteLibraryEntry Entry)
{
    public string Name => Entry.Definition?.Name ?? Path.GetFileNameWithoutExtension(Entry.Path);
    public string Description => Entry.Error ?? (string.IsNullOrWhiteSpace(Entry.Definition?.Description) ? "Organize mappings and validation rules for your data pipeline." : Entry.Definition.Description);
    public string Counts => Entry.Definition is not { } s ? "Needs attention" : $"{(s.TestCases.Count > 0 ? s.TestCases.Count : s.Tests.Count)} tests  ·  {s.Tests.Count} validations";
    public string Connection => Entry.Definition?.Defaults.Connection ?? "Unavailable";
    public string Origin => Entry.Bundled ? "STARTER SUITE" : "TEAM SUITE";
    public string FilePath => Entry.Path;
    public string Accent => Entry.Error != null ? "#C05D28" : Entry.Bundled ? "#8059CD" : "#087E91";
}
public sealed record TestCaseRow(TestCaseDefinition Definition, int Validations, int Mappings)
{
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string Details => $"{Validations} validations · {Mappings} mappings";
    public string State => Definition.Enabled ? "● Enabled" : "○ Disabled";
}
public sealed partial class StudioViewModel
{
    private SuiteLibraryStore? suiteLibrary;
    private SuiteLibraryStore SuiteLibrary => suiteLibrary ??= new(root, Path.Combine(AppContext.BaseDirectory, "samples", "suites"));
    public ObservableCollection<SuiteCard> SuiteCards { get; } = [];
    public ObservableCollection<TestCaseRow> TestCases { get; } = [];
    private ICollectionView? suiteCardsView;
    public ICollectionView SuiteCardsView => suiteCardsView ??= CreateSuiteCardsView();
    private ICollectionView CreateSuiteCardsView() { var view = CollectionViewSource.GetDefaultView(SuiteCards); view.Filter = o => o is SuiteCard c && (string.IsNullOrWhiteSpace(SuiteSearch) || (c.Name + " " + c.Description + " " + c.Connection).Contains(SuiteSearch, StringComparison.OrdinalIgnoreCase)); return view; }
    private string suiteSearch = "", libraryMessage = "", testCaseName = "", testCaseDescription = "", savedSuiteText = "";
    private bool showingLibrary = true;
    private TestCaseRow? selectedTestCase;
    private bool refreshingTestCases;
    public bool ShowingLibrary { get => showingLibrary; set => Set(ref showingLibrary, value); }
    public string SuiteSearch { get => suiteSearch; set { Set(ref suiteSearch, value); SuiteCardsView.Refresh(); } }
    public string LibraryMessage { get => libraryMessage; set => Set(ref libraryMessage, value); }
    public string LibrarySummary => $"{SuiteCards.Count} available suites · Select a suite to explore its tests and validations";
    public string TestCaseName { get => testCaseName; set => Set(ref testCaseName, value); }
    public string TestCaseDescription { get => testCaseDescription; set => Set(ref testCaseDescription, value); }
    public string TestCaseSummary => $"{TestCases.Count} tests · {suite.Tests.Count} validations · {suite.Tests.Sum(t => t.Columns.Count)} column mappings";
    public TestCaseRow? SelectedTestCase
    {
        get => selectedTestCase;
        set
        {
            if (refreshingTestCases) return;
            if (selectedTestCase?.Id == value?.Id && selectedTestCase == value) return;
            if (designer != null && !Designer.ConfirmDiscard()) { Notify(); return; }
            Set(ref selectedTestCase, value); TestCaseName = value?.Name ?? ""; TestCaseDescription = value?.Definition.Description ?? "";
            Designer.EditorReset(); Designer.OpenTest(value?.Id);
        }
    }
    private void RefreshTestCases()
    {
        refreshingTestCases = true;
        try
        {
        var id = selectedTestCase?.Id;
        TestCases.Clear(); foreach (var c in suite.TestCases) TestCases.Add(new(c, suite.Tests.Count(t => t.TestCaseId == c.Id), suite.Tests.Where(t => t.TestCaseId == c.Id).Sum(t => t.Columns.Count)));
        selectedTestCase = TestCases.FirstOrDefault(c => c.Id == id) ?? TestCases.FirstOrDefault();
        TestCaseName = selectedTestCase?.Name ?? ""; TestCaseDescription = selectedTestCase?.Definition.Description ?? "";
        if (designer != null) designer.ActiveTestCaseId = selectedTestCase?.Id;
        }
        finally { refreshingTestCases = false; }
        Notify(nameof(SelectedTestCase)); Notify(nameof(TestCaseSummary));
    }
    public async Task RefreshLibraryAsync()
    {
        var entries = await SuiteLibrary.DiscoverAsync(); SuiteCards.Clear(); foreach (var e in entries) SuiteCards.Add(new(e));
        Notify(nameof(LibrarySummary)); LibraryMessage = "Local suites, imported files and starter suites are shown here. Import a folder to include your team's collection.";
        RebuildOverview();
    }
    private async Task<bool> LeaveSuiteAsync()
    {
        if (!Designer.CanInteract || !Designer.ConfirmDiscard()) return false;
        if (savedSuiteText == JsonText || string.IsNullOrEmpty(savedSuiteText)) return true;
        var choice = MessageBox.Show("Save your suite changes before leaving?", "Unsaved suite", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (choice == MessageBoxResult.Cancel) return false;
        if (choice == MessageBoxResult.Yes) { await SaveAsync(); return savedSuiteText == JsonText; }
        return true;
    }
    private DesignerCommand LibraryCommand(Func<object?, Task> action) => new(async p =>
    {
        if (!CanEdit || (designer?.IsWorking ?? false)) return;
        try { await action(p); } catch (Exception e) { LibraryMessage = e.Message; Status = e.Message; }
    });
    public ICommand RefreshLibraryCommand => LibraryCommand(async _ => await RefreshLibraryAsync());
    public ICommand BackToLibraryCommand => LibraryCommand(async _ => { if (!await LeaveSuiteAsync()) return; Designer.EditorReset(); ShowingLibrary = true; await RefreshLibraryAsync(); });
    public ICommand OpenLibrarySuiteCommand => LibraryCommand(async p =>
    {
        if (p is not SuiteCard card) return;
        await OpenLibrarySuiteAsync(card);
    });
    private async Task<bool> OpenLibrarySuiteAsync(SuiteCard card)
    {
        if (!await LeaveSuiteAsync()) return false;
        var text = await File.ReadAllTextAsync(card.FilePath); LoadSuite(text); SuitePath = card.FilePath; savedSuiteText = JsonText;
        Designer.EditorReset(); Designer.OpenTest(SelectedTestCase?.Id); ShowingLibrary = false; Status = "Suite opened · " + suite.Name;
        return true;
    }
    public ICommand ImportSuitesCommand => LibraryCommand(async _ =>
    {
        var dialog = new OpenFileDialog { Filter = "ETL suite JSON|*.json", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        await SuiteLibrary.RegisterAsync(dialog.FileNames); await RefreshLibraryAsync();
    });
    public ICommand ImportSuiteFolderCommand => LibraryCommand(async _ =>
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder containing suite JSON files" };
        if (dialog.ShowDialog() != true) return;
        await SuiteLibrary.RegisterAsync([], dialog.FolderName); await RefreshLibraryAsync();
    });
    public ICommand NewSuiteCommand => LibraryCommand(async _ =>
    {
        var details = new SuiteDetailsDialog("New test suite", "New test suite", "");
        if (details.ShowDialog() != true) return;
        if (!await LeaveSuiteAsync()) return;
        var created = new Suite { Id = "suite-" + Guid.NewGuid().ToString("N")[..12], Name = details.SuiteName, Description = details.Description, Defaults = suite.Defaults, TestCases = [new() { Id = "test-" + Guid.NewGuid().ToString("N")[..12], Name = "New test" }] };
        SuitePath = await SuiteLibrary.SaveNewAsync(created); LoadSuite(StudioJson.Write(created)); savedSuiteText = JsonText;
        Designer.EditorReset(); Designer.OpenTest(SelectedTestCase?.Id); ShowingLibrary = false; await RefreshLibraryAsync();
    });
    public ICommand RenameSuiteCommand => LibraryCommand(_ =>
    {
        var details = new SuiteDetailsDialog("Suite details", suite.Name, suite.Description);
        if (details.ShowDialog() == true) { Designer.ApplyInlineDraft(); LoadSuite(StudioJson.Write(suite with { Name = details.SuiteName, Description = details.Description })); }
        return Task.CompletedTask;
    });
    public ICommand DuplicateSuiteCommand => LibraryCommand(async _ =>
    {
        if (!Designer.ConfirmDiscard()) return;
        var copy = SuiteHierarchy.Duplicate(DefinitionLoader.Parse(JsonText), suite.Name + " copy");
        SuitePath = await SuiteLibrary.SaveNewAsync(copy); LoadSuite(StudioJson.Write(copy)); savedSuiteText = JsonText; Designer.EditorReset(); Designer.OpenTest(SelectedTestCase?.Id); await RefreshLibraryAsync();
    });
    public ICommand AddTestCaseCommand => LibraryCommand(_ =>
    {
        if (!Designer.ConfirmDiscard()) return Task.CompletedTask;
        var c = new TestCaseDefinition { Id = "test-" + Guid.NewGuid().ToString("N")[..12], Name = "New test " + (suite.TestCases.Count + 1) };
        LoadSuite(StudioJson.Write(suite with { TestCases = [.. suite.TestCases, c] })); SelectedTestCase = TestCases.First(t => t.Id == c.Id); return Task.CompletedTask;
    });
    public ICommand SaveTestCaseCommand => LibraryCommand(_ =>
    {
        var id = SelectedTestCase?.Id ?? throw new InvalidDataException("Select a test first.");
        if (string.IsNullOrWhiteSpace(TestCaseName)) throw new InvalidDataException("Enter a test name.");
        Designer.ApplyInlineDraft();
        LoadSuite(StudioJson.Write(suite with { TestCases = suite.TestCases.Select(c => c.Id == id ? c with { Name = TestCaseName.Trim(), Description = TestCaseDescription.Trim() } : c).ToList() }));
        Status = "Test details updated. Save the suite file to persist changes."; return Task.CompletedTask;
    });
    public ICommand DuplicateTestCaseCommand => LibraryCommand(_ =>
    {
        if (!Designer.ConfirmDiscard() || SelectedTestCase == null) return Task.CompletedTask;
        var original = SelectedTestCase.Definition; var id = "test-" + Guid.NewGuid().ToString("N")[..12];
        LoadSuite(StudioJson.Write(suite with { TestCases = [.. suite.TestCases, original with { Id = id, Name = original.Name + " copy" }], Tests = [.. suite.Tests, .. suite.Tests.Where(t => t.TestCaseId == original.Id).Select(t => t with { Id = "check-" + Guid.NewGuid().ToString("N")[..12], TestCaseId = id })] }));
        SelectedTestCase = TestCases.First(t => t.Id == id); return Task.CompletedTask;
    });
    public ICommand DeleteTestCaseCommand => LibraryCommand(_ =>
    {
        if (SelectedTestCase == null || !Designer.ConfirmDiscard()) return Task.CompletedTask;
        var id = SelectedTestCase.Id;
        if (MessageBox.Show("Delete this test and all its validations?", "Delete test", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return Task.CompletedTask;
        LoadSuite(StudioJson.Write(suite with { TestCases = suite.TestCases.Where(c => c.Id != id).ToList(), Tests = suite.Tests.Where(t => t.TestCaseId != id).ToList() })); Designer.EditorReset(); Designer.OpenTest(SelectedTestCase?.Id); return Task.CompletedTask;
    });
    public ICommand RunTestCaseCommand => LibraryCommand(async _ =>
    {
        var ids = suite.Tests.Where(t => t.TestCaseId == SelectedTestCase?.Id).Select(t => t.Id).ToArray();
        if (ids.Length == 0) throw new InvalidDataException("Add a validation to this test before running it.");
        await RunSuiteAsync(ids);
    });
}
