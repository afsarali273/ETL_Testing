# Connections and SQL Workspace verification

The 0.2 change provides a structured Connections profile manager and a separate SQL Workspace navigation item with its own saved-profile selection. Database browsing and queries use saved settings rather than unsaved profile drafts.

The automated suite adds profile migration, multiple servers/environments, persistence/reload, unique logical-key checks, import verification reset, password-reference validation, stale-window write protection, external legacy-environment preservation, safe preview SQL, actual database discovery/catalog/column queries, bounded data previews, typed/multiple result sets, long-cell limits and cancellation checks.

Local SQL checks use the named LocalDB instance `EtlTestStudioChecks` and a fixture database whose files are in `artifacts/sql-fixture/`. This isolates the checks from the default LocalDB instance while other database setup work is taking place on this PC. Run with `ETL_TEST_LOCALDB=1` and `ETL_TEST_SQL_ENVIRONMENT` pointing to `artifacts/sql-fixture/environment.json`. TRX output is `artifacts/tests/connections-workspace.trx`.

The first UI startup check found a WPF resource-scope issue: the embedded view could not find the window-local PrimaryButton style. Shared resources now load at application scope through `Themes/StudioTheme.xaml`. No new UI-control package was required.

The final automated run passed 53 checks with none skipped, including a dummy Windows Credential Manager write/read/delete round trip and verification that the credential password is excluded from serialization. Actual SQL checks use the separate named fixture instance described above.

The published 0.2 app was installed under the current user's Programs folder and launched successfully. The Connections screen displayed the preserved ETLTeamDev profile with a successful connection test and six visible tables. The independent SQL Workspace navigation item displayed its own profile selector and the six-table explorer, a generated Customer query, a 100-row result grid and explicit display-limit messaging. Existing table metadata showed about 1,000 additional test rows created outside this UI change; this feature did not insert those rows. Captured actual screens are `screenshots/connections-0.2.png` and `screenshots/sql-workspace-0.2.png`. Desktop/Start menu shortcuts now target 0.2, and the app is left on SQL Workspace.

Enterprise SQL credentials/TLS and remote servers require their actual environments and were not verified here. Query sessions are intentionally per execution; large data display is capped and explicitly labeled. This is a database browser/query workspace, not a complete implementation of every SSMS administration feature.
