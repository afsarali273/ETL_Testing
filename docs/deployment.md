# Deployment and team setup

## Windows desktop

`scripts/publish.ps1` produces self-contained win-x64 builds, using the pinned SDK and .NET runtime 10.0.13 for distribution. The development machine currently has SDK 10.0.102/runtime 10.0.2; production packages carry their own serviced runtime. Distribute the ZIP internally; extract all files, then open `Desktop/EtlTesting.Desktop.exe`. No SDK, web service, account or AI API is required. Signing, enterprise installer packaging and automatic updates are deferred. Review/sign binaries using your enterprise process before broad rollout.

Default local state: `%LOCALAPPDATA%/EtlTestStudio/`. Suite copies live in `suites/`, reports in `artifacts/<runId>/`, history in `history/`, temporary comparison indexes in `work/`. Each person has independent local state. Share canonical suite JSON through Git and export reports explicitly; no shared SQLite database is required. History retention is manual in this release. A dedicated purge/migration UI remains deferred.

The CLI is separate (`Cli/etltest.exe`) and has no WPF requirement. It accepts explicit artifact paths and environment-variable secret references. Published CLI includes samples and schemas. The repository README examples use `dotnet run`; for the published CLI replace `dotnet run --project src/EtlTesting.Cli --` with `.\etltest.exe` and use its `samples/` directory.

## Connect to SQL Server

Use the Connections screen to save profiles for each server/database, or import an environment such as `samples/environments/qa.json`. Choose integrated authentication, Windows Credential Manager for SQL credentials, or environment-variable references. Passwords never belong in suite/environment JSON. Vault references work on Windows under the identity that saved them; other runners use environment credentials or integrated authentication. The separate SQL Workspace uses any saved profile without changing the active suite connection. See [Connections and SQL Workspace](connections-and-sql-workspace.md).

Production defaults encrypt connections and validate server certificates. Install a trusted enterprise SQL certificate with the appropriate DNS name. The LocalDB-only sample explicitly trusts its local certificate; do not copy that exception into enterprise profiles blindly. Connection timeout and per-suite/CLI query timeout are separate.

Grant SELECT on required tables/views and metadata visibility appropriate to the suite. Authored SQL/filter code runs with that identity: use database permissions for read-only enforcement. `ApplicationIntent` or keyword scanning would not provide a security boundary. Setup fixtures use a separate identity with create/write permission and the explicitly named local sample database. The validator never performs ETL writes or triggers jobs.

All three sample comparisons inherit one connection, so separate staging/warehouse servers are unnecessary. Optional per-test and per-side overrides support cross-connection comparisons with the same disk-index strategy. Separate connections do not provide a globally consistent snapshot. Finish ETL and freeze the relevant scope before validation.

## CI

The GitHub Actions example is `.github/workflows/etl-validation.example.yml`; configure the self-hosted Windows runner with SQL network access and either runner identity permissions or secret environment references. The workflow is manual-only until customized. JUnit is included in the uploaded artifacts; GitHub has no built-in JUnit ingestion step in this example. Azure DevOps uses `PublishTestResults@2` and always publishes artifacts even when validations fail. Both examples run against the QA environment and need your server/database and batch parameter.

For SQL authentication set `ETL_SQL_USER` and `ETL_SQL_PASSWORD` from pipeline secret variables and use `samples/environments/sql-auth.json`. Never put secret values in command-line arguments or report directories. Point work/output at a writable local runner directory with sufficient space for both streamed datasets plus the index. Publish only retained report artifacts and apply your team's retention/access policies.
