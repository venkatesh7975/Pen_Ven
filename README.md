# ScreenInk

A native Windows screen annotation application built incrementally with C#, .NET 10, WinUI 3, and Windows App SDK. The compact floating toolbar provides drawing, colors, shapes, a laser, screenshots and QR sharing. Hide collapses the toolbar and ink into a small purple pen button; click it to reopen and restore the session.

## Current architecture

- `ScreenInk.App` owns the WinUI application, window, and view model.
- `ScreenInk.Core` contains UI-independent identity, stroke models and bounded command history. It has no WinUI dependencies.
- `ScreenInk.Drawing` owns midpoint interpolation and the retained Direct2D drawing surface.
- `ScreenInk.Tools` owns shape drafts and whole-stroke erasure using smoothed, pressure-aware geometry hit testing.
- `ScreenInk.Input` owns mouse capture, signed desktop coordinates, and native pen sample retrieval.
- `ScreenInk.Overlay` manages native surface lifetime, monitor bounds, and input modes.
- `ScreenInk.Native` isolates documented Win32 interop and native resource management.
- `ScreenInk.Sharing` serves one captured PNG through a temporary LAN link and generates its QR code.
- `MainWindow.xaml.cs` initializes the floating toolbar, handles placement/theme/flyouts, and forwards actions to the overlay controller.
- SDK and package versions are pinned in `global.json` and `Directory.Packages.props`.

See [the screenshot and sharing guide](docs/Screenshots-and-sharing.md) for capture/share/toggle behavior and checks, and [the toolbar and tools guide](docs/Phase-07.md) for drawing tools. The [toolbar brief](docs/Toolbar-design.md) records the Epic Pen-inspired design direction. The [Phase 6 guide](docs/Phase-06.md) explains history; the [Phase 5 guide](docs/Phase-05.md), [Phase 4 guide](docs/Phase-04.md), [Phase 3 guide](docs/Phase-03.md), [Phase 2 guide](docs/Phase-02.md) and [Phase 1 guide](docs/Phase-01.md) record earlier work.

## Quick start

In PowerShell, from the repository root:

```powershell
.\scripts\Install-DotNet.ps1
.\scripts\Build.ps1 -Configuration Debug
.\scripts\Build.ps1 -Configuration Release
.\scripts\Test-Overlay.ps1
.\scripts\Test-Sharing.ps1
.\scripts\Run.ps1 -Configuration Debug
```

The SDK and NuGet cache live inside ignored `.tools` directories. The initial setup and package restore need internet access. The built app runs locally without an internet connection or administrator privileges.

Click **Pen**, **Eraser**, a **Shape**, or **Laser** to start annotation mode. **Cursor** returns input to other apps while ink remains visible. **Hide** collapses the toolbar and hides ink; click the little purple pen to reopen and restore it. Drag the top grip to move the toolbar. The color circle opens quick colors and a custom picker; **Tool size** adjusts pen/shape width and eraser diameter. **More** contains help, light/dark appearance, the annotation list, optional boundary, and diagnostics. Recovery shortcuts are **Ctrl+Alt+F9** (toggle draw/cursor, reopening a collapsed toolbar) and **Ctrl+Alt+F10** (hide). The app starts with the overlay disabled.

Click **Camera** to capture the monitor containing the toolbar, including visible annotations and excluding the toolbar. The small preview offers **Save** (PNG), **Copy** (clipboard), **Share QR**, and **New**. The camera reopens the latest snapshot/QR; New takes another screenshot. Scan the QR on another device on the same Wi-Fi to view/download the screenshot. Sharing is local HTTP, starts only when Share QR is clicked, and ends after ten minutes, Stop sharing, a new capture, or Exit. The camera turns purple while sharing. Private-network firewall access and a reachable Wi-Fi/Ethernet network are required; an isolated guest network can block device-to-device access. This shares a snapshot, not a live video feed.

In annotation mode, draw outside the toolbar using the mouse or a Windows-compatible stylus. The selected width is 1–32 physical pixels (default 4). Mouse drawing and shapes use this fixed width; freehand stylus drawing varies from 20% to 100% of it with pressure, or uses the fixed width when pressure is unavailable. Color/width changes affect new annotations and preserve existing ink. Enable Windows Ink in your tablet driver for the native pen path. Hiding and cursor mode preserve ink in memory; closing the application ends this temporary session.

Open **Shapes** and choose **Line**, **Arrow**, **Rectangle**, or **Ellipse**, then drag from one endpoint/corner to the other. A live outline previews the shape; release commits one undoable annotation. Rectangle and ellipse outlines stay hollow. Select **Laser** and press/drag to point in the chosen color; lifting hides it. Laser pointing does not become retained ink or an undo action.

Choose **Stroke eraser** and drag over ink to remove each whole stroke touched. Set its diameter from 4–128 physical pixels (default 24). A supported stylus eraser/inverted end automatically erases during contact, even with Pen selected. The stroke list supports **Delete selected stroke**; **Clear all** removes all retained ink. Deletion also works while hidden or in click-through. Pixel erasing remains deferred.

Use **Undo/Redo** or **Ctrl+Z/Ctrl+Y** to reverse ink edits. One drawing contact or complete eraser drag is one action; selected deletion and clear-all are actions too. Buttons work in all overlay modes. Shortcuts work while drawing or when this panel has keyboard focus; click-through and hide release the global Ctrl+Z/Y registrations so other apps keep their own history. Shortcut conflicts are reported and the buttons remain available. A new ink edit after undo discards redo; an empty eraser drag or a tool/mode change preserves it. History retains at most 256 actions and uses a soft 250,000-sample reference budget, always preserving the newest action. Closing the app still ends the session.

The panel reports pen pressure/tilt and button state after contact. Barrel-button data is retained. Touch drawing and classic WinTab integration are not implemented. Real tablet compatibility and latency still require the hardware checks in the Phase 4 and Phase 5 guides.

The renderer retains its pixels and draws each new segment without replaying old strokes. Erasing reconstructs remaining strokes only when a hit removes ink, batching coalesced pen history into one reconstruction per update. It is CPU-backed Direct2D with a full-surface layered-window transfer on presentation; GPU composition and measured performance optimization remain future work. There is no continuous render loop when idle.

The tests cover grouped history/order, branch invalidation, bounded retention, mixed-edit model comparison, shortcut ownership/conflicts, swept eraser geometry, restored output pixels, hidden history, pen metadata/history conversion, pressure-rendered output pixels, contact interruption and cross-process click-through. They briefly create transparent test windows in two processes. Close any already-running ScreenInk instance before testing so its global shortcuts do not conflict with the test controller.

## Keyboard shortcuts

These shortcuts work while ScreenInk is running, including from another app or with the toolbar hidden. Tool shortcuts reopen the toolbar and enable drawing. Open **More** or press **Ctrl+Alt+H** for the list and any conflicts with other apps.

| Keys | Feature |
| --- | --- |
| Ctrl+Alt+P / E / L | Pen / stroke eraser / laser |
| Ctrl+Alt+1 / 2 / 3 / 4 | Line / arrow / rectangle / ellipse (number row) |
| Ctrl+Alt+C | Cursor mode |
| Ctrl+Alt+S | Take a new screenshot |
| Ctrl+Alt+M | Media and definitions |
| Ctrl+Alt+K / W | Open color picker / size controls |
| Ctrl+Alt+Up / Down | Increase / decrease current tool size (pen by 1 px, eraser by 4 px) |
| Ctrl+Alt+Delete | Clear all annotations, undoable |
| Ctrl+Alt+D | Delete the annotation selected in More |
| Ctrl+Alt+Z / Y | Undo / redo in any mode |
| Ctrl+Alt+H | Help and options |
| Ctrl+Alt+F9 / F10 | Toggle draw/cursor / hide toolbar and ink |
| Ctrl+Z / Y | Undo / redo in draw mode or with toolbar focus |

With focus inside the screenshot preview, **Ctrl+S** saves, **Ctrl+C** copies, **Ctrl+Q** opens Share QR, and **Ctrl+N** takes a new screenshot. These preview shortcuts are scoped to the preview. Global feature shortcuts pause during capture and while the screenshot save dialog is open. A new screenshot ends any existing sharing session. A shortcut reserved by another app is reported as unavailable; the toolbar still works. Close the conflicting app and restart ScreenInk to retry registration.

## Install on this laptop

After setting up the local SDK, run `./scripts/Install-App.ps1` in PowerShell. This builds Release, copies the complete self-contained app to `%LOCALAPPDATA%\Programs\ScreenInk`, and adds **ScreenInk** shortcuts to your Desktop and Start menu. No administrator privileges are needed. Use `-NoBuild` to install an existing Release build. Close the installed app before rerunning the script to update it.

The installed copy runs independently of this source folder. To remove it, close ScreenInk, delete `%LOCALAPPDATA%\Programs\ScreenInk`, and remove its Desktop and Start menu shortcuts.

## Development prerequisites

- x64 Windows development machine.
- PowerShell and the Windows `tar.exe` command.
- For IDE development: Visual Studio 2026 with the **WinUI application development** workload. Visual Studio is not required to run the compiled executable.

This phase uses an unpackaged, self-contained app. Both the .NET runtime and Windows App SDK runtime are copied to the output directory. Keep the output directory together; copying only the EXE is insufficient. The installation script copies that complete directory for the current user. A packaged installer and optional MSIX package are planned for Phase 24.

The project compiles against Windows SDK contracts `10.0.26100.0`, with a declared minimum Windows build of `19041`. Windows 10 compatibility still needs a separate real-machine test; running on the development machine does not verify it.

## Official references

- [WinUI setup](https://learn.microsoft.com/en-us/windows/apps/get-started/start-here)
- [.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Windows App SDK stable releases](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads)
- [Windows App SDK NuGet package](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/2.5.1)
