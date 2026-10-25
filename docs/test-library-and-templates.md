# Test suites and validation templates (0.3)

Test Suites opens a library of actual suite files: local suites, bundled starters, imported JSON files, and recursively imported folders. Refresh rescans these locations. A local copy supersedes a bundled starter with the same suite ID; separate imported files remain visible. Invalid or missing files show an error on their card.

Open a suite to see its tests in the left pane. Select a test to see its validations. Each mapped comparison contains its own source/target scope, business keys, column mappings and comparison settings. Add, copy, rename and delete tests independently; run a selected test or the whole suite. Changes stay in memory until **Save suite**. Switching suites warns about unsaved changes. Saving a bundled starter defaults to the local suite folder.

## Add validations

1. Select a test, then **Add validations**.
2. Filter by category or search, and select the desired templates.
3. Choose a template row to configure its scope, columns, mappings, expectations and evidence settings. **Load tables & columns** uses the selected connection profile.
4. Select **Mark configured** for each selected check. Editing it again invalidates its configured status. Drafts remain intact while switching templates.
5. Optionally **Preview check** against the configured connection. Preview results and evidence do not alter the suite or run history. Demo mode cannot execute SQL recipe previews.
6. **Add N checks to test** becomes available when all selected checks have valid configurations. Checks are added together; **Save suite** persists them.

**Save as preset** exports the configured selection as JSON. **Load preset** on a test adds independent editable copies, with new validation IDs and remapped test ownership. Review connection names, parameters and table scopes before execution.

Executable templates: mapped comparison, required values, unique business key, allowed values, numeric range, date/time range, text length, referential integrity, row count reconciliation, aggregate reconciliation, and custom SQL assertions. Incremental loads, historical/SCD and schema checks are explicitly marked unavailable pending engine support.

## File compatibility

Suite JSON keeps `tests` as runnable validation definitions for the shared desktop/CLI engine. Optional `testCases` defines user-facing test containers; `testCaseId` links each validation to its test. Existing flat suites are grouped in memory when opened, with existing validation IDs and configuration retained. Their files change only on explicit save. A disabled parent skips all its validations, and results include the parent test ID/name. Use the 0.3 desktop and CLI together for grouped suite files.

The library registry is `%LOCALAPPDATA%/EtlTestStudio/suite-library/index-v1.json`; suite files, connection profiles and run history remain separate. No shared network service is required.
