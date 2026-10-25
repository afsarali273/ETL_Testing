param(
    [string]$PackageDirectory = (Join-Path $PSScriptRoot '../artifacts/distribution'),
    [switch]$Launch
)
$ErrorActionPreference = 'Stop'
$PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
$sourceExe = Join-Path $PackageDirectory 'Desktop/EtlTesting.Desktop.exe'
if (-not (Test-Path -LiteralPath $sourceExe)) { throw 'Publish first using scripts/publish.ps1.' }
$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs/EtlTestStudio/0.3.7'
$targetExe = Join-Path $installDirectory 'Desktop/EtlTesting.Desktop.exe'
$runningVersion = Get-Process -Name EtlTesting.Desktop -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $targetExe }
if ($runningVersion) { throw 'Close ETL Test Studio 0.3.7 before updating its program files.' }
New-Item -ItemType Directory -Force -Path $installDirectory | Out-Null
Get-ChildItem -LiteralPath $PackageDirectory -Force | Copy-Item -Destination $installDirectory -Recurse -Force
$desktopDirectory = [Environment]::GetFolderPath('Desktop')
$startMenuDirectory = Join-Path ([Environment]::GetFolderPath('Programs')) 'ETL Test Studio'
New-Item -ItemType Directory -Force -Path $startMenuDirectory | Out-Null
$shell = New-Object -ComObject WScript.Shell
foreach ($path in @((Join-Path $desktopDirectory 'ETL Test Studio.lnk'), (Join-Path $startMenuDirectory 'ETL Test Studio.lnk'))) {
    $shortcut = $shell.CreateShortcut($path)
    $shortcut.TargetPath = $targetExe
    $shortcut.WorkingDirectory = Split-Path $targetExe -Parent
    $shortcut.Description = 'ETL Test Studio - connections, SQL workspace and ETL validation'
    $shortcut.IconLocation = "$targetExe,0"
    $shortcut.Save()
}
$workspaceShortcut = $shell.CreateShortcut((Join-Path $startMenuDirectory 'SQL Workspace.lnk'))
$workspaceShortcut.TargetPath = $targetExe
$workspaceShortcut.Arguments = '--workspace'
$workspaceShortcut.WorkingDirectory = Split-Path $targetExe -Parent
$workspaceShortcut.IconLocation = "$targetExe,0"
$workspaceShortcut.Save()
if ($Launch) { Start-Process -FilePath $targetExe -ArgumentList '--reports' -WorkingDirectory (Split-Path $targetExe -Parent) -WindowStyle Normal }
Write-Output "Installed ETL Test Studio 0.3.7: $installDirectory"



