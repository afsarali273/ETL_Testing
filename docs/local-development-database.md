# Local ETL development database

`ETLTeamDev` is a persistent SQL Server database on this Windows account's existing LocalDB instance. It uses the reservation/customer/payment table pairs in the supplied project specification so the team can build and validate actual SQL queries before connecting a business system.

The rows inserted by `samples/sql/setup-teamdev.sql` are representative **synthetic test data**, never company or customer data. The database is real SQL Server; the sample rows are fictional. This repository does not yet contain your team's actual business data, production schemas, or credentials.

## Desktop and CLI connection

Use `samples/environments/local-teamdev.json`, logical connection `warehouse`, and runtime parameter `batchId=B-20261024`. The profile is Windows-integrated and only authenticates the current Windows user. Certificate trust is relaxed only for this local `(localdb)` development connection. Do not carry that setting to QA or production.

In ETL Test Studio, open **Connections**, import `samples/environments/local-teamdev.json`, save the profile, then turn off **Use deterministic demo fixtures** under **Settings**. Run the sample suite and results will identify environment `LOCAL TEAM DEVELOPMENT`. From PowerShell, run:

```powershell
& "$env:LOCALAPPDATA\Programs\EtlTestStudio\0.1\Cli\etltest.exe" run `
  --suite "$env:LOCALAPPDATA\EtlTestStudio\suites\reservations.json" `
  --environment "<repository>\samples\environments\local-teamdev.json" `
  --parameter batchId=B-20261024 `
  --output "$env:LOCALAPPDATA\EtlTestStudio\artifacts\teamdev" `
  --formats html,json,csv,junit
```

Run `sqllocaldb start MSSQLLocalDB` if the instance has stopped. Use `sqlcmd -S '(localdb)\MSSQLLocalDB' -E -d ETLTeamDev` to inspect it, or run `samples/sql/setup-teamdev.sql` to reset only its explicitly named sample batch. The setup is idempotent for that batch and leaves other batch IDs unchanged.

LocalDB is attached to one Windows account and is meant for local development. Other Windows users and computers cannot connect to this user's LocalDB instance. They can each create the same development database in their own LocalDB, or you can later move the suite to a shared SQL Server Express/Standard service or team SQL Server. Installing the full shared SQL Server service requires Windows administrator approval; creating this LocalDB database does not.
