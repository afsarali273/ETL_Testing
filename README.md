# ETL Test Studio

A Windows desktop tool and independent CLI for a team's SQL Server ETL validation. This is the first working comparison release, based on the supplied handoff and nine design references.

## Try the desktop

From this repository in PowerShell:

```powershell
dotnet run --project src/EtlTesting.Desktop
```

The app opens a four-test sample in **DEMO DATA** mode. Click **Run suite** to exercise the shared engine and inspect intentional failures. Demo assertions use a fixed synthetic fixture; arbitrary SQL is not executed or interpreted in demo mode. The badge, history and exports identify demo runs.

**Overview** shows totals from the suite library, the latest saved execution, recent runs, suite status, and failure details. Its links open the relevant suite or run; Refresh reloads local files and history. Use **Test Suites** to edit source/target scopes, composite keys and mappings. The JSON tab retains supported settings. Settings contains runtime parameters and the demo switch. **Connections** manages saved profiles across multiple servers and databases using structured fields and protected SQL credentials. The separate **SQL Workspace** selects any saved profile for table/view browsing, column inspection, paged data previews and SQL queries with result grids. Reports includes searchable, paged evidence and offline HTML exports. See [Connections and SQL Workspace](docs/connections-and-sql-workspace.md).

The published package runs without an SDK: extract the ZIP, open **Desktop/EtlTesting.Desktop.exe**. Keep all adjacent files with the executable.

## Run from the CLI

```powershell
dotnet run --project src/EtlTesting.Cli -- validate --suite samples/suites/reservations.json
dotnet run --project src/EtlTesting.Cli -- run --suite samples/suites/reservations.json --demo --output artifacts/demo
```

The demo intentionally returns exit code **1**, demonstrating validation failures. Exit codes: 0 required validations passed; 1 validation failures; 2 configuration/execution/report error; 3 cancelled. Cancellation takes precedence, followed by runtime error, then required validation failure. Warning failures remain visible but do not block.

For real SQL, edit `samples/environments/qa.json` with your server and database, then:

```powershell
dotnet run --project src/EtlTesting.Cli -- run `
  --suite samples/suites/reservations.json `
  --environment samples/environments/qa.json `
  --parameter batchId=B-20261024 `
  --output artifacts/etl-results --formats html,junit,csv,json
```

Source and target can share **one database**. Production validation should use a read-only identity and a stable data boundary after ETL completes. Raw queries and filters are authored SQL; database permissions enforce access.

## Local SQL fixture and tests

```powershell
.\scripts\verify.ps1 -LocalDb
dotnet run --project src/EtlTesting.Cli -- run --suite samples/suites/reservations.json --environment samples/environments/localdb.json --output artifacts/localdb
```

The setup script creates `EtlTestStudioSample` and replaces only its named fixture batch. It contains three mapped table pairs, null keys/values, composite duplicates, equal row counts with different keys, multiple mismatches per row, decimal tolerance pass/fail, case/whitespace normalization and an orphan reference. The real fixture also intentionally returns 1.

Run `scripts/verify.ps1` without `-LocalDb` for portable tests; real SQL tests are explicitly skipped without opt-in.

## Local database for the team project

The `ETLTeamDev` database is set up on this PC's SQL Server LocalDB instance. The desktop connection profile is saved in `%LOCALAPPDATA%\EtlTestStudio\qa.json`; the app is switched to SQL mode. This database contains representative fictional fixtures, not your team's business data. See [local development database](docs/local-development-database.md) for the profile, tables, setup and limitations. Each teammate can create the same local development database on their own computer.

## Team package

```powershell
.\scripts\publish.ps1
```

Creates separate self-contained Windows x64 desktop/CLI folders and a distributable ZIP in `artifacts/`. See [deployment](docs/deployment.md), [contract and semantics](docs/contracts.md), [verification](docs/verification.md) and [implementation status](docs/implementation-status.md).

For a complete onboarding and module reference, see [project reference](docs/project-reference.md).

Baseline/incremental workflows, checkpoint advancement, SCD/schema validators, managed quality forms and automatic ETL triggering remain future milestones. They are not advertised as executable checks in this release.
