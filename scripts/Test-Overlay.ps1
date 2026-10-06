[CmdletBinding()]
param([switch]$RenderingOnly)

$ErrorActionPreference = 'Stop'
$dotnetPath = & (Join-Path $PSScriptRoot 'Initialize-Environment.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot
$nugetConfigPath = Join-Path $projectRoot 'NuGet.Config'
Push-Location $projectRoot
try {
    & $dotnetPath build 'tests\ScreenInk.Overlay.Tests\ScreenInk.Overlay.Tests.csproj' --configuration Debug "-p:RestoreConfigFile=$nugetConfigPath" --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Native overlay integration test build failed.' }
    $testArgs = @()
    if ($RenderingOnly) { $testArgs += '--render-only' }
    & $dotnetPath 'tests\ScreenInk.Overlay.Tests\bin\Debug\net10.0-windows10.0.26100.0\win-x64\ScreenInk.Overlay.Tests.dll' @testArgs
    if ($LASTEXITCODE -ne 0) { throw 'Native overlay integration checks failed.' }
} finally {
    Pop-Location
}
