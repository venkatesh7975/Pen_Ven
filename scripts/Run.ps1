[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
if (-not $NoBuild) {
    & (Join-Path $PSScriptRoot 'Build.ps1') -Configuration $Configuration
}
$projectRoot = Split-Path -Parent $PSScriptRoot
$executablePath = Join-Path $projectRoot "src\ScreenInk.App\bin\x64\$Configuration\net10.0-windows10.0.26100.0\win-x64\ScreenInk.App.exe"
if (-not (Test-Path -LiteralPath $executablePath)) { throw "Build output was not found: $executablePath" }
# This is the interactive application the user is launching, not a background helper.
Start-Process -FilePath $executablePath -WorkingDirectory (Split-Path -Parent $executablePath) -WindowStyle Normal
