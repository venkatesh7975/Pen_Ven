[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sdkVersion = (Get-Content -LiteralPath (Join-Path $projectRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$sdkRoot = Join-Path $projectRoot '.tools\dotnet'
$dotnetPath = Join-Path $sdkRoot 'dotnet.exe'

if (Test-Path -LiteralPath $dotnetPath) {
    $installedSdks = & $dotnetPath --list-sdks
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the existing local SDK.' }
    if ($installedSdks -match "^$([regex]::Escape($sdkVersion)) ") {
        Write-Host ".NET SDK $sdkVersion is already installed locally."
        return
    }
}

if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
    throw 'Phase 1 currently targets an x64 Windows development machine.'
}

Write-Host "Resolving .NET SDK $sdkVersion from Microsoft release metadata..."
$metadata = Invoke-RestMethod -Uri 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json' -TimeoutSec 60
$sdk = @($metadata.releases | ForEach-Object { $_.sdks; $_.sdk } | Where-Object { $_.version -eq $sdkVersion }) | Select-Object -First 1
if (-not $sdk) { throw "SDK $sdkVersion was not found in Microsoft's release metadata." }
$sdkFile = $sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
if (-not $sdkFile) { throw 'No Windows x64 SDK ZIP was found.' }
$downloadUri = [Uri]$sdkFile.url
if ($downloadUri.Scheme -ne 'https' -or $downloadUri.Host -notin @('builds.dotnet.microsoft.com', 'download.visualstudio.microsoft.com')) {
    throw "Unexpected SDK download host: $($downloadUri.Host)"
}

$downloadRoot = Join-Path $projectRoot '.tools\downloads'
New-Item -ItemType Directory -Path $downloadRoot, $sdkRoot -Force | Out-Null
$archivePath = Join-Path $downloadRoot "dotnet-sdk-$sdkVersion-win-x64.zip"
Write-Host 'Downloading the SDK into .tools/downloads...'
Invoke-WebRequest -Uri $sdkFile.url -OutFile $archivePath -TimeoutSec 600
$actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA512).Hash
if ($actualHash -ne $sdkFile.hash) { throw 'The SDK archive failed its SHA-512 integrity check.' }
Write-Host 'SHA-512 verified. Extracting the SDK...'
& tar.exe -xf $archivePath -C $sdkRoot
if ($LASTEXITCODE -ne 0) { throw 'SDK extraction failed.' }
Push-Location $projectRoot
try {
    $installedVersion = & $dotnetPath --version
    if ($LASTEXITCODE -ne 0 -or $installedVersion -ne $sdkVersion) { throw 'SDK version verification failed.' }
} finally {
    Pop-Location
}
Write-Host "Installed .NET SDK $sdkVersion locally. No machine-wide SDK installation was changed."
