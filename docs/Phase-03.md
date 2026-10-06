# Phase 3: Mouse drawing

## Goal

Draw smooth freehand ink over the current monitor using the left mouse button. Keep the background completely transparent, retain ink during hide/click-through, and end drawing safely on mouse-up, capture loss, a mode change or shutdown. This phase uses a fixed red 4-physical-pixel pen. Pen/stylus input is Phase 4; eraser is Phase 5; history is Phase 6; the floating toolbar is Phase 7.

## Architecture and why

The Phase 2 visual and input windows remain separate. The visual window always passes input through; the invisible input window intercepts desktop mouse input only in draw mode. The WinUI panel stays above them and remains usable.

The mouse path is: native window messages → `MouseCapture` coordinate conversion → `StrokeBuilder` → retained `InkStroke` samples → `InkSurface` → layered visual window. The XAML view model receives status at stroke start/end and mode changes, rather than every mouse move.

- **Core:** stroke identity, style and read-only sample view, without UI dependencies.
- **Input:** `SetCapture`, `GetCapture`, `ReleaseCapture` and signed mouse-coordinate conversion.
- **Drawing:** midpoint quadratic interpolation plus a native Direct2D surface with round caps and anti-aliasing.
- **Overlay:** manages the current stroke, retained document, mode transitions, lifetime and error recovery.
- **App:** displays instructions/status and forwards mode actions.

Samples are physical desktop pixels, including negative monitor origins. Direct2D uses 96 DPI for this surface so it maps one drawing unit to one bitmap pixel; WinUI still uses its normal scaling for the panel. Ink stays anchored to desktop coordinates when the surface is rebuilt. Multi-monitor behavior is not completed until Phase 15.

The smoothing engine connects adjacent samples through their midpoints using quadratic curves. This avoids extrapolated overshoot at corners. It has one sample of latency and flushes the final half-segment when drawing ends, preserving the first and last point. Duplicate/near-duplicate samples under half a pixel are ignored. A mouse click produces a round dot. Retained replay reconstructs the same geometry.

## Rendering and current performance limit

The surface retains its Direct2D target, brush, round stroke style and premultiplied BGRA bitmap. Each mouse event draws only its new segment. Completed strokes are replayed only when rebuilding the surface, changing its origin/size, or changing the diagnostic boundary. There is no screen capture, polling timer or idle animation loop.

This is a **CPU-backed Direct2D DC render target**, not a GPU renderer. `UpdateLayeredWindow` transfers the full bitmap when ink changes, and Direct2D's DC path also copies its backing image. The first implementation establishes correct rendering and input; it does not claim measured 60 FPS, dirty-region uploads or production performance on 4K/high-refresh displays. GPU composition, batching, large-session benchmarks and performance tuning remain later work. The renderer is separate so those changes do not require rewriting the stroke model or UI.

The pen is opaque in this phase. Adjustable opacity/highlighter rendering needs a separate stroke-compositing strategy to avoid dark joins and is deferred to its planned phase.

## Version and API decisions

Existing .NET 10.0.401, Windows App SDK 2.5.1 and SDK BuildTools 10.0.28000.2705 pins remain in place. Microsoft.Windows.CsWin32 **0.3.346** is the stable build-time generator added here. It generates Direct2D structures and COM calls from Microsoft metadata, avoiding manual COM vtable layouts. It has no additional runtime dependency in the shipped application. Its official transitive metadata packages carry alpha/preview/experimental labels; these are build-time metadata, are recorded in the lock file, and do not select preview Windows runtime APIs. Generated files live under ignored `obj` output and must not be hand-edited.

The implementation calls documented APIs in Windows system DLLs; it never replaces them and requires no administrator rights:

- `D2D1CreateFactory`, `ID2D1Factory.CreateDCRenderTarget`, `BindDC`: create the native drawing target.
- `CreatePathGeometry`, geometry sinks and `DrawGeometry`: render quadratic strokes with round endpoints.
- `FillEllipse`: mouse-down dots.
- `CreateDIBSection`, `UpdateLayeredWindow`: a zero-alpha desktop overlay with premultiplied visible ink.
- `SetCapture`, `ReleaseCapture`: continue an active drag over the control-panel area and release it at stroke end.
- `WM_LBUTTONDOWN`, `WM_MOUSEMOVE`, `WM_LBUTTONUP`, `WM_CAPTURECHANGED`, `WM_CANCELMODE`: mouse lifecycle and interruption handling.

Direct2D's DC APIs predate Windows 10. The application still declares Windows 10 build 19041 as its minimum; actual Windows 10 compatibility needs its own machine test. Because the input surface does not activate, Windows' foreground/background capture restrictions still apply when crossing outside the active monitor. Cross-monitor capture and differing monitor DPI require hardware validation in Phase 15.

## Files created or modified

Complete code is saved in the source files below; there are no placeholders or omitted methods.

| File | Change/purpose |
| --- | --- |
| `src/ScreenInk.Core/Models/InkStroke.cs` | New stroke model, validated width/coordinates, duplicate filtering |
| `src/ScreenInk.Drawing/ScreenInk.Drawing.csproj` | New Windows drawing library and build-time generator |
| `src/ScreenInk.Drawing/NativeMethods.txt` | Requested Direct2D bindings |
| `src/ScreenInk.Drawing/NativeMethods.json` | Generate unmanaged COM bindings |
| `src/ScreenInk.Drawing/StrokeEngine/StrokeBuilder.cs` | Incremental smoothing, final endpoint and retained replay |
| `src/ScreenInk.Drawing/Rendering/InkSurface.cs` | Retained bitmap/Direct2D resources, rendering, presentation and disposal |
| `src/ScreenInk.Input/ScreenInk.Input.csproj` | New input library |
| `src/ScreenInk.Input/MouseInput/MouseCapture.cs` | Native mouse capture and desktop coordinates |
| `src/ScreenInk.Native/NativeMethods.cs` | Mouse messages and capture APIs |
| `src/ScreenInk.Native/ScreenInk.Native.csproj` | Internal access for input/drawing libraries |
| `src/ScreenInk.Overlay/ScreenInk.Overlay.csproj` | Drawing and input references |
| `src/ScreenInk.Overlay/OverlayController.cs` | Stroke lifecycle, document retention, renderer integration and recovery |
| `src/ScreenInk.Overlay/OverlayStatus.cs` | Stroke count in view-model status |
| `src/ScreenInk.Overlay/TransparentFrame.cs` | Removed: diagnostic frame is now part of InkSurface |
| `src/ScreenInk.Core/ApplicationInfo.cs` | Phase 3 identity |
| `src/ScreenInk.App/MainWindow.xaml` | Drawing instructions |
| `src/ScreenInk.App/ViewModels/MainViewModel.cs` | Retained stroke count |
| `tests/ScreenInk.Overlay.Tests/Program.cs` | Geometry, output pixels, capture and routing checks |
| `Directory.Packages.props` | Pin stable interop generator |
| `ScreenInk.sln` | Add drawing/input projects to x64 solution configurations |
| `README.md`, `docs/Phase-03.md` | Current usage and this guide |

NuGet updates the affected `packages.lock.json` files, including the new Drawing/Input locks. Existing scripts still build, test and run the app.

## Exact commands

Close ScreenInk before rebuilding Debug or running the tests. Closing currently loses in-memory ink, because session files arrive in Phase 16.

```powershell
Set-Location 'C:\Users\Venkatesh\Documents\ChatGPT\Pen_ven'
.\scripts\Build.ps1 -Configuration Debug
.\scripts\Build.ps1 -Configuration Release
.\scripts\Test-Overlay.ps1
.\scripts\Run.ps1 -Configuration Debug -NoBuild
```

If a new machine needs the project-local SDK first, run `.\scripts\Install-DotNet.ps1`. Initial package restore needs internet; drawing runs entirely locally afterward. The self-contained executable needs its entire output directory, not just the EXE.

## What you should see

The dark ScreenInk panel says **Phase 3 · Mouse drawing** and starts with the overlay disabled. Choose **Draw mode**, then drag outside the panel: a red stroke follows the mouse. A single click leaves a small round dot. The panel shows the retained stroke count and the last stroke's sample count.

**Click-through** leaves ink visible while other applications receive mouse input. **Hide overlay** hides ink and releases desktop input. Selecting **Draw mode** again shows the existing ink. The optional boundary can be toggled without deleting strokes. Closing ScreenInk ends the temporary session.

Recovery shortcuts remain **Ctrl+Alt+F9** to toggle draw/click-through and **Ctrl+Alt+F10** to hide. Draw mode refuses to start if those shortcuts conflict. There is no global Escape shortcut yet.

## Automated checks and verification record

Debug and Release builds completed with **zero warnings and zero errors** on this Windows 11 machine. The test runner passed:

- Stroke creation, duplicate filtering, connected quadratic geometry, exact endpoints and matching retained replay.
- Invalid coordinate rejection and signed mouse coordinates with negative desktop origins.
- Real native Direct2D output: zero-alpha background, opaque ink, anti-aliased edges, preserved earlier pixels and endpoint pixels.
- Reconstruction with/without the boundary while retaining ink.
- Controller mouse-down/move/up handling, dot output, retained count and native capture release.
- Ending a stroke on mode switch or actual native capture loss; later movement does not extend it.
- Visible ink and boundary click-through to a separate-process fixture.
- Ten draw/click-through cycles, hotkey conflict refusal, hide, disposal and shortcut reuse.

Controller tests invoke the handler with mouse samples; they do not prove the physical mouse event path or device latency. The capture-loss test uses the real native API. Tests do not move the desktop pointer or inject clicks into unrelated apps.

The running WinUI preview's Phase 3 instructions, mode buttons, retained-count status and recovery-shortcut status were inspected. Further automated physical mouse interaction was interrupted by concurrent user window changes; use the checklist below for the final hands-on acceptance. No pen pressure, tablet, touch, alternate DPI, second monitor, Windows 10 or 4K performance claim is made by these checks.

## Manual acceptance checklist

- [ ] Start disabled; verify other apps remain clickable.
- [ ] Draw slow loops, handwriting, quick zigzags and long diagonals outside the panel; check smooth edges and final endpoints.
- [ ] Click once for a dot; draw two separated strokes and verify no line connects them.
- [ ] Drag over the panel, release, then move without holding the button; verify drawing stops and buttons remain accessible afterward.
- [ ] Toggle click-through: ink stays visible and an underlying app receives input, including where ink is visible.
- [ ] Hide/re-enable: all existing strokes return without changes or accidental connecting lines.
- [ ] Toggle the boundary in both modes; existing ink survives.
- [ ] While drawing, use Ctrl+Alt+F9 or Ctrl+Alt+F10; verify capture ends and normal input returns.
- [ ] Check recovery shortcuts while an underlying app has keyboard focus.
- [ ] Close ScreenInk and verify the overlay/capture disappear; reopen to a blank temporary session.
- [ ] Repeat on 100%, 125%, 150% and 200% scaling: cursor and ink align, and 4 px means physical pixels.
- [ ] Record Windows 10 and monitor/device results separately; pen pressure and touch are not Phase 3 acceptance criteria.

## Sources

- [Microsoft: Direct2D DC render target](https://learn.microsoft.com/en-us/windows/win32/api/d2d1/nn-d2d1-id2d1dcrendertarget)
- [Microsoft: mouse movement and signed coordinates](https://learn.microsoft.com/en-us/windows/win32/learnwin32/mouse-movement)
- [Microsoft: SetCapture and foreground restrictions](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setcapture)
- [Microsoft: layered window presentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow)
- [Microsoft: CsWin32 generator](https://microsoft.github.io/CsWin32/docs/getting-started.html)
- [Verified stable generator package](https://www.nuget.org/packages/Microsoft.Windows.CsWin32/0.3.346)

## Progress gate

Stop here. Confirm that mouse drawing, click-through and hide/re-enable work on your machine before starting **Phase 4: pen/stylus input**.
