using System.Diagnostics;
using System.Reflection;
using ScreenInk.Core.Models;
using ScreenInk.Native;
using ScreenInk.Overlay;
using System.Numerics;
using ScreenInk.Drawing.Rendering;
using ScreenInk.Drawing.StrokeEngine;
using ScreenInk.Input.MouseInput;
using ScreenInk.Input.PenInput;
using ScreenInk.Drawing.Geometry;
using ScreenInk.Tools.Eraser;
using ScreenInk.Core.History;
using ScreenInk.App.ViewModels;
using ScreenInk.Tools.Shapes;

NativeMethods.SetProcessDpiAwarenessContext(new nint(-4));
if (args.Contains("--fixture")) {
    RunFixture();
    return;
}

CheckStrokeEngine();
CheckRenderer();
CheckPenMetadata();
CheckEraserGeometry();
CheckInkHistory();
CheckViewModel();
CheckShapeRendering();
CheckScreenshotCapture();
CheckLaserTrail();
if (args.Contains("--render-only")) {
    Console.WriteLine("PASS: Rendering and model checks completed; native controller checks were not requested.");
    return;
}

using var fixture = StartFixture();
try {
    var ready = fixture.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
    if (ready is null || !ready.StartsWith("READY:")) { throw new InvalidOperationException("The separate-process input fixture did not start."); }
    var fixtureHandle = new nint(long.Parse(ready[6..]));
    nint disposedInput;
    using (var overlay = new OverlayController(0)) {
        Require(overlay.HotkeysReady, overlay.HotkeyStatus);
        Require(overlay.Mode == OverlayMode.Disabled, "Overlay starts disabled.");
        var emptyArea = new NativeMethods.Point(overlay.Bounds.Left + overlay.Bounds.Width / 2,
            overlay.Bounds.Top + overlay.Bounds.Height / 2);
        var borderArea = new NativeMethods.Point(overlay.Bounds.Left + 1, overlay.Bounds.Top + overlay.Bounds.Height / 2);
        Require(NativeMethods.WindowFromPoint(emptyArea) == fixtureHandle, "Disabled overlay leaves another process's window reachable.");

        overlay.SetMode(OverlayMode.Draw);
        Require(overlay.Mode == OverlayMode.Draw && overlay.Status.Error is null, "Draw mode activates without errors.");
        Require(NativeMethods.WindowFromPoint(emptyArea) == overlay.InputHandle, "An empty transparent area intercepts input in draw mode.");

        var centerX = overlay.Bounds.Width / 2;
        var centerY = overlay.Bounds.Height / 2;
        overlay.OnInputMessage(NativeMethods.LeftButtonDown, 1, MousePosition(centerX, centerY));
        Require(NativeMethods.GetCapture() == overlay.InputHandle, "Mouse down captures the native input surface.");
        Require(overlay.ReadInkPixel(centerX, centerY) >> 24 > 0, "Mouse down paints a visible dot.");
        overlay.OnInputMessage(NativeMethods.MouseMove, 1, MousePosition(centerX + 40, centerY));
        overlay.OnInputMessage(NativeMethods.LeftButtonUp, 0, MousePosition(centerX + 80, centerY));
        Require(NativeMethods.GetCapture() != overlay.InputHandle, "Mouse up releases capture.");
        Require(overlay.Status.StrokeCount == 1 && overlay.Status.PointerStatus.Contains("complete"), "Mouse drag commits one retained stroke.");
        Require(overlay.ReadInkPixel(centerX + 79, centerY) >> 24 > 0, "Mouse up renders the final endpoint.");

        overlay.OnInputMessage(NativeMethods.LeftButtonDown, 1, MousePosition(centerX, centerY + 30));
        overlay.OnInputMessage(NativeMethods.MouseMove, 1, MousePosition(centerX + 40, centerY + 30));
        overlay.SetMode(OverlayMode.ClickThrough);
        Require(NativeMethods.GetCapture() != overlay.InputHandle && overlay.Status.StrokeCount == 2, "Changing modes ends the stroke and releases capture.");
        overlay.OnInputMessage(NativeMethods.MouseMove, 1, MousePosition(centerX + 100, centerY + 30));
        Require(overlay.ReadInkPixel(centerX + 100, centerY + 30) == 0, "Movement after a mode switch does not extend ink.");
        Require(NativeMethods.WindowFromPoint(emptyArea) == fixtureHandle, "Visible ink passes through to a different process.");
        overlay.SetMode(OverlayMode.Draw);
        overlay.OnInputMessage(NativeMethods.LeftButtonDown, 1, MousePosition(centerX, centerY + 60));
        NativeMethods.ReleaseCapture();
        Require(overlay.Status.PointerStatus.Contains("complete"), "Capture loss finishes the active stroke.");
        overlay.OnInputMessage(NativeMethods.MouseMove, 1, MousePosition(centerX + 100, centerY + 60));
        Require(overlay.ReadInkPixel(centerX + 100, centerY + 60) == 0, "Capture loss does not leave a stuck stroke.");

        overlay.SetBoundaryVisible(true);
        Require(NativeMethods.WindowFromPoint(borderArea) == overlay.InputHandle, "Visible boundary does not prevent input capture.");
        overlay.SetMode(OverlayMode.ClickThrough);
        Require(overlay.Mode == OverlayMode.ClickThrough, "Click-through mode activates.");
        Require(NativeMethods.WindowFromPoint(emptyArea) == fixtureHandle, "An empty area passes through to a different process.");
        Require(NativeMethods.WindowFromPoint(borderArea) == fixtureHandle, "A visible alpha-blended pixel also passes through to a different process.");

        for (var index = 0; index < 10; index++) {
            overlay.SetMode(OverlayMode.Draw);
            Require(NativeMethods.WindowFromPoint(emptyArea) == overlay.InputHandle, $"Mode cycle {index + 1}: draw routing.");
            overlay.SetMode(OverlayMode.ClickThrough);
            Require(NativeMethods.WindowFromPoint(emptyArea) == fixtureHandle, $"Mode cycle {index + 1}: cross-process click-through routing.");
        }

        using (var conflictingOverlay = new OverlayController(0)) {
            Require(!conflictingOverlay.HotkeysReady, "A second controller detects the recovery hotkey conflict.");
            conflictingOverlay.SetMode(OverlayMode.Draw);
            Require(conflictingOverlay.Mode == OverlayMode.Disabled && conflictingOverlay.Status.Error is not null,
                "Draw capture stays disabled when recovery hotkeys cannot be registered.");
        }
        overlay.SetMode(OverlayMode.Disabled);
        Require(overlay.Status.StrokeCount == 3, "Mode changes and hiding preserve the session's three strokes.");
        Require(NativeMethods.WindowFromPoint(emptyArea) == fixtureHandle, "Hide releases desktop input.");
        disposedInput = overlay.InputHandle;
    }

    using (var recreated = new OverlayController(0)) {
        Require(recreated.HotkeysReady, "Disposal releases global shortcuts for the next controller.");
        var point = new NativeMethods.Point(recreated.Bounds.Left + recreated.Bounds.Width / 2,
            recreated.Bounds.Top + recreated.Bounds.Height / 2);
        Require(NativeMethods.WindowFromPoint(point) == fixtureHandle, "Disposed overlay windows do not intercept input.");
        Require(disposedInput != 0, "The test exercised a real native input window.");
    }
    CheckPenController();
    CheckEraserController();
    CheckUndoController();
    CheckToolbarTools();
    CheckFeatureShortcuts();
    Console.WriteLine("PASS: Floating toolbar tools, shapes, color, laser, history and native input regression checks completed.");
} finally {
    fixture.StandardInput.WriteLine("exit");
    fixture.StandardInput.Flush();
    if (!fixture.WaitForExit(5000)) { fixture.Kill(); }
}

static void Require(bool condition, string message)
{
    if (!condition) { throw new InvalidOperationException($"FAIL: {message}"); }
    Console.WriteLine($"PASS: {message}");
}

static nint MousePosition(int x, int y) => new(unchecked((int)(((uint)(ushort)y << 16) | (ushort)x)));

static void CheckLaserTrail()
{
    var trail = new LaserTrail();
    var red = new InkColor(255, 30, 30);
    trail.Begin(new(-80, 30), red, 0);
    trail.Move(new(-20, 30), 100);
    trail.End(200);
    trail.Advance(1299);
    Require(trail.Visible && trail.PointCount == 2 && trail.Opacity == 1, "Laser marks remain intact throughout the delay after release.");
    trail.Begin(new(-80, 60), red, 1300);
    trail.Move(new(-20, 60), 1400);
    trail.End(1400);
    Require(trail.Paths.Count == 2 && trail.PointCount == 4, "Quick successive laser strokes share a session and remain separate paths.");
    trail.Advance(2500);
    Require(trail.Opacity == 1 && trail.PointCount == 4, "A new stroke restarts the delay for the whole group.");
    using var surface = new InkSurface(-100, 0, 120, 100);
    surface.DrawLaserTrail(trail);
    var fullAlpha = surface.ReadPixel(50, 30) >> 24;
    Require(fullAlpha > 100 && surface.ReadPixel(50, 45) == 0 && surface.ReadPixel(1, 1) == 0,
        "Laser paths render connected glow without a line between separate strokes or a background rectangle.");
    trail.Advance(2850);
    surface.DrawLaserTrail(trail);
    var fadedPixel = surface.ReadPixel(50, 60);
    Require(trail.Visible && trail.PointCount < 4 && trail.Opacity is > 0 and < 1 &&
        (fadedPixel >> 24) > 0 && (fadedPixel >> 24) < fullAlpha && ((fadedPixel >> 16) & 255) <= (fadedPixel >> 24),
        "Grouped fade removes older points and reduces premultiplied glow alpha.");
    trail.Advance(3100);
    surface.DrawLaserTrail(trail);
    Require(!trail.Visible && trail.Paths.Count == 0 && surface.ReadPixel(50, 60) == 0, "The complete group disappears after the fixed fade duration.");
    trail.DelayMilliseconds = 3000;
    trail.Begin(new(0, 0), red, 4000); trail.End(4000); trail.Advance(6500);
    Require(trail.Visible && trail.Opacity == 1, "The configurable delay keeps marks visible for longer.");
    trail.Advance(7500);
    Require(!trail.Visible, "Skipped animation frames still expire the trail at the correct time.");
    trail.DelayMilliseconds = 1200;
    trail.Begin(new(0, 0), red, 8000);
    for (var i = 1; i < LaserTrail.MaxPoints + 50; i++) { trail.Move(new(i, 0), 8000 + i * .01); }
    Require(trail.PointCount == LaserTrail.MaxPoints, "Long laser drags have bounded sample storage.");
    trail.Clear();
    for (var i = 0; i < LaserTrail.MaxPaths + 10; i++) { trail.Begin(new(i, 0), red, 10000); trail.End(10000); }
    Require(trail.Paths.Count == LaserTrail.MaxPaths, "Repeated laser dots have bounded path storage.");
    trail.Clear();
    Require(!trail.Visible, "Canceling a laser session removes all temporary marks immediately.");
}

static void CheckFeatureShortcuts()
{
    using var probe = new NativeWindow("Feature shortcut conflict probe", 0, 0, 0, 1, 1);
    var penShortcut = FeatureShortcuts.All.Single(shortcut => shortcut.Action == FeatureAction.Pen);
    Require(NativeMethods.RegisterHotKey(probe.Handle, 201, FeatureShortcuts.Modifiers, penShortcut.VirtualKey),
        "A separate window can reserve the pen shortcut before ScreenInk starts.");
    try {
        using var conflicted = new OverlayController(0);
        Require(conflicted.HotkeysReady && conflicted.FeatureShortcutStatus.Contains(penShortcut.Gesture),
            "A feature shortcut conflict is reported without disabling recovery or drawing.");
        var invoked = false;
        conflicted.FeatureShortcutInvoked += _ => invoked = true;
        conflicted.OnVisualMessage(NativeMethods.HotKey, (nuint)penShortcut.Id, 0);
        Require(!invoked, "Messages for an unavailable shortcut cannot invoke an action.");
        conflicted.SetMode(OverlayMode.Draw);
        Require(conflicted.Mode == OverlayMode.Draw, "Drawing remains available when an optional shortcut conflicts.");
    } finally { NativeMethods.UnregisterHotKey(probe.Handle, 201); }

    var overlay = new OverlayController(0);
    try {
        Require(!overlay.FeatureShortcutStatus.Contains("conflict"), "All feature shortcuts register after the conflicting window releases its key.");
        Require(!NativeMethods.RegisterHotKey(probe.Handle, 201, FeatureShortcuts.Modifiers, penShortcut.VirtualKey),
            "Windows reserves the registered feature shortcut while ScreenInk is running.");
        var actions = new List<FeatureAction>();
        overlay.FeatureShortcutInvoked += action => { actions.Add(action); overlay.ApplyFeatureShortcut(action); };
        void Invoke(FeatureAction action) => overlay.OnVisualMessage(NativeMethods.HotKey,
            (nuint)FeatureShortcuts.All.Single(shortcut => shortcut.Action == action).Id, 0);

        Invoke(FeatureAction.Pen);
        Require(overlay.Mode == OverlayMode.Draw && overlay.Tool == AnnotationTool.Pen, "The pen shortcut enters drawing from a hidden overlay.");
        overlay.OnInputMessage(NativeMethods.LeftButtonDown, 1, MousePosition(150, 150));
        overlay.OnInputMessage(NativeMethods.MouseMove, 1, MousePosition(180, 150));
        Invoke(FeatureAction.Eraser);
        Require(overlay.Tool == AnnotationTool.StrokeEraser && overlay.Status.StrokeCount == 1 && NativeMethods.GetCapture() != overlay.InputHandle,
            "A tool shortcut commits active ink and releases pointer capture.");
        Invoke(FeatureAction.IncreaseSize);
        Require(overlay.EraserDiameter == 28 && overlay.PenWidth == 4, "Size shortcuts adjust the eraser independently of pen width.");
        overlay.SetEraserDiameter(128); Invoke(FeatureAction.IncreaseSize);
        overlay.SetEraserDiameter(4); Invoke(FeatureAction.DecreaseSize);
        Require(overlay.EraserDiameter == 4, "Eraser size shortcuts respect both limits.");
        foreach (var (action, tool) in new[] {
            (FeatureAction.Line, AnnotationTool.Line), (FeatureAction.Arrow, AnnotationTool.Arrow),
            (FeatureAction.Rectangle, AnnotationTool.Rectangle), (FeatureAction.Ellipse, AnnotationTool.Ellipse),
            (FeatureAction.Laser, AnnotationTool.Laser)
        }) {
            Invoke(action);
            Require(overlay.Mode == OverlayMode.Draw && overlay.Tool == tool, $"The {tool} shortcut selects its drawing tool.");
        }
        overlay.SetPenWidth(32); Invoke(FeatureAction.IncreaseSize);
        Require(overlay.PenWidth == 32, "Pen size shortcut cannot exceed the maximum width.");
        overlay.SetPenWidth(1); Invoke(FeatureAction.DecreaseSize);
        Require(overlay.PenWidth == 1, "Pen size shortcut cannot go below the minimum width.");
        Invoke(FeatureAction.Cursor);
        Require(overlay.Mode == OverlayMode.ClickThrough, "The cursor shortcut releases desktop input.");
        Invoke(FeatureAction.Undo);
        Require(overlay.Status.StrokeCount == 0 && overlay.Mode == OverlayMode.ClickThrough, "Global undo works in cursor mode without enabling input capture.");
        overlay.SetMode(OverlayMode.Disabled);
        Invoke(FeatureAction.Redo);
        Require(overlay.Status.StrokeCount == 1 && overlay.Mode == OverlayMode.Disabled, "Global redo restores ink while the overlay stays hidden.");
        Invoke(FeatureAction.ClearAll); Invoke(FeatureAction.Undo);
        Require(overlay.Status.StrokeCount == 1, "Clear-all shortcut remains undoable while hidden.");
        foreach (var action in new[] { FeatureAction.Screenshot, FeatureAction.Media, FeatureAction.Color, FeatureAction.Size, FeatureAction.Help, FeatureAction.DeleteSelected }) {
            Invoke(action);
            Require(actions[^1] == action && overlay.Mode == OverlayMode.Disabled, $"The {action} shortcut reaches the application without changing overlay mode itself.");
        }
        var count = actions.Count;
        overlay.OnVisualMessage(NativeMethods.HotKey, 9999, 0);
        Require(actions.Count == count, "Unknown hotkey messages are ignored.");
        overlay.Dispose();
        overlay.OnVisualMessage(NativeMethods.HotKey, (nuint)penShortcut.Id, 0);
        Require(actions.Count == count, "Queued hotkeys cannot invoke a disposed controller.");
        Require(NativeMethods.RegisterHotKey(probe.Handle, 201, FeatureShortcuts.Modifiers, penShortcut.VirtualKey),
            "Disposal releases feature shortcuts for other applications.");
        NativeMethods.UnregisterHotKey(probe.Handle, 201);
    } finally { overlay.Dispose(); }
}

static void CheckStrokeEngine()
{
    var builder = new StrokeBuilder(new Vector2(-120, 40), InkStyle.MousePen);
    Require(builder.Append(new Vector2(-120, 40)) is null, "Duplicate samples are ignored.");
    var first = builder.Append(new Vector2(-100, 40))!.Value;
    var second = builder.Append(new Vector2(-100, 60))!.Value;
    Require(first.Start == new Vector2(-120, 40) && first.End == second.Start, "Smoothing preserves the first point and connects segments.");
    Require(builder.Finish().End == new Vector2(-100, 60), "Smoothing finishes at the last mouse sample.");
    Require(StrokeBuilder.Replay(builder.Stroke).SequenceEqual(new[] { first, second, builder.Finish() }), "Retained replay matches incremental geometry.");
    var dot = new StrokeBuilder(new Vector2(10, 10), InkStyle.MousePen);
    Require(dot.Stroke.Samples.Count == 1 && dot.Finish().Start == dot.Finish().End, "A single click forms a valid dot stroke.");
    var decoded = MouseCapture.DesktopPoint(MousePosition(-7, 21), -1920, -300);
    Require(decoded == new Vector2(-1927, -279), "Signed native coordinates and negative monitor origins are preserved.");
    Require(MouseCapture.DesktopPoint(MousePosition(150, 200), 0, 0) == new Vector2(150, 200), "Physical pixels are not rescaled at 150% UI scaling.");
    var rejected = false;
    try { builder.Append(new Vector2(float.NaN, 0)); } catch (ArgumentOutOfRangeException) { rejected = true; }
    Require(rejected, "Invalid input coordinates cannot enter retained ink.");
}

static void CheckScreenshotCapture()
{
    var dc = NativeMethods.CreateCompatibleDC(0);
    nint bitmap = 0, old = 0;
    try {
        var info = new NativeMethods.BitmapInfo { Size = 40, Width = 6, Height = -4, Planes = 1, BitCount = 32 };
        bitmap = NativeMethods.CreateDIBSection(dc, ref info, 0, out var bits, 0, 0);
        old = NativeMethods.SelectObject(dc, bitmap);
        Require(dc != 0 && bitmap != 0 && bits != 0 && old != 0, "Synthetic capture fixture creates its memory bitmap.");
        var colors = new byte[6 * 4 * 4];
        for (var y = 0; y < 4; y++) {
            for (var x = 0; x < 6; x++) { var i = (y * 6 + x) * 4; colors[i] = (byte)(10 + x); colors[i + 1] = (byte)(20 + y); colors[i + 2] = (byte)(30 + x + y); }
        }
        System.Runtime.InteropServices.Marshal.Copy(colors, 0, bits, colors.Length);
        var capture = DesktopCapture.CaptureFromDc(dc, new CaptureBounds(2, 1, 3, 2));
        Require(capture.Width == 3 && capture.Height == 2 && capture.Bgra.Length == 24, "Screenshot crop preserves physical dimensions.");
        for (var y = 0; y < 2; y++) {
            for (var x = 0; x < 3; x++) {
                var i = (y * 3 + x) * 4;
                Require(capture.Bgra[i] == 12 + x && capture.Bgra[i + 1] == 21 + y && capture.Bgra[i + 2] == 33 + x + y && capture.Bgra[i + 3] == 255,
                    "Screenshot keeps top-down BGRA rows and makes GDI alpha opaque.");
            }
        }
        Console.WriteLine("PASS: Native screenshot capture crop, colors, row orientation and opaque alpha (synthetic pixels only).");
    } finally {
        if (old != 0 && old != new nint(-1)) { NativeMethods.SelectObject(dc, old); }
        if (bitmap != 0) { NativeMethods.DeleteObject(bitmap); }
        if (dc != 0) { NativeMethods.DeleteDC(dc); }
    }
}

static void CheckRenderer()
{
    using var surface = new InkSurface(-200, -100, 128, 128);
    var builder = new StrokeBuilder(new Vector2(-180, -80), InkStyle.MousePen);
    surface.DrawDot(builder.Stroke.Samples[0], builder.Stroke.Style);
    Require(surface.ReadPixel(20, 20) >> 24 == 255 && surface.ReadPixel(100, 100) == 0, "Native Direct2D renders opaque ink over a zero-alpha background.");
    var segment = builder.Append(new Vector2(-120, -80))!.Value;
    surface.DrawSegment(segment, builder.Stroke.Style);
    surface.DrawSegment(builder.Finish(), builder.Stroke.Style);
    Require(surface.ReadPixel(20, 20) >> 24 == 255 && surface.ReadPixel(79, 20) >> 24 > 0, "Incremental rendering retains earlier pixels and reaches the endpoint.");
    Require(surface.ReadPixel(50, 30) == 0, "No background rectangle is introduced by drawing.");
    var hasAntialiasing = Enumerable.Range(15, 10).Any(y => Enumerable.Range(15, 10).Any(x => {
        var alpha = surface.ReadPixel(x, y) >> 24;
        return alpha > 0 && alpha < 255;
    }));
    Require(hasAntialiasing, "Stroke edges contain anti-aliased alpha pixels.");
    surface.Redraw([builder.Stroke], true, false);
    Require(surface.ReadPixel(20, 20) >> 24 == 255 && surface.ReadPixel(1, 60) >> 24 > 0, "Full reconstruction keeps retained ink and the diagnostic border.");
    surface.Redraw([builder.Stroke], false, false);
    Require(surface.ReadPixel(1, 60) == 0 && surface.ReadPixel(50, 20) >> 24 > 0, "Removing the boundary preserves the session ink.");
}

static void CheckPenMetadata()
{
    Require(System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.PointerInfo>() == 96 &&
        System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.PointerPenInfo>() == 120,
        "Pointer interop structures match the x64 Windows ABI.");
    var native = new NativeMethods.PointerPenInfo {
        Pointer = new NativeMethods.PointerInfo { PointerId = 7, FrameId = 10, Flags = 4, PixelLocation = new(-300, 120) },
        PenMask = 15, PenFlags = 1, Pressure = 512, TiltX = -30, TiltY = 40, Rotation = 270
    };
    var converted = PenInputReader.Convert(native);
    Require(converted.Sample.Pressure == .5f && converted.Sample.Position == new Vector2(-300, 120), "Windows pressure is normalized and desktop coordinates stay signed.");
    Require(converted.Sample.TiltX == -30 && converted.Sample.TiltY == 40 && converted.Sample.Rotation == 270 &&
        converted.Sample.Buttons == PenButtons.Barrel, "Valid tilt, rotation and barrel-button data are retained.");
    native.PenMask = 0;
    var unavailable = PenInputReader.Convert(native);
    Require(unavailable.Sample.Pressure is null && unavailable.Sample.TiltX is null && unavailable.Sample.WidthFactor == 1,
        "Missing pressure is distinct from zero and falls back to fixed width.");
    native.PenMask = 1;
    native.Pressure = 0;
    Require(PenInputReader.Convert(native).Sample.Pressure == 0 && PenInputReader.Convert(native).Sample.WidthFactor == .2f,
        "Reported zero pressure retains the minimum visible width.");
    var older = native;
    older.Pointer.FrameId = 9;
    var ordered = PenInputReader.ConvertNewestFirst([native, older]);
    Require(ordered[0].FrameId == 9 && ordered[1].FrameId == 10, "Coalesced native history is processed oldest first.");
    Require(PenInputReader.IsPromotedMouse(new nint(unchecked((int)0xFF515701))) &&
        PenInputReader.IsPromotedMouse(new nint(unchecked((int)0xFF515781))) && !PenInputReader.IsPromotedMouse(0),
        "Pen/touch-promoted mouse messages are recognized without filtering physical mice.");

    var builder = new StrokeBuilder(new InkSample(new Vector2(20, 50), 0), InkStyle.TabletPen, InkInputKind.Pen);
    var pressureOnly = builder.Append(new InkSample(new Vector2(20, 50), .5f));
    var ramp = builder.Append(new InkSample(new Vector2(100, 50), 1));
    Require(pressureOnly.HasValue && ramp.HasValue, "Pressure changes at a stationary tip are preserved.");
    Require(StrokeBuilder.Replay(builder.Stroke).SequenceEqual(new[] { pressureOnly!.Value, ramp!.Value, builder.Finish() }),
        "Pressure interpolation replays identically to incremental segments.");
    using var surface = new InkSurface(0, 0, 128, 128);
    var gradient = new StrokeBuilder(new InkSample(new Vector2(20, 50), 0), InkStyle.TabletPen, InkInputKind.Pen);
    surface.DrawDot(gradient.Stroke.Samples[0], gradient.Stroke.Style);
    surface.DrawSegment(gradient.Append(new InkSample(new Vector2(100, 50), 1))!.Value, gradient.Stroke.Style);
    surface.DrawSegment(gradient.Finish(), gradient.Stroke.Style);
    uint[] pixels = Enumerable.Range(0, 128 * 128).Select(i => surface.ReadPixel(i % 128, i / 128)).ToArray();
    var thin = Enumerable.Range(40, 20).Count(y => surface.ReadPixel(20, y) >> 24 > 0);
    var thick = Enumerable.Range(40, 20).Count(y => surface.ReadPixel(98, y) >> 24 > 0);
    Require(thick > thin && surface.ReadPixel(60, 50) >> 24 > 0, "Native output grows thicker with pressure and remains connected.");
    surface.Redraw([gradient.Stroke], false, false);
    Require(pixels.SequenceEqual(Enumerable.Range(0, 128 * 128).Select(i => surface.ReadPixel(i % 128, i / 128))),
        "Pressure ink survives full reconstruction pixel-for-pixel.");
    surface.DrawSegment(new InkSegment(new(30, 70), new(70, 70), new(30, 70), .2f, 1, .4f), InkStyle.TabletPen);
    Require(surface.ReadPixel(30, 70) >> 24 > 0, "A pressure curve reversing direction renders without a zero-tangent failure.");
}

static void CheckPenController()
{
    var reader = new TestPenReader();
    using var overlay = new OverlayController(0, reader);
    overlay.SetMode(OverlayMode.Draw);
    var x = overlay.Bounds.Left + 300;
    var y = overlay.Bounds.Top + 300;
    PenFrame Frame(float dx, float pressure, uint frameId, bool contact = true, bool canceled = false,
        PenButtons buttons = PenButtons.None) => new(7, new InkSample(new Vector2(x + dx, y), pressure, -20, 15, null, buttons), contact, canceled, frameId);
    reader.Frames = [Frame(0, .1f, 10)];
    overlay.OnInputMessage(NativeMethods.PointerDown, 7, 0);
    Require(overlay.Status.StrokeCount == 1 && overlay.Strokes[0].InputKind == InkInputKind.Pen, "A pen-down starts a distinct retained pen stroke.");
    overlay.OnInputMessage(NativeMethods.LeftButtonDown, 1, MousePosition(500, 500));
    Require(overlay.Status.StrokeCount == 1, "Mouse events cannot start a duplicate stroke while pen contact is active.");
    reader.Frames = [Frame(0, .1f, 10), Frame(40, .5f, 11), Frame(80, 1, 12, buttons: PenButtons.Barrel)];
    overlay.OnInputMessage(NativeMethods.PointerUpdate, 7, 0);
    Require(reader.HistoryRequested && overlay.Strokes[0].Samples.Count == 3, "Coalesced pen samples are retained and overlapping history is deduplicated.");
    reader.Frames = [Frame(100, 0, 13, false)];
    overlay.OnInputMessage(NativeMethods.PointerUp, 7, 0);
    Require(overlay.Status.PointerStatus.Contains("complete") && overlay.Strokes[0].Samples[^1].Pressure == 1,
        "Pen-up finishes at the endpoint while retaining the last contact pressure.");
    var firstCount = overlay.Strokes[0].Samples.Count;
    reader.Frames = [Frame(120, 0, 14, false)];
    overlay.OnInputMessage(NativeMethods.PointerUpdate, 7, 0);
    Require(overlay.Strokes[0].Samples.Count == firstCount, "Hover cannot extend completed pen ink.");
    reader.Kind = PointerDeviceKind.Touch;
    overlay.OnInputMessage(NativeMethods.PointerDown, 99, 0);
    Require(overlay.Status.StrokeCount == 1, "Touch does not create unintended pen strokes.");
    reader.Kind = PointerDeviceKind.Pen;
    reader.Frames = [Frame(130, .5f, 20, buttons: PenButtons.Eraser)];
    overlay.OnInputMessage(NativeMethods.PointerDown, 7, 0);
    Require(overlay.Status.StrokeCount == 1 && overlay.Status.PointerStatus.Contains("Eraser"), "Eraser-end contact is recognized and does not paint pen ink.");
    overlay.OnInputMessage(NativeMethods.PointerCaptureChanged, 7, 0);
    reader.Frames = [Frame(140, .5f, 21)];
    overlay.OnInputMessage(NativeMethods.PointerDown, 7, 0);
    overlay.OnInputMessage(NativeMethods.PointerCaptureChanged, 7, 0);
    Require(overlay.Status.PointerStatus.Contains("complete"), "Pointer capture loss finishes an active pen stroke.");
    reader.Frames = [Frame(160, .5f, 30)];
    overlay.OnInputMessage(NativeMethods.PointerDown, 7, 0);
    reader.Frames = [Frame(180, .7f, 31), Frame(200, .8f, 32, canceled: true)];
    overlay.OnInputMessage(NativeMethods.PointerUpdate, 7, 0);
    Require(overlay.Status.PointerStatus.Contains("interrupted") && overlay.ReadInkPixel(478, 300) >> 24 > 0,
        "Cancellation flushes earlier coalesced ink and ends the pen stroke.");
    reader.Frames = [Frame(220, .5f, 40)];
    overlay.OnInputMessage(NativeMethods.PointerDown, 7, 0);
    reader.Fail = true;
    overlay.OnInputMessage(NativeMethods.PointerUpdate, 7, 0);
    Require(overlay.Status.Error is not null && overlay.Status.PointerStatus.Contains("complete"), "A pen read failure ends contact and reports an actionable error.");
    reader.Fail = false;
    reader.Frames = [Frame(240, .5f, 50)];
    overlay.OnInputMessage(NativeMethods.PointerDown, 7, 0);
    overlay.SetMode(OverlayMode.ClickThrough);
    var count = overlay.Status.StrokeCount;
    reader.Frames = [Frame(260, 1, 51)];
    overlay.OnInputMessage(NativeMethods.PointerUpdate, 7, 0);
    Require(overlay.Status.StrokeCount == count && overlay.Status.Error is null, "Click-through ends pen contact and ignores subsequent updates.");
}

static void CheckEraserGeometry()
{
    InkStroke Line(Vector2 a, Vector2 b, float? pressure = null) {
        var stroke = new InkStroke(pressure.HasValue ? InkStyle.TabletPen : InkStyle.MousePen);
        stroke.AddSample(new InkSample(a, pressure));
        stroke.AddSample(new InkSample(b, pressure));
        return stroke;
    }
    var line = Line(new(-300, -100), new(-200, -100));
    Require(StrokeHitTester.Intersects(line, new(-250, -130), new(-250, -70), 2),
        "A fast swept eraser crosses ink between input samples, including negative desktop coordinates.");
    Require(!StrokeHitTester.Intersects(line, new(-250, -130), new(-250, -110), 2),
        "An eraser sweep that stops short preserves untouched ink.");
    var dot = new InkStroke(InkStyle.MousePen);
    dot.AddSample(new Vector2(10, 10));
    Require(StrokeHitTester.Intersects(dot, new(14, 10), new(14, 10), 2) &&
        !StrokeHitTester.Intersects(dot, new(15, 10), new(15, 10), 2), "Single-dot hit testing includes the stroke radius.");
    var thin = Line(new(0, 0), new(100, 0), 0);
    var thick = Line(new(0, 0), new(100, 0), 1);
    Require(!StrokeHitTester.Intersects(thin, new(50, 5), new(50, 5), 2) &&
        StrokeHitTester.Intersects(thick, new(50, 5), new(50, 5), 2), "Eraser hit testing respects pressure-dependent stroke width.");
    var curve = new InkStroke(InkStyle.MousePen);
    curve.AddSample(new Vector2(0, 0));
    curve.AddSample(new Vector2(100, 0));
    curve.AddSample(new Vector2(100, 100));
    Require(StrokeHitTester.Intersects(curve, new(87.5f, 12.5f), new(87.5f, 12.5f), 2) &&
        !StrokeHitTester.Intersects(curve, new(100, 0), new(100, 0), 2), "Hit testing follows the smoothed curve rather than the raw corner samples.");
    var untouched = Line(new(0, 100), new(100, 100));
    List<InkStroke> strokes = [thin, untouched, thick];
    var removed = StrokeEraser.Erase(strokes, new(50, -10), new(50, 10), 4);
    Require(strokes.Count == 1 && ReferenceEquals(strokes[0], untouched) &&
        removed.Select(entry => entry.Index).SequenceEqual(new[] { 0, 2 }) &&
        ReferenceEquals(removed[0].Stroke, thin) && ReferenceEquals(removed[1].Stroke, thick),
        "Overlapping strokes are removed together while remaining identity and original removal indices are preserved.");
    var rejected = false;
    try { StrokeEraser.Erase(strokes, Vector2.Zero, Vector2.Zero, float.NaN); }
    catch (ArgumentOutOfRangeException) { rejected = true; }
    Require(rejected, "Invalid eraser sizes are rejected before changing retained ink.");
}

static void CheckEraserController()
{
    var reader = new TestPenReader();
    using var overlay = new OverlayController(0, reader);
    overlay.SetMode(OverlayMode.Draw);
    void MouseLine(int y) {
        overlay.OnInputMessage(NativeMethods.LeftButtonDown, 1, MousePosition(200, y));
        overlay.OnInputMessage(NativeMethods.LeftButtonUp, 0, MousePosition(400, y));
    }
    MouseLine(200);
    MouseLine(260);
    var survivor = overlay.Strokes[1].Id;
    overlay.SetBoundaryVisible(true);
    overlay.SetTool(AnnotationTool.StrokeEraser);
    overlay.SetEraserDiameter(4);
    overlay.OnInputMessage(NativeMethods.LeftButtonDown, 1, MousePosition(300, 170));
    overlay.OnInputMessage(NativeMethods.MouseMove, 1, MousePosition(300, 230));
    overlay.OnInputMessage(NativeMethods.LeftButtonUp, 0, MousePosition(300, 230));
    Require(overlay.Strokes.Count == 1 && overlay.Strokes[0].Id == survivor && overlay.ReadInkPixel(300, 200) == 0 &&
        overlay.ReadInkPixel(300, 260) >> 24 > 0 && overlay.ReadInkPixel(1, 200) >> 24 > 0,
        "Mouse erasing removes a whole swept stroke, clears its pixels, and preserves untouched ink and the boundary.");
    Require(NativeMethods.GetCapture() != overlay.InputHandle && overlay.Status.PointerStatus.Contains("1 stroke"),
        "Mouse eraser contact releases capture and reports the removal count.");
    overlay.OnInputMessage(NativeMethods.LeftButtonDown, 1, MousePosition(600, 500));
    overlay.SetTool(AnnotationTool.Pen);
    Require(NativeMethods.GetCapture() != overlay.InputHandle, "Changing tools ends eraser contact and releases mouse capture.");
    overlay.OnInputMessage(NativeMethods.MouseMove, 1, MousePosition(300, 260));
    Require(overlay.Strokes.Count == 1, "Movement after a tool change cannot erase or paint without a new contact.");
    PenFrame Frame(int x, int y, uint id, PenButtons buttons = PenButtons.None, bool contact = true, bool canceled = false) =>
        new(7, new InkSample(new(overlay.Bounds.Left + x, overlay.Bounds.Top + y), .5f, Buttons: buttons), contact, canceled, id);
    reader.Frames = [Frame(300, 260, 10, PenButtons.Inverted)];
    overlay.OnInputMessage(NativeMethods.PointerDown, 7, 0);
    Require(overlay.Strokes.Count == 0 && overlay.Status.Tool == AnnotationTool.Pen && overlay.ReadInkPixel(300, 260) == 0,
        "The hardware eraser end removes ink without changing the selected pen tool.");
    overlay.OnInputMessage(NativeMethods.PointerCaptureChanged, 7, 0);
    reader.Frames = [Frame(200, 300, 20)];
    overlay.OnInputMessage(NativeMethods.PointerDown, 7, 0);
    reader.Frames = [Frame(400, 300, 21, contact: false)];
    overlay.OnInputMessage(NativeMethods.PointerUp, 7, 0);
    Require(overlay.Strokes.Count == 1 && overlay.Strokes[0].InputKind == InkInputKind.Pen,
        "The next normal tip contact returns to pressure-sensitive drawing.");
    overlay.SetTool(AnnotationTool.StrokeEraser);
    reader.Frames = [Frame(300, 270, 30)];
    overlay.OnInputMessage(NativeMethods.PointerDown, 7, 0);
    reader.Frames = [Frame(300, 290, 31), Frame(300, 310, 32), Frame(300, 340, 33, canceled: true)];
    overlay.OnInputMessage(NativeMethods.PointerUpdate, 7, 0);
    Require(overlay.Strokes.Count == 0 && overlay.ReadInkPixel(300, 300) == 0 && overlay.Status.PointerStatus.Contains("interrupted"),
        "Selected eraser uses coalesced pen history, rebuilds erased pixels, and ends on cancellation.");
    overlay.SetTool(AnnotationTool.Pen);
    MouseLine(350);
    MouseLine(400);
    var idToDelete = overlay.Strokes[0].Id;
    overlay.SetMode(OverlayMode.ClickThrough);
    Require(overlay.DeleteStroke(idToDelete) && !overlay.DeleteStroke(idToDelete) && overlay.ReadInkPixel(300, 350) == 0 &&
        overlay.ReadInkPixel(300, 400) >> 24 > 0, "Deleting by stroke identity works in click-through and preserves other strokes.");
    overlay.SetMode(OverlayMode.Disabled);
    overlay.ClearAll();
    overlay.SetMode(OverlayMode.Draw);
    Require(overlay.GetStrokeSummaries().Count == 0 && overlay.ReadInkPixel(300, 400) == 0 && overlay.ReadInkPixel(1, 200) >> 24 > 0,
        "Clear all while hidden cannot resurrect ink on re-enable, and preserves the diagnostic boundary.");
}

static void CheckInkHistory()
{
    InkStroke Dot(int position) {
        var stroke = new InkStroke(InkStyle.MousePen);
        stroke.AddSample(new Vector2(position, 10));
        return stroke;
    }
    List<InkStroke> canvas = [];
    var history = new InkHistory();
    Require(!history.Undo(canvas) && !history.Redo(canvas), "Empty undo/redo are harmless.");
    var a = Dot(10); var b = Dot(20); var c = Dot(30); var d = Dot(40);
    foreach (var stroke in new[] { a, b, c, d }) {
        canvas.Add(stroke);
        history.RecordApplied(StrokeEditCommand.Added(canvas.Count - 1, stroke));
    }
    Require(history.Undo(canvas) && canvas.SequenceEqual(new[] { a, b, c }) && history.Redo(canvas) && canvas[^1] == d,
        "Drawing history reuses original stroke identities on undo and redo.");
    canvas.RemoveAt(2);
    var firstErase = StrokeEditCommand.Removed("Erase strokes", [new IndexedStroke(2, c)]);
    canvas.RemoveAt(0);
    var secondErase = StrokeEditCommand.Removed("Erase strokes", [new IndexedStroke(0, a)]);
    history.RecordApplied(new CompositeInkCommand("Erase strokes", [firstErase, secondErase]));
    Require(history.Undo(canvas) && canvas.SequenceEqual(new[] { a, b, c, d }) && history.Redo(canvas) && canvas.SequenceEqual(new[] { b, d }),
        "One grouped eraser command reverses changing list indices in the correct order.");
    var clear = StrokeEditCommand.Removed("Clear all", canvas.Select((stroke, index) => new IndexedStroke(index, stroke)));
    canvas.Clear(); history.RecordApplied(clear);
    Require(history.Undo(canvas) && canvas.SequenceEqual(new[] { b, d }), "Clear-all undo restores original order and identities.");
    var newStroke = Dot(50); canvas.Add(newStroke); history.RecordApplied(StrokeEditCommand.Added(canvas.Count - 1, newStroke));
    Require(!history.CanRedo && !history.Redo(canvas), "A new edit after undo discards the previous redo branch.");

    var bounded = new InkHistory(maximumCommands: 2);
    List<InkStroke> baseline = [];
    foreach (var stroke in new[] { a, b, c }) { baseline.Add(stroke); bounded.RecordApplied(StrokeEditCommand.Added(baseline.Count - 1, stroke)); }
    bounded.Undo(baseline); bounded.Undo(baseline);
    Require(!bounded.Undo(baseline) && baseline.SequenceEqual(new[] { a }) && bounded.RetainedSampleCost == 2,
        "History eviction leaves old ink on the canvas while bounding retained undo/redo entries.");
    var budgeted = new InkHistory(sampleBudget: 2);
    budgeted.RecordApplied(StrokeEditCommand.Added(0, a));
    budgeted.RecordApplied(StrokeEditCommand.Added(1, b));
    budgeted.RecordApplied(StrokeEditCommand.Removed("Clear all", [new IndexedStroke(0, a), new IndexedStroke(1, b), new IndexedStroke(2, c)]));
    Require(budgeted.UndoCount == 1 && budgeted.RetainedSampleCost == 3, "The sample budget evicts old commands but preserves an oversized newest clear.");
    budgeted.Clear();
    Require(!budgeted.CanUndo && !budgeted.CanRedo && budgeted.RetainedSampleCost == 0, "History disposal releases both branches and sample accounting.");

    // Compare command history to independent canvas snapshots through a deterministic mixed edit sequence.
    var random = new Random(6106);
    var randomHistory = new InkHistory(2000, 1_000_000);
    List<InkStroke> randomCanvas = [];
    List<InkStroke[]> states = [[]];
    var stateIndex = 0;
    for (var operation = 0; operation < 1200; operation++) {
        var choice = random.Next(5);
        if (choice == 0) {
            var expected = stateIndex > 0;
            if (randomHistory.Undo(randomCanvas) != expected) { throw new InvalidOperationException("Random undo availability differs from the snapshot model."); }
            if (expected) { stateIndex--; }
        } else if (choice == 1) {
            var expected = stateIndex < states.Count - 1;
            if (randomHistory.Redo(randomCanvas) != expected) { throw new InvalidOperationException("Random redo availability differs from the snapshot model."); }
            if (expected) { stateIndex++; }
        } else {
            IInkCommand? command = null;
            if (choice == 2 || randomCanvas.Count == 0) {
                var stroke = Dot(operation); randomCanvas.Add(stroke);
                command = StrokeEditCommand.Added(randomCanvas.Count - 1, stroke);
            } else if (choice == 3) {
                var index = random.Next(randomCanvas.Count); var stroke = randomCanvas[index];
                randomCanvas.RemoveAt(index);
                command = StrokeEditCommand.Removed("Delete selected stroke", [new IndexedStroke(index, stroke)]);
            } else {
                command = StrokeEditCommand.Removed("Clear all", randomCanvas.Select((stroke, index) => new IndexedStroke(index, stroke)));
                randomCanvas.Clear();
            }
            randomHistory.RecordApplied(command);
            states.RemoveRange(stateIndex + 1, states.Count - stateIndex - 1);
            states.Add(randomCanvas.ToArray()); stateIndex++;
        }
        if (!randomCanvas.SequenceEqual(states[stateIndex])) { throw new InvalidOperationException($"Mixed history differs from the snapshot model at operation {operation}."); }
    }
    Require(true, "1,200 mixed draw/delete/clear/undo/redo operations match an independent snapshot model.");
    var stress = new InkHistory(4096, 1_000_000);
    List<InkStroke> many = [];
    var watch = Stopwatch.StartNew();
    for (var i = 0; i < 2500; i++) { var stroke = Dot(i); many.Add(stroke); stress.RecordApplied(StrokeEditCommand.Added(i, stroke)); }
    while (stress.Undo(many)) { }
    while (stress.Redo(many)) { }
    watch.Stop();
    Require(many.Count == 2500 && stress.UndoCount == 2500 && stress.RetainedSampleCost == 2500,
        $"2,500 strokes recorded/undone/redone without sample copies ({watch.Elapsed.TotalMilliseconds:F1} ms for the history model; excludes rendering).");
}

static void CheckViewModel()
{
    var viewModel = new MainViewModel();
    var selectionNotifications = 0;
    var liveRegionNotifications = 0;
    viewModel.PropertyChanged += (_, args) => {
        if (args.PropertyName == nameof(MainViewModel.SelectedStroke)) {
            if (++selectionNotifications > 8) { throw new InvalidOperationException("Two-way selection recursively writes back into the view model."); }
            // A native two-way SelectedItem binding synchronously echoes the selected object.
            viewModel.SelectedStroke = viewModel.SelectedStroke;
        }
        if (args.PropertyName is nameof(MainViewModel.OverlayModeLabel) or nameof(MainViewModel.PointerStatus) or nameof(MainViewModel.ErrorStatus)) {
            liveRegionNotifications++;
        }
    };
    var bounds = new MonitorBounds(0, 0, 1920, 1200);
    var initial = new OverlayStatus(OverlayMode.Disabled, bounds, 0, "Ready", null);
    var a = new StrokeSummary(Guid.NewGuid(), 1, InkInputKind.Mouse, 3);
    var b = new StrokeSummary(Guid.NewGuid(), 2, InkInputKind.Pen, 4);
    viewModel.UpdateOverlay(initial with { StrokeCount = 2, CanUndo = true }, "Recovery ready", [a, b]);
    Require(selectionNotifications == 1 && viewModel.SelectedStroke?.Id == b.Id && viewModel.CanDelete && viewModel.CanUndo && !viewModel.CanRedo,
        "Synchronous two-way selection echo stops after one notification and history buttons bind correctly.");
    var liveRegionBefore = liveRegionNotifications;
    viewModel.UpdateOverlay(initial with { StrokeCount = 2, CanUndo = true, EraserDiameter = 40 }, "Recovery ready", [a, b]);
    Require(liveRegionNotifications == liveRegionBefore && selectionNotifications == 1,
        "A slider update does not reset unchanged selection or unrelated accessibility live regions.");
    viewModel.SelectedStroke = viewModel.StrokeChoices[0];
    selectionNotifications = 0;
    viewModel.UpdateOverlay(initial with { StrokeCount = 2, CanUndo = true }, "Recovery ready", [a, b with { SampleCount = 8 }]);
    Require(viewModel.SelectedStroke?.Id == a.Id && viewModel.StrokeChoices.Any(choice => ReferenceEquals(choice, viewModel.SelectedStroke)) && selectionNotifications == 1,
        "Replacing stroke labels preserves selection using the actual new collection item.");
    viewModel.UpdateOverlay(initial with { CanRedo = true }, "Recovery ready", []);
    Require(viewModel.SelectedStroke is null && !viewModel.CanDelete && !viewModel.HasInk && !viewModel.CanUndo && viewModel.CanRedo,
        "Cleared canvas selection and Undo/Redo enabled states update without binding recursion.");
}

static void CheckUndoController()
{
    using var probe = new NativeWindow("ScreenInk shortcut test", NativeMethods.ToolWindow | NativeMethods.NoActivate, 0, 0, 1, 1);
    var reader = new TestPenReader();
    using var overlay = new OverlayController(0, reader);
    void Line(int y) {
        overlay.OnInputMessage(NativeMethods.LeftButtonDown, 1, MousePosition(200, y));
        overlay.OnInputMessage(NativeMethods.LeftButtonUp, 0, MousePosition(400, y));
    }
    Require(NativeMethods.RegisterHotKey(probe.Handle, 99, 0x4002, 0x5A), "Ctrl+Z is unclaimed while ScreenInk starts disabled.");
    try {
        overlay.SetMode(OverlayMode.Draw);
        Require(overlay.Mode == OverlayMode.Draw && overlay.Status.HistoryShortcuts.Contains("conflict"),
            "An undo-shortcut conflict is reported without blocking drawing or history buttons.");
        Line(100);
        Require(overlay.Undo() && overlay.Redo(), "Undo and redo remain functional with a shortcut conflict.");
    } finally { NativeMethods.UnregisterHotKey(probe.Handle, 99); }
    overlay.SetMode(OverlayMode.Draw);
    Require(!overlay.Status.HistoryShortcuts.Contains("conflict") && !NativeMethods.RegisterHotKey(probe.Handle, 99, 0x4002, 0x5A),
        "Draw mode owns the Ctrl+Z shortcut after a conflict clears.");
    Line(150); Line(200); Line(250);
    var original = overlay.Strokes.ToArray();
    uint[] Pixels() => Enumerable.Range(0, 260 * 200).Select(i => overlay.ReadInkPixel(190 + i % 260, 90 + i / 260)).ToArray();
    var originalPixels = Pixels();
    overlay.SetTool(AnnotationTool.StrokeEraser); overlay.SetEraserDiameter(4);
    overlay.OnInputMessage(NativeMethods.LeftButtonDown, 1, MousePosition(300, 200));
    overlay.OnInputMessage(NativeMethods.MouseMove, 1, MousePosition(300, 100));
    overlay.OnInputMessage(NativeMethods.LeftButtonUp, 0, MousePosition(300, 100));
    Require(overlay.Strokes.SequenceEqual(new[] { original[3] }) && overlay.Undo() && overlay.Strokes.SequenceEqual(original) && Pixels().SequenceEqual(originalPixels),
        "One undo restores every stroke erased across a drag, original ordering and native output pixels.");
    Require(overlay.Redo() && overlay.Strokes.SequenceEqual(new[] { original[3] }) && overlay.ReadInkPixel(300, 150) == 0,
        "Redo reapplies the complete eraser drag without ghost pixels.");
    overlay.Undo();
    overlay.DeleteStroke(original[1].Id);
    Require(overlay.Undo() && overlay.Strokes.SequenceEqual(original) && overlay.Redo() && !overlay.Strokes.Contains(original[1]),
        "Selected-stroke deletion restores at its original position and redoes by identity.");
    overlay.SetMode(OverlayMode.Disabled);
    overlay.ClearAll();
    Require(overlay.Undo() && overlay.Strokes.Count == 3 && overlay.Redo() && overlay.Strokes.Count == 0,
        "Clear-all history works while the overlay is hidden.");
    overlay.Undo(); overlay.SetMode(OverlayMode.Draw);
    Require(overlay.ReadInkPixel(300, 250) >> 24 > 0, "Re-enabling presents restored hidden ink.");
    overlay.SetTool(AnnotationTool.Pen);
    overlay.OnInputMessage(NativeMethods.LeftButtonDown, 1, MousePosition(200, 350));
    Require(overlay.Undo() && NativeMethods.GetCapture() != overlay.InputHandle && overlay.Strokes.Count == 3,
        "Undo during mouse contact commits then undoes that stroke and releases capture.");
    overlay.OnInputMessage(NativeMethods.MouseMove, 1, MousePosition(400, 350));
    Require(overlay.ReadInkPixel(300, 350) == 0 && overlay.Redo(), "Late mouse messages cannot extend an undone stroke; redo restores it.");
    overlay.Undo();
    overlay.SetTool(AnnotationTool.StrokeEraser);
    overlay.OnInputMessage(NativeMethods.LeftButtonDown, 1, MousePosition(600, 500));
    overlay.OnInputMessage(NativeMethods.LeftButtonUp, 0, MousePosition(650, 500));
    overlay.SetBoundaryVisible(true); overlay.SetEraserDiameter(30);
    Require(overlay.Status.CanRedo && !overlay.DeleteStroke(Guid.NewGuid()) && overlay.Redo(),
        "Empty eraser gestures, mode/tool/size/boundary changes and missing deletion preserve redo.");
    overlay.Undo(); overlay.SetTool(AnnotationTool.Pen); Line(400);
    Require(!overlay.Status.CanRedo && !overlay.Redo(), "New drawing after undo invalidates the old redo branch.");
    overlay.OnVisualMessage(NativeMethods.HotKey, 3, 0);
    Require(overlay.ReadInkPixel(300, 400) == 0, "The native undo hotkey dispatch performs exactly one ink undo.");
    overlay.OnVisualMessage(NativeMethods.HotKey, 4, 0);
    Require(overlay.ReadInkPixel(300, 400) >> 24 > 0, "The native redo hotkey dispatch restores ink.");
    overlay.SetMode(OverlayMode.ClickThrough);
    var passThroughCount = overlay.Strokes.Count;
    Require(NativeMethods.RegisterHotKey(probe.Handle, 99, 0x4002, 0x5A) && NativeMethods.RegisterHotKey(probe.Handle, 100, 0x4002, 0x59),
        "Click-through releases Ctrl+Z/Y for underlying applications.");
    NativeMethods.UnregisterHotKey(probe.Handle, 99); NativeMethods.UnregisterHotKey(probe.Handle, 100);
    overlay.OnVisualMessage(NativeMethods.HotKey, 3, 0);
    Require(overlay.Strokes.Count == passThroughCount, "Queued undo messages after leaving draw mode are ignored.");

    overlay.SetMode(OverlayMode.Draw);
    var x = overlay.Bounds.Left + 200; var yPen = overlay.Bounds.Top + 450;
    reader.Frames = [new(7, new InkSample(new(x, yPen), .1f), true, false, 10)];
    overlay.OnInputMessage(NativeMethods.PointerDown, 7, 0);
    reader.Frames = [new(7, new InkSample(new(x + 100, yPen), 1), true, false, 11)];
    overlay.OnInputMessage(NativeMethods.PointerUpdate, 7, 0);
    Require(overlay.Undo() && overlay.ReadInkPixel(250, 450) == 0, "Undo during pen contact finishes and removes the pressure stroke.");
    reader.Frames = [new(7, new InkSample(new(x + 200, yPen), 1), true, false, 12)];
    overlay.OnInputMessage(NativeMethods.PointerUpdate, 7, 0);
    Require(overlay.Redo() && overlay.Strokes[^1].Samples.Count == 2 && overlay.Strokes[^1].Samples[0].Pressure == .1f &&
        overlay.Strokes[^1].Samples[1].Pressure == 1 && overlay.ReadInkPixel(299, 450) >> 24 > 0,
        "Pressure samples and finished endpoints survive redo, and late pen updates are ignored.");
    var countBeforeErase = overlay.Strokes.Count;
    overlay.SetTool(AnnotationTool.StrokeEraser);
    overlay.OnInputMessage(NativeMethods.LeftButtonDown, 1, MousePosition(250, 450));
    Require(overlay.Undo() && overlay.Strokes.Count == countBeforeErase && NativeMethods.GetCapture() != overlay.InputHandle,
        "Undo during an eraser contact commits and restores that erase as a single action.");
    overlay.SetMode(OverlayMode.Disabled);
}

static void CheckShapeRendering()
{
    var blue = new InkStyle(4, 0, 80, 255);
    using var surface = new InkSurface(0, 0, 128, 128);
    foreach (var tool in new[] { AnnotationTool.Line, AnnotationTool.Arrow, AnnotationTool.Rectangle, AnnotationTool.Ellipse }) {
        var draft = new ShapeDraft(tool, new(100, 100), blue, InkInputKind.Mouse);
        draft.Update(new(20, 20));
        Require(draft.IsMeaningful && draft.Stroke.Samples.Count == 2 && ShapeGeometry.Replay(draft.Stroke).Any(),
            $"{tool} accepts reverse drags with only two retained endpoints.");
        surface.Redraw([draft.Stroke], false, false);
        var point = tool == AnnotationTool.Ellipse ? new Vector2(20, 60) : tool == AnnotationTool.Rectangle ? new(60, 20) : new(60, 60);
        Require(surface.ReadPixel((int)point.X, (int)point.Y) >> 24 > 0 &&
            StrokeHitTester.Intersects(draft.Stroke, point, point, 2), $"{tool} renders native pixels and erases on its visible outline.");
        if (tool is AnnotationTool.Rectangle or AnnotationTool.Ellipse) {
            Require(surface.ReadPixel(60, 60) == 0 && !StrokeHitTester.Intersects(draft.Stroke, new(60, 60), new(60, 60), 2),
                $"{tool} stays hollow and the eraser ignores its empty center.");
        }
        if (tool == AnnotationTool.Ellipse) {
            Require(surface.ReadPixel(20, 20) == 0 && !StrokeHitTester.Intersects(draft.Stroke, new(20, 20), new(20, 20), 2),
                "Ellipse hit testing does not invent ink at the drag rectangle's corner.");
        }
    }
    var empty = new ShapeDraft(AnnotationTool.Rectangle, new(10, 10), blue, InkInputKind.Mouse);
    Require(!empty.IsMeaningful && !ShapeGeometry.Replay(empty.Stroke).Any(), "A zero-size shape produces no retained outline.");
    surface.Redraw([], false, false);
    surface.DrawDot(new Vector2(30, 30), new InkStyle(8, 255, 0, 0, 128));
    var translucent = surface.ReadPixel(30, 30);
    Require(translucent >> 24 is > 100 and < 150 && ((translucent >> 16) & 255) <= (translucent >> 24),
        "Laser glow uses premultiplied alpha on a transparent native surface.");
}

static void CheckToolbarTools()
{
    var reader = new TestPenReader();
    var clock = new TestTimeProvider();
    using var overlay = new OverlayController(0, reader, timeProvider: clock);
    overlay.SetMode(OverlayMode.Draw);
    overlay.SetColor(new InkColor(0, 80, 255));
    overlay.SetPenWidth(6);
    overlay.OnInputMessage(NativeMethods.LeftButtonDown, 1, MousePosition(150, 150));
    overlay.OnInputMessage(NativeMethods.LeftButtonUp, 0, MousePosition(350, 150));
    var blueStroke = overlay.Strokes[0];
    Require(overlay.ReadInkPixel(250, 150) == 0xFF0050FF && blueStroke.Style.Width == 6, "The selected color and size reach actual native pen pixels.");
    overlay.SetColor(new InkColor(255, 80, 80));
    overlay.SetPenWidth(3);
    overlay.SetTool(AnnotationTool.Rectangle);
    overlay.OnInputMessage(NativeMethods.LeftButtonDown, 1, MousePosition(150, 200));
    overlay.OnInputMessage(NativeMethods.MouseMove, 1, MousePosition(350, 350));
    Require(overlay.Strokes.Count == 1 && overlay.ReadInkPixel(250, 200) >> 24 > 0,
        "A shape preview appears before committing and does not enter history as many small strokes.");
    overlay.OnInputMessage(NativeMethods.LeftButtonUp, 0, MousePosition(350, 350));
    var rectangle = overlay.Strokes[1];
    Require(rectangle.Tool == AnnotationTool.Rectangle && rectangle.Style.Red == 255 && overlay.ReadInkPixel(250, 150) == 0xFF0050FF && overlay.ReadInkPixel(250, 250) == 0,
        "A colored rectangle commits one hollow annotation without recoloring earlier ink.");
    Require(overlay.Undo() && overlay.ReadInkPixel(250, 200) == 0 && overlay.Redo() && ReferenceEquals(overlay.Strokes[1], rectangle),
        "Shape undo removes its preview pixels and redo restores the same shape identity.");
    overlay.SetTool(AnnotationTool.StrokeEraser);
    overlay.OnInputMessage(NativeMethods.LeftButtonDown, 1, MousePosition(250, 250));
    overlay.OnInputMessage(NativeMethods.LeftButtonUp, 0, MousePosition(250, 250));
    Require(overlay.Strokes.Count == 2, "Erasing inside a hollow shape leaves its outline untouched.");
    overlay.OnInputMessage(NativeMethods.LeftButtonDown, 1, MousePosition(250, 200));
    overlay.OnInputMessage(NativeMethods.LeftButtonUp, 0, MousePosition(250, 200));
    Require(overlay.Strokes.Count == 1 && overlay.Undo() && overlay.Strokes[1].Tool == AnnotationTool.Rectangle,
        "Touching a shape edge erases the entire shape and one undo restores it.");
    overlay.SetTool(AnnotationTool.Arrow);
    overlay.OnInputMessage(NativeMethods.LeftButtonDown, 1, MousePosition(450, 200));
    overlay.OnInputMessage(NativeMethods.MouseMove, 1, MousePosition(550, 300));
    overlay.SetMode(OverlayMode.ClickThrough);
    Require(overlay.Strokes.Count == 3 && overlay.Strokes[^1].Tool == AnnotationTool.Arrow && NativeMethods.GetCapture() != overlay.InputHandle,
        "Changing modes ends a shape draft and releases native capture.");
    overlay.SetMode(OverlayMode.Draw);
    overlay.SetTool(AnnotationTool.Ellipse);
    overlay.OnInputMessage(NativeMethods.LeftButtonDown, 1, MousePosition(500, 400));
    overlay.OnInputMessage(NativeMethods.LeftButtonUp, 0, MousePosition(500, 400));
    Require(overlay.Strokes.Count == 3, "Clicking without sizing a shape creates no empty history action.");
    var beforeLaser = overlay.Strokes.ToArray();
    overlay.SetTool(AnnotationTool.Laser);
    overlay.OnInputMessage(NativeMethods.LeftButtonDown, 1, MousePosition(600, 400));
    overlay.OnInputMessage(NativeMethods.MouseMove, 1, MousePosition(620, 420));
    overlay.AdvanceLaser();
    Require(overlay.LaserVisible && overlay.Strokes.SequenceEqual(beforeLaser) && overlay.ReadInkPixel(620, 420) == 0,
        "Laser contact shows a separate temporary trail without adding ink or history.");
    Require(overlay.ReadLaserPixel(overlay.Bounds.Left + 610, overlay.Bounds.Top + 410) >> 24 > 0,
        "Native laser output connects earlier and current pointer positions.");
    overlay.OnInputMessage(NativeMethods.LeftButtonUp, 0, MousePosition(620, 420));
    Require(overlay.LaserVisible && overlay.LaserTimerRunning && NativeMethods.GetCapture() != overlay.InputHandle,
        "Releasing the laser retains its fading trail and releases mouse capture immediately.");
    clock.Milliseconds = 1400; overlay.AdvanceLaser();
    Require(overlay.LaserVisible, "The laser stays visible during its fade after release.");
    clock.Milliseconds = 1700; overlay.AdvanceLaser();
    Require(!overlay.LaserVisible && !overlay.LaserTimerRunning && overlay.Strokes.SequenceEqual(beforeLaser),
        "Laser expiration hides the trail, stops the animation timer, and preserves retained ink.");
    overlay.OnInputMessage(NativeMethods.LeftButtonDown, 1, MousePosition(600, 400));
    clock.Milliseconds = 3400; overlay.AdvanceLaser();
    overlay.OnInputMessage(NativeMethods.MouseMove, 1, MousePosition(630, 430)); overlay.AdvanceLaser();
    Require(overlay.LaserVisible && overlay.LaserTimerRunning, "Movement after a stationary held laser expires restarts its trail and timer.");
    overlay.SetMode(OverlayMode.Disabled);
    Require(!overlay.LaserVisible && !overlay.LaserTimerRunning && overlay.Strokes.SequenceEqual(beforeLaser), "Hiding during laser contact clears the trail, stops its timer, and preserves retained ink.");
    overlay.SetMode(OverlayMode.Draw);
    overlay.SetTool(AnnotationTool.Line);
    var origin = new Vector2(overlay.Bounds.Left + 500, overlay.Bounds.Top + 450);
    reader.Frames = [new(7, new InkSample(origin, .1f), true, false, 10)];
    overlay.OnInputMessage(NativeMethods.PointerDown, 7, 0);
    reader.Frames = [new(7, new InkSample(origin + new Vector2(100, 0), 1), true, false, 11),
        new(7, new InkSample(origin + new Vector2(150, 0), 0), false, false, 12)];
    overlay.OnInputMessage(NativeMethods.PointerUp, 7, 0);
    Require(overlay.Strokes[^1].Tool == AnnotationTool.Line && overlay.Strokes[^1].InputKind == InkInputKind.Pen &&
        overlay.Strokes[^1].Samples.Count == 2 && overlay.Strokes[^1].Samples.All(sample => sample.Pressure is null),
        "Pen history produces a uniform-width shape with only its final endpoints retained.");
    Require(overlay.Undo(), "A pen-created shape participates in the same history.");
    overlay.SetColor(new InkColor(20, 200, 100)); overlay.SetPenWidth(8);
    Require(overlay.Status.CanRedo && overlay.Redo() && overlay.Strokes[^1].Style.Red == 255 && overlay.Strokes[0].Style.Blue == 255,
        "Color and size changes preserve redo and the styles captured by earlier annotations.");
}

static Process StartFixture()
{
    var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the test host.");
    var start = new ProcessStartInfo(processPath) {
        UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardInput = true,
        CreateNoWindow = true
    };
    if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase)) {
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    }
    start.ArgumentList.Add("--fixture");
    return Process.Start(start) ?? throw new InvalidOperationException("Could not start the native fixture.");
}

static void RunFixture()
{
    var monitorInfo = new NativeMethods.MonitorInfo { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
    if (!NativeMethods.GetMonitorInfoW(NativeMethods.MonitorFromWindow(0, 2), ref monitorInfo)) {
        throw new InvalidOperationException("Could not query the fixture monitor.");
    }
    var bounds = monitorInfo.Monitor;
    using var window = new NativeWindow("ScreenInk cross-process test fixture",
        NativeMethods.Topmost | NativeMethods.ToolWindow | NativeMethods.NoActivate | NativeMethods.NoRedirectionBitmap,
        bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    window.MessageHandler = (message, _, _) => {
        if (message != NativeMethods.Close) { return null; }
        window.Dispose();
        NativeMethods.PostQuitMessage(0);
        return nint.Zero;
    };
    window.Position(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, true);
    Console.WriteLine($"READY:{window.Handle.ToInt64()}");
    Console.Out.Flush();
    var handle = window.Handle;
    _ = Task.Run(() => {
        Console.ReadLine();
        NativeMethods.PostMessageW(handle, NativeMethods.Close, 0, 0);
    });
    int result;
    while ((result = NativeMethods.GetMessageW(out var message, 0, 0, 0)) > 0) {
        NativeMethods.TranslateMessage(ref message);
        NativeMethods.DispatchMessageW(ref message);
    }
    if (result < 0) { throw new InvalidOperationException("The native fixture message loop failed."); }
}

sealed class TestPenReader : IPenInputReader
{
    public PenFrame[] Frames { get; set; } = [];
    public PointerDeviceKind Kind { get; set; } = PointerDeviceKind.Pen;
    public bool Fail { get; set; }
    public bool HistoryRequested { get; private set; }
    public PointerDeviceKind GetDeviceKind(uint pointerId) => Kind;
    public bool TryRead(uint pointerId, bool history, out PenFrame[] frames, out string? error)
    {
        HistoryRequested |= history;
        frames = Frames;
        error = Fail ? "Test: Windows pen data unavailable." : null;
        return !Fail;
    }
}

sealed class TestTimeProvider : TimeProvider
{
    public long Milliseconds { get; set; }
    public override long TimestampFrequency => 1000;
    public override long GetTimestamp() => Milliseconds;
}
