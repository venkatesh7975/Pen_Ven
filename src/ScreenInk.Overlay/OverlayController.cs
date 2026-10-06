using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Numerics;
using ScreenInk.Core.Models;
using ScreenInk.Native;
using ScreenInk.Drawing.Rendering;
using ScreenInk.Drawing.StrokeEngine;
using ScreenInk.Input.MouseInput;
using ScreenInk.Input.PenInput;
using ScreenInk.Tools.Eraser;
using ScreenInk.Core.History;
using ScreenInk.Tools.Shapes;

namespace ScreenInk.Overlay;

public sealed class OverlayController : IDisposable
{
    private const int ToggleHotkey = 1;
    private const int HideHotkey = 2;
    private const int UndoHotkey = 3;
    private const int RedoHotkey = 4;
    private readonly NativeWindow _visual;
    private readonly NativeWindow _input;
    private readonly nint _controlWindow;
    private readonly bool _keepControlTopmost;
    private readonly bool _toggleRegistered, _hideRegistered;
    private readonly Dictionary<int, FeatureShortcut> _featureHotkeys = [];
    private bool _disposed, _updating;
    private int _capturedClicks;
    private string _pointerStatus = "No overlay input yet.";
    private string? _error;
    private MonitorBounds? _presentedBounds;
    private bool _presentedBoundary, _presentedDrawMode;
    private readonly List<InkStroke> _strokes = [];
    private readonly InkHistory _history = new();
    private readonly List<IInkCommand> _eraseCommands = [];
    private bool _undoRegistered, _redoRegistered;
    private string _historyShortcuts = "Ctrl+Z / Ctrl+Y control ink in draw mode; buttons work in every mode.";
    private readonly MouseCapture _capture;
    private StrokeBuilder? _builder;
    private ShapeDraft? _shapeDraft;
    private Vector2? _laserPoint;
    private NativeWindow? _laserWindow;
    private InkSurface? _laserSurface;
    private readonly LaserTrail _laserTrail = new();
    private readonly TimeProvider _timeProvider;
    private bool _laserTimerRunning;
    private double LaserNow => _timeProvider.GetTimestamp() * 1000d / _timeProvider.TimestampFrequency;
    private bool _previewDirty;
    private Vector2? _erasePoint;
    private int _erasedInContact;
    private bool _eraseDirty;
    private bool ContactActive => _builder is not null || _erasePoint.HasValue || _shapeDraft is not null || _laserPoint.HasValue;
    private InkSurface? _surface;
    private readonly IPenInputReader _penReader;
    private uint? _activePenId;
    private uint _lastPenFrame;
    private ulong _lastPenCounter;
    private string _penDetails = "Pen: waiting for Windows pointer input. Enable Windows Ink in your tablet driver for pressure.";

    public OverlayMode Mode { get; private set; }
    public MonitorBounds Bounds { get; private set; }
    public bool ShowBoundary { get; private set; }
    public AnnotationTool Tool { get; private set; }
    public float EraserDiameter { get; private set; } = 24;
    public InkColor Color { get; private set; } = InkColor.Default;
    public float PenWidth { get; private set; } = 4;
    public double LaserDelayMilliseconds => _laserTrail.DelayMilliseconds;
    private InkStyle CurrentStyle => new(PenWidth, Color.Red, Color.Green, Color.Blue);
    public bool HotkeysReady => _toggleRegistered && _hideRegistered;
    public string HotkeyStatus { get; }
    public string FeatureShortcutStatus { get; }
    public event Action<FeatureAction>? FeatureShortcutInvoked;
    public OverlayStatus Status => new(Mode, Bounds, _capturedClicks, _pointerStatus, _error, _strokes.Count, _penDetails, Tool, EraserDiameter,
        _history.CanUndo || _builder is not null || _shapeDraft?.IsMeaningful == true || _eraseCommands.Count > 0,
        _history.CanRedo && _builder is null && _shapeDraft?.IsMeaningful != true && _eraseCommands.Count == 0,
        _builder is not null ? "Draw stroke" : _shapeDraft?.IsMeaningful == true ? $"Draw {_shapeDraft.Stroke.Tool}" : _eraseCommands.Count > 0 ? "Erase strokes" : _history.UndoDescription,
        _history.RedoDescription, _historyShortcuts, Color, PenWidth, LaserDelayMilliseconds);
    public event EventHandler<OverlayStatus>? StatusChanged;

    internal nint InputHandle => _input.Handle;
    internal nint VisualHandle => _visual.Handle;
    internal uint ReadInkPixel(int x, int y) => _surface?.ReadPixel(x, y) ?? 0;
    internal IReadOnlyList<InkStroke> Strokes => _strokes;
    internal bool LaserVisible => _laserTrail.Visible;
    internal bool LaserTimerRunning => _laserTimerRunning;
    internal uint ReadLaserPixel(int x, int y) => LaserVisible && _laserSurface is { } surface && x >= surface.Left && y >= surface.Top &&
        x < surface.Left + surface.Width && y < surface.Top + surface.Height ? surface.ReadPixel(x - surface.Left, y - surface.Top) : 0;

    public void SetLaserDelay(double milliseconds)
    {
        VerifyEditable();
        if (!double.IsFinite(milliseconds) || milliseconds is < 200 or > 5000) { throw new ArgumentOutOfRangeException(nameof(milliseconds)); }
        if (LaserDelayMilliseconds == milliseconds) { return; }
        _laserTrail.DelayMilliseconds = milliseconds;
        Publish();
    }

    public void SetColor(InkColor color)
    {
        VerifyEditable();
        if (Color == color) { return; }
        FinishStroke();
        ClearLaser();
        Color = color;
        Publish();
    }

    public void SetPenWidth(float width)
    {
        VerifyEditable();
        if (!float.IsFinite(width) || width is < 1 or > 32) { throw new ArgumentOutOfRangeException(nameof(width)); }
        if (PenWidth == width) { return; }
        FinishStroke();
        PenWidth = width;
        Publish();
    }

    public IReadOnlyList<StrokeSummary> GetStrokeSummaries() => _strokes
        .Select((stroke, index) => new StrokeSummary(stroke.Id, index + 1, stroke.InputKind, stroke.Samples.Count)).ToArray();

    public void SetTool(AnnotationTool tool)
    {
        VerifyEditable();
        if (!Enum.IsDefined(tool)) { throw new ArgumentOutOfRangeException(nameof(tool)); }
        if (Tool == tool) { return; }
        FinishStroke();
        ClearLaser();
        Tool = tool;
        Publish();
    }

    public void SetEraserDiameter(float diameter)
    {
        VerifyEditable();
        if (!float.IsFinite(diameter) || diameter is < 4 or > 128) { throw new ArgumentOutOfRangeException(nameof(diameter)); }
        if (EraserDiameter == diameter) { return; }
        FinishStroke();
        EraserDiameter = diameter;
        Publish();
    }

    public bool DeleteStroke(Guid id)
    {
        VerifyEditable();
        FinishStroke();
        var index = _strokes.FindIndex(stroke => stroke.Id == id);
        if (index < 0) { return false; }
        var strokeToDelete = _strokes[index];
        _strokes.RemoveAt(index);
        _history.RecordApplied(StrokeEditCommand.Removed("Delete selected stroke", [new IndexedStroke(index, strokeToDelete)]));
        _eraseDirty = true;
        PresentErasedInk();
        _pointerStatus = "Selected stroke deleted.";
        Publish();
        return true;
    }

    public void ClearAll()
    {
        VerifyEditable();
        FinishStroke();
        if (_strokes.Count == 0) { return; }
        var command = StrokeEditCommand.Removed("Clear all", _strokes.Select((stroke, index) => new IndexedStroke(index, stroke)));
        _strokes.Clear();
        _history.RecordApplied(command);
        _eraseDirty = true;
        PresentErasedInk();
        _pointerStatus = "All strokes cleared.";
        Publish();
    }

    public bool Undo() => MoveHistory(false);
    public bool Redo() => MoveHistory(true);

    private bool MoveHistory(bool redo)
    {
        VerifyEditable();
        try {
            // Commit before moving history; later pointer messages cannot extend a restored stroke.
            FinishStroke();
            var description = redo ? _history.RedoDescription : _history.UndoDescription;
            var changed = redo ? _history.Redo(_strokes) : _history.Undo(_strokes);
            if (!changed) { return false; }
            _eraseDirty = true;
            PresentErasedInk();
            _pointerStatus = $"{(redo ? "Redo" : "Undo")}: {description}";
            Publish();
            return true;
        } catch (Exception error) {
            DisableAfterError(error);
            Publish();
            return false;
        }
    }

    private void VerifyEditable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _input.VerifyThread();
    }

    public OverlayController(nint controlWindow, IPenInputReader? penReader = null, bool keepControlTopmost = false, TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _penReader = penReader ?? new PenInputReader();
        _controlWindow = controlWindow;
        _keepControlTopmost = keepControlTopmost;
        Bounds = ReadMonitorBounds();
        var commonStyle = NativeMethods.Topmost | NativeMethods.ToolWindow | NativeMethods.NoActivate;
        _visual = new NativeWindow("ScreenInk visual surface", commonStyle | NativeMethods.Layered | NativeMethods.Transparent,
            Bounds.Left, Bounds.Top, Bounds.Width, Bounds.Height);
        try {
            _input = new NativeWindow("ScreenInk input surface", commonStyle | NativeMethods.NoRedirectionBitmap,
                Bounds.Left, Bounds.Top, Bounds.Width, Bounds.Height);
        } catch {
            _visual.Dispose();
            throw;
        }
        _visual.MessageHandler = OnVisualMessage;
        _capture = new MouseCapture(_input.Handle);
        _input.MessageHandler = OnInputMessage;
        _visual.CallbackFailed += OnCallbackFailed;
        _input.CallbackFailed += OnCallbackFailed;
        // Recovery keys remain independent of optional feature shortcut conflicts.
        _toggleRegistered = NativeMethods.RegisterHotKey(_visual.Handle, ToggleHotkey, 0x4003, 0x78);
        var toggleError = _toggleRegistered ? null : new Win32Exception(Marshal.GetLastWin32Error()).Message;
        _hideRegistered = NativeMethods.RegisterHotKey(_visual.Handle, HideHotkey, 0x4003, 0x79);
        var hideError = _hideRegistered ? null : new Win32Exception(Marshal.GetLastWin32Error()).Message;
        HotkeyStatus = HotkeysReady
            ? "Ctrl + Alt + F9: toggle input · Ctrl + Alt + F10: hide overlay"
            : $"Recovery shortcuts unavailable. F9: {toggleError ?? "ready"}; F10: {hideError ?? "ready"}. Draw mode is unavailable until the conflict is resolved.";
        var unavailable = new List<string>();
        foreach (var shortcut in FeatureShortcuts.All) {
            if (NativeMethods.RegisterHotKey(_visual.Handle, shortcut.Id, FeatureShortcuts.Modifiers, shortcut.VirtualKey)) {
                _featureHotkeys.Add(shortcut.Id, shortcut);
            } else {
                unavailable.Add($"{shortcut.Gesture} ({shortcut.Label})");
            }
        }
        FeatureShortcutStatus = unavailable.Count == 0
            ? "Feature shortcuts work in every mode, including when the toolbar is hidden."
            : $"Shortcut conflict: {string.Join(", ", unavailable)} unavailable. Use the toolbar for these actions; close the other app and restart ScreenInk to retry.";
    }

    public bool ApplyFeatureShortcut(FeatureAction action)
    {
        VerifyEditable();
        AnnotationTool? tool = action switch {
            FeatureAction.Pen => AnnotationTool.Pen,
            FeatureAction.Eraser => AnnotationTool.StrokeEraser,
            FeatureAction.Laser => AnnotationTool.Laser,
            FeatureAction.Line => AnnotationTool.Line,
            FeatureAction.Arrow => AnnotationTool.Arrow,
            FeatureAction.Rectangle => AnnotationTool.Rectangle,
            FeatureAction.Ellipse => AnnotationTool.Ellipse,
            _ => null
        };
        if (tool.HasValue) { SetTool(tool.Value); SetMode(OverlayMode.Draw); return true; }
        switch (action) {
            case FeatureAction.Cursor: SetMode(OverlayMode.ClickThrough); break;
            case FeatureAction.ClearAll: ClearAll(); break;
            case FeatureAction.Undo: Undo(); break;
            case FeatureAction.Redo: Redo(); break;
            case FeatureAction.IncreaseSize:
            case FeatureAction.DecreaseSize:
                var direction = action == FeatureAction.IncreaseSize ? 1 : -1;
                if (Tool == AnnotationTool.StrokeEraser) { SetEraserDiameter(Math.Clamp(EraserDiameter + direction * 4, 4, 128)); }
                else { SetPenWidth(Math.Clamp(PenWidth + direction, 1, 32)); }
                break;
            default: return false;
        }
        return true;
    }

    public void SetMode(OverlayMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _input.VerifyThread();
        if (!Enum.IsDefined(mode)) { throw new ArgumentOutOfRangeException(nameof(mode)); }
        if (mode == OverlayMode.Draw && !HotkeysReady) {
            _error = HotkeyStatus;
            Publish();
            return;
        }
        if (_updating) { return; }
        _updating = true;
        try {
            FinishStroke();
            ClearLaser();
            _input.Hide();
            if (mode == OverlayMode.Disabled) {
                _visual.Hide();
                SetControlTopmost(false);
            } else {
                Bounds = ReadMonitorBounds();
                var drawMode = mode == OverlayMode.Draw;
                if (_presentedBounds != Bounds || _presentedBoundary != ShowBoundary || (ShowBoundary && _presentedDrawMode != drawMode)) {
                    if (_surface is null || _presentedBounds != Bounds) {
                        _surface?.Dispose();
                        _surface = new InkSurface(Bounds.Left, Bounds.Top, Bounds.Width, Bounds.Height);
                    }
                    _surface.Redraw(_strokes, ShowBoundary, drawMode);
                    _surface.Present(_visual.Handle);
                    _presentedBounds = Bounds;
                    _presentedBoundary = ShowBoundary;
                    _presentedDrawMode = drawMode;
                }
                _visual.Position(Bounds.Left, Bounds.Top, Bounds.Width, Bounds.Height, true);
                if (mode == OverlayMode.Draw) {
                    _input.Position(Bounds.Left, Bounds.Top, Bounds.Width, Bounds.Height, true);
                }
                SetControlTopmost(true);
            }
            Mode = mode;
            UpdateHistoryShortcuts(mode == OverlayMode.Draw);
            _error = null;
        } catch (Exception error) {
            DisableAfterError(error);
        } finally {
            _updating = false;
        }
        Publish();
    }

    public void SetBoundaryVisible(bool visible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _input.VerifyThread();
        ShowBoundary = visible;
        if (Mode != OverlayMode.Disabled) { SetMode(Mode); }
    }

    private MonitorBounds ReadMonitorBounds()
    {
        var monitor = NativeMethods.MonitorFromWindow(_controlWindow, 2);
        var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfoW(monitor, ref info)) {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not query the current monitor.");
        }
        return new MonitorBounds(info.Monitor.Left, info.Monitor.Top,
            info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
    }

    private void SetControlTopmost(bool topmost)
    {
        if (_controlWindow == 0) { return; }
        topmost |= _keepControlTopmost;
        var insertAfter = topmost ? NativeMethods.TopmostHandle : NativeMethods.NotTopmostHandle;
        if (!NativeMethods.SetWindowPos(_controlWindow, insertAfter, 0, 0, 0, 0,
            NativeMethods.NoMove | NativeMethods.NoSize | NativeMethods.NoActivatePosition)) {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not keep the control panel accessible.");
        }
    }

    internal nint? OnInputMessage(uint message, nuint wParam, nint lParam)
    {
        if (message is NativeMethods.PointerDown or NativeMethods.PointerUpdate or NativeMethods.PointerUp) {
            return OnPenMessage(message, unchecked((uint)wParam) & 0xFFFF);
        }
        if (message is NativeMethods.LeftButtonDown or NativeMethods.MouseMove or NativeMethods.LeftButtonUp &&
            PenInputReader.IsPromotedMouse(NativeMethods.GetMessageExtraInfo())) { return nint.Zero; }
        switch (message) {
            case NativeMethods.TabletQuerySystemGestureStatus:
                return new nint(0x10019); // Disable press-and-hold, feedback rings and flick gestures for this input surface.
            case NativeMethods.NcHitTest:
                return new nint(1); // HTCLIENT, including empty transparent areas.
            case NativeMethods.MouseActivate:
                return new nint(3); // MA_NOACTIVATE: intercept clicks without stealing keyboard focus.
            case NativeMethods.PointerActivate:
                return new nint(3); // PA_NOACTIVATE.
            case NativeMethods.PointerEnter:
                var kind = _penReader.GetDeviceKind(unchecked((uint)wParam) & 0xFFFF);
                return kind is PointerDeviceKind.Pen or PointerDeviceKind.Touch ? nint.Zero : null;
            case NativeMethods.PointerCaptureChanged:
            case NativeMethods.PointerLeave:
                if (_activePenId == (unchecked((uint)wParam) & 0xFFFF)) { FinishStroke(); }
                return nint.Zero;
            case NativeMethods.EraseBackground:
                return new nint(1);
            case NativeMethods.LeftButtonDown:
                if (Mode != OverlayMode.Draw || _surface is null) { return nint.Zero; }
                if (_activePenId.HasValue) { return nint.Zero; }
                FinishStroke();
                if (!_capture.Begin()) { throw new InvalidOperationException("Could not capture the mouse for drawing."); }
                _capturedClicks++;
                var point = MouseCapture.DesktopPoint(lParam, Bounds.Left, Bounds.Top);
                BeginContact(new InkSample(point), InkInputKind.Mouse);
                PresentContact();
                Publish();
                return nint.Zero;
            case NativeMethods.MouseMove:
                if (!ContactActive || _activePenId.HasValue) { return nint.Zero; }
                if ((wParam & 1) == 0 || !_capture.IsCaptured) { FinishStroke(); return nint.Zero; }
                AppendPoint(lParam);
                return nint.Zero;
            case NativeMethods.LeftButtonUp:
                if (ContactActive && !_activePenId.HasValue) { AppendPoint(lParam); FinishStroke(); }
                return nint.Zero;
            case NativeMethods.CaptureChanged:
                if (!_activePenId.HasValue && lParam != _input.Handle) { FinishStroke(); }
                return nint.Zero;
            case NativeMethods.CancelMode:
                FinishStroke();
                ClearLaser();
                return nint.Zero;
            case NativeMethods.DisplayChange:
            case NativeMethods.DpiChanged:
                if (Mode != OverlayMode.Disabled) { SetMode(Mode); }
                return nint.Zero;
        }
        return null;
    }

    private nint? OnPenMessage(uint message, uint pointerId)
    {
        var kind = _activePenId == pointerId ? PointerDeviceKind.Pen : _penReader.GetDeviceKind(pointerId);
        if (kind == PointerDeviceKind.Touch) { return nint.Zero; } // Touch drawing is a later input feature.
        if (kind != PointerDeviceKind.Pen) { return null; }
        if (Mode != OverlayMode.Draw || _surface is null) { return nint.Zero; }
        if (message != NativeMethods.PointerDown && _activePenId != pointerId) { return nint.Zero; }
        if (message == NativeMethods.PointerDown && ContactActive) { return nint.Zero; }
        if (!_penReader.TryRead(pointerId, message != NativeMethods.PointerDown, out var frames, out var error)) {
            if (_activePenId == pointerId) { FinishStroke(); }
            _error = error ?? "Windows pen data could not be read.";
            Publish();
            return nint.Zero;
        }
        var segments = new List<InkSegment>(frames.Length);
        void FlushSegments()
        {
            if (segments.Count > 0 && _builder is not null) {
                _surface.DrawSegments(CollectionsMarshal.AsSpan(segments), _builder.Stroke.Style);
                segments.Clear();
            }
        }
        foreach (var frame in frames) {
            if (frame.PointerId != pointerId) { continue; }
            if (frame.Canceled) {
                FlushSegments();
                if (_activePenId == pointerId) { FinishStroke(); }
                ClearLaser();
                _penDetails = DescribePen(frame.Sample);
                _pointerStatus = "Pen contact interrupted.";
                Publish();
                break;
            }
            if (message == NativeMethods.PointerDown) {
                if (_activePenId.HasValue) { break; }
                if (!frame.InContact) { continue; }
                _lastPenFrame = 0;
                _lastPenCounter = 0;
                _activePenId = pointerId;
                BeginContact(frame.Sample, InkInputKind.Pen);
                _error = null;
                _penDetails = DescribePen(frame.Sample);
                Publish();
            } else if (ContactActive) {
                // History can contain the last already-consumed frame. QPC takes precedence.
                var stale = frame.PerformanceCount != 0 && _lastPenCounter != 0
                    ? frame.PerformanceCount <= _lastPenCounter
                    : frame.FrameId != 0 && _lastPenFrame != 0 && unchecked((int)(frame.FrameId - _lastPenFrame)) <= 0;
                if (stale) { continue; }
                if (!frame.InContact && message != NativeMethods.PointerUp) { FlushSegments(); FinishStroke(); break; }
                var sample = frame.InContact || _builder is null ? frame.Sample
                    : frame.Sample with { Pressure = _builder.Stroke.Samples[^1].Pressure };
                // A driver changing to an eraser flag mid-contact must never paint with that end.
                if (!_erasePoint.HasValue && IsEraserEnd(sample)) {
                    FlushSegments();
                    FinishStroke();
                    if (!frame.InContact) { break; }
                    _activePenId = pointerId;
                    BeginErasing(sample.Position);
                } else if (_builder is not null) {
                    var segment = _builder.Append(sample);
                    if (segment.HasValue) { segments.Add(segment.Value); }
                } else if (_erasePoint.HasValue) {
                    EraseTo(sample.Position);
                } else {
                    UpdatePreview(sample.Position);
                }
                _penDetails = DescribePen(sample);
            }
            _lastPenFrame = frame.FrameId;
            _lastPenCounter = frame.PerformanceCount;
        }
        FlushSegments();
        PresentContact();
        if (message == NativeMethods.PointerUp && _activePenId == pointerId) { FinishStroke(); }
        return nint.Zero;
    }

    private static string DescribePen(InkSample sample) =>
        $"Pen pressure: {(sample.Pressure.HasValue ? $"{sample.Pressure:P0}" : "unavailable · fixed width")} · " +
        $"Tilt: {(sample.TiltX.HasValue ? sample.TiltX.ToString() : "—")}, {(sample.TiltY.HasValue ? sample.TiltY.ToString() : "—")}° · " +
        $"Barrel: {((sample.Buttons & PenButtons.Barrel) != 0 ? "on" : "off")} · " +
        $"Eraser: {((sample.Buttons & (PenButtons.Eraser | PenButtons.Inverted)) != 0 ? "on" : "off")}";

    private static bool IsEraserEnd(InkSample sample) => (sample.Buttons & (PenButtons.Eraser | PenButtons.Inverted)) != 0;

    internal nint? OnVisualMessage(uint message, nuint wParam, nint lParam)
    {
        if (_disposed) { return null; }
        if (message == NativeMethods.HotKey) {
            if (_featureHotkeys.TryGetValue((int)wParam, out var shortcut)) {
                FeatureShortcutInvoked?.Invoke(shortcut.Action);
                return nint.Zero;
            }
            if ((int)wParam == HideHotkey) { SetMode(OverlayMode.Disabled); }
            if ((int)wParam == ToggleHotkey) {
                SetMode(Mode == OverlayMode.Draw ? OverlayMode.ClickThrough : OverlayMode.Draw);
            }
            // Ignore any queued history hotkey after capture has been disabled.
            if (Mode == OverlayMode.Draw && (int)wParam == UndoHotkey && _undoRegistered) { Undo(); }
            if (Mode == OverlayMode.Draw && (int)wParam == RedoHotkey && _redoRegistered) { Redo(); }
            return nint.Zero;
        }
        if (message == NativeMethods.DisplayChange && Mode != OverlayMode.Disabled) {
            SetMode(Mode);
            return nint.Zero;
        }
        return null;
    }

    private void OnCallbackFailed(Exception error)
    {
        DisableAfterError(error);
        Publish();
    }

    private void UpdateHistoryShortcuts(bool drawing)
    {
        if (!drawing) {
            if (_undoRegistered) { NativeMethods.UnregisterHotKey(_visual.Handle, UndoHotkey); }
            if (_redoRegistered) { NativeMethods.UnregisterHotKey(_visual.Handle, RedoHotkey); }
            _undoRegistered = _redoRegistered = false;
            _historyShortcuts = "Ctrl+Z / Ctrl+Y control ink in draw mode; buttons work in every mode.";
            return;
        }
        if (!_undoRegistered) { _undoRegistered = NativeMethods.RegisterHotKey(_visual.Handle, UndoHotkey, 0x4002, 0x5A); }
        if (!_redoRegistered) { _redoRegistered = NativeMethods.RegisterHotKey(_visual.Handle, RedoHotkey, 0x4002, 0x59); }
        _historyShortcuts = _undoRegistered && _redoRegistered
            ? "Ctrl+Z: undo ink · Ctrl+Y: redo ink · active only in draw mode"
            : $"History shortcut conflict: Ctrl+Z {(_undoRegistered ? "ready" : "unavailable")}, Ctrl+Y {(_redoRegistered ? "ready" : "unavailable")}. Use the Undo/Redo buttons.";
    }

    private void DisableAfterError(Exception error)
    {
        // Input may already have changed retained ink when rendering fails. Keep its history valid.
        if (_builder is not null) { _history.RecordApplied(StrokeEditCommand.Added(_strokes.Count - 1, _builder.Stroke)); }
        if (_shapeDraft?.IsMeaningful == true) {
            _strokes.Add(_shapeDraft.Stroke);
            _history.RecordApplied(StrokeEditCommand.Added(_strokes.Count - 1, _shapeDraft.Stroke));
        }
        if (_eraseCommands.Count > 0) { _history.RecordApplied(new CompositeInkCommand("Erase strokes", _eraseCommands)); }
        _builder = null;
        _shapeDraft = null;
        _laserPoint = null;
        ClearLaser();
        _previewDirty = false;
        _erasePoint = null;
        _eraseDirty = false;
        _eraseCommands.Clear();
        _activePenId = null;
        _capture.Release();
        _input.Hide();
        _visual.Hide();
        _surface?.Dispose();
        _surface = null;
        _presentedBounds = null;
        Mode = OverlayMode.Disabled;
        UpdateHistoryShortcuts(false);
        _error = $"Overlay disabled: {error.Message}";
        SetControlTopmost(false);
    }

    private void AppendPoint(nint lParam)
    {
        if (_surface is null) { return; }
        var point = MouseCapture.DesktopPoint(lParam, Bounds.Left, Bounds.Top);
        if (_erasePoint.HasValue) { EraseTo(point); PresentErasedInk(); return; }
        if (_shapeDraft is not null || _laserPoint.HasValue) { UpdatePreview(point); PresentContact(); return; }
        if (_builder is null) { return; }
        var segment = _builder.Append(point);
        if (segment is null) { return; }
        _surface.DrawSegment(segment.Value, _builder.Stroke.Style);
        _surface.Present(_visual.Handle);
    }

    private void FinishStroke()
    {
        var builder = _builder;
        var shape = _shapeDraft;
        var laser = _laserPoint.HasValue;
        var erasing = _erasePoint.HasValue;
        _builder = null; // ReleaseCapture sends WM_CAPTURECHANGED synchronously.
        _shapeDraft = null;
        _laserPoint = null;
        _erasePoint = null;
        _activePenId = null;
        try {
            if (builder is not null && _surface is not null) {
                _history.RecordApplied(StrokeEditCommand.Added(_strokes.Count - 1, builder.Stroke));
                _surface.DrawSegment(builder.Finish(), builder.Stroke.Style);
                _surface.Present(_visual.Handle);
                _pointerStatus = $"Stroke {_strokes.Count} complete · {builder.Stroke.Samples.Count} {builder.Stroke.InputKind.ToString().ToLowerInvariant()} samples";
                Publish();
            }
            if (erasing) {
                if (_eraseCommands.Count > 0) {
                    _history.RecordApplied(new CompositeInkCommand("Erase strokes", _eraseCommands));
                    _eraseCommands.Clear();
                }
                PresentErasedInk();
                _pointerStatus = $"Eraser complete · {_erasedInContact} stroke{(_erasedInContact == 1 ? "" : "s")} removed";
                Publish();
            }
            if (shape is not null) {
                if (shape.IsMeaningful) {
                    _strokes.Add(shape.Stroke);
                    _history.RecordApplied(StrokeEditCommand.Added(_strokes.Count - 1, shape.Stroke));
                    _pointerStatus = $"{shape.Stroke.Tool} added · {_strokes.Count} annotations";
                } else { _pointerStatus = "Shape canceled · drag to set its size"; }
                _previewDirty = false;
                _eraseDirty = true;
                PresentErasedInk();
                Publish();
            }
            if (laser) {
                _laserTrail.End(LaserNow);
                _previewDirty = false;
                RenderLaser();
                _pointerStatus = "Laser released · trail fades after a short delay";
                Publish();
            }
        } finally { _capture.Release(); }
    }

    private void BeginErasing(Vector2 point)
    {
        _erasePoint = point;
        _erasedInContact = 0;
        _eraseCommands.Clear();
        EraseTo(point);
    }

    private void BeginContact(InkSample sample, InkInputKind kind)
    {
        if (Tool == AnnotationTool.StrokeEraser || IsEraserEnd(sample)) {
            BeginErasing(sample.Position);
            _pointerStatus = "Eraser active · touching ink removes its whole stroke";
        } else if (Tool.IsShape()) {
            _shapeDraft = new ShapeDraft(Tool, sample.Position, CurrentStyle, kind);
            _previewDirty = true;
            _pointerStatus = $"Drawing {Tool} · drag to set its size";
        } else if (Tool == AnnotationTool.Laser) {
            BeginLaser(sample.Position);
            _pointerStatus = "Laser active · draw temporary trails; pause to fade";
        } else {
            _builder = new StrokeBuilder(sample, CurrentStyle, kind);
            _strokes.Add(_builder.Stroke);
            _surface!.DrawDot(sample, CurrentStyle);
            _pointerStatus = kind == InkInputKind.Pen ? "Drawing with pen · pressure changes width" : "Drawing with mouse";
        }
    }

    private void UpdatePreview(Vector2 point)
    {
        if (_shapeDraft is not null) { _shapeDraft.Update(point); }
        if (_laserPoint.HasValue) {
            _laserPoint = point;
            _laserTrail.Move(point, LaserNow);
            EnsureLaserTimer();
        }
        _previewDirty = true;
    }

    private void BeginLaser(Vector2 point)
    {
        if (_laserWindow is null) {
            _laserWindow = new NativeWindow("ScreenInk laser", NativeMethods.Topmost | NativeMethods.ToolWindow |
                NativeMethods.NoActivate | NativeMethods.Layered | NativeMethods.Transparent, 0, 0, 1, 1);
            _laserWindow.CallbackFailed += OnCallbackFailed;
            _laserWindow.MessageHandler = OnLaserMessage;
        }
        _laserTrail.Begin(point, Color, LaserNow);
        _laserPoint = point;
        _previewDirty = true;
        EnsureLaserTimer();
        RenderLaser();
    }

    private void EnsureLaserTimer()
    {
        if (!_laserTimerRunning) {
            if (NativeMethods.SetTimer(_laserWindow!.Handle, 1, 33, 0) == 0) { throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start the laser fade timer."); }
            _laserTimerRunning = true;
        }
    }

    private nint? OnLaserMessage(uint message, nuint wParam, nint lParam)
    {
        if (message != NativeMethods.Timer || wParam != 1) { return null; }
        if (!_disposed && _laserTimerRunning) { AdvanceLaser(); }
        return nint.Zero;
    }

    internal void AdvanceLaser()
    {
        VerifyEditable();
        var changed = _laserTrail.Advance(LaserNow);
        if (!_laserTrail.Visible) { ClearLaser(); return; }
        if (changed || _previewDirty) { RenderLaser(); }
    }

    private void ClearLaser()
    {
        _laserTrail.Clear();
        _laserWindow?.Hide();
        if (_laserTimerRunning && _laserWindow is not null) { NativeMethods.KillTimer(_laserWindow.Handle, 1); }
        _laserTimerRunning = false;
    }

    private void RenderLaser()
    {
        if (!_laserTrail.Visible || _laserWindow is null) { return; }
        var minimum = new Vector2(float.MaxValue);
        var maximum = new Vector2(float.MinValue);
        foreach (var path in _laserTrail.Paths) {
            foreach (var point in path.Points) { minimum = Vector2.Min(minimum, point); maximum = Vector2.Max(maximum, point); }
        }
        // Tile-aligned bounds reduce bitmap reallocations while drawing. Clamp to this monitor.
        var left = Math.Max(Bounds.Left, (int)(Math.Floor((minimum.X - 12) / 64) * 64));
        var top = Math.Max(Bounds.Top, (int)(Math.Floor((minimum.Y - 12) / 64) * 64));
        var right = Math.Min(Bounds.Left + Bounds.Width, (int)(Math.Ceiling((maximum.X + 12) / 64) * 64));
        var bottom = Math.Min(Bounds.Top + Bounds.Height, (int)(Math.Ceiling((maximum.Y + 12) / 64) * 64));
        if (right <= left || bottom <= top) { _laserWindow.Hide(); return; }
        if (_laserSurface is null || _laserSurface.Left != left || _laserSurface.Top != top ||
            _laserSurface.Width != right - left || _laserSurface.Height != bottom - top) {
            _laserSurface?.Dispose();
            _laserSurface = new InkSurface(left, top, right - left, bottom - top);
        }
        _laserSurface.DrawLaserTrail(_laserTrail);
        _laserSurface.Present(_laserWindow.Handle);
        _laserWindow.Position(left, top, right - left, bottom - top, true);
        _previewDirty = false;
    }

    private void PresentContact()
    {
        if (_erasePoint.HasValue) { PresentErasedInk(); return; }
        if (_laserPoint.HasValue) {
            // The native timer coalesces pen history and mouse movement into one frame.
            return;
        }
        if (_shapeDraft is not null) {
            if (_previewDirty) { _surface!.Redraw(_strokes.Append(_shapeDraft.Stroke), ShowBoundary, Mode == OverlayMode.Draw); }
            _previewDirty = false;
        }
        _surface?.Present(_visual.Handle);
    }

    private void EraseTo(Vector2 point)
    {
        if (!_erasePoint.HasValue) { return; }
        var removed = StrokeEraser.Erase(_strokes, _erasePoint.Value, point, EraserDiameter);
        _erasePoint = point;
        _erasedInContact += removed.Count;
        if (removed.Count > 0) {
            _eraseCommands.Add(StrokeEditCommand.Removed("Erase strokes", removed.Select(entry => new IndexedStroke(entry.Index, entry.Stroke))));
        }
        _eraseDirty |= removed.Count > 0;
    }

    private void PresentErasedInk()
    {
        if (!_eraseDirty) { return; }
        if (_surface is not null) {
            _surface.Redraw(_strokes, ShowBoundary, Mode == OverlayMode.Draw);
            if (Mode != OverlayMode.Disabled) { _surface.Present(_visual.Handle); }
        }
        _eraseDirty = false;
    }

    private void Publish() => StatusChanged?.Invoke(this, Status);

    public void Dispose()
    {
        if (_disposed) { return; }
        _input.VerifyThread();
        _builder = null;
        _shapeDraft = null;
        _laserPoint = null;
        ClearLaser();
        _erasePoint = null;
        _activePenId = null;
        _eraseCommands.Clear();
        _history.Clear();
        UpdateHistoryShortcuts(false);
        _capture.Dispose();
        _input.Hide();
        _visual.Hide();
        if (_toggleRegistered) { NativeMethods.UnregisterHotKey(_visual.Handle, ToggleHotkey); }
        if (_hideRegistered) { NativeMethods.UnregisterHotKey(_visual.Handle, HideHotkey); }
        foreach (var id in _featureHotkeys.Keys) { NativeMethods.UnregisterHotKey(_visual.Handle, id); }
        _featureHotkeys.Clear();
        FeatureShortcutInvoked = null;
        _input.Dispose();
        _visual.Dispose();
        _surface?.Dispose();
        _laserWindow?.Dispose();
        _laserSurface?.Dispose();
        _disposed = true;
        Mode = OverlayMode.Disabled;
    }
}
