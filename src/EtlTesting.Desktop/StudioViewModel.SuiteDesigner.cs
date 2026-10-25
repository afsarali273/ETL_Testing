using System.IO;
using EtlTesting.Core;
using EtlTesting.Persistence;
using EtlTesting.SqlServer;

namespace EtlTesting.Desktop;

public sealed partial class StudioViewModel
{
    private SuiteDesignerViewModel? designer;
    public SuiteDesignerViewModel Designer
    {
        get
        {
            if (designer != null) return designer;
            designer = new(() => JsonText, LoadSuite, () => environment, () => busy || Connections.IsWorking, () => IsDemo,
                definitions => DefinitionLoader.Bind(DefinitionLoader.Parse(JsonText) with { Parameters = definitions }, Overrides()),
                id => RunSuiteAsync(id == null ? null : [id]), PreviewDesignerValidationAsync, SaveAsync, message => Status = message);
            designer.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SuiteDesignerViewModel.IsWorking)) Notify(nameof(CanEdit)); };
            designer.ActiveTestCaseId = selectedTestCase?.Id;
            return designer;
        }
    }
    private async Task<RunResult> PreviewDesignerValidationAsync(TestDefinition draft, Dictionary<string, ParameterDefinition> declarations, CancellationToken token)
    {
        if (busy || Connections.IsWorking) throw new InvalidDataException("Wait for the current operation to finish.");
        var previewSuite = DefinitionLoader.Parse(JsonText) with { Tests = [draft with { Enabled = true }], Parameters = declarations };
        var parameters = DefinitionLoader.Bind(previewSuite, Overrides());
        IDataConnector connector;
        if (IsDemo) connector = new DemoConnector();
        else { var sql = new SqlServerConnector(environment, new EnvironmentCredentialResolver()); sql.Preflight(previewSuite, parameters); connector = sql; }
        IsBusy = true; cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            var runner = new SuiteRunner([new ComparisonHandler(new SqliteRowIndexFactory()), new SqlAssertionHandler()]);
            return await Task.Run(() => runner.RunAsync(previewSuite, IsDemo ? "DEMO" : environment.Name, connector, parameters, new(Path.Combine(root, "work"), IsDemo), null, cancellation.Token));
        }
        finally { IsBusy = false; cancellation.Dispose(); cancellation = null; }
    }
}
