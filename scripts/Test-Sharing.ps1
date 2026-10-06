[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$dotnetPath = & (Join-Path $PSScriptRoot 'Initialize-Environment.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot
$nugetConfigPath = Join-Path $projectRoot 'NuGet.Config'
Push-Location $projectRoot
try {
    & $dotnetPath build 'tests\ScreenInk.Sharing.Tests\ScreenInk.Sharing.Tests.csproj' --configuration Debug "-p:RestoreConfigFile=$nugetConfigPath" --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Screenshot/sharing test build failed.' }
    & $dotnetPath 'tests\ScreenInk.Sharing.Tests\bin\Debug\net10.0-windows10.0.26100.0\win-x64\ScreenInk.Sharing.Tests.dll'
    if ($LASTEXITCODE -ne 0) { throw 'Screenshot/sharing checks failed.' }
} finally { Pop-Location }
