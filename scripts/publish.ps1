param([string]$Output = (Join-Path $PSScriptRoot '../artifacts/distribution'))
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force -Path $Output | Out-Null
foreach ($application in @('Desktop','Cli')) {
    dotnet publish (Join-Path $repository "src/EtlTesting.$application") -c Release -r win-x64 --self-contained true -p:RuntimeFrameworkVersion=10.0.13 -o (Join-Path $Output $application)
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $application" }
}
Copy-Item -LiteralPath (Join-Path $repository 'README.md') -Destination $Output
Copy-Item -LiteralPath (Join-Path $repository 'docs') -Destination $Output -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repository 'samples') -Destination (Join-Path $Output 'Cli') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repository 'schemas') -Destination (Join-Path $Output 'Cli') -Recurse -Force
Compress-Archive -Path (Join-Path $Output '*') -DestinationPath ($Output + '.zip') -Force
Write-Output "Team package: $Output.zip"
