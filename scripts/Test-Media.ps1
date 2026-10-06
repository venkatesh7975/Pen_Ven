[CmdletBinding()]
param([switch]$Live)
$ErrorActionPreference = 'Stop'
$dotnetPath = & (Join-Path $PSScriptRoot 'Initialize-Environment.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    & $dotnetPath build 'tests\ScreenInk.Media.Tests\ScreenInk.Media.Tests.csproj' --configuration Debug "-p:RestoreConfigFile=$projectRoot\NuGet.Config" --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Media test build failed.' }
    $testArgs = @()
    if ($Live) { $testArgs += '--live' }
    & $dotnetPath 'tests\ScreenInk.Media.Tests\bin\Debug\net10.0-windows10.0.26100.0\win-x64\ScreenInk.Media.Tests.dll' @testArgs
    if ($LASTEXITCODE -ne 0) { throw 'Media checks failed.' }
} finally { Pop-Location }
