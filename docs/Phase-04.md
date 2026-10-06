# Phase 4: Native pen/stylus input

## Goal

Draw over the current monitor using a Windows-compatible stylus, with pressure-sensitive width when available. Consume coalesced pen samples, retain tilt/rotation/button measurements, avoid duplicate mouse ink, and finish a stroke safely when contact ends or is interrupted. Keep Phase 3 mouse drawing and overlay recovery intact. Actual erasing remains Phase 5.

## Architecture

The existing two-window overlay remains. `WM_POINTER*` messages arrive at the native input window; `PenInputReader` retrieves Windows data immediately while the message is current. `StrokeBuilder` smooths positions and width together. `InkSurface` draws pressure-varying ribbons on the retained Direct2D surface. The view model exposes diagnostic text at stroke boundaries.

- **Core:** `InkSample` holds desktop position, optional normalized pressure, tilt, rotation and button flags. A stroke identifies whether input came from a mouse or pen.
- **Input:** `IPenInputReader` separates Windows acquisition from stroke handling and provides a test seam. The production reader calls `GetPointerType`, `GetPointerPenInfo` and `GetPointerPenInfoHistory`.
- **Drawing:** midpoint interpolation smooths width factors as well as geometry. Opaque variable-width ribbons have round endpoints; constant-width strokes retain the existing Direct2D geometry path.
- **Overlay:** tracks one active pen ID, ignores other pointers during a stroke, suppresses pen/touch-promoted mouse messages, and owns contact/mode/error transitions.
- **App:** shows pen measurements and driver guidance without adding a toolbar or settings features ahead of their planned phases.

This uses native Windows Pointer APIs rather than vendor-specific Wacom/XP-Pen APIs or classic WinTab. A tablet's Windows Ink path must be enabled and its driver must expose `PT_PEN`. A driver that emits ordinary mouse events may still draw as a mouse, without pressure; pen-tagged compatibility mouse messages are suppressed to prevent duplicate strokes. Windows Ink disabled/classic WinTab-only setups require driver configuration or future WinTab work.

## Input and pressure decisions

Windows reports valid pressure in 0–1024. The pressure mask determines availability: an absent measurement becomes `null`, while a reported zero remains zero. Pressure is normalized to 0–1. The fixed initial pen style has an 8-physical-pixel maximum, using:

`width = 8 × (0.2 + 0.8 × pressure)`

The visible width therefore ranges from 1.6 to 8 physical pixels. This makes hardware pressure easy to verify while keeping feather-light contact visible. Missing pressure falls back to a fixed 8 pixels. Mouse drawing remains a fixed 4 pixels. Configurable size/pressure curves are later settings work.

Tilt and rotation are used only when their native validity masks are present. Their values and barrel/eraser/inverted flags are retained. Tilt does not change the round pen's shape yet. A barrel button does not change tools in this phase. Eraser/inverted contact is recognized, reported, and prevented from painting pen ink; deleting ink is Phase 5.

Coalesced history arrives newest first and is reversed before use. Up to 256 native samples are buffered per message; if Windows reports more, the newest 256 are used. A history-query failure falls back to the successfully read latest sample. Overlapping history is deduplicated by performance counter when available, otherwise frame ID. Samples from a different pointer cannot join the active stroke.

Windows implicitly captures a contacting pen to its target window until contact ends. This implementation handles `WM_POINTERUP`, `WM_POINTERCAPTURECHANGED`, leave, canceled samples, read failures and mode changes; it does not invent a Win32 `SetPointerCapture` API. Mode changes end logical drawing and hide the input window. Lift the stylus before interacting with another app so Windows can complete any existing contact sequence.

Pen-up keeps the last contact pressure at the final endpoint, avoiding a sudden minimum-width tail or full-width bulge when pressure becomes unavailable on lift. Hover does not draw. Touch input is consumed while the drawing overlay is active to prevent unintended promoted mouse strokes; touch drawing and palm-rejection policy are later work.

The input surface returns documented tablet gesture flags that disable press-and-hold, flicks and feedback rings locally. Other applications and the WinUI panel retain their own behavior. No global Windows pen setting is changed.

## Rendering and performance limits

The existing CPU-backed Direct2D surface remains; this phase does not claim GPU acceleration or measured tablet latency. Pen history segments are batched into a single Direct2D draw pass/presentation for a normal update. Pressure ribbons approximate the smoothed curve at roughly two-pixel intervals, with a bounded subdivision count. Completed strokes are retained and replayed only for reconstruction/diagnostic-boundary changes. There is no idle render loop.

Full-surface bitmap transfer still occurs on presentation. GPU composition, 4K/large-session benchmarks, allocation tuning and stronger timing guarantees remain later performance work. Real handwriting feel needs hardware testing.

## Native APIs, privileges and versions

The implementation uses documented `user32.dll` pointer APIs and the existing Direct2D/GDI presentation APIs. It requires no administrator privileges and modifies no system DLLs. Pointer APIs are available from Windows 8; the application's declared minimum stays Windows 10 build 19041. Actual Windows 10 validation remains pending.

New interop structures match the x64 SDK ABI: `POINTER_INFO` is 96 bytes and `POINTER_PEN_INFO` is 120 bytes. This app still targets x64. New message constants and structure fields were checked against Microsoft documentation. Existing .NET/WinUI/Windows App SDK and interop-generator pins are unchanged; no new package was needed.

## Complete files created/modified

All code is saved in complete source files with no placeholders:

| File | Purpose/change |
| --- | --- |
| `src/ScreenInk.Core/Models/InkSample.cs` | New optional pressure/tilt/rotation/button sample model and validation |
| `src/ScreenInk.Core/Models/InkStroke.cs` | Retain full samples and input kind; tablet pen style |
| `src/ScreenInk.Input/PenInput/PenInputReader.cs` | Native acquisition, history ordering, availability masks and promotion detection |
| `src/ScreenInk.Input/ScreenInk.Input.csproj` | Core reference and test access |
| `src/ScreenInk.Native/NativeMethods.cs` | Pointer structures/functions/messages and gesture query constant |
| `src/ScreenInk.Drawing/StrokeEngine/StrokeBuilder.cs` | Position/width interpolation and retained replay |
| `src/ScreenInk.Drawing/Rendering/InkSurface.cs` | Pressure ribbons, round caps, stationary-pressure handling and batched rendering |
| `src/ScreenInk.Overlay/OverlayController.cs` | Pen ID/lifecycle tracking, interruption handling, diagnostics and duplicate-mouse suppression |
| `src/ScreenInk.Overlay/OverlayStatus.cs` | Pen diagnostic status |
| `src/ScreenInk.Core/ApplicationInfo.cs` | Phase 4 label |
| `src/ScreenInk.App/MainWindow.xaml` | Stylus instructions and pen measurements |
| `src/ScreenInk.App/ViewModels/MainViewModel.cs` | Pen measurement binding |
| `tests/ScreenInk.Overlay.Tests/Program.cs` | Metadata, pressure pixels, simulated pen lifecycle and existing regressions |
| `README.md`, `docs/Phase-04.md` | Current usage and this guide |

The Input, Overlay, App and test NuGet lock files update their project-reference graphs. Existing build/run scripts remain valid. Session serialization is not introduced; closing the app still loses temporary ink.

## Exact commands

Close an existing ScreenInk instance before rebuilding Debug or running the native tests.

```powershell
Set-Location 'C:\Users\Venkatesh\Documents\ChatGPT\Pen_ven'
.\scripts\Build.ps1 -Configuration Debug
.\scripts\Build.ps1 -Configuration Release
.\scripts\Test-Overlay.ps1
.\scripts\Run.ps1 -Configuration Debug -NoBuild
```

Initial SDK/package setup on a new machine remains `.\scripts\Install-DotNet.ps1`. The built app requires neither internet nor administrator privileges. Keep the full self-contained output directory together.

## What you should see

The panel says **Phase 4 · Pen and pressure**. It starts disabled. Enable Windows Ink for ScreenInk in your tablet driver's application profile, choose **Draw mode**, then write outside the panel. Light contact makes thinner red ink; heavier contact makes thicker ink. Lift the stylus to finish the stroke. The panel shows pressure availability, tilt and barrel/eraser state for the last contact.

Mouse drawing should still work at 4 physical pixels. Click-through keeps ink visible; hiding/re-enabling preserves its pressure shape. Recovery remains **Ctrl+Alt+F9** to toggle draw/click-through and **Ctrl+Alt+F10** to hide. The panel must remain reachable.

## Automated verification

Debug and Release builds and the native test runner passed with zero warnings/errors. The runner verifies:

- x64 native structure sizes; signed desktop positions; pressure normalization; validity masks; tilt/rotation/barrel data.
- Missing pressure versus reported zero, stationary pressure changes and oldest-first history conversion.
- Pen/touch mouse-promotion signatures versus ordinary mouse events.
- Connected native pressure pixels, thicker output at higher pressure, and pixel-identical retained reconstruction.
- A reversing pressure curve does not fail at a zero tangent.
- Simulated pen down/update/up, coalesced samples, history deduplication, hover rejection and no duplicate mouse stroke during contact.
- Eraser recognition, capture loss, cancellation with earlier history flushed, read failure and mode changes.
- All prior mouse capture, transparent output, hotkey conflict, disposal and cross-process click-through checks.

Pen controller tests use an injected reader with known frames. The native renderer is real, but these tests do not validate a physical tablet, actual `GetPointerPenInfo` delivery, device latency or native implicit pen capture. Those require the checklist below. The Debug preview was launched for hands-on acceptance; physical tablet operation remains unverified.

## Hardware/manual acceptance checklist

Repeat the core tablet checks on **Wacom**, **XP-Pen** and another Windows-compatible stylus when available; record device model, driver version, Windows version, monitor resolution/scaling and whether Windows Ink is enabled.

- [ ] Confirm normal input works before enabling the overlay.
- [ ] Hover without contact: no ink. Tap: a dot. Lift and move: no trailing or connecting line.
- [ ] Draw slowly with light pressure, then firmly: visible width changes; the panel reports pressure rather than unavailable.
- [ ] Write quickly, draw loops and zigzags: no missing portions, duplicate strokes, unexpected gaps or obvious width steps.
- [ ] Separate two strokes: no connecting line; one retained stroke per physical contact.
- [ ] Tilt a supported stylus: diagnostic values change; unsupported measurements remain unavailable.
- [ ] Hold/release the barrel button: state is reported without duplicate mouse ink or unintended tool switching.
- [ ] Use an eraser end if present: eraser state is recognized and no pen ink is painted. Erasing itself is not implemented yet.
- [ ] Cross the panel during a contact and lift: drawing ends; controls work afterward.
- [ ] Switch click-through/hide during contact, then lift: no stuck stroke; underlying apps are usable and existing ink remains aligned.
- [ ] Confirm Ctrl+Alt+F9/F10 while another app has keyboard focus; reconnect the tablet and start a new stroke after an interruption.
- [ ] Hide/re-enable and toggle the boundary: pressure shape remains unchanged.
- [ ] Repeat mouse drawing and click-through; verify no regression.
- [ ] Repeat at 100%, 125%, 150% and 200% scaling: cursor/ink alignment is correct. Record dual-monitor and Windows 10 results separately.
- [ ] With Windows Ink disabled, record whether the driver falls back to ordinary mouse drawing; do not expect pressure through classic WinTab.

If pressure does not work, report the tablet model, driver version, Windows Ink setting, the panel's exact diagnostic text, and whether mouse drawing still works. Do not change system DLLs or reinstall unrelated software.

## Official sources

- [POINTER_PEN_INFO measurements and availability](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-pointer_pen_info)
- [POINTER_INFO coordinates, frames and timestamps](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-pointer_info)
- [GetPointerPenInfoHistory ordering and fallback conditions](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getpointerpeninfohistory)
- [WM_POINTERDOWN handling](https://learn.microsoft.com/en-us/windows/win32/inputmsg/wm-pointerdown)
- [WM_POINTERUP and implicit capture](https://learn.microsoft.com/en-us/windows/win32/inputmsg/wm-pointerup)
- [Mouse-promotion signature](https://learn.microsoft.com/en-us/windows/win32/tablet/system-events-and-mouse-messages)
- [Window-local tablet gesture policy](https://learn.microsoft.com/en-us/windows/win32/tablet/wm-tablet-querysystemgesturestatus-message)

## Progress gate

Stop here. Confirm that stylus contact, pressure and recovery work on your hardware before starting **Phase 5: eraser**.
