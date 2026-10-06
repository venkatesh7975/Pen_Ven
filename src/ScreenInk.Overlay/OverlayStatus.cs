using ScreenInk.Core.Models;

namespace ScreenInk.Overlay;

public sealed record OverlayStatus(OverlayMode Mode, MonitorBounds Bounds, int CapturedClicks,
    string PointerStatus, string? Error, int StrokeCount = 0, string PenDetails = "",
    AnnotationTool Tool = AnnotationTool.Pen, float EraserDiameter = 24,
    bool CanUndo = false, bool CanRedo = false, string? UndoDescription = null, string? RedoDescription = null,
    string HistoryShortcuts = "Ctrl+Z / Ctrl+Y control ink in draw mode; buttons work in every mode.",
    InkColor Color = default, float PenWidth = 4, double LaserDelayMilliseconds = 1200);
