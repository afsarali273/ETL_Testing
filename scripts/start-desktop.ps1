$ErrorActionPreference = 'Stop'
dotnet run --project (Join-Path $PSScriptRoot '../src/EtlTesting.Desktop')
