# Implementation status — 0.1 team preview

## Milestone 1 — working comparison slice

- [x] Seven-project .NET solution, independent engine and CLI, WPF MVVM host.
- [x] Pinned .NET 10 LTS SDK, stable packages and dependency lock files.
- [x] JSON Schema v1 plus semantic validation, typed parameters and stable IDs.
- [x] SQL Server logical connections, integrated/SQL auth references, TLS, metadata validation and correctly quoted identifiers.
- [x] Exact mapped comparisons with streamed reads and a disk index, composite keys, duplicate/null keys, missing/extra records, string normalization and decimal/date tolerances.
- [x] SQL assertions executed once per check, bounded evidence, row-count and typed scalar expectations.
- [x] Canonical results, structured progress, explicit demo provenance, cancellation and deterministic CLI exits.
- [x] Offline HTML, JSON, safe CSV and JUnit XML.
- [x] Desktop suite loading/JSON editing, engine invocation, results and actual screen evidence.
- [x] Same-database fixture with three mapped table pairs; portable, CLI-process and real LocalDB tests.

## Milestone 2 — partially implemented

- [x] Source/target forms and editable key/column mapping grids with comparison options.
- [x] Structured multi-profile connection manager with multiple servers/databases, environments, save/load/import/export, database discovery, connection health and Windows Credential Manager.
- [x] Separate SQL Workspace navigation with an independent saved-profile picker, table/view explorer, column metadata, paged data previews and SQL execution/results/cancellation.
- [x] Comparison and SQL assertion templates; versioned single-test presets as ordinary suites.
- [x] SQL recipes for required values, whitespace, uniqueness, allowed values, ranges, null business keys and aggregates.
- [x] Progress/log monitor, persisted history, searchable paged mismatch evidence and rule/provenance details.
- [ ] Multi-select validation catalog, full SQL syntax highlighting/autocomplete and remaining catalog coverage.
- [ ] Form dirty-state prompts/auto-apply, suite library, deletion/reordering and dedicated rerun-failed lineage flow.

## Milestone 3 — deferred, unavailable

- [ ] Typed baselines/compatibility manifests, expected deltas, half-open windows, insert/update/delete/unchanged validation, wizard, idempotency and atomic checkpoint gates.
- No baseline capture, incremental mutation, ETL triggering or checkpoint advancement occurs in v0.1. Incremental fixtures and checkpoint tests are not claimed.

## Milestone 4 — partially prepared

- [x] Separate self-contained Windows x64 desktop/CLI publish script and team ZIP.
- [x] Self-hosted GitHub Actions/Azure DevOps examples with report publication after failure.
- [ ] SCD/schema/transformation catalog, server-side optimizations, ETL triggers, signing/installer/updater, full accessibility/scaling audit and measured scale benchmark.

## Assumptions and limits

All nine supplied PNGs were inspected. Their navy sidebar, compact grids, light workspace, white panels and blue actions guide the actual WPF interface. Unavailable features are labeled. SDK 10.0.102 is installed and pinned; distribution carries .NET 10.0.13. Package audit reports no known vulnerabilities with the configured feeds at verification time.

Real SQL tests use the existing SQL Server 2025 LocalDB RC1 instance. Production versions, enterprise TLS/SQL authentication, clean-machine deployment, full DPI/screen-reader coverage and million-row performance remain unverified. No team credentials were needed.

Reads are separate read-committed statements: run after ETL finishes against a stable scope. Server-side reconciliation and consistent snapshots are future work. History uses atomic JSON files with manual retention; the UI loads the 200 most recent files. Disk space is required for both comparison datasets.

See verification.md for actual checks and deployment.md for distribution and connection setup.
