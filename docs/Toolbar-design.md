# Toolbar design direction and implementation

User direction, confirmed October 6, 2026: **“its design should be like epic pen ui.”**

Use an Epic Pen-inspired compact floating tool strip as ScreenInk's primary annotation interface. Epic Pen's [official user guide](https://epicpen.com/userguide) describes a small toolbar for quick access to annotation tools; that interaction model is the reference. This brief is an implementation direction, not a claim of pixel-perfect reproduction. ScreenInk keeps its own name and native WinUI controls.

## Layout

- A narrow vertical floating strip, implemented at 72 DIPs wide, with rounded corners and a subtle border.
- A small drag grip at the top; the toolbar can move without drawing on the desktop.
- Icon buttons 48 × 40 DIPs, with consistent spacing, visible selected-tool state, hover feedback, tooltips and accessible names.
- Cursor, Pen, Eraser, Shapes, Laser, Color, Size, Undo, Redo, Clear, Hide, More and Exit are implemented controls.
- Pen/shape width and eraser size open in a shared Size flyout. Secondary controls and diagnostics live under More.
- Red is the default; quick colors and a native custom picker change actual rendered ink. The user's additional request brought shapes and laser forward into this phase.
- Use native light/dark theme resources, simple monochrome icons and one restrained accent for selection. Avoid large headings, instructional cards and diagnostic paragraphs in the primary toolbar.

## Native behavior

Keep the toolbar above the annotation surface and reachable while drawing. Pointer input inside it operates controls; it must not leak through to the desktop or create an ink stroke. Switching to cursor/click-through returns desktop input to underlying apps while ink stays visible.

Flyouts choose the side that fits the monitor work area. Clamp the toolbar to a reachable position after movement or display changes, accounting for physical pixels versus DIPs. Preserve keyboard navigation, pen-friendly targets and the existing recovery shortcuts. Phase 7 should make the compact strip the default experience, with the existing diagnostics available as a secondary panel.

## Phase boundary

Phase 6 supplied history. Phase 7 now implements the floating toolbar, with shapes, laser and colors added under the user's expanded request. The native rendering, input and command history stay behind the same controller APIs. See [Phase-07.md](Phase-07.md) for behavior, implementation and verification.
