# Connections and SQL Workspace — 0.2

Connections and SQL Workspace are separate items in the main navigation.

## Manage profiles in Connections

The searchable profile library supports friendly names, environments, stable logical connection keys, multiple servers/instances, optional TCP ports, databases, authentication, TLS and timeouts. Create, edit, save, duplicate, revert, remove, import or export profiles through structured fields. Draft edits stay in this session when switching profiles; saved settings survive restarts. Removing a profile does not delete its database. Select another active suite profile before removing the current active one.

Windows authentication uses the current Windows identity. SQL authentication supports Windows Credential Manager or environment-variable references. Passwords entered in the form stay transient until Save; Test can use them before saving. Save writes to the Windows vault and clears the password box. JSON files/exports contain credential references, never password values. Duplicated vault profiles require their own password. Vault entries are retained on profile removal because an exported environment may still reference them.

Discover lists accessible databases on a server; database names can also be typed. Test connection verifies connectivity and metadata, showing server version, elapsed connection/metadata time and last checked timestamp. Editing settings resets verification; imported profiles start unverified. Visible tables do not imply SELECT permission on every table.

Use for suite selects an environment and updates the suite's default connection key without executing it. Profiles in one environment must have unique logical keys. Save profiles such as source and warehouse in the same environment to compare databases on different servers. A key may be reused across different environments.

Profiles live in `%LOCALAPPDATA%/EtlTestStudio/connections/profiles-v1.json`. The legacy `qa.json` environment migrates automatically, preserving all logical connections. The selected environment is also written to `qa.json` for CLI compatibility. Atomic saves, a writer lock and stale-file detection prevent another window from overwriting newer profiles. Reopen the app if another window changed the library.

## Query from SQL Workspace

SQL Workspace has its own saved-profile selector and does not change the active suite profile. It uses saved settings, so unsaved connection edits cannot silently change a query's destination.

Choose a profile and Load tables. The explorer lists visible user tables/views with search by name, schema and type. Selecting an object loads Table data and Columns. Column details include SQL type/length/precision, nullability and primary-key membership. Object row counts are estimates. Preview pages show up to 200 rows; primary-key ordering is used when available. Without a primary key, or during concurrent writes, page contents may shift or overlap.

Open in SQL editor generates a SELECT. F5 or Ctrl+Enter executes SQL. The workspace supports opening/saving `.sql` files, typed result grids, multiple result sets, messages, errors, elapsed time and cancellation. Ctrl+C copies selected cells with headers. SQL dates do not acquire an invented timezone; NULL displays explicitly. Each execution opens a fresh connection: temp tables and transactions do not span Execute actions. Put a complete transaction in one script. Use SQL DECLARE for script variables; suite assertion parameters remain in the suite editor.

SQL runs with the profile's database permissions. Use a read-only identity when only read access is intended. Keyword scanning is not treated as a security boundary. Query errors/messages and rows are held in memory rather than saved in profiles. Save .sql writes the editor text without appending connection credentials.

Result display limits: 1–1,000 rows per result (default 200), ten result sets, 200 columns per result, 50,000 total cells and four million text characters per batch. Text cells stop at 4,096 characters and binary previews at 64 bytes, with indicators. A display cap requests cancellation; other statements may already have executed on the server. These are display limits, not guarantees about server execution or validation scope. Cancelled results remain visibly partial. Query timeout is configurable from 1–600 seconds.

## Distribution and verification

Run `scripts/publish.ps1`, then `scripts/install-local.ps1 -Launch` for a user installation under `%LOCALAPPDATA%/Programs/EtlTestStudio/0.2`. Desktop and Start menu shortcuts point to this version. A SQL Workspace Start menu shortcut opens that navigation item directly. Existing suites/history/profiles use the same application-data folder; 0.1 program files are retained.

SQL checks use a dedicated `EtlTestStudioChecks` LocalDB instance and an isolated database with fictional rows. Set `ETL_TEST_SQL_ENVIRONMENT` to an environment JSON file to opt SQL integration tests into a separate fixture instead of the default LocalDB sample.
