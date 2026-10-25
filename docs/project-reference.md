# ETL Test Studio — project reference

This document is the map for a new developer, tester, or QA engineer joining the project. It explains what the product does today, where each responsibility lives, how a validation run moves through the system, how to run the project locally, and where to extend it safely.

**Current repository state:** working Windows desktop application, independent CLI, SQL Server connector, local persistence, validation-template editor, SQL workspace, run history, searchable reports, offline HTML/PDF/JSON/CSV/JUnit exports, demo fixtures, and an opt-in LocalDB integration suite.

**Product principles**

- Local first: the application does not require an account, cloud service, central server, or AI API.
- JSON is the portable suite contract. Suite files belong in Git.
- The desktop and CLI use the same engine and result model.
- SQL reads are performed with the selected connection's permissions. The validator does not trigger ETL jobs or write target data.
- Demo data is synthetic and always labelled as demo data.
- Evidence is bounded for storage, but validation counts cover the configured scope.

## 1. Repository map

    EtlTesting.sln
    ├── src/
    │   ├── EtlTesting.Core/          Engine contracts, models, schema validation, handlers
    │   ├── EtlTesting.SqlServer/     Microsoft.Data.SqlClient connector and metadata workspace
    │   ├── EtlTesting.Reporting/     JSON, HTML, PDF, CSV and JUnit reporters
    │   ├── EtlTesting.Persistence/   Atomic files, suite library, connection profiles, history, indexes
    │   ├── EtlTesting.Cli/           Non-interactive etltest host for CI and PowerShell
    │   └── EtlTesting.Desktop/       WPF/MVVM Windows application
    ├── tests/
    │   ├── EtlTesting.Tests/         Core, connector, persistence, reporter, CLI and LocalDB tests
    │   └── EtlTesting.Desktop.Tests/ WPF view-model and designer tests
    ├── schemas/                      Versioned JSON Schema
    ├── samples/                      Starter suites, environments and SQL setup scripts
    ├── docs/                         Product, deployment, verification and reference docs
    ├── design-reference/             Nine visual reference images
    └── scripts/                      Verification, publishing and local install scripts

The solution targets .NET 10 through global.json. Directory.Build.props enables nullable reference types, implicit usings, locked package restore, and warnings as errors.

## 2. Application surfaces

The desktop navigation is defined in MainWindow.xaml. The main surfaces are:

| Surface | Purpose | Primary code |
| --- | --- | --- |
| Overview | Suite totals, latest execution, recent runs and failure summaries | OverviewView.xaml and StudioViewModel.Overview.cs |
| Test Suites | Discover suite files, edit tests, map source/target columns, configure templates, save and run | SuiteWorkspaceView.xaml, SuiteDesignerView.xaml, SuiteDesignerViewModel.cs |
| Connections | Save multiple server/database profiles, choose authentication, test access, inspect metadata | ConnectionManagerView.xaml and ConnectionManagerViewModel.cs |
| SQL Workspace | Use any saved profile to browse schemas/tables/columns, preview rows and run SQL | DatabaseWorkspaceView.xaml and DatabaseWorkspace.cs |
| Run History | Browse persisted executions and live progress/logs | StudioViewModel.cs and MainWindow.xaml |
| Reports | Read a run as a summary, mismatch table, selected evidence detail and execution context; export | ReportView.xaml and StudioViewModel.Report.cs |
| Settings | Demo mode, runtime parameters, CLI preview, exit-code guidance and release notes | MainWindow.xaml |

The WPF theme and reusable visual tokens live in Themes/StudioTheme.xaml. Icons are local Fluent-style SVG assets under Assets/FluentIcons; the app does not require a CDN.

## 3. End-to-end execution flow

    Suite JSON
       │
       ├─ DefinitionLoader.Parse / Validate / Bind parameters
       │
       ├─ Desktop or CLI selects demo connector or SqlServerConnector
       │
       ├─ SuiteRunner selects enabled tests and handler by TestDefinition.Type
       │       ├─ ComparisonHandler: stream source + target into SQLite row index
       │       └─ SqlAssertionHandler: execute one query, compare typed expectation
       │
       ├─ RunResult records status, counts, errors and bounded Evidence
       │
       ├─ FileResultStore saves run JSON and the desktop refreshes history
       │
       └─ ReportWriter produces HTML/PDF/JSON/CSV/JUnit artifacts

The central contracts are in Models.cs: Suite, TestDefinition, TestResult, Evidence, RunResult, IDataConnector, IValidationHandler, ISuiteRunner, IResultStore, and IReportWriter.

SuiteRunner is the extension point for validation types. It validates the suite, creates a result for every selected test, reports structured progress, honours cancellation, and applies deterministic exit-code precedence:

1. cancelled → 3
2. runtime error → 2
3. required validation failure → 1
4. otherwise → 0

Warning-severity failures remain visible in results and reports but do not make the process exit with 1.

## 4. Suite JSON contract

The canonical contract is schemas/suite-v1.schema.json. The strongly typed runtime model is in Models.cs, and semantic checks are in DefinitionLoader.cs.

A suite contains:

- schemaVersion, stable id, display name, description and tags;
- defaults.connection and defaults.timeoutSeconds;
- typed parameter declarations with required/default values;
- runnable tests;
- optional testCases containers used by the desktop suite hierarchy.

Each test has a stable ID, name, type, enabled state, severity, connection override, tags, timeout, evidence limit, and type-specific fields.

### Mapped comparison

type dataComparison reads source and target scopes, builds a composite key from keys, and compares mapped columns. A test can enable duplicate/unique-key checking, missing target records, unexpected target records, string trimming and case handling, empty-as-null handling, decimal absolute tolerance, and date/time tolerance.

The comparison handler streams both sides into the disk-backed SQLite index in SqliteRowIndex.cs. It counts source rows, target rows, duplicate groups, null-key rows, missing keys, unexpected keys, mismatched records, and field mismatches separately. A row with five differing mapped fields is one mismatched record and five field mismatches.

### SQL assertion

type sqlAssertion executes an authored query once through the selected connection. It supports expect.rowCount, a typed expect.scalar, and generated template SQL in the optional template property.

The query result is bounded for evidence, while the returned count remains the validation result. SQL parameters are bound as SqlParameter; identifiers are validated and quoted separately.

### Template catalog

The compiler and catalog are in ValidationTemplates.cs. The current executable templates are requiredValues, uniqueKeys, allowedValues, numericRange, dateRange, lengthRange, referentialIntegrity, rowCount, and aggregate.

The desktop template workflow is implemented by ValidationTemplatePickerView.xaml, ValidationFormView.xaml, and SuiteDesignerViewModel.Templates.cs. Incremental, SCD/historical, and schema templates are shown as planned/unavailable; they must not be advertised as runnable until the engine supports them.

## 5. SQL Server and connection model

The SQL connector is SqlServerConnector.cs. It handles Windows integrated authentication, SQL authentication using Windows Credential Manager or environment-variable references, encryption and certificate validation flags, connection and command timeouts, identifier validation/quoting, streamed reads and one-shot query execution, and metadata discovery for schemas, tables, views, columns, and row previews.

Connection profiles are structured data. Passwords are never stored in suite JSON or environment JSON. The persistence model and validation rules are in ConnectionProfileStore.cs. The connection manager supports multiple profiles for multiple environments, servers, databases, and logical connection keys.

The SQL Workspace intentionally uses a selected saved profile independently of the suite's active connection. It is a user-facing SQL client surface, but access remains constrained by the profile's SQL permissions.

Important operational rule: reads are separate read-committed statements. Run after the ETL has completed against a stable scope. Two separate connections do not create a single cross-database snapshot.

## 6. Local state and artifacts

The default desktop state root is %LOCALAPPDATA%\EtlTestStudio\:

    EtlTestStudio/
    ├── suites/                       Local editable suite copies
    ├── suite-library/index-v1.json   Registered files/folders
    ├── connections/                  Saved profiles and verification state
    ├── history/                      RunResult JSON, newest 200 loaded by the UI
    ├── artifacts/<runId>/            Portable run reports and execution artifacts
    └── work/                         Temporary comparison indexes

Writes use AtomicFile in ResultStore.cs, which writes a temporary file and replaces the destination. History is file-based and local; it is not a shared multi-user database.

The suite library is implemented in SuiteLibraryStore.cs. It discovers bundled samples, local copies, registered files, and registered folders. A local editable suite with the same stable ID supersedes its bundled starter.

## 7. Reports

All reporters consume the same RunResult; no reporter reruns SQL. ReportWriter.cs supports:

| Format | Use |
| --- | --- |
| json | Canonical machine-readable run and evidence record |
| html | Standalone offline report with summary, outcomes, evidence, technical details, responsive layout, and print CSS |
| pdf | Paginated enterprise-style report generated locally with PDFsharp |
| csv | Evidence rows for spreadsheet analysis; formula-like values are prefixed to reduce spreadsheet injection risk |
| junit | CI test-result ingestion; validation failures map to failures and runtime failures map to errors |

Reports distinguish what the validation checked; whether it passed, failed, errored, was skipped, or was cancelled; counts and duration; expected/source versus actual/target values where available; plain-language explanation; technical rule, key, reason, and captured row details; and evidence sampling/truncation.

Reports must never include credentials or secret values.

## 8. Desktop editing and data flow

StudioViewModel is the application coordinator. Partial files keep major areas maintainable:

- StudioViewModel.cs: application state, navigation, running, history, and common commands;
- StudioViewModel.Overview.cs: dashboard projections;
- StudioViewModel.SuiteLibrary.cs: discovery and suite selection;
- StudioViewModel.SuiteDesigner.cs: test editing and suite saving;
- SuiteDesignerViewModel.Templates.cs: catalog and template configuration;
- StudioViewModel.Report.cs: report filters, KPI values, evidence rows, and friendly explanations;
- ConnectionManagerViewModel.cs: profile lifecycle and connection tests.

When editing a suite, changes remain in memory until Save suite. Advanced JSON editing must preserve supported fields. A preview runs against the real engine but does not add a run to history. A selected-test run is explicitly a partial selection; skipped tests must not be mistaken for passed tests.

## 9. CLI and CI

The CLI project produces etltest and does not depend on WPF. Commands:

    dotnet run --project src/EtlTesting.Cli -- validate --suite samples/suites/reservations.json
    dotnet run --project src/EtlTesting.Cli -- list-checks
    dotnet run --project src/EtlTesting.Cli -- run --suite samples/suites/reservations.json --environment samples/environments/qa.json --parameter batchId=B-20261024 --output artifacts/etl-results --formats html,pdf,json,csv,junit

Use --demo instead of --environment for deterministic synthetic fixtures. --parameter, --test, --tag, and --timeout follow the CLI parser rules in Program.cs. Never put secret values in command-line arguments; use the environment profile's credential references.

The repository includes CI examples in .github/workflows/etl-validation.example.yml and azure-pipelines.example.yml. Publish JUnit and report artifacts even when validations fail. The runner needs SQL network access, a writable local output directory, and enough disk for streamed datasets plus indexes.

## 10. Local development database

The supplied fixture uses a real SQL Server LocalDB database with synthetic rows:

    sqllocaldb start MSSQLLocalDB
    sqlcmd -S '(localdb)\MSSQLLocalDB' -E -b -i samples/sql/setup-teamdev.sql

Use samples/environments/local-teamdev.json, logical connection warehouse, and batchId=B-20261024. The complete setup and limitations are in local-development-database.md.

The fixture is for development and verification. It is not a production schema and contains no team business data.

## 11. Build, test, and publish

Portable verification:

    .\scripts\verify.ps1

Verification with the LocalDB fixture:

    .\scripts\verify.ps1 -LocalDb

Run the desktop during development:

    dotnet run --project src/EtlTesting.Desktop

Run the CLI demo:

    dotnet run --project src/EtlTesting.Cli -- run --suite samples/suites/reservations.json --demo --output artifacts/demo

Publish the self-contained Windows package:

    .\scripts\publish.ps1

The package contains independent Desktop and Cli folders. Extract the ZIP as a whole; adjacent runtime files are required. Local installation is handled by install-local.ps1. Signing, enterprise installer packaging, automatic updates, and clean-machine deployment remain separate release work.

## 12. Testing strategy

The tests cover core comparison semantics, SQL assertions, template compilation, schema strictness, parameters and identifier quoting, connection profiles, suite library discovery, report escaping and formats, CLI process behavior, WPF designer behavior, and opt-in LocalDB integration.

The real SQL integration tests are intentionally separated from portable tests. Read verification.md before interpreting a test count; a passing portable test run does not prove enterprise SQL connectivity or large-data performance.

## 13. Adding a new validation type

1. Add or update the schema and typed model in Models.cs.
2. Add semantic validation and parameter rules in DefinitionLoader.cs.
3. Implement IValidationHandler in Execution.cs or a focused core file.
4. Register the handler in both desktop and CLI SuiteRunner construction.
5. Add a template compiler/form if the rule is user-configurable.
6. Emit stable counts and bounded Evidence; distinguish records, fields, groups, and errors.
7. Add tests for pass, fail, error, cancellation, evidence limits, warning severity, and malformed input.
8. Confirm all reporters and the desktop report screen display the new category without raw secrets.
9. Update list-checks, README, implementation status, and this reference.

Do not place database access in WPF code. Do not duplicate validation logic in the CLI or UI. The engine should remain usable without a desktop installation.

## 14. Known limitations and future work

The following are deliberately not complete in the current release:

- baseline capture and incremental change-window validation;
- checkpoint advancement and ETL triggering;
- SCD, schema, transformation, and historical validators;
- full SQL editor syntax highlighting/autocomplete;
- full template-catalog multi-select workflow for every planned type;
- complete dirty-state prompts, deletion/reordering workflows, and rerun-failed lineage;
- central multi-user history, migration/retention UI, signing, installer/updater;
- full accessibility and 100–200% DPI audit;
- enterprise SQL/TLS credential verification on clean machines;
- measured million-row performance and server-side reconciliation.

Before presenting a feature as production-ready, update implementation-status.md, add verification evidence, and keep the UI language aligned with what the engine actually executes.

## 15. Useful companion documents

- [Contracts and semantics](contracts.md)
- [Suite designer](suite-designer.md)
- [Test library and validation templates](test-library-and-templates.md)
- [Connections and SQL Workspace](connections-and-sql-workspace.md)
- [Deployment](deployment.md)
- [Verification report](verification.md)
- [Implementation status](implementation-status.md)
- [Local development database](local-development-database.md)
- [Design references](../design-reference/)

