# Build ETL Test Studio

You are the senior .NET engineer responsible for implementing an internal ETL testing product for a QA automation team. Build a working application, shared engine and CLI, not just a plan or prototype. Work incrementally, keep the solution buildable, and implement the first end-to-end slice before expanding the catalog.

## 1. Product decisions already made

- Windows desktop application using .NET WPF.
- Shared .NET engine usable independently by the desktop application and a CLI for CI pipelines. No WPF dependency in the engine.
- Microsoft SQL Server is the primary data source. Multiple source/target table pairs in the SAME database are a core use case. Do not assume separate servers or databases are required.
- Core capabilities: data mismatch comparison, incremental load validation, multi-table suites, configurable validation templates, detailed reports and reusable presets.
- JSON is the canonical suite format, validated against a versioned JSON Schema. WPF is the primary editing experience. Include an advanced JSON editor. YAML support is deferred.
- Local-first operation on the internal network. No cloud service, account signup, AI API or central server is required.
- Use the attached design images as the visual baseline. Product name: ETL Test Studio. Keep branding centralized so it can be renamed later.

## 2. Execution instructions

Inspect the repository, repository instructions, installed SDKs and existing code first. Preserve unrelated work. If the repository is empty, scaffold the solution. Select a currently supported stable .NET LTS release approved by the environment; document the choice and pin the SDK. Check official documentation when choosing current packages. Avoid preview dependencies and commercial controls requiring unapproved licenses.

Make routine decisions yourself and record assumptions. Ask only when a missing decision prevents correct implementation. Do not wait for production credentials: build a deterministic demo mode and SQL Server sample dataset. Demo data must be clearly labeled and must never appear as a real database run.

Maintain docs/implementation-status.md with milestones, working capabilities, known limitations and next actions. Deliver source, tests, sample suites, run instructions, deployment instructions and a concise verification report. If Windows or SQL Server is unavailable, implement what can be verified, provide commands for the remaining checks and state they were not run. Never claim screenshots, builds or database checks that did not happen.

## 3. Visual reference files

Inspect every image before building the UI:

| File | Screen |
| --- | --- |
| design-reference/01-dashboard.png | Execution overview, suite metrics, recent runs, failures |
| design-reference/02-suite-designer.png | Multi-test suite, source/target mapping, keys and comparison rules |
| design-reference/03-incremental-validation.png | Baseline, change window, expected changes, review |
| design-reference/04-mismatch-report.png | Run summary, filters, mismatches and selected record details |
| design-reference/05-connection-manager.png | Saved connections, authentication, TLS and connection test |
| design-reference/06-sql-assertion-editor.png | SQL editor, typed parameters, assertion and preview |
| design-reference/07-execution-monitor.png | Live statuses, progress, scanned records and logs |
| design-reference/08-cli-pipeline-configuration.png | Runner inputs, command preview, outputs and exit codes |
| design-reference/09-validation-templates.png | Check catalog, multiple selection, configuration and presets |

These are image-generation mockups, not exact functional contracts. Preserve their layout, hierarchy, colors and workflow; correct inconsistent labels, counts, timestamps and SQL details. In particular: incremental validation observes expected ETL changes; it does not silently perform the ETL mutations. Render command previews correctly for the selected shell rather than copying continuation characters from a mockup.

## 4. WPF design system and navigation

Use a Fluent-inspired Windows desktop interface: Segoe UI, dark navy left navigation, light gray workspace, white panels, subtle borders, blue primary buttons, teal/green success, amber warnings and red errors. Suggested centralized theme tokens: sidebar #102842, background #F4F7FB, primary #0067D9, success #008C72, warning #B96A00, error #C42B1C. Treat these as starting values to match the PNGs. Use consistent spacing, restrained corner radii and compact readable grids; do not turn the application into a marketing website.

Persistent navigation: Overview, Test Suites, Connections, Run History, Reports, Settings. Use clear breadcrumbs, environment selection and a bottom status bar. Connection status must reflect observed state and last verification rather than always saying connected. Support keyboard navigation, meaningful labels, resizable panes, virtualized grids, text plus color status cues, 100–200% Windows scaling and a usable 1280×800 layout. Long operations must not block the UI thread. Include loading, empty, error, cancelled and partial-results states.

## 5. Solution boundaries

Suggested solution structure; refine names if necessary while preserving responsibilities:

```text
EtlTesting.sln
src/
  EtlTesting.Core/          # contracts, definitions, rules, execution, results
  EtlTesting.SqlServer/     # SqlClient, metadata, SQL generation, streamed reads
  EtlTesting.Reporting/     # HTML, CSV, JSON, JUnit XML
  EtlTesting.Persistence/   # local history, baseline storage, checkpoints
  EtlTesting.Cli/           # non-interactive host
  EtlTesting.Desktop/       # WPF MVVM host
tests/
  EtlTesting.Core.Tests/
  EtlTesting.SqlServer.IntegrationTests/
  EtlTesting.Cli.Tests/
schemas/
samples/
  suites/
  environments/
  sql/
docs/
design-reference/
```

Use MVVM, dependency injection, async database APIs, CancellationToken and structured logging. Prefer Microsoft.Data.SqlClient for SQL Server. Store money using decimal, not floating point. The engine must not depend on WPF, console output, interactive dialogs or desktop-local credentials.

Define focused contracts such as ISuiteRunner, IValidationHandler, IDataConnector, IComparisonService, IBaselineStore, ICheckpointStore, IResultStore and IReportWriter. Use a registry of check types rather than a separate execution path for every screen. Publish structured progress/log events. Use bounded concurrency and stable deterministic output ordering. Share request models, validation, execution and result semantics between CLI and WPF.

## 6. Suite and environment contract

Create JSON Schema v1 plus examples and typed .NET models together. Define exact semantics for every option; reject unknown test types and invalid combinations. Show errors with the test ID and property path. Stable IDs are required. Preserve supported advanced settings when editing via WPF; never silently drop them. If definitions omit columns, either resolve explicit mappings before saving or clearly require an explicit compare-all mode; never infer changing semantics invisibly.

A suite contains schemaVersion, id, name, description, tags, defaults, typed parameter declarations and tests. Each test contains id, name, type, enabled, severity, connection references, scope, check-specific configuration and evidence settings. Define execution options separately from the business validation rule. Environment files resolve logical connections. Credentials are referenced, never embedded.

Starter shape (extend through a documented schema; this is not the complete catalog):

```json
{
  "schemaVersion": "1.0",
  "id": "reservation-etl",
  "name": "Reservation ETL validation",
  "defaults": { "connection": "warehouse", "timeoutSeconds": 120 },
  "parameters": { "batchId": { "type": "string", "required": true } },
  "tests": [
    {
      "id": "reservation-values",
      "name": "Reservation source-to-target comparison",
      "type": "dataComparison",
      "source": {
        "schema": "staging", "table": "Reservation",
        "filter": "BatchId = @batchId"
      },
      "target": {
        "schema": "dbo", "table": "Reservation",
        "filter": "LoadBatchId = @batchId"
      },
      "keys": [
        { "source": "PropertyCode", "target": "PropertyCode" },
        { "source": "ReservationId", "target": "ReservationId" }
      ],
      "columns": [
        {
          "source": "GuestName", "target": "GuestName",
          "comparison": { "trim": true, "ignoreCase": false }
        },
        {
          "source": "NetAmount", "target": "RevenueAmount",
          "comparison": { "absoluteTolerance": 0.01 }
        }
      ],
      "checks": {
        "uniqueKeys": true,
        "missingTargetRows": true,
        "unexpectedTargetRows": true
      }
    },
    {
      "id": "payment-reference",
      "name": "Payments must reference reservations",
      "type": "sqlAssertion",
      "query": "SELECT p.PaymentId FROM dbo.Payment p LEFT JOIN dbo.Reservation r ON p.ReservationId = r.ReservationId WHERE p.LoadBatchId = @batchId AND r.ReservationId IS NULL",
      "expect": { "rowCount": 0 }
    }
  ]
}
```

Both source and target inherit the same logical connection above. Permit individual overrides for future cross-database cases. Parameters must be bound using SqlParameter, not string replacement. Table/schema/column identifiers require metadata validation and correct quoting; identifiers cannot be value parameters. Raw SQL and filter expressions are authored code: use database permissions to enforce access, not a claim that keyword scanning makes SQL safe.

Environment examples should demonstrate Windows integrated authentication and SQL authentication through environment-variable credential references, explicit TLS configuration and separate timeouts. Define parameter override precedence. Resolve file paths relative to the suite/config location predictably. Missing inputs cause a clear error before a run starts.

## 7. Validation templates and suite creation

Build the catalog shown in image 09. Each template has a stable type ID, category, description, required inputs, defaults, validation rules and a configuration form. Templates create normal suite tests; they do not remain a second execution format. Support searching, filtering, selecting multiple templates, configuring each, previewing scope/results, and adding configured checks to a suite. Show selected-but-incomplete checks; prevent final submission until required configuration is valid. Presets are reusable configurations with a version and editable copies.

Implement in stages, visibly marking deferred templates unavailable rather than pretending they work:

| Category | Checks to support |
| --- | --- |
| Data quality | Required/non-null values; empty/whitespace strings; allowed values; numeric/date range; length; freshness |
| Constraints | Single/composite-key uniqueness; null business keys; referential integrity/orphans; conditional required values; cross-column business relationships |
| Reconciliation | Row counts; missing/extra records; mapped column values; sum/count/min/max totals; grouped aggregate comparison |
| Transformations | Derived expected values; default values; lookup mapping; conversion/precision/truncation checks |
| Incremental | Inserts/updates; hard or soft deletes; unchanged rows; rerun idempotency; watermark boundaries; late arrivals; failed-batch recovery |
| Historical | SCD Type 2 current-row uniqueness; effective-date overlap; closing previous versions; correct new version |
| Schema | Column existence; type; length; precision/scale; nullability |
| Advanced SQL | Query-to-query comparison; zero-violation-row assertions; scalar expectations |

Do not assume declared primary/foreign-key constraints exist or are enabled. These templates validate data; they do not alter database constraints.

Common settings: connection, table/query, selected columns, keys, filter, typed parameters, severity, threshold and evidence limit. Define whether a threshold is row-based, group-based or percentage-based, including its denominator. Evidence sampling never limits validation scope. Data quality checks should be batch-applicable or full-table-applicable as configured.

## 8. Comparison correctness

Return distinct categories: missing target record, unexpected target record, field mismatch, duplicate key, null-key violation, transformation mismatch and unexpected incremental change. Capture table/test, business key, source/target columns, expected/actual typed values, rule and reason.

Validate key uniqueness first. Never join duplicate keys into a misleading many-to-many result. For ambiguous groups, report duplicate violations and exclude or stop ambiguous matching according to documented policy. Never arbitrarily choose the first row.

Use null-aware comparisons. Null equals null by default; null and empty string differ unless explicitly normalized. Configure trimming and case sensitivity. Define collation behavior, including string keys, so database-side and engine-side comparisons agree. Configure decimal tolerances, datetime precision/timezone policy and ignored columns explicitly. Do not coerce dates or decimals through culture-dependent strings. SQL Server datetime values without timezone information must not be silently treated as UTC. Report incompatible types clearly.

Record counts are not proof of equal data. EXCEPT alone loses duplicate multiplicity. Hash/checksum equality alone must not be presented as exact equality. Do not use NOLOCK to hide blocking and risk inconsistent evidence.

Prefer server-side reconciliation for same-database comparisons when semantics permit it. For cross-connection comparisons use bounded streaming/chunking and a documented keyed strategy. Avoid loading whole tables into DataTables or WPF collections. Use stable partition/key boundaries, support cancellation and page stored mismatch evidence. Record the strategy and scope in results. Never silently sample and call it a full comparison.

## 9. Incremental validation contract

Support validation-only operation after externally executed ETL, and a later controlled lifecycle: baseline capture → controlled fixture changes → ETL trigger/wait → post-load validation. First-release validation must not require automatic job triggering.

For each incremental test define: business key; change tracking mode; lower and upper boundaries; comparison scope; batch marker if used; baseline reference; expected inserts, updates and deletes; unchanged-record policy; checkpoint storage. Use an explicit half-open timestamp window [lower, upper) by default, with typed persisted boundaries. Capture/freeze the upper boundary before execution. Timestamp columns alone may miss concurrent or late commits: document the upstream contract and support overlap/dedup or CDC/sequence strategies as later capabilities rather than claiming timestamp-only completeness.

Expected changes must come from explicit expected-change data, a trusted source delta, or before/after snapshots under a defined contract. Do not infer a hard delete from ModifiedDate alone: require tombstones, CDC, an explicit delete list or sufficient before/after scope. Soft-delete rules configure the inactive field/value. Idempotency requires comparing relevant target states across repeated ETL execution, not merely equal final row counts.

A local baseline is a tool snapshot, not a requirement to create a SQL Server database snapshot. Store typed data and manifest metadata sufficient to reproduce interpretation: schema/format version, key/column mappings, normalized scope, environment identity, capture times and test definition hash. Reference it by a stable ID/path. Reject incompatible or wrong-environment baselines. Capture source/target under an agreed consistent boundary; separate transactions on different databases do not guarantee a shared snapshot.

Checkpoint updates must be atomic and concurrency protected. Define which required validations gate advancement. Failed, errored, incomplete or cancelled validations cannot advance the successful checkpoint. Preserve attempted windows and all outcomes. Do not update the ETL system's own watermark unless a later explicit integration requires it.

Show affected-record checks AND checks that out-of-scope/unchanged records remain unchanged. Historical validation is a distinct configured mode, not ordinary current-row matching.

## 10. Screen behaviors

1. Overview: real saved suites, recent executions and failure summaries. Metrics must reconcile and identify their run/scope. Demo mode has a persistent demo badge.
2. Suite designer: test list; source/target table or query selection; composite keys; column mappings; comparison options; parameter editing; validate/save/run; JSON tab. Permit many tests sharing one database and connection.
3. Incremental wizard: Baseline, Change window, Expected changes, Review. Show required inputs, scope preview and checkpoint policy. Expected behavior descriptions say what the ETL should do, not that validation writes target rows.
4. Report: summary, mismatches, logs; table/category/key filters; detail panel with expected/actual values, rule and provenance; export; rerun failed checks against explicitly confirmed scope/baseline. Reruns create a new run linked to the original.
5. Connections: saved logical profiles, authentication, TLS, timeouts, test connection, accessible metadata. Schema/table access should handle partial permissions gracefully. No plaintext credential export.
6. SQL assertions: syntax-highlighted SQL, typed parameter grid, expected row count/scalar control, preview, JSON tab. Preview result display can be capped but must say so. Avoid executing the same expensive query twice merely to collect evidence/counts.
7. Monitor: queued/running/passed/failed/error/skipped/cancelled states, completed/total progress, elapsed time, records scanned and logs. Do not invent percentages when total work is unknown. Partial results remain explicitly incomplete.
8. CLI configuration: suite/environment/runtime inputs, shell-correct copyable command, report formats, exit code explanation and working GitHub Actions/Azure DevOps examples.
9. Template catalog: categorized list, multiple selection, active rule form, configuration-complete indicators, preview, presets, add-to-suite. Include the selected incomplete state in the reference.

## 11. Reporting and persistence

One canonical run result drives all reporters. Store run ID, suite ID/version/hash, environment, start/end/duration, runtime scope, batch/boundaries, baseline references, per-test status, severity, counts, errors, evidence paths and sampling metadata. Never store secrets in reports or logs.

Distinguish mismatched records from field mismatches: one row with five differing columns is one mismatched record and five field mismatches. Report counts per test and table. Do not sum into a suite-wide unique-record count unless cross-test identity/deduplication is defined; label sums as per-test totals. Key violations and rejected/ambiguous records have separate counts.

Produce machine-readable JSON, standalone HTML, mismatch CSV and JUnit XML. HTML must be readable offline with no CDN dependency, escaped values, summary and drill-down or links to evidence. Mark truncation/sampling clearly. CSV must handle quoting, Unicode and spreadsheet-formula injection safely. JUnit maps data assertion failures to failures and runtime errors to errors. Document warning severity and exit-code behavior consistently. PDF and Word are later options, not first-release requirements.

SQLite is suitable for local history/settings. Versioned suite JSON belongs in files suitable for Git. Baseline/checkpoint and artifact paths are explicit, injectable and usable by CI. Do not place SQLite on a shared network folder as a multi-user database. Use atomic file saves, schema migrations and configurable retention. Separate application history from portable execution artifacts.

## 12. CLI contract and CI

Implement at least validate, run and list-checks commands; add baseline capture and report rendering as incremental capabilities require. CLI must run without WPF installation, desktop state or interactive prompts. Support suite/environment selection, typed --parameter name=value overrides, output path, report formats, timeouts, cancellation and tag/test selection. Define whether skipped selections affect checkpoint eligibility; partial runs cannot silently advance a full-suite checkpoint.

Example, PowerShell syntax:

```powershell
etltest run `
  --suite samples/suites/reservations.json `
  --environment samples/environments/qa.json `
  --parameter batchId=B-20261024 `
  --output artifacts/etl-results `
  --formats html,junit,csv,json
```

Exit codes: 0 all required validations passed, 1 validation failures, 2 configuration/execution error, 3 cancelled. Define deterministic precedence when a run contains multiple outcome types; runtime error takes precedence over ordinary validation failure, and cancellation remains explicit. Artifact generation failure must not report success. Preserve partial diagnostic artifacts where possible.

Provide working GitHub Actions and Azure DevOps examples for an appropriate self-hosted runner with network access to SQL Server. Publish JUnit results and report artifacts even on failed validations. Credentials come from runner identity or pipeline secrets. CLI and desktop given the same suite, parameters, baseline and data must produce equivalent validation outcomes.

## 13. Permissions and distribution

Validation defaults to read-only database permissions. Test data setup or job-trigger actions are separate, explicit configured lifecycle steps using appropriately scoped connections. Credential references resolve through an abstraction: Windows credential storage for desktop, environment/pipeline secret references for CLI. Keep secrets out of suite JSON, command previews, telemetry and exceptions. Use TLS with certificate validation by default; unsupported enterprise certificate configuration must produce actionable errors, not a silent bypass.

Package desktop and CLI separately. Provide self-contained Windows x64 desktop publishing instructions without requiring users to install the SDK. Check native dependencies and baseline/history paths. Keep signing and installer updates documented for a later enterprise release. A heavy installer/updater is not a prerequisite for the first usable version.

## 14. Delivery milestones and acceptance

### Milestone 1: working comparison slice

Build shared contracts, versioned JSON and validation, SQL Server connection/metadata, key-based exact comparison with null and duplicate handling, SQL assertions, CLI exit codes and JSON/HTML/CSV/JUnit reports. Scaffold the WPF shell matching the reference and connect suite loading, run invocation and results to the real engine. Include a same-database sample with at least three mapped table pairs. It must execute end to end; fake button handlers do not count.

### Milestone 2: productive desktop and templates

Implement connection manager, suite designer, template catalog/forms, JSON editing, mapping rules, live monitor, mismatch drill-down, presets and persisted run history. Add required/null, uniqueness, referential, allowed-values, range and aggregate checks. Every enabled template must create a valid executable test.

### Milestone 3: incremental workflows

Implement compatible baseline capture/storage, expected deltas, bounded windows, insert/update/delete/unchanged validation, idempotency fixtures and atomic checkpoint gating. Add wizard and explicit validation-only mode. Support tests of boundary records, late arrivals under documented policy, failed reruns and incompatible baselines.

### Milestone 4: advanced coverage and production hardening

Add remaining catalog types, SCD checks, schema comparison, optional ETL triggers, query-to-query comparison, scale optimizations, CI configuration generation, distribution and accessibility refinements. Do not advertise unfinished capabilities as functional.

Create deterministic SQL Server sample schemas/data with the following cases: matching rows; equal counts but different keys; missing/extra rows; multiple mismatching columns on one record; composite duplicate keys; null key/value cases; valid and invalid foreign references; decimal tolerance pass/fail; case/whitespace behavior; date precision; incremental insert/update/soft delete; explicit hard-delete evidence; unchanged-row unexpected mutation; window boundary; rerun duplicate; failed checkpoint advance; ambiguous duplicate matching. Keep fixtures repeatable and isolated from real data.

Test the comparison algorithms, SQL generator/identifier handling, typed parameters, result counts, checkpoint gating/concurrency, cancellation, reports and CLI exit behavior. Use real SQL Server integration tests for semantics that an in-memory fake cannot verify. Verify HTML escaping, CSV handling, JUnit validity and UI loading/error states. For scale, publish measured behavior on a reproducible dataset; avoid unsupported throughput claims. A million-row benchmark can be a later opt-in check rather than a requirement for every test run.

On Windows, build and launch WPF and capture actual screen evidence against the reference images. On other platforms, build/test portable components and clearly document the remaining Windows visual verification. The final handoff must identify what works, what was actually tested, how to run it, what remains and any material limitations.

Begin now with repository inspection, a concise milestone checklist and the first working vertical slice. Continue implementation rather than returning only an architecture proposal.
