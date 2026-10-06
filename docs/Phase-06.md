# Phase 6: Undo and redo

## Goal and visible behavior

Undo/redo drawing, whole-stroke erasure, selected-stroke deletion and clear-all. Preserve original stroke identity, order, pressure data and reconstructed output. One completed drawing contact is one action. One complete eraser drag is one action even when it removes several strokes over several input samples.

Use the new icon/text Undo and Redo buttons or Ctrl+Z / Ctrl+Y. Buttons work while drawn, click-through or hidden. The panel also supports local WinUI keyboard accelerators when it has keyboard focus. Global Ctrl+Z/Y registrations exist only in Draw mode because the overlay deliberately does not take keyboard focus from underlying apps. They are released on click-through, hide, errors and disposal. Ctrl+Alt+F9/F10 recovery stays unchanged.

If another application owns a history shortcut, the panel reports which shortcut is unavailable and keeps the buttons working. Shortcut conflicts do not disable drawing. A queued history hotkey arriving after Draw mode ends is ignored.

The user requested an Epic Pen-like interface. The compact, floating, icon-based toolbar direction is recorded in [Toolbar-design.md](Toolbar-design.md). The floating toolbar is Phase 7; this phase supplies the history operations it will call.

## Architecture and decisions

- **Core/History:** `IInkCommand` defines Undo/Redo, a description and retained sample cost. `StrokeEditCommand` retains changed stroke objects and their list indices. `CompositeInkCommand` groups multiple changes, applying reverse order during undo and forward order during redo.
- **InkHistory:** maintains undo and redo linked lists. Moving a command between branches reuses its node. Recording an already-applied edit clears the redo branch and evicts old entries as needed.
- **Overlay:** commits a completed drawing contact or grouped eraser contact, records selected deletion/clear-all, owns conditional shortcut registrations and reconstructs ink after a history move.
- **App/view model:** binds action availability, descriptions and shortcut status, with existing changed-field notifications to avoid unnecessary accessibility live-region updates.

Erasure indices are relative to the canvas at each input sample. Undo reverses the per-sample batches before inserting their strokes at recorded positions. This restores the original order even when a later sample removes an earlier list entry. The command stores object references; pressure samples are never flattened or deep-copied into canvas snapshots.

History commits after logical contact ends. Calling Undo during drawing finishes the current contact, then removes that new stroke. Calling Undo during a hit-producing eraser drag finishes and restores that drag. Mouse capture is released and late mouse/pen updates cannot extend the undone contact. Cancellation, mode changes, pen read failure and capture loss commit the ink already changed, making it recoverable. A render failure also records any pending retained change before disabling the overlay, keeping the retained list and command indices consistent.

New ink edits after undo invalidate redo. Empty eraser gestures, deleting a missing identity, an already-empty clear, and tool/size/boundary/mode changes do not create commands. A no-hit eraser contact followed by Undo ends the contact and undoes the preceding actual edit.

## Retention and performance

Default history is bounded to 256 actions with a soft budget of 250,000 retained sample references. The budget counts samples represented by each command conservatively; it is not a byte-accurate memory limit. A stroke referenced by drawing and deletion commands can contribute more than once. The newest command is always retained, even when a single large clear exceeds the soft budget, so the most recent destructive action remains undoable.

Eviction removes history references and does not remove old visible ink. Redo references are released on a new edit. Both branches are released on disposal. There is no persistent history/session storage yet.

An append/remove-last drawing command uses list-end operations; arbitrary removal/restoration shifts list entries as needed. History stores changed strokes rather than copying every canvas/sample on each edit. The deterministic 2,500-stroke record/undo/redo model check took about 6.3 ms on this machine; this excludes native rendering and is not an end-to-end latency promise. Undo/redo reconstructs the current retained canvas on the existing CPU-backed Direct2D surface. Large-session/4K rendering performance and GPU composition remain later performance work.

## Complete source files created or modified

| File | Purpose |
| --- | --- |
| `src/ScreenInk.Core/History/InkCommand.cs` | Indexed stroke changes and grouped eraser commands |
| `src/ScreenInk.Core/History/InkHistory.cs` | Bounded undo/redo stacks and branch invalidation |
| `src/ScreenInk.Core/ApplicationInfo.cs` | Phase 6 label |
| `src/ScreenInk.Overlay/OverlayController.cs` | History commits, Undo/Redo, redraws and scoped global shortcuts |
| `src/ScreenInk.Overlay/OverlayStatus.cs` | History availability, descriptions and shortcut diagnostics |
| `src/ScreenInk.App/MainWindow.xaml` | Accessible icon/text action buttons and local accelerators |
| `src/ScreenInk.App/MainWindow.xaml.cs` | Button/accelerator forwarding |
| `src/ScreenInk.App/ViewModels/MainViewModel.cs` | History bindings with changed-field notifications |
| `tests/ScreenInk.Overlay.Tests/Program.cs` | Core model, retention, shortcut ownership and native history regressions |
| `tests/ScreenInk.Overlay.Tests/ScreenInk.Overlay.Tests.csproj` | Compile the UI-independent view model into the native regression runner |
| `README.md`, `docs/Phase-06.md`, `docs/Toolbar-design.md` | Current usage, this guide and Epic Pen-inspired design direction |

All implementation is saved in complete source files. No new project, NuGet package or native interop declaration is needed. Existing SDK/package pins and lock files remain unchanged.

## Exact commands

Close a running ScreenInk instance before rebuilding Debug or running native tests.

```powershell
Set-Location 'C:\Users\Venkatesh\Documents\ChatGPT\Pen_ven'
.\scripts\Build.ps1 -Configuration Debug
.\scripts\Build.ps1 -Configuration Release
.\scripts\Test-Overlay.ps1
.\scripts\Run.ps1 -Configuration Debug -NoBuild
```

On a new machine, use `.\scripts\Install-DotNet.ps1` first. Keep the full self-contained output directory together. The app needs neither administrator privileges nor internet to run.

## Automated verification

Debug and Release builds and the native test runner passed with zero warnings/errors. Checks include:

- Empty history, stable identity/order, grouped removal with changing indices, clear-all restoration and redo-branch invalidation.
- Command-count eviction, soft sample-budget behavior and history release.
- 1,200 mixed draw/delete/clear/undo/redo operations compared against independent canvas snapshots.
- 2,500 strokes recorded, undone and redone in the Core model without copying their samples.
- Native shortcut conflict detection, recovery after conflict release, Ctrl+Z/Y ownership in Draw mode, release in click-through and queued-message rejection.
- One undo restoring a complete multi-sample eraser drag, original ordering and exact native output pixels; redo removing them again without ghost ink.
- Selected deletion and clear-all history in hidden mode, with restored ink appearing on re-enable.
- Undo during mouse, pen and eraser contacts; capture release; late input rejection; pressure sample preservation.
- Empty gestures/tool/mode/size/boundary changes preserving redo and new drawing invalidating it.
- All previous geometry, pressure/history acquisition conversion, mouse capture, alpha rendering, erasing and cross-process click-through regressions.
- View-model selection echoes from a synchronous two-way binding terminate after one notification; new collection items preserve selection by identity; slider updates leave unrelated live regions alone.

Controller pen frames are injected; renderer pixels and native windows are real. These checks do not prove actual tablet delivery, physical implicit capture, end-to-end latency or focus/keyboard behavior in every other app. The preview is launched for hands-on acceptance.

The Phase 5 XAML stack overflow recurred during Phase 6 preview verification. The stroke selector used a two-way binding whose setter always published a change, even when the native selector synchronously echoed the same object back. That reentrancy path is now guarded by reference equality; the regression runner exercises a synchronous selection echo and collection replacement. The rebuilt preview was observed recording a drawing action and showing its successful Undo with Redo enabled. Continued hands-on slider/selection checks remain useful; a short successful preview is not proof against every native XAML failure.

## Manual acceptance checklist

- [ ] Draw three separate strokes. Undo three times; redo three times. Original pressure shapes and order return.
- [ ] Erase several strokes in one drag. One Undo restores all of that drag; one Redo removes them again.
- [ ] Delete the middle stroke, undo and redo. Clear all, undo and redo.
- [ ] Undo while drawing/erasing, lift, then move. No stuck capture, continuation or duplicate stroke.
- [ ] Undo then draw new ink: redo becomes unavailable. Undo then sweep empty space or change tools/modes: redo remains available.
- [ ] With another editor focused, Draw mode Ctrl+Z/Y controls annotations. Switch to click-through/hide: the editor's own Ctrl+Z/Y works again.
- [ ] Focus ScreenInk in click-through/hidden mode and use Ctrl+Z/Y: local panel history works.
- [ ] Simulate a shortcut conflict if applicable; its warning appears and buttons still work.
- [ ] Hide, undo/redo with buttons, then re-enable. Restored/deleted pixels are correct and the boundary remains aligned.
- [ ] Repeat with a physical pen tablet, display scaling, negative-origin monitors and longer teaching sessions. Verify Ctrl+Alt+F9/F10 recovery.

## Official sources

- [Epic Pen user guide: compact toolbar reference](https://epicpen.com/userguide)
- [RegisterHotKey: modifier flags, conflict behavior and registration scope](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
- [KeyboardAccelerator API](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.input.keyboardaccelerator)

## Progress gate

Stop after Phase 6. Confirm history and shortcut behavior before starting **Phase 7: Epic Pen-inspired floating toolbar**.
