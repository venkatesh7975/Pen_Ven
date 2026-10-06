[CmdletBinding()]
param([switch]$NoBuild)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $NoBuild) {
    & (Join-Path $PSScriptRoot 'Build.ps1') -Configuration Release
}

$sourceRoot = Join-Path $projectRoot 'src\ScreenInk.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64'
$sourceExecutable = Join-Path $sourceRoot 'ScreenInk.App.exe'
if (-not (Test-Path -LiteralPath $sourceExecutable)) {
    throw 'The Release build is missing. Run scripts/Build.ps1 -Configuration Release first.'
}

$installRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\ScreenInk'
$installedExecutable = Join-Path $installRoot 'ScreenInk.App.exe'
$runningApp = Get-Process -Name 'ScreenInk.App' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $installedExecutable }
if ($runningApp) { throw 'Close the installed ScreenInk app before updating it.' }

New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
Get-ChildItem -LiteralPath $sourceRoot -Force | Copy-Item -Destination $installRoot -Recurse -Force

$shell = New-Object -ComObject WScript.Shell
$shortcutFolders = @(
    [Environment]::GetFolderPath('DesktopDirectory'),
    [Environment]::GetFolderPath('Programs')
)
foreach ($folder in $shortcutFolders) {
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $shortcut = $shell.CreateShortcut((Join-Path $folder 'ScreenInk.lnk'))
    $shortcut.TargetPath = $installedExecutable
    $shortcut.WorkingDirectory = $installRoot
    $shortcut.Description = 'Screen annotation, screenshots, and local QR sharing'
    $shortcut.IconLocation = "$installedExecutable,0"
    $shortcut.Save()
}

Write-Host "Installed ScreenInk to $installRoot"
Write-Host 'Desktop and Start menu shortcuts are ready.'
