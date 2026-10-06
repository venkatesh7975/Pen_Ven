# Phase 2: Transparent desktop overlay

## Goal and scope

Create a transparent overlay on the monitor containing the ScreenInk control panel. Prove that empty areas can capture input in draw mode, that input reaches another application in click-through mode, and that the overlay can always be hidden using its recovery shortcut. Freehand drawing is deliberately deferred to Phase 3.

## Architecture

The overlay uses two independent borderless, topmost Win32 windows:

1. A **visual surface** with `WS_EX_LAYERED | WS_EX_TRANSPARENT`. A premultiplied BGRA frame provides true zero-alpha pixels. The surface never intercepts input, including at visible boundary pixels.
2. An **input surface** with `WS_EX_NOREDIRECTIONBITMAP`. It has no visible background, but its entire client area participates in hit testing. It is shown only in draw mode and hidden in click-through and disabled modes.

Separating these surfaces avoids two common problems: alpha-zero layered pixels failing to capture pen-down on an empty canvas, and relying on `HTTRANSPARENT` to forward input across unrelated application threads. There is no almost-transparent full-screen tint.

The WinUI control panel starts centered and bounded by the monitor's work area, with its size adjusted for display scaling. It stays above the overlay while it is active. Both surfaces use `WS_EX_NOACTIVATE` so mouse-down does not take keyboard focus from the underlying application. Draw mode intercepts pointer input; it does not globally block the underlying app's keyboard shortcuts. Native callbacks run on the WinUI window's UI thread.

`ScreenInk.Core` holds the overlay mode enum. `ScreenInk.Native` owns P/Invoke declarations and native windows. `ScreenInk.Overlay` owns monitor geometry, mode switching, the temporary boundary frame, and recovery shortcuts. `MainViewModel` exposes status; the window only forwards user actions.

## Rendering in this phase

`UpdateLayeredWindow` presents an empty frame or an optional three-physical-pixel monitor border. All pixels outside that border have zero alpha. Frames update only when their bounds or visible appearance changes. There is no render timer, animation loop, desktop screenshot, or screen polling.

This static diagnostic frame uses GDI/DIB allocation. It is not the final low-latency ink renderer. The drawing phases will add the drawing engine and GPU rendering without changing the separation of visual and input surfaces.

## Native APIs and privileges

The implementation calls documented APIs from `user32.dll`, `gdi32.dll`, and `kernel32.dll`. It does not modify those files.

- `RegisterClassExW`, `CreateWindowExW`, `DefWindowProcW`, `DestroyWindow`: native surface lifetime and message handling.
- `SetWindowPos`, `ShowWindow`: topmost ordering, positioning, and hiding.
- `MonitorFromWindow`, `GetMonitorInfoW`: current monitor's full physical bounds, including the taskbar area.
- `CreateDIBSection`, `UpdateLayeredWindow`: transparent static pixels and the optional border.
- `RegisterHotKey`, `UnregisterHotKey`: recovery while desktop input is intercepted.
- `WindowFromPoint`: integration-test verification of Windows hit routing.

These operations require no administrator privileges. `WS_EX_NOREDIRECTIONBITMAP` is supported from Windows 8; the application's declared minimum remains Windows 10 build 19041. Windows 10 compatibility and real pen/touch routing still need hardware validation.

## Recovery and lifetime

- **Ctrl+Alt+F9** toggles draw and click-through. From disabled, it enables draw mode.
- **Ctrl+Alt+F10** hides both surfaces.
- These are temporary Phase 2 shortcuts. Configurable product shortcuts are planned for Phase 17.
- Draw mode is refused if either recovery shortcut cannot be registered. The panel reports the conflict instead of creating an input surface without working recovery keys.
- Closing ScreenInk disposes both native windows and unregisters its shortcuts.
- Managed exceptions are caught inside native callbacks; input capture is hidden on failure.
- The overlay starts disabled and does not auto-enable at startup.
- Escape is not a global shortcut in this phase. Use the hide button or Ctrl+Alt+F10.

The current phase supports one monitor at a time. Move the control panel and select a mode again to choose another monitor. Full multi-monitor topology handling remains Phase 15; current active surfaces refresh their bounds on display changes.

## Files created

| File | Purpose |
| --- | --- |
| `src/ScreenInk.Core/Models/OverlayMode.cs` | Disabled/draw/click-through mode model |
| `src/ScreenInk.Native/ScreenInk.Native.csproj` | Native interop library |
| `src/ScreenInk.Native/NativeMethods.cs` | Documented Win32 declarations and structures |
| `src/ScreenInk.Native/NativeWindow.cs` | Native class registration, callback dispatch, thread checks and disposal |
| `src/ScreenInk.Overlay/ScreenInk.Overlay.csproj` | Overlay service library |
| `src/ScreenInk.Overlay/MonitorBounds.cs` | Physical monitor coordinates |
| `src/ScreenInk.Overlay/OverlayStatus.cs` | Mode, geometry, input and error status |
| `src/ScreenInk.Overlay/TransparentFrame.cs` | Static transparent pixels and optional boundary |
| `src/ScreenInk.Overlay/OverlayController.cs` | Surface management, mode switching and recovery hotkeys |
| `tests/ScreenInk.Overlay.Tests/ScreenInk.Overlay.Tests.csproj` | Dependency-free Windows integration checks |
| `tests/ScreenInk.Overlay.Tests/Program.cs` | Real native-window checks against a separate-process fixture |
| `scripts/Test-Overlay.ps1` | Build and execute native checks |
| `docs/Phase-02.md` | This guide |

NuGet also generates lock files for the new projects. The solution, App project reference, application identity, control-panel XAML/code-behind, view model, and README are updated. Complete source is saved in these files.

## Commands

Close an already-running Phase 2 instance before running the integration checks:

```powershell
Set-Location 'C:\Users\Venkatesh\Documents\ChatGPT\Pen_ven'
.\scripts\Build.ps1 -Configuration Debug
.\scripts\Build.ps1 -Configuration Release
.\scripts\Test-Overlay.ps1
.\scripts\Run.ps1 -Configuration Debug -NoBuild
```

The test runner checks real Windows hit routing in two processes. It does not inject clicks into unrelated applications or change the mouse position. Its invisible fixture is destroyed in the cleanup path.

## Expected result

ScreenInk opens with the overlay disabled. Selecting **Draw mode** keeps the desktop visually unchanged while clicks outside the control panel increment **Captured mouse clicks**. They should not activate the application underneath. Selecting **Click-through** allows those applications to receive input again. Selecting **Hide overlay** disables both surfaces.

Enable **Show monitor boundary** to see a cyan frame in draw mode or a green frame in click-through. No border is shown by default. The border's pixels also pass through in click-through mode.

## Manual checklist

- [ ] Debug and Release build without errors or warnings.
- [ ] Native integration checks pass while no other ScreenInk instance owns the recovery shortcuts.
- [ ] Blank desktop regions capture clicks in draw mode; the counter and desktop coordinates update.
- [ ] Click-through permits normal clicking, scrolling, and dragging in another application.
- [ ] The control panel remains clickable in draw mode.
- [ ] There is no full-screen background, tint, or flickering in either mode.
- [ ] Optional boundary changes from cyan to green and remains click-through.
- [ ] Ctrl+Alt+F9 toggles modes even when another application has keyboard focus.
- [ ] Ctrl+Alt+F10 hides the overlay; it also works when the panel is minimized.
- [ ] Closing ScreenInk releases input and shortcuts.
- [ ] Moving the panel to another monitor and reselecting a mode uses that monitor's bounds.
- [ ] Tablet and touch input do not reach underlying apps in draw mode, and do reach them in click-through mode.

Mouse hit routing is checked automatically. Tablet pressure, tilt, and stroke capture belong to Phase 4 and are not claimed by these tests.

## References

- [Windows layered-window hit testing](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features)
- [Extended window styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)
- [Transparent windows without redirection bitmaps](https://learn.microsoft.com/en-us/archive/msdn-magazine/2014/june/windows-with-c-high-performance-window-layering-using-the-windows-composition-engine)
- [UpdateLayeredWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow)
- [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)

## Progress gate

Wait for the user's confirmation that overlay modes work before implementing Phase 3 mouse drawing.

## Verification on 6 October 2026

- Final Debug and Release builds completed with zero warnings and zero errors.
- The native integration runner also built without warnings and passed its checks for empty-area draw capture, click-through to a separate process, visible-pixel click-through, ten mode cycles, shortcut conflict detection, and cleanup/re-registration.
- The running WinUI panel reported captured mouse-down events and desktop coordinates in draw mode, and correctly reported click-through and disabled states during interactive review.
- Ctrl+Alt+F9 was exercised through Computer Use and enabled draw mode from disabled.
- The final panel was visually verified inside the monitor work area with all controls and its status visible at the development display's scaling.
- The app was left open for user review. Drawing is not implemented yet.

Several keyboard automation attempts were interrupted by the Computer Use user-interaction guard. Global hide-shortcut activation and toggling while another application has focus remain manual checklist items; registration and release of both recovery shortcuts passed native integration checks. Pen, touch, Windows 10, and additional-monitor checks remain unverified.
