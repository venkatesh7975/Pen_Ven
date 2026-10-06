# Phase 5: Stroke eraser

## Goal and behavior

Remove whole retained strokes with mouse or pen contact. Adjust the circular eraser diameter, delete a selected stroke, or clear all ink. Preserve pressure-sensitive drawing, untouched ink, click-through, hidden state and recovery shortcuts.

The eraser is a **stroke eraser**: touching any part of a stroke deletes that entire stroke, including a single-dot stroke. A sweep can remove several overlapping strokes. The panel states this behavior explicitly. Diameter is 4–128 physical pixels, default 24, independent of WinUI scaling and pen pressure.

Pixel erasing is deferred. A vector implementation would need to split affected strokes and preserve interpolated pressure and caps at each cut. Clearing pixels alone would leave retained ink that reappears during reconstruction. Whole-stroke deletion provides consistent retained geometry for this phase and for the next phase's history.

## Architecture and implementation decisions

- **Core:** `AnnotationTool` selects Pen or StrokeEraser. Immutable `StrokeSummary` records expose identity, current list number, input kind and sample count to the panel.
- **Drawing:** `StrokeHitTester` replays exactly the midpoint-smoothed quadratic geometry used for painting. It tests the eraser's swept circle against curves and pressure-dependent width, including the starting dot and final tail.
- **Tools:** `StrokeEraser` removes every hit from the retained collection. Its returned `RemovedStroke` entries retain original indices and stroke objects for a future history command; no undo/redo is implemented here.
- **Overlay:** owns one drawing or erasing contact at a time, with the existing mouse capture or active pen ID. Erasure updates retained ink immediately and reconstructs the visible surface on actual removal. Pen history batches removals into one reconstruction per normal pointer update.
- **App:** Pen/Stroke eraser buttons, an accessible size slider, a stroke selector, Delete selected stroke and Clear all. These live in the current control panel; the compact floating toolbar is Phase 7.

Positions are signed desktop physical pixels, including negative monitor origins. Each eraser update checks the complete segment from the previous input point to the new point, so a fast drag cannot jump across a narrow stroke without testing it. Curve bounds reject distant segments cheaply. Recursive de Casteljau subdivision narrows curvature and width variation, with a depth limit of 16. The leaf distance test includes the local maximum half-width and a conservative 0.2 px geometry tolerance. This is approximate curve-edge selection, not a pixel-alpha lookup; antialias fringes and subpixel edges may differ slightly from the hit boundary.

The eraser does not change stroke color or cover ink with a background color. After removing strokes, the surface clears to transparent and replays the survivors plus the optional monitor boundary. This prevents ghost ink and retains pressure shape.

## Input lifecycle

The selected Stroke eraser works with either a physical mouse or the normal pen tip. Windows Eraser/Inverted flags override Pen for the current contact. The selected tool remains unchanged, and the next normal tip contact draws when Pen is selected. If a driver changes to an eraser flag mid-contact, existing drawing finishes before erasing begins; that flagged end never paints new pen ink.

Hover does not erase. Other pointers cannot join an active contact. Promoted pen/touch mouse messages remain suppressed. Cancellation, capture loss, pointer leave, pen-read failure, tool/diameter changes and mode changes finish the active contact. Deletion takes effect as contact proceeds; cancellation ends contact without restoring strokes. Restoration belongs to Phase 6 undo/redo.

Delete selected stroke operates by stable Guid identity rather than a changing list index. The selector refreshes at contact boundaries, preserving the chosen identity if it still exists and otherwise selecting the last remaining stroke. Clear all and selected deletion work while hidden and in click-through as well as draw mode. Empty/missing deletion is harmless. Hiding itself still preserves all remaining ink, while closing the app ends the temporary session.

## Performance and dependencies

No new runtime package or native interop API is required. Existing .NET 10, Windows App SDK, SDK contracts and build-only CsWin32 pins remain unchanged. A new Tools project references Core and Drawing.

Erasing scans retained strokes and their smoothed geometry. An empty sweep does not reconstruct or present the surface. A hit reconstructs all remaining ink; there is no idle loop. The renderer is still CPU-backed Direct2D with layered-window bitmap presentation. Spatial indexing, dirty-region presentation, GPU composition and large-session/4K benchmarks remain later performance work. Physical tablet compatibility and latency remain hardware checks.

## Complete files created or modified

| File | Purpose |
| --- | --- |
| `src/ScreenInk.Core/Models/AnnotationTool.cs` | Tool enum and immutable stroke summary |
| `src/ScreenInk.Core/ApplicationInfo.cs` | Phase 5 label |
| `src/ScreenInk.Drawing/Geometry/StrokeHitTester.cs` | Swept, pressure-aware quadratic hit testing |
| `src/ScreenInk.Tools/ScreenInk.Tools.csproj` | New native Windows Tools library |
| `src/ScreenInk.Tools/Eraser/StrokeEraser.cs` | Whole-stroke removal with identity/index preservation |
| `src/ScreenInk.Overlay/OverlayController.cs` | Tool/diameter state, eraser contacts, selected deletion, clear-all and reconstruction |
| `src/ScreenInk.Overlay/OverlayStatus.cs` | Tool and eraser diameter diagnostics |
| `src/ScreenInk.Overlay/ScreenInk.Overlay.csproj`, `ScreenInk.sln` | Tools reference and x64 solution configuration |
| `src/ScreenInk.App/MainWindow.xaml` | Tool, slider, selector and deletion controls |
| `src/ScreenInk.App/MainWindow.xaml.cs` | Forward panel actions and expose stroke summaries |
| `src/ScreenInk.App/ViewModels/MainViewModel.cs` | Tool/size bindings and stroke selection |
| `tests/ScreenInk.Overlay.Tests/Program.cs` | Geometry, eraser lifecycle and output-pixel regressions |
| `README.md`, `docs/Phase-05.md` | Usage and this guide |

Complete source is saved in these files, with no placeholder methods. Tools, Overlay, App and test lock files reflect the project graph. Existing build/run scripts remain valid.

## Exact commands

Close a running ScreenInk instance before rebuilding Debug or running native tests, to release its output files and recovery hotkeys.

```powershell
Set-Location 'C:\Users\Venkatesh\Documents\ChatGPT\Pen_ven'
.\scripts\Build.ps1 -Configuration Debug
.\scripts\Build.ps1 -Configuration Release
.\scripts\Test-Overlay.ps1
.\scripts\Run.ps1 -Configuration Debug -NoBuild
```

On a new machine, initialize the pinned local SDK with `.\scripts\Install-DotNet.ps1` first. Keep the full self-contained output directory together. The app runs without internet or administrator privileges.

## What you should see

The panel says **Phase 5 · Stroke eraser** and starts with the overlay disabled, Pen selected and diameter 24. Draw two separate strokes outside the panel. Choose Stroke eraser, then drag across one stroke: all of that stroke disappears and the other remains. The panel reports the number removed after contact ends. Choose Pen to draw again.

The selector lists retained strokes. Choose one and use Delete selected stroke to remove it. Clear all removes retained ink while retaining the optional monitor boundary. Undo is not available until the next phase. Recovery remains Ctrl+Alt+F9 for draw/click-through and Ctrl+Alt+F10 for hide.

## Verification

Debug and Release builds and the native test runner passed with zero warnings/errors. Automated checks include:

- Swept crossing between samples, negative desktop coordinates, near misses and single-dot radius.
- Pressure-dependent hit width and smoothed-curve selection instead of raw-corner selection.
- Multiple overlapping removals, untouched stroke identity/order, and invalid diameter rejection.
- Real native output pixels after mouse erasure: removed ink becomes transparent, survivors and boundary remain.
- Mouse release and tool-switch capture cleanup; movement after tool changes cannot continue the previous contact.
- Injected hardware eraser-end flags override Pen without changing the selected tool; the next tip contact draws normally.
- Coalesced pen erasure and cancellation; stable-identity deletion in click-through; clear while hidden without ink resurrection.
- All previous pen pressure/history, mouse capture, alpha output, mode switching, hotkey conflict, disposal and cross-process click-through regressions.

Pen controller frames are injected for repeatability; rendering and native overlay windows are real. These tests do not prove physical tablet delivery, latency or driver eraser support. The preview's tool and diameter bindings are checked separately through its native controls.

During preview QA, the first direct UI Automation slider change caused a native XAML stack overflow. Mouse input and a later direct automation change succeeded. The view model now notifies only changed fields, avoiding unnecessary updates to unrelated accessibility live regions. The rebuilt preview passed the same tool-selection/direct-slider sequence with the displayed diameter updating to 40. The exact cause of that first crash is unconfirmed; slider/accessibility stability should remain part of hands-on acceptance.

## Manual acceptance checklist

- [ ] Draw two strokes; erase one with a mouse. The whole touched stroke disappears and the other remains.
- [ ] Tap a dot with the eraser; sweep across thin ink quickly; erase crossing/overlapping strokes.
- [ ] Try diameter 4, 24 and 128. Size changes match physical pixels at the current display scaling.
- [ ] Draw with light and heavy stylus pressure; erase their visible portions without nearby strokes disappearing unexpectedly.
- [ ] With Pen selected, use a supported eraser end. Lift, then use the normal tip: drawing returns automatically.
- [ ] With Stroke eraser selected, use the normal tip. Hover never erases; contact does. Unsupported eraser ends can use this selector.
- [ ] Switch tools, size, click-through or hide during an eraser contact. Lift afterward: no stuck capture or stray ink.
- [ ] Select a stroke and delete it; clear all in draw, click-through and hidden modes. Re-enable: deleted ink stays gone.
- [ ] Toggle the boundary and hide/re-enable after partial erasure: remaining pressure shape and alignment stay unchanged.
- [ ] Verify Ctrl+Alt+F9/F10 recovery and ordinary input to underlying applications in click-through.
- [ ] Repeat with Wacom, XP-Pen and another Windows stylus where available, recording model/driver/Windows Ink setting. Repeat scaling and negative-origin monitor checks from Phase 4.

## Progress gate

Stop after Phase 5. Confirm erasing and recovery work before starting **Phase 6: undo/redo**.
