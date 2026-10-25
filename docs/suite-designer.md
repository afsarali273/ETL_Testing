# Focused suite designer

The Test Suites screen is a compact overview of the suite's validations. Search by name, table, type or stable ID; filter by type and enabled state. Each row has Edit, Copy, Run and Delete actions. The first checkbox selects rows for bulk actions; the On checkbox changes whether a validation participates in suite runs.

## Create and edit

Choose **Add validation**, search/filter the catalog, and select **Configure validation**. Only that rule's inputs appear in its focused editor. A new rule remains a draft until **Save validation** validates and adds it to the suite. Save the suite file separately to persist its JSON. Existing definitions reopen in the same form, with their IDs and supported settings preserved. Copy creates a new stable ID and retains the rule configuration.

Available forms: mapped data comparison, required values, unique business keys, allowed values, numeric/date/length ranges, referential integrity, row counts, aggregate reconciliation and custom SQL assertions. Incremental, historical/SCD and schema forms are displayed as planned and cannot be submitted.

**Load tables & columns** uses saved connection profiles and actual SQL metadata. You can also enter exact schema/table/column names when metadata access is limited. Mapping editors support composite keys and mapped columns. Comparison sides can override the logical connection to use different servers. The SQL quality/reconciliation templates use one connection for their scopes; choose mapped comparison for comparisons across servers.

## Template settings

- Required values: selected columns, null rejection, optional empty/whitespace rejection. Whitespace includes spaces, tabs, CR/LF, vertical tab, form feed and non-breaking spaces. A row failing several columns is returned once.
- Unique keys: selected single/composite key, null key policy. Violations are key groups rather than individual rows.
- Allowed values: a typed list, optional allowed empty string, null policy. Values are SqlParameter bindings, never SQL string substitution. SQL equality follows the database collation.
- Numeric/date/length ranges: lower/upper boundaries, inclusive/exclusive settings and null policy. Dates use `yyyy-MM-ddTHH:mm:ss[.fffffff]` without timezone conversion. Character length includes trailing spaces and follows SQL collation character counting.
- Referential integrity: child/parent scopes, key mappings and null policy. Checks actual data using NOT EXISTS without relying on declared constraints.
- Row count reconciliation: both scopes and absolute count tolerance. Passing this check does not prove that values or business keys match.
- Aggregate reconciliation: sum/count/min/max, numeric measure columns, optional mapped grouping keys and absolute tolerance. Groups missing from either side fail. SQL aggregation uses SQL Server's normal null semantics.
- Custom SQL: suite parameter declarations, zero-violation row expectation, exact row count or a typed scalar. Scalar mode requires one row and one column.

Execution settings include enabled state, severity, evidence cap, optional per-test timeout (suite default if empty), tags and allowed violating rows/groups for SQL templates. The threshold uses the whole returned violation set, not the sampled evidence. Runtime timeout precedence is CLI/run override, test override, then suite default. Evidence caps do not reduce validation scope. Generated rules execute through the shared SQL assertion handler in both hosts.

## Preview and run

**Preview scope** displays up to 50 source/target rows in bounded grids; it is explicitly a display sample. **Preview validation** runs the current draft, including its parameter declarations, through the real engine without adding it to saved history or modifying the suite. Stored evidence displayed in the preview is capped separately. Cancellation retains the draft. SQL templates require SQL mode for their preview; demo SQL is a fixed synthetic fixture and does not interpret arbitrary queries.

**Show SQL** displays the generated SQL with parameter placeholders. Custom SQL runs with the selected connection's actual database permissions. Use a read-only identity when validation must be read-only; the application does not claim that keyword filtering can enforce permissions.

The row's **Run** action executes that validation only and creates a normal result marked as a partial suite selection. Other tests are skipped. Run suite rejects unsaved validation edits; use Preview validation or save the draft first.

## Delete and recover

Delete and bulk delete remove definitions from the in-memory suite. **Undo delete** restores their IDs, configuration and order. You can remove the last validation and save an empty suite; execution rejects an empty suite. Deletion Undo is scoped to the current suite and session. Navigation away from dirty form or JSON edits requests Save/Discard handling rather than silently losing changes.

The advanced **Suite JSON** screen remains available for portable editing. SQL template configuration is stored in the optional `template` property of a normal `sqlAssertion` test. The engine regenerates SQL from that configuration at execution, so changed configuration never executes stale generated SQL. The optional generated `query` is for inspection. Supported advanced options remain present when the form saves a rule.

`samples/suites/data-quality.json` contains all nine SQL template types against the isolated reservation/payment fixture. The `SuiteDesignerPreview` build configuration, or `--suite-designer-preview`, opens a separately titled designer preview with state under the executable's `preview-data/` folder. It does not use the installed app's saved suite/history files.
