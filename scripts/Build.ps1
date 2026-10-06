[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$dotnetPath = & (Join-Path $PSScriptRoot 'Initialize-Environment.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot
$nugetConfigPath = Join-Path $projectRoot 'NuGet.Config'
Push-Location $projectRoot
try {
    & $dotnetPath build 'ScreenInk.sln' --configuration $Configuration -p:Platform=x64 "-p:RestoreConfigFile=$nugetConfigPath" --nologo
    if ($LASTEXITCODE -ne 0) { throw "ScreenInk $Configuration build failed (exit code $LASTEXITCODE)." }
} finally {
    Pop-Location
}
