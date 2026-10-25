using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using EtlTesting.Core;
using EtlTesting.Persistence;
using EtlTesting.SqlServer;
using Microsoft.Win32;

namespace EtlTesting.Desktop;

public sealed record SqlResultView(string Label, DataView View);
public sealed record SchemaSummary(string Name, int TableCount)
{
    public string TableLabel => TableCount == 1 ? "1 table" : $"{TableCount} tables";
}

public sealed class ConnectionManagerViewModel : Observable
{
    private readonly ConnectionProfileStore store;
    private readonly Func<EnvironmentDefinition, string?, Task> activate;
    private readonly Action<string> status;
    private ConnectionLibrary library = new();
    private ConnectionProfileEditor? selectedProfile;
    private ConnectionProfileEditor? workspaceProfile;
    private DatabaseObject? selectedObject;
    private SqlResultView? selectedSqlResult;
    private CancellationTokenSource? cancellation;
    private string profileSearch = "", objectSearch = "", feedback = "Select a profile to manage its connection.", queryText = "SELECT name, create_date FROM sys.tables ORDER BY name;", queryMessage = "Run SQL to view results here.", queryTimeout = "120", resultLimit = "200", browserMessage = "Load tables to explore this database.";
    private bool working, hostBusy, feedbackError, commandRunning;
    private int workspaceTab, page;
    private readonly List<long> pageOffsets = [0];
    private DataView? preview;

    public ConnectionManagerViewModel(string path, Func<EnvironmentDefinition, string?, Task> activate, Action<string> status)
    {
        store = new(path); this.activate = activate; this.status = status;
        ProfilesView = CollectionViewSource.GetDefaultView(Profiles); ProfilesView.Filter = item => item is ConnectionProfileEditor p && (p == SelectedProfile && p.IsDirty || Matches(ProfileSearch, p.Name, p.EnvironmentName, p.Server, p.Database, p.ConnectionKey));
        ObjectsView = CollectionViewSource.GetDefaultView(Objects); ObjectsView.Filter = item => item is DatabaseObject o && Matches(ObjectSearch, o.Schema, o.Name, o.Kind);
        NewCommand = Command(NewProfileAsync); DuplicateCommand = Command(DuplicateAsync); SaveCommand = Command(SaveAsync); RevertCommand = Command(() => { SelectedProfile?.Revert(); Feedback = "Saved settings restored."; return Task.CompletedTask; });
        DeleteCommand = Command(DeleteAsync); UseCommand = Command(UseAsync); ImportCommand = Command(ImportAsync); ExportCommand = Command(ExportAsync);
        TestCommand = Command(TestAsync); DiscoverCommand = Command(DiscoverAsync); LoadObjectsCommand = Command(LoadObjectsAsync);
        NextPageCommand = Command(async () => { if (HasMoreRows && Preview?.Count > 0) { var offset = pageOffsets[page] + Preview.Count; if (pageOffsets.Count > page + 1) pageOffsets[page + 1] = offset; else pageOffsets.Add(offset); page++; await PreviewAsync(); } }); PreviousPageCommand = Command(async () => { if (page > 0) { page--; await PreviewAsync(); } });
        QueryObjectCommand = Command(() => { if (SelectedObject is null) throw new InvalidDataException("Select a table or view first."); QueryText = $"SELECT TOP (200) *\nFROM {SelectedObject.QualifiedName};"; WorkspaceTab = 0; return Task.CompletedTask; });
        ExecuteSqlCommand = Command(ExecuteSqlAsync); OpenSqlCommand = Command(OpenSqlAsync); SaveSqlCommand = Command(SaveSqlAsync);
        CancelCommand = new ActionCommand(() => { cancellation?.Cancel(); Feedback = "Cancelling…"; return Task.CompletedTask; });
    }

    private ActionCommand Command(Func<Task> action) => new(async () =>
    {
        if (!CanInteract) return;
        commandRunning = true; Notify(nameof(IsWorking)); Notify(nameof(CanInteract));
        try { FeedbackError = false; await action(); }
        catch (Exception e) { FeedbackError = true; Feedback = e switch
        {
            InvalidDataException or System.Text.Json.JsonException => e.Message,
            FileNotFoundException missing => $"Required file was not found: {missing.FileName ?? missing.Message}",
            _ => $"Unable to complete the action ({e.GetType().Name}): {e.Message}"
        }; status(Feedback); }
        finally { commandRunning = false; Notify(nameof(IsWorking)); Notify(nameof(CanInteract)); }
    });

    public ObservableCollection<ConnectionProfileEditor> Profiles { get; } = [];
    public ObservableCollection<ConnectionProfileEditor> SavedProfiles { get; } = [];
    public ICollectionView ProfilesView { get; }
    public ObservableCollection<string> Databases { get; } = [];
    public ObservableCollection<string> VisibleTables { get; } = [];
    public ObservableCollection<SchemaSummary> VisibleSchemas { get; } = [];
    public ObservableCollection<DatabaseObject> Objects { get; } = [];
    public ICollectionView ObjectsView { get; }
    public ObservableCollection<DatabaseColumn> Columns { get; } = [];
    public ObservableCollection<SqlResultView> SqlResults { get; } = [];
    public string[] EnvironmentSuggestions { get; } = ["Development", "QA", "UAT", "Production"];
    public string[] RowLimits { get; } = ["100", "200", "500", "1000"];
    public bool HostBusy { get => hostBusy; set { Set(ref hostBusy, value); Notify(nameof(CanInteract)); } }
    public bool IsWorking { get => working || commandRunning; private set { Set(ref working, value); Notify(nameof(CanInteract)); } }
    public bool CanInteract => !IsWorking && !HostBusy;
    public bool HasSelection => SelectedProfile != null;
    public bool HasWorkspaceProfile => WorkspaceProfile != null;
    public string WorkspaceDatabase => WorkspaceProfile?.Saved?.Connection.Database ?? "Select a saved profile";
    public string WorkspaceServer => WorkspaceProfile?.Saved?.Connection.Server ?? "";
    public string WorkspaceEnvironment => WorkspaceProfile?.Saved?.EnvironmentName ?? "";
    public string ProfileSearch { get => profileSearch; set { Set(ref profileSearch, value); ProfilesView.Refresh(); } }
    public string ObjectSearch { get => objectSearch; set { Set(ref objectSearch, value); ObjectsView.Refresh(); } }
    public string Feedback { get => feedback; private set => Set(ref feedback, value); }
    public bool FeedbackError { get => feedbackError; private set => Set(ref feedbackError, value); }
    public int WorkspaceTab { get => workspaceTab; set => Set(ref workspaceTab, value); }
    public string QueryText { get => queryText; set => Set(ref queryText, value); }
    public string QueryTimeout { get => queryTimeout; set => Set(ref queryTimeout, value); }
    public string ResultLimit { get => resultLimit; set => Set(ref resultLimit, value); }
    public string QueryMessage { get => queryMessage; private set => Set(ref queryMessage, value); }
    public string BrowserMessage { get => browserMessage; private set => Set(ref browserMessage, value); }
    public DataView? Preview { get => preview; private set => Set(ref preview, value); }
    public bool HasMoreRows { get; private set; }
    public string PageLabel => $"Page {page + 1} · {Preview?.Count ?? 0} rows shown · up to 200";
    public int ProfileCount => Profiles.Count;
    public int ServerCount => Profiles.Select(p => p.EndpointSummary).Where(s => s != "Server not configured").Distinct(StringComparer.OrdinalIgnoreCase).Count();
    public string ActiveLabel => library.Profiles.FirstOrDefault(p => p.Id == library.ActiveProfileId) is { } p ? $"In use: {p.Name} · {p.EnvironmentName}" : "No active profile";
    public string ActiveConnectionSummary => library.Profiles.FirstOrDefault(p => p.Id == library.ActiveProfileId) is { } p ? $"{p.Name} · {p.VerificationStatus}" : "SQL Server has not been configured";
    public bool SelectedIsActive => SelectedProfile?.Id == library.ActiveProfileId;
    public string UseLabel => SelectedProfile?.IsDirty == true ? "Save & use for suite" : "Use for suite";
    public SqlResultView? SelectedSqlResult { get => selectedSqlResult; set => Set(ref selectedSqlResult, value); }
    public ConnectionProfileEditor? SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (!Set(ref selectedProfile, value)) return;
            Databases.Clear(); VisibleTables.Clear(); VisibleSchemas.Clear(); Notify(nameof(HasSelection)); Notify(nameof(SelectedIsActive)); Notify(nameof(UseLabel));
            FeedbackError = false; Feedback = value?.IsDirty == true ? "Draft settings are kept while you switch profiles. Save when ready." : "Profile loaded. Use it for a suite, explore its database or run SQL.";
        }
    }
    public ConnectionProfileEditor? WorkspaceProfile
    {
        get => workspaceProfile;
        set
        {
            if (!Set(ref workspaceProfile, value)) return;
            ResetWorkspace(); NotifyWorkspaceContext();
        }
    }
    private void ResetWorkspace()
    {
        Objects.Clear(); Columns.Clear(); selectedObject = null; Notify(nameof(SelectedObject)); Preview = null;
        SqlResults.Clear(); SelectedSqlResult = null; QueryMessage = "Run SQL to view results for this saved profile.";
        page = 0; pageOffsets.Clear(); pageOffsets.Add(0); HasMoreRows = false; Notify(nameof(PageLabel));
        BrowserMessage = "Load tables to explore this database.";
    }
    private void NotifyWorkspaceContext() { foreach (var property in new[] { nameof(HasWorkspaceProfile), nameof(WorkspaceDatabase), nameof(WorkspaceServer), nameof(WorkspaceEnvironment) }) Notify(property); }
    public DatabaseObject? SelectedObject
    {
        get => selectedObject;
        set { if (Set(ref selectedObject, value) && value != null) _ = SelectObjectAsync(value); }
    }

    public ICommand NewCommand { get; } public ICommand DuplicateCommand { get; } public ICommand SaveCommand { get; } public ICommand RevertCommand { get; } public ICommand DeleteCommand { get; } public ICommand UseCommand { get; }
    public ICommand ImportCommand { get; } public ICommand ExportCommand { get; } public ICommand TestCommand { get; } public ICommand DiscoverCommand { get; } public ICommand CancelCommand { get; }
    public ICommand LoadObjectsCommand { get; } public ICommand NextPageCommand { get; } public ICommand PreviousPageCommand { get; } public ICommand QueryObjectCommand { get; }
    public ICommand ExecuteSqlCommand { get; } public ICommand OpenSqlCommand { get; } public ICommand SaveSqlCommand { get; }

    public async Task InitializeAsync(EnvironmentDefinition legacy, bool importLegacyChanges = false)
    {
        library = await store.LoadAsync(legacy, importLegacyChanges: importLegacyChanges);
        foreach (var profile in library.Profiles) AddEditor(new(profile));
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == library.ActiveProfileId) ?? Profiles.FirstOrDefault();
        WorkspaceProfile = SelectedProfile;
        if (library.ActiveProfileId is string active) await activate(library.EnvironmentFor(active), null);
        RefreshLibraryState();
    }
    private void AddEditor(ConnectionProfileEditor editor)
    {
        Profiles.Add(editor);
        if (editor.Saved != null) SavedProfiles.Add(editor);
        editor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ConnectionProfileEditor.Name) or nameof(ConnectionProfileEditor.Server) or nameof(ConnectionProfileEditor.Database) or nameof(ConnectionProfileEditor.EnvironmentName) or null)
            { Notify(nameof(ProfileCount)); Notify(nameof(ServerCount)); ProfilesView.Refresh(); }
            Notify(nameof(UseLabel));
            if (editor == WorkspaceProfile) NotifyWorkspaceContext();
        };
        RefreshLibraryState();
    }
    private void RefreshLibraryState() { foreach (var name in new[] { nameof(ProfileCount), nameof(ServerCount), nameof(ActiveLabel), nameof(ActiveConnectionSummary), nameof(SelectedIsActive), nameof(UseLabel) }) Notify(name); }
    private static bool Matches(string search, params string[] values) => string.IsNullOrWhiteSpace(search) || values.Any(v => v.Contains(search, StringComparison.OrdinalIgnoreCase));
    private ConnectionProfileEditor RequiredProfile() => SelectedProfile ?? throw new InvalidDataException("Create or select a connection profile first.");
    private string UniqueKey(string prefix, string environment)
    {
        var candidate = prefix; var index = 2;
        while (Profiles.Any(p => string.Equals(p.EnvironmentName, environment, StringComparison.OrdinalIgnoreCase) && string.Equals(p.ConnectionKey, candidate, StringComparison.OrdinalIgnoreCase))) candidate = prefix + "-" + index++;
        return candidate;
    }
    private Task NewProfileAsync()
    {
        var environment = SelectedProfile?.EnvironmentName ?? "Development";
        var editor = new ConnectionProfileEditor(new() { Name = "New connection", EnvironmentName = environment, ConnectionKey = UniqueKey("connection", environment) }, false);
        ProfileSearch = ""; AddEditor(editor); SelectedProfile = editor; Feedback = "New profile. Enter its server and database, then save."; return Task.CompletedTask;
    }
    private Task DuplicateAsync()
    {
        var source = RequiredProfile().ToDefinition();
        var copy = source with { Id = Guid.NewGuid().ToString("N"), Name = source.Name + " copy", ConnectionKey = UniqueKey(source.ConnectionKey + "-copy", source.EnvironmentName), Connection = source.Connection with { CredentialTarget = null } };
        // Vault secrets are deliberately not shared between editable copies.
        if (source.Connection.CredentialTarget != null) copy = copy with { Connection = copy.Connection with { CredentialTarget = WindowsCredentialStore.TargetPrefix + copy.Id } };
        var editor = new ConnectionProfileEditor(ConnectionProfileStore.ClearVerification(copy), false);
        ProfileSearch = ""; AddEditor(editor); SelectedProfile = editor; Feedback = "Editable copy created. SQL passwords must be entered again for a separate vault entry."; return Task.CompletedTask;
    }
    private async Task SaveAsync()
    {
        var editor = RequiredProfile(); var definition = editor.ToDefinition(); var connectionChanged = editor.Saved?.Connection != definition.Connection;
        var profiles = library.Profiles.Where(p => p.Id != definition.Id).Append(definition).ToList();
        var next = library with { Profiles = profiles };
        ConnectionProfileStore.Validate(next);
        if (editor.UseWindowsCredentials && !string.IsNullOrEmpty(editor.PendingPassword)) WindowsCredentialStore.Write(definition.Connection.CredentialTarget!, new(editor.UserName.Trim(), editor.PendingPassword));
        await store.SaveAsync(next); library = next; editor.MarkSaved(definition);
        if (!SavedProfiles.Contains(editor)) SavedProfiles.Add(editor);
        if (WorkspaceProfile == null) WorkspaceProfile = editor;
        if (connectionChanged && WorkspaceProfile == editor) ResetWorkspace();
        if (library.ActiveProfileId == definition.Id) await activate(library.EnvironmentFor(definition.Id), null);
        Feedback = $"Saved {definition.Name}."; RefreshLibraryState(); status(Feedback);
    }
    private async Task UseAsync()
    {
        var editor = RequiredProfile(); if (editor.IsDirty) await SaveAsync();
        var next = library with { ActiveProfileId = editor.Id };
        var environment = next.EnvironmentFor(editor.Id);
        await store.SaveAsync(next);
        try { await activate(environment, editor.ConnectionKey); }
        catch { await store.SaveAsync(library); throw; }
        library = next;
        Feedback = $"{editor.Name} is now selected for suite connection '{editor.ConnectionKey}'."; RefreshLibraryState(); status(Feedback);
    }
    private async Task DeleteAsync()
    {
        var editor = RequiredProfile();
        if (SelectedIsActive) throw new InvalidDataException("Use another profile before removing the active connection.");
        if (MessageBox.Show($"Remove '{editor.Name}' from saved connections? This does not delete its database.", "Remove connection", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var next = library with { Profiles = library.Profiles.Where(p => p.Id != editor.Id).ToList() };
        await store.SaveAsync(next); library = next; Profiles.Remove(editor); SavedProfiles.Remove(editor); if (WorkspaceProfile == editor) WorkspaceProfile = SavedProfiles.FirstOrDefault(); SelectedProfile = Profiles.FirstOrDefault(); RefreshLibraryState(); Feedback = "Connection removed. Its database was not changed.";
        // Keep referenced vault entries: an exported environment may still use them.
    }
    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog { Filter = "Connection library or environment|*.json" }; if (dialog.ShowDialog() != true) return;
        var imported = ConnectionProfileStore.ParseImport(await File.ReadAllTextAsync(dialog.FileName));
        var additions = new List<SavedConnectionProfile>();
        foreach (var profile in imported)
        {
            var key = profile.ConnectionKey; var index = 2;
            while (Profiles.Any(p => p.EnvironmentName.Equals(profile.EnvironmentName, StringComparison.OrdinalIgnoreCase) && p.ConnectionKey.Equals(key, StringComparison.OrdinalIgnoreCase)) || additions.Any(p => p.EnvironmentName.Equals(profile.EnvironmentName, StringComparison.OrdinalIgnoreCase) && p.ConnectionKey.Equals(key, StringComparison.OrdinalIgnoreCase))) key = profile.ConnectionKey + "-" + index++;
            additions.Add(profile with { Id = Guid.NewGuid().ToString("N"), ConnectionKey = key });
        }
        var next = library with { Profiles = library.Profiles.Concat(additions).ToList() }; await store.SaveAsync(next); library = next;
        foreach (var profile in additions) AddEditor(new(profile)); ProfileSearch = ""; SelectedProfile = Profiles.LastOrDefault();
        Feedback = $"Imported {additions.Count} profiles. Existing profiles were kept; duplicate logical keys received a numeric suffix.";
    }
    private async Task ExportAsync()
    {
        var dialog = new SaveFileDialog { Filter = "Connection library|*.json", FileName = "etl-connections.json" }; if (dialog.ShowDialog() != true) return;
        if (Path.GetFullPath(dialog.FileName).Equals(store.FilePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Choose a separate export file; the active profile library is managed by the application.");
        await AtomicFile.WriteAsync(dialog.FileName, StudioJson.Write(library)); Feedback = "Saved profiles exported. Passwords are excluded; SQL credentials remain local vault or environment references. Unsaved drafts are not exported.";
    }
    private async Task OperateAsync(Func<CancellationToken, Task> action)
    {
        IsWorking = true; cancellation = new();
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { Feedback = "Cancelled. No complete result is claimed."; }
        finally { cancellation.Dispose(); cancellation = null; IsWorking = false; }
    }
    private (SqlServerConnector Connector, string Key) Connector(bool databaseRequired = true)
    {
        var editor = RequiredProfile(); var connection = editor.BuildConnection(databaseRequired);
        return (new(new() { Name = editor.EnvironmentName, Connections = new() { [editor.ConnectionKey] = connection } }, editor.Credentials(connection)), editor.ConnectionKey);
    }
    private (SqlServerConnector Connector, string Key) WorkspaceConnector()
    {
        var saved = WorkspaceProfile?.Saved ?? throw new InvalidDataException("Choose a saved connection profile in SQL Workspace. Save new connections in the Connections tab first.");
        return (new(new() { Name = saved.EnvironmentName, Connections = new() { [saved.ConnectionKey] = saved.Connection } }, new EnvironmentCredentialResolver()), saved.ConnectionKey);
    }
    private async Task TestAsync()
    {
        var editor = RequiredProfile(); var (connector, key) = Connector(); VisibleTables.Clear(); VisibleSchemas.Clear();
        await OperateAsync(async token =>
        {
            var watch = Stopwatch.StartNew();
            try
            {
                var inspection = await connector.InspectAsync(key, token);
                editor.Verify("Verified", "Connected successfully. Database metadata is accessible with this identity.", inspection, watch.ElapsedMilliseconds);
                foreach (var table in inspection.Tables) VisibleTables.Add(table);
                foreach (var schema in inspection.Tables.GroupBy(table => table.Split('.', 2)[0], StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key))
                    VisibleSchemas.Add(new(schema.Key, schema.Count()));
                Feedback = $"Connected to {inspection.Database}. {inspection.Tables.Count} tables are visible.";
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                var message = e switch
                {
                    InvalidDataException => e.Message,
                    FileNotFoundException missing => $"Connection test could not load a required file: {missing.FileName ?? missing.Message}. Check the SQL Server client installation.",
                    _ => $"Connection test failed: {e.Message}"
                };
                editor.Verify("Failed", message, null, null); FeedbackError = true; Feedback = message;
            }
        });
        // Save observed verification only when it used the unchanged saved configuration.
        if (!editor.IsDirty)
        {
            var updated = editor.ToDefinition(); var next = library with { Profiles = library.Profiles.Select(p => p.Id == updated.Id ? updated : p).ToList() };
            await store.SaveAsync(next); library = next; editor.MarkSaved(updated); RefreshLibraryState();
        }
    }
    private async Task DiscoverAsync()
    {
        var (connector, key) = Connector(false); Databases.Clear();
        await OperateAsync(async token => { foreach (var database in await connector.ListDatabasesAsync(key, token)) Databases.Add(database); Feedback = $"Found {Databases.Count} accessible databases. Choose one in the database field."; });
    }
    private async Task LoadObjectsAsync()
    {
        var (connector, key) = WorkspaceConnector(); Objects.Clear(); Columns.Clear(); Preview = null; selectedObject = null; Notify(nameof(SelectedObject));
        await OperateAsync(async token => { foreach (var table in await connector.CatalogAsync(key, token)) Objects.Add(table); BrowserMessage = $"{Objects.Count} visible tables and views. Select an object to preview data and columns."; Feedback = Objects.Count == 0 ? "No user tables or views are visible with this identity." : "Database catalog loaded."; });
    }
    private async Task SelectObjectAsync(DatabaseObject table)
    {
        if (!CanInteract) return;
        try
        {
            FeedbackError = false; var (connector, key) = WorkspaceConnector(); page = 0; pageOffsets.Clear(); pageOffsets.Add(0); Columns.Clear(); Preview = null; WorkspaceTab = 1;
            await OperateAsync(async token =>
            {
                foreach (var column in await connector.ColumnsAsync(key, table, token)) Columns.Add(column);
                var result = await connector.PreviewAsync(key, table, Columns.ToArray(), 0, 200, token); ShowPreview(result);
            });
        }
        catch (Exception e) { FeedbackError = true; Feedback = e is InvalidDataException ? e.Message : $"Unable to load the selected object ({e.GetType().Name})."; BrowserMessage = Feedback; }
    }
    private async Task PreviewAsync()
    {
        var table = SelectedObject ?? throw new InvalidDataException("Select a table or view first."); var (connector, key) = WorkspaceConnector();
        await OperateAsync(async token => ShowPreview(await connector.PreviewAsync(key, table, Columns.ToArray(), pageOffsets[page], 200, token)));
    }
    private void ShowPreview(SqlExecutionResult result)
    {
        Preview = result.Results.FirstOrDefault() is { } data ? ToView(data) : null;
        HasMoreRows = result.Capped;
        BrowserMessage = result.Error ?? (result.Cancelled ? "Cancelled. Preview is incomplete." : $"{Columns.Count} columns · {(Columns.Any(c => c.PrimaryKey) ? "Ordered by primary key." : "No primary key; page order may change or overlap.")} {(result.Results.Any(r => r.TruncatedCells > 0) ? "Large cells are shortened for preview." : "")} {result.Message}");
        Notify(nameof(PageLabel)); Notify(nameof(HasMoreRows)); Feedback = result.Error ?? $"Loaded {Preview?.Count ?? 0} preview rows."; FeedbackError = result.Error != null;
    }
    private async Task ExecuteSqlAsync()
    {
        var (connector, key) = WorkspaceConnector();
        if (!int.TryParse(QueryTimeout, out var timeout) || !int.TryParse(ResultLimit, out var limit)) throw new InvalidDataException("Enter numeric query timeout and result limit values.");
        var sql = QueryText; SqlResults.Clear(); SelectedSqlResult = null; QueryMessage = "Executing…";
        await OperateAsync(async token =>
        {
            var result = await connector.ExecuteSqlAsync(key, sql, timeout, limit, [], token);
            for (var i = 0; i < result.Results.Count; i++) SqlResults.Add(new($"Result {i + 1} · {result.Results[i].Rows.Count:N0} rows", ToView(result.Results[i])));
            SelectedSqlResult = SqlResults.FirstOrDefault();
            QueryMessage = $"{(result.Cancelled ? "Cancelled · partial results" : result.Error != null ? "SQL error · partial results" : "Completed")} · {result.DurationSeconds:F3} s · {result.Results.Sum(r => r.Rows.Count):N0} rows displayed" + (result.RecordsAffected >= 0 ? $" · {result.RecordsAffected:N0} affected rows" : "") + "\n" + result.Message;
            if (result.Capped) QueryMessage += "\nDisplay limit reached. Cancellation was requested; statements in this batch may already have executed.";
            if (result.Results.Any(r => r.TruncatedCells > 0)) QueryMessage += "\nLong text and binary cells are shortened for display.";
            if (result.Error != null) QueryMessage += "\n" + result.Error;
            FeedbackError = result.Error != null; Feedback = result.Error != null ? "SQL execution returned an error. See query messages." : result.Cancelled ? "Query cancelled; any returned rows are partial." : "SQL execution finished.";
        });
    }
    private async Task OpenSqlAsync() { var dialog = new OpenFileDialog { Filter = "SQL script|*.sql|Text files|*.txt" }; if (dialog.ShowDialog() == true) { QueryText = await File.ReadAllTextAsync(dialog.FileName); WorkspaceTab = 0; } }
    private async Task SaveSqlAsync() { var dialog = new SaveFileDialog { Filter = "SQL script|*.sql", FileName = "query.sql" }; if (dialog.ShowDialog() == true) { await AtomicFile.WriteAsync(dialog.FileName, QueryText); Feedback = "SQL script saved. Connection credentials are not included."; } }

    private static DataView ToView(SqlResultSet result)
    {
        var table = new DataTable { Locale = System.Globalization.CultureInfo.InvariantCulture };
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in result.Columns)
        {
            var candidate = string.IsNullOrWhiteSpace(name) ? "Column" : name; var suffix = 2; var original = candidate;
            while (!names.Add(candidate)) candidate = original + " (" + suffix++ + ")";
            table.Columns.Add(candidate, typeof(object));
        }
        foreach (var row in result.Rows) table.Rows.Add(row.Select(v => v ?? DBNull.Value).ToArray());
        return table.DefaultView;
    }
    public void Cancel() => cancellation?.Cancel();
}
