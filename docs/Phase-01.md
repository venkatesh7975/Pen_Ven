# Phase 1: Native Windows foundation

## Goal

Create and build a small native WinUI 3 desktop application called ScreenInk. Verify application startup, XAML compilation, a separate Core project, UI binding, and Debug/Release output. Stop before creating the transparent overlay.

## Version decisions

Verified against Microsoft release metadata and documentation on 6 October 2026:

- .NET SDK: `10.0.401`, pinned without roll-forward.
- C#: `14.0`.
- Windows App SDK: `2.5.1` stable.
- Windows SDK build-tools NuGet package: `10.0.28000.2705`, the latest published stable NuGet version checked.
- Target framework: `net10.0-windows10.0.26100.0`.
- Declared minimum Windows version: `10.0.19041.0`.
- Initial architecture: `win-x64`.

Build-tool NuGet versions, standalone SDK installer versions, and API contract versions are separate. The newer standalone SDK installer release is not necessarily published as a NuGet build-tools package. The SDK contract version does not set the application's minimum operating system version. Compatibility must be verified on the Windows versions we ship for.

The initial app is unpackaged and self-contained. This avoids requiring MSIX registration, Developer Mode, or a separately installed Windows App Runtime for this launch check. The application manifest requests `asInvoker` privileges and per-monitor V2 DPI awareness. No Win32 P/Invoke is needed in Phase 1.

## Files

| File | Purpose |
| --- | --- |
| `ScreenInk.sln` | App and Core solution; x64 Debug/Release configurations |
| `global.json` | Exact .NET SDK selection |
| `Directory.Build.props` | Shared compiler settings and package lock files |
| `Directory.Packages.props` | Central, stable NuGet versions |
| `NuGet.Config` | Explicit NuGet.org source |
| `.gitignore` | Excludes SDK downloads, caches, and build outputs |
| `src/ScreenInk.Core/ScreenInk.Core.csproj` | UI-independent .NET library |
| `src/ScreenInk.Core/ApplicationInfo.cs` | App name and phase identity |
| `src/ScreenInk.App/ScreenInk.App.csproj` | Native WinUI x64 application |
| `src/ScreenInk.App/app.manifest` | Normal-user execution and DPI configuration |
| `src/ScreenInk.App/App.xaml` | Dark theme and Fluent resources |
| `src/ScreenInk.App/App.xaml.cs` | Application and window lifetime |
| `src/ScreenInk.App/MainWindow.xaml` | Foundation screen and launch-check button |
| `src/ScreenInk.App/MainWindow.xaml.cs` | Window initialization and event forwarding |
| `src/ScreenInk.App/ViewModels/MainViewModel.cs` | Launch-check state and change notification |
| `src/ScreenInk.App/Properties/launchSettings.json` | Unpackaged Visual Studio launch profile |
| `scripts/Install-DotNet.ps1` | Downloads a local SDK; verifies Microsoft's SHA-512 hash |
| `scripts/Initialize-Environment.ps1` | Configures the local SDK and package cache |
| `scripts/Build.ps1` | Builds the solution in Debug or Release |
| `scripts/Run.ps1` | Builds and launches the selected configuration |
| `README.md`, `docs/Phase-01.md` | Setup and phase documentation |

NuGet generates `packages.lock.json` for each project after the first successful restore. Keep these lock files in source control. Future CI can restore with `--locked-mode`.

## Commands

Open PowerShell in the repository root:

```powershell
Set-Location 'C:\Users\Venkatesh\Documents\ChatGPT\Pen_ven'
.\scripts\Install-DotNet.ps1
.\scripts\Build.ps1 -Configuration Debug
.\scripts\Build.ps1 -Configuration Release
.\scripts\Run.ps1 -Configuration Debug
```

If that configuration has already been built, launch it without rebuilding:

```powershell
.\scripts\Run.ps1 -Configuration Debug -NoBuild
```

The install script downloads to `.tools/downloads` and extracts to `.tools/dotnet`. The build scripts isolate the CLI home and NuGet cache under `.tools`; they do not require a machine-wide SDK install.

If an execution-policy error blocks a script, run that individual script with a process-scoped policy:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1 -Configuration Debug
```

Do not change the machine's execution policy for this project.

## Expected result

A resizable dark window titled **ScreenInk** opens. Its initial size accounts for the current display's DPI scaling. It displays **Phase 1 · Native Windows foundation**, a welcome card, and a **Verify launch** button. Clicking it updates the status and increments the click count. Closing the window terminates the app.

## Manual checklist

- [ ] Debug build completes without errors.
- [ ] Release build completes without errors.
- [ ] Both configurations launch successfully.
- [ ] Verify launch updates the count on each click.
- [ ] Tab focuses the button; Enter or Space activates it.
- [ ] Window resizing keeps the text readable and permits scrolling.
- [ ] Moving the window between monitors does not crash the app.
- [ ] Closing the window exits the process.
- [ ] Built app starts with networking disconnected.
- [ ] Built app launches as a normal user without a UAC prompt.

There are no ink-engine unit tests in this phase because the ink engine has not been created. Build and UI launch checks are the relevant validation.

## Progress gate

Wait for the user's confirmation that this window works before implementing Phase 2.

## Verification on 6 October 2026

- Installed .NET SDK `10.0.401` under `.tools/dotnet`; verified the downloaded archive against Microsoft's SHA-512 release hash.
- Restored stable Microsoft packages and generated package lock files. No resolved package version contains a prerelease suffix.
- Built the final source in Debug and Release: both completed with zero warnings and zero errors.
- Launched the final Debug executable and inspected its native window and accessibility tree using Computer Use.
- Visually confirmed the initial window size accommodates the development display's DPI scaling and shows the full welcome card and status.
- Activated the launch-check button and observed `Launch check passed · 1 click` in the final build.
- Left the Debug window open for user review.

Keyboard-only navigation, Release startup, offline startup, window closing, different monitors, and Windows 10 compatibility remain manual checklist items. A user-interaction guard interrupted the keyboard automation check; it is not reported as passed.
