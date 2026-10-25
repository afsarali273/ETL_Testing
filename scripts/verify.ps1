param([switch]$LocalDb)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    dotnet build EtlTesting.sln
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    if ($LocalDb) {
        sqllocaldb start MSSQLLocalDB
        sqlcmd -S '(localdb)\MSSQLLocalDB' -E -b -i samples/sql/setup.sql
        if ($LASTEXITCODE -ne 0) { throw 'LocalDB fixture setup failed' }
        $env:ETL_TEST_LOCALDB = '1'
    }
    dotnet test EtlTesting.sln --no-build --logger 'trx;LogFileName=verification.trx' --results-directory artifacts/tests
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
} finally { Pop-Location }
