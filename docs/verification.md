# Verification report

## Environment

Windows x64, .NET SDK 10.0.102 and development runtime 10.0.2. Existing SQL Server LocalDB reports SQL Server 2025 RC1 17.0.925.4. This is an isolated local fixture, not a claim about production versions. Self-contained distribution uses .NETCore/WindowsDesktop runtime 10.0.13.

## Executed checks

- Solution build succeeds with warnings treated as errors.
- 26 automated tests pass, including three opted-in real LocalDB tests and four CLI process tests. None skipped in the opted-in run. TRX: `artifacts/tests/verification.trx`.
- Unit tests cover equal counts/different keys, multiple field differences per record, duplicate exclusion, null keys/values, evidence caps independent of scope, decimal/date tolerances, ordinal strings, cancellation, error precedence, warning semantics, partial selections, strict schema/options, parameter conversion and identifier quoting.
- Real SQL tests cover three mapped pairs, composite duplicates, nulls, decimal tolerance, orphan references, typed scalar reads, metadata failures and injection-shaped values bound as data.
- Reporter tests cover HTML escaping, CSV formula protection/quoting, JUnit failure/error/warning semantics and atomic history round-trip.
- CLI tests cover validate/list-checks, bad inputs, explicit real/demo choice, demo exit 1 with four report formats and artifact failure exit 2.
- Real CLI fixture: one passed / three failed, exit 1, artifacts in `artifacts/localdb/`. Reservation comparison has 7 source / 7 target rows; 1 mismatched matched record / 3 fields; 1 missing key, 1 extra key, 1 duplicate group and 2 null-key rows. Customer passes; payment amount and one orphan reference fail.
- Demo CLI: four intentionally failed synthetic checks, explicit demo markers and four report formats in `artifacts/demo/`.
- WPF was built and launched. An initial progress-bar binding startup error was corrected to one-way. The relaunched application ran the demo through the actual Run suite button, reached Reports and saved history/artifacts. Actual evidence: `screenshots/desktop-demo-report.png`. Navigation, suite forms, connection editor and report view were inspected against the references. A failed connection state was observed and did not claim success.
- A persistent `ETLTeamDev` database was created in the current user's LocalDB instance using `samples/sql/setup-teamdev.sql`. It contains six tables in `staging` and `dbo`. The integrated profile was saved to `%LOCALAPPDATA%/EtlTestStudio/qa.json`; the app's live connection test found all six tables. With demo mode disabled, the installed desktop executed the suite against that SQL database. The CLI ran against the same database and resolved batch ID, table comparisons and SQL assertions. Its three expected fixture failures and one passing customer comparison were saved in `artifacts/ETLTeamDev-cli-setup/` and local app history/reports. Fixture rows are explicitly synthetic.
- An earlier SQL Server 2022 Express setup attempt reported canceled features. A later read-only inspection found `SQLEXPRESS` running version 16.0.1000.6 with only system databases visible at that inspection. These are environment observations; this UI change does not install or migrate that server.
- Dependency audit reports no known vulnerable packages. Restore initially detected vulnerable transitive PKCS 9.0.18; explicit stable 10.0.13 replaces it.

## Reproduce

```powershell
.\scripts\verify.ps1 -LocalDb
dotnet run --project src/EtlTesting.Cli -- run --suite samples/suites/reservations.json --demo --output artifacts/demo
dotnet run --project src/EtlTesting.Cli -- run --suite samples/suites/reservations.json --environment samples/environments/localdb.json --output artifacts/localdb
.\scripts\publish.ps1
```

Both sample runs intentionally return 1. Runtime errors return 2; cancellation returns 3. Without `-LocalDb`, the three integration tests are explicitly skipped.

## Not verified / not implemented

Enterprise SQL/TLS/SQL credentials, clean-machine installation, complete accessibility/DPI coverage and large-data performance remain unverified. Guided quality forms, baselines, checkpoints, SCD/schema checks, triggers and signed installers are deferred. Desktop execution was exercised in demo mode; real SQL execution was checked through the shared engine/CLI. No production-readiness or throughput claim is made.
