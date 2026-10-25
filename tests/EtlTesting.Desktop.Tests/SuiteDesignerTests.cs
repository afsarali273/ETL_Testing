using EtlTesting.Core;
using EtlTesting.Desktop;
using Xunit;

namespace EtlTesting.Desktop.Tests;

public sealed class SuiteDesignerTests
{
    [Fact] public void BatchTemplatesRetainDraftsInvalidateEditsAndCommitAtomically() => OnSta(() =>
    {
        var fixture = new Fixture(); var vm = fixture.Vm;
        vm.AddCommand.Execute(null);
        var required = vm.BatchItems.Single(i => i.Template.Kind == "requiredValues");
        var unique = vm.BatchItems.Single(i => i.Template.Kind == "uniqueKeys");
        vm.ActiveBatchItem = required; required.Selected = true; vm.Editor!.SourceTable = "Reservation"; vm.Editor.ColumnText = "Id";
        vm.ConfigureBatchCommand.Execute(null); Assert.True(required.Ready);
        vm.ActiveBatchItem = unique; unique.Selected = true; vm.Editor!.SourceTable = "Reservation"; vm.Editor.ColumnText = "Id";
        vm.ConfigureBatchCommand.Execute(null); Assert.True(vm.CanAddBatch, vm.Feedback);
        vm.ActiveBatchItem = required; Assert.Equal("Id", vm.Editor!.ColumnText);
        vm.Editor.ColumnText = "Id\nCode"; Assert.False(required.Ready); Assert.False(vm.CanAddBatch);
        vm.AddBatchCommand.Execute(null); Assert.Single(fixture.Saved.Tests);
        vm.ConfigureBatchCommand.Execute(null); vm.AddBatchCommand.Execute(null);
        Assert.Equal(3, fixture.Saved.Tests.Count); Assert.Equal(0, vm.Stage); Assert.Empty(vm.BatchItems);
    });

    [Fact] public void ParentTestFiltersValidationsWithoutDroppingOtherTests() => OnSta(() =>
    {
        var fixture = new Fixture(); var vm = fixture.Vm;
        var grouped = SuiteHierarchy.ForEditing(fixture.Saved);
        var second = new TestCaseDefinition { Id = "second", Name = "Second test" };
        grouped = grouped with { TestCases = [.. grouped.TestCases, second], Tests = [.. grouped.Tests, grouped.Tests[0] with { Id = "second-check", TestCaseId = second.Id }] };
        vm.LoadSuite(grouped); vm.ActiveTestCaseId = second.Id;
        Assert.Equal("second-check", Assert.Single(vm.RowsView.Cast<SuiteValidationRow>()).Id);
        Assert.Equal(2, vm.Rows.Count);
    });

    [Fact] public void InlineMappingsUseRealProfileAndApplyWithoutChangingOtherChecks() => OnSta(() =>
    {
        var fixture = new Fixture(); var vm = fixture.Vm;
        vm.OpenTest(null); Assert.NotNull(vm.Editor); Assert.Equal(0, vm.Stage);
        Assert.Equal("test", vm.SourceServer); Assert.Equal("test", vm.TargetDatabase);
        vm.Editor!.Mappings[0].Tolerance = "0.05"; vm.ApplyInlineDraft();
        Assert.Equal(.05m, fixture.Saved.Tests[0].Columns[0].Comparison.AbsoluteTolerance);
        Assert.False(vm.Editor!.IsDirty); Assert.Equal(0, vm.Stage);
    });

    [Fact] public void MappingActionsReorderRemoveAndPersistComparisonRules() => OnSta(() =>
    {
        var fixture = new Fixture(); var vm = fixture.Vm; vm.OpenTest(null);
        vm.AddMappingCommand.Execute(null); var added = vm.Editor!.Mappings.Last(); added.Source = "Code"; added.Target = "Code"; added.IgnoreCase = true;
        vm.SelectedMapping = added; vm.MoveMappingUpCommand.Execute(null); Assert.Same(added, vm.Editor.Mappings[0]);
        Assert.Contains("Ignore case", added.RuleSummary); vm.RemoveMappingCommand.Execute(added); Assert.Single(vm.Editor.Mappings);
        vm.ApplyInlineDraft(); Assert.Single(fixture.Saved.Tests[0].Columns);
    });

    [Fact] public void ParameterCollectionChangesInvalidateConfiguredTemplate() => OnSta(() =>
    {
        var fixture = new Fixture(); var vm = fixture.Vm; vm.AddCommand.Execute(null);
        var item = vm.BatchItems.Single(i => i.Template.Kind == "sqlAssertion"); vm.ActiveBatchItem = item;
        vm.ConfigureBatchCommand.Execute(null); Assert.True(item.Ready);
        vm.Parameters.Add(new() { Name = "limit", Type = "int64", DefaultValue = "10" }); Assert.False(item.Ready);
        vm.ConfigureBatchCommand.Execute(null); Assert.True(item.Ready);
        vm.Parameters.RemoveAt(0); Assert.False(item.Ready);
    });

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private sealed class Fixture
    {
        public string Json { get; private set; }
        public string? RunId { get; private set; }
        public SuiteDesignerViewModel Vm { get; }
        public Fixture()
        {
            var test = new TestDefinition { Id = "values", Name = "Reservation values", Type = "dataComparison", Source = new() { Schema = "staging", Table = "Reservation" }, Target = new() { Table = "Reservation" }, Keys = [new() { Source = "Id", Target = "Id" }], Columns = [new() { Source = "Amount", Target = "Revenue", Comparison = new() { AbsoluteTolerance = .01m, Trim = false } }], EvidenceLimit = 25, Tags = ["smoke"] };
            var suite = new Suite { Id = "suite", Name = "Test suite", Tests = [test] }; Json = StudioJson.Write(suite);
            Vm = new(() => Json, value => Json = value, () => new() { Connections = new() { ["warehouse"] = new() { Server = "test", Database = "test" } } }, () => false, () => false, definitions => [], id => { RunId = id; return Task.CompletedTask; }, (draft, declarations, token) => Task.FromResult(new RunResult { Tests = [new() { Id = draft.Id, Status = "Passed" }] }), () => Task.CompletedTask, _ => { });
            Vm.LoadSuite(suite);
        }
        public Suite Saved => DefinitionLoader.Parse(Json);
    }

    [Fact] public void EditingValidationPreservesIdMappingsAndAdvancedSettings() => OnSta(() =>
    {
        var fixture = new Fixture(); var vm = fixture.Vm;
        vm.EditCommand.Execute(vm.Rows[0]); vm.Editor!.Name = "Updated values"; vm.SaveValidationCommand.Execute(null);
        Assert.False(vm.FeedbackError, vm.Feedback); Assert.Equal(0, vm.Stage);
        var test = Assert.Single(fixture.Saved.Tests);
        Assert.Equal("values", test.Id); Assert.Equal("Updated values", test.Name); Assert.Equal(.01m, test.Columns[0].Comparison.AbsoluteTolerance); Assert.Equal(25, test.EvidenceLimit); Assert.Equal(["smoke"], test.Tags);
    });

    [Fact] public void DuplicateCreatesIndependentStableIdAndDeleteLastSupportsUndo() => OnSta(() =>
    {
        var fixture = new Fixture(); var vm = fixture.Vm;
        vm.DuplicateCommand.Execute(vm.Rows[0]); vm.SaveValidationCommand.Execute(null);
        Assert.Equal(2, fixture.Saved.Tests.Count); Assert.Equal(2, fixture.Saved.Tests.Select(t => t.Id).Distinct().Count());
        foreach (var row in vm.Rows) row.Selected = true;
        vm.DeleteSelectedCommand.Execute(null); Assert.Empty(fixture.Saved.Tests);
        vm.UndoCommand.Execute(null); Assert.Equal(2, fixture.Saved.Tests.Count); Assert.Equal("values", fixture.Saved.Tests[0].Id);
    });

    [Fact] public void BulkEnableAndRunOneUseStableSelections() => OnSta(() =>
    {
        var fixture = new Fixture(); var vm = fixture.Vm;
        vm.Rows[0].Selected = true; vm.DisableSelectedCommand.Execute(null); Assert.False(fixture.Saved.Tests[0].Enabled);
        vm.EnableSelectedCommand.Execute(null); Assert.True(fixture.Saved.Tests[0].Enabled);
        vm.RunOneCommand.Execute(vm.Rows[0]); Assert.Equal("values", fixture.RunId);
    });

    [Fact] public void InvalidDraftCannotOverwriteSavedValidation() => OnSta(() =>
    {
        var fixture = new Fixture(); var before = fixture.Json; var vm = fixture.Vm;
        vm.EditCommand.Execute(vm.Rows[0]); vm.Editor!.Name = ""; vm.SaveValidationCommand.Execute(null);
        Assert.True(vm.FeedbackError); Assert.Equal(2, vm.Stage); Assert.Equal(before, fixture.Json);
        vm.Editor.Name = "Corrected"; vm.SaveValidationCommand.Execute(null); Assert.Equal("Corrected", fixture.Saved.Tests[0].Name);
    });

    [Fact] public void AllAvailableTemplatesSaveAndReopenInTheirOwnFocusedEditor() => OnSta(() =>
    {
        var fixture = new Fixture(); var vm = fixture.Vm;
        foreach (var template in ValidationTemplateChoice.Catalog.Where(t => t.Available && t.Kind != "dataComparison"))
        {
            vm.ChooseTemplateCommand.Execute(template);
            var editor = vm.Editor!; editor.SourceTable = "Reservation"; editor.TargetTable = "Parent"; editor.ColumnText = "Amount";
            editor.Keys.Add(new() { Source = "ParentId", Target = "Id" }); editor.SourceMeasure = "Amount"; editor.TargetMeasure = "Amount";
            editor.AllowedValues = "Active\nInactive"; editor.Minimum = template.Kind == "dateRange" ? "2026-10-24T00:00:00" : "0";
            vm.SaveValidationCommand.Execute(null); Assert.False(vm.FeedbackError, template.Kind + ": " + vm.Feedback);
            var row = vm.Rows.Last(); vm.EditCommand.Execute(row);
            Assert.Equal(template.Kind, vm.Editor!.Kind); Assert.Equal(template.Name, vm.Editor.TemplateName);
            vm.BackCommand.Execute(null);
        }
        Assert.Equal(11, fixture.Saved.Tests.Count);
    });

    [Fact] public void SqlScalarAndAllowedEmptyStringConfigurationsAreEditable() => OnSta(() =>
    {
        var fixture = new Fixture(); var vm = fixture.Vm;
        vm.ChooseTemplateCommand.Execute(ValidationTemplateChoice.Catalog.Single(t => t.Kind == "sqlAssertion"));
        vm.Editor!.Query = "SELECT CAST(123.45 AS decimal(18,2));"; vm.Editor.ExpectationMode = "scalar"; vm.Editor.ScalarKind = "decimal"; vm.Editor.ScalarValue = "123.45"; vm.SaveValidationCommand.Execute(null);
        var sql = fixture.Saved.Tests.Last(); Assert.Equal(new Cell("decimal", "123.45"), sql.Expect!.Scalar);
        vm.ChooseTemplateCommand.Execute(ValidationTemplateChoice.Catalog.Single(t => t.Kind == "allowedValues"));
        vm.Editor!.SourceTable = "T"; vm.Editor.ColumnText = "Status"; vm.Editor.AllowEmptyString = true; vm.SaveValidationCommand.Execute(null);
        Assert.False(vm.FeedbackError, vm.Feedback); Assert.Equal([""], fixture.Saved.Tests.Last().Template!.AllowedValues);
    });

    [Fact] public void PreviewUsesDraftParametersAndLeavesSavedSuiteUntouched() => OnSta(() =>
    {
        var fixture = new Fixture(); var before = fixture.Json; var vm = fixture.Vm;
        vm.ChooseTemplateCommand.Execute(ValidationTemplateChoice.Catalog.Single(t => t.Kind == "sqlAssertion"));
        vm.Editor!.Query = "SELECT @limit"; vm.Parameters.Add(new() { Name = "limit", Type = "int64", DefaultValue = "10", Required = true });
        vm.PreviewValidationCommand.Execute(null);
        Assert.False(vm.FeedbackError, vm.Feedback); Assert.Contains("Passed", vm.PreviewMessage); Assert.Equal(before, fixture.Json); Assert.True(vm.Editor.IsDirty);
    });
}
