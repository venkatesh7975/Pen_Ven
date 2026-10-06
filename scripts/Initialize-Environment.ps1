[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $projectRoot '.tools'
$sdkRoot = Join-Path $toolsRoot 'dotnet'
$dotnetPath = Join-Path $sdkRoot 'dotnet.exe'

if (-not (Test-Path -LiteralPath $dotnetPath)) {
    throw 'The project-local SDK is missing. Run scripts/Install-DotNet.ps1 first.'
}

$env:DOTNET_ROOT = $sdkRoot
$env:DOTNET_ROOT_X64 = $sdkRoot
$env:DOTNET_CLI_HOME = Join-Path $toolsRoot 'dotnet-home'
$env:NUGET_PACKAGES = Join-Path $toolsRoot 'nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:PATH = "$sdkRoot;$env:PATH"

$dotnetPath
