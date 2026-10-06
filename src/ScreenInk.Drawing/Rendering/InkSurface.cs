using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using ScreenInk.Core.Models;
using ScreenInk.Drawing.StrokeEngine;
using ScreenInk.Drawing.Geometry;
using ScreenInk.Native;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.Graphics.Gdi;

namespace ScreenInk.Drawing.Rendering;

// Keep the bitmap and Direct2D target between samples; no animation loop runs when idle.
public sealed unsafe class InkSurface : IDisposable
{
    private readonly uint _thread = NativeMethods.GetCurrentThreadId();
    private nint _dc, _bitmap, _previousBitmap, _bits;
    private ID2D1Factory* _factory;
    private ID2D1DCRenderTarget* _target;
    private ID2D1SolidColorBrush* _brush;
    private ID2D1StrokeStyle* _roundStroke;
    private bool _disposed;
    public int Left { get; }
    public int Top { get; }
    public int Width { get; }
    public int Height { get; }

    public InkSurface(int left, int top, int width, int height)
    {
        if (width <= 0 || height <= 0) { throw new ArgumentOutOfRangeException(nameof(width)); }
        _ = checked(width * height * 4);
        Left = left; Top = top; Width = width; Height = height;
        try {
            _dc = NativeMethods.CreateCompatibleDC(0);
            if (_dc == 0) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
            var info = new NativeMethods.BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            _bitmap = NativeMethods.CreateDIBSection(_dc, ref info, 0, out _bits, 0, 0);
            if (_bitmap == 0 || _bits == 0) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
            _previousBitmap = NativeMethods.SelectObject(_dc, _bitmap);
            if (_previousBitmap == 0 || _previousBitmap == new nint(-1)) { throw new Win32Exception("Cannot select the ink bitmap."); }

            PInvoke.D2D1CreateFactory(D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED,
                typeof(ID2D1Factory).GUID, null, out var factory).ThrowOnFailure();
            _factory = (ID2D1Factory*)factory;
            var properties = new D2D1_RENDER_TARGET_PROPERTIES {
                type = D2D1_RENDER_TARGET_TYPE.D2D1_RENDER_TARGET_TYPE_SOFTWARE,
                pixelFormat = new D2D1_PIXEL_FORMAT {
                    format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                    alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED
                },
                // Physical-pixel coordinates must not be scaled again for monitor DPI.
                dpiX = 96, dpiY = 96
            };
            ID2D1DCRenderTarget* target;
            _factory->CreateDCRenderTarget(&properties, &target);
            _target = target;
            _target->BindDC(new HDC(_dc), new RECT { right = width, bottom = height });
            var color = new D2D1_COLOR_F { a = 1 };
            ID2D1SolidColorBrush* brush;
            _target->CreateSolidColorBrush(&color, null, &brush);
            _brush = brush;
            var style = new D2D1_STROKE_STYLE_PROPERTIES {
                startCap = D2D1_CAP_STYLE.D2D1_CAP_STYLE_ROUND,
                endCap = D2D1_CAP_STYLE.D2D1_CAP_STYLE_ROUND,
                dashCap = D2D1_CAP_STYLE.D2D1_CAP_STYLE_ROUND,
                lineJoin = D2D1_LINE_JOIN.D2D1_LINE_JOIN_ROUND, miterLimit = 10
            };
            ID2D1StrokeStyle* roundStroke;
            _factory->CreateStrokeStyle(&style, null, 0, &roundStroke);
            _roundStroke = roundStroke;
            Redraw([], false, false);
        } catch { Dispose(); throw; }
    }

    public void Redraw(IEnumerable<InkStroke> strokes, bool boundary, bool drawMode)
    {
        Verify();
        _target->BeginDraw();
        try {
            _target->Clear(null);
            if (boundary) {
                var color = drawMode ? new D2D1_COLOR_F { r = .235f, g = .813f, b = 1, a = .75f }
                    : new D2D1_COLOR_F { r = .235f, g = .867f, b = .47f, a = .75f };
                _brush->SetColor(&color);
                FillRectangle(0, 0, Width, 3);
                FillRectangle(0, Height - 3, Width, Height);
                FillRectangle(0, 3, 3, Height - 3);
                FillRectangle(Width - 3, 3, Width, Height - 3);
            }
            foreach (var stroke in strokes) {
                if (stroke.Samples.Count == 0) { continue; }
                SetColor(stroke.Style);
                if (stroke.Tool.IsShape()) { Shape(stroke); continue; }
                Dot(stroke.Samples[0].Position, stroke.Style.Width * stroke.Samples[0].WidthFactor);
                foreach (var segment in StrokeBuilder.Replay(stroke)) { Segment(segment, stroke.Style.Width); }
            }
        } finally { _target->EndDraw().ThrowOnFailure(); }
    }

    public void DrawDot(Vector2 desktopPoint, InkStyle style)
        => DrawDot(new InkSample(desktopPoint), style);

    public void DrawDot(InkSample sample, InkStyle style)
    {
        Verify();
        _target->BeginDraw();
        try { SetColor(style); Dot(sample.Position, style.Width * sample.WidthFactor); }
        finally { _target->EndDraw().ThrowOnFailure(); }
    }

    public void DrawSegment(InkSegment segment, InkStyle style)
        => DrawSegments([segment], style);

    public void DrawSegments(ReadOnlySpan<InkSegment> segments, InkStyle style)
    {
        Verify();
        _target->BeginDraw();
        try {
            SetColor(style);
            foreach (var segment in segments) { Segment(segment, style.Width); }
        }
        finally { _target->EndDraw().ThrowOnFailure(); }
    }

    public void Present(nint window)
    {
        Verify();
        var destination = new NativeMethods.Point(Left, Top);
        var size = new NativeMethods.Size(Width, Height);
        var source = new NativeMethods.Point(0, 0);
        var blend = new NativeMethods.BlendFunction { ConstantAlpha = 255, AlphaFormat = 1 };
        if (!NativeMethods.UpdateLayeredWindow(window, 0, ref destination, ref size, _dc, ref source, 0, ref blend, 2)) {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not present ink on the transparent overlay.");
        }
    }

    private D2D_POINT_2F Local(Vector2 point) => new() { x = point.X - Left, y = point.Y - Top };
    private void SetColor(InkStyle style)
    {
        var color = new D2D1_COLOR_F { r = style.Red / 255f, g = style.Green / 255f, b = style.Blue / 255f, a = style.Alpha / 255f };
        _brush->SetColor(&color);
    }
    private void FillRectangle(float left, float top, float right, float bottom)
    {
        var rect = new D2D_RECT_F { left = left, top = top, right = right, bottom = bottom };
        _target->FillRectangle(&rect, (ID2D1Brush*)_brush);
    }
    private void Dot(Vector2 point, float width)
    {
        var ellipse = new D2D1_ELLIPSE { point = Local(point), radiusX = width / 2, radiusY = width / 2 };
        _target->FillEllipse(&ellipse, (ID2D1Brush*)_brush);
    }
    private void Segment(InkSegment segment, float width)
    {
        if (segment.StartWidthFactor != segment.ControlWidthFactor || segment.ControlWidthFactor != segment.EndWidthFactor) {
            PressureSegment(segment, width);
            return;
        }
        width *= segment.StartWidthFactor;
        ID2D1PathGeometry* path = null;
        ID2D1GeometrySink* sink = null;
        try {
            _factory->CreatePathGeometry(&path);
            path->Open(&sink);
            sink->BeginFigure(Local(segment.Start), D2D1_FIGURE_BEGIN.D2D1_FIGURE_BEGIN_HOLLOW);
            var curve = new D2D1_QUADRATIC_BEZIER_SEGMENT { point1 = Local(segment.Control), point2 = Local(segment.End) };
            sink->AddQuadraticBezier(&curve);
            sink->EndFigure(D2D1_FIGURE_END.D2D1_FIGURE_END_OPEN);
            sink->Close();
            _target->DrawGeometry((ID2D1Geometry*)path, (ID2D1Brush*)_brush, width, _roundStroke);
        } finally {
            if (sink != null) { sink->Release(); }
            if (path != null) { path->Release(); }
        }
    }

    private void Shape(InkStroke stroke)
    {
        ID2D1PathGeometry* path = null;
        ID2D1GeometrySink* sink = null;
        try {
            _factory->CreatePathGeometry(&path);
            path->Open(&sink);
            var begun = false;
            foreach (var segment in ShapeGeometry.Replay(stroke)) {
                if (!begun || stroke.Tool == AnnotationTool.Arrow) {
                    sink->BeginFigure(Local(segment.Start), D2D1_FIGURE_BEGIN.D2D1_FIGURE_BEGIN_HOLLOW);
                    begun = true;
                }
                sink->AddLine(Local(segment.End));
                if (stroke.Tool == AnnotationTool.Arrow) { sink->EndFigure(D2D1_FIGURE_END.D2D1_FIGURE_END_OPEN); }
            }
            if (begun && stroke.Tool != AnnotationTool.Arrow) {
                sink->EndFigure(stroke.Tool is AnnotationTool.Rectangle or AnnotationTool.Ellipse
                    ? D2D1_FIGURE_END.D2D1_FIGURE_END_CLOSED : D2D1_FIGURE_END.D2D1_FIGURE_END_OPEN);
            }
            sink->Close();
            _target->DrawGeometry((ID2D1Geometry*)path, (ID2D1Brush*)_brush, stroke.Style.Width, _roundStroke);
        } finally {
            if (sink != null) { sink->Release(); }
            if (path != null) { path->Release(); }
        }
    }

    // Fill an anti-aliased ribbon; interpolating width avoids visible pressure steps.
    private void PressureSegment(InkSegment segment, float width)
    {
        var length = Vector2.Distance(segment.Start, segment.Control) + Vector2.Distance(segment.Control, segment.End);
        if (length < .01f) { Dot(segment.End, width * segment.EndWidthFactor); return; }
        var steps = Math.Clamp((int)MathF.Ceiling(length / 2), 1, 4096);
        var left = new D2D_POINT_2F[steps + 1];
        var right = new D2D_POINT_2F[steps + 1];
        for (var index = 0; index <= steps; index++) {
            var t = (float)index / steps;
            var u = 1 - t;
            var point = u * u * segment.Start + 2 * u * t * segment.Control + t * t * segment.End;
            var tangent = 2 * u * (segment.Control - segment.Start) + 2 * t * (segment.End - segment.Control);
            if (tangent.LengthSquared() < .0001f) { tangent = segment.End - segment.Start; }
            if (tangent.LengthSquared() < .0001f) { tangent = segment.Control - segment.Start; }
            if (tangent.LengthSquared() < .0001f) { tangent = Vector2.UnitX; }
            var normal = Vector2.Normalize(new Vector2(-tangent.Y, tangent.X));
            var radius = width * (u * u * segment.StartWidthFactor + 2 * u * t * segment.ControlWidthFactor + t * t * segment.EndWidthFactor) / 2;
            left[index] = Local(point + normal * radius);
            right[index] = Local(point - normal * radius);
        }
        ID2D1PathGeometry* path = null;
        ID2D1GeometrySink* sink = null;
        try {
            _factory->CreatePathGeometry(&path);
            path->Open(&sink);
            sink->BeginFigure(left[0], D2D1_FIGURE_BEGIN.D2D1_FIGURE_BEGIN_FILLED);
            for (var index = 1; index <= steps; index++) { sink->AddLine(left[index]); }
            for (var index = steps; index >= 0; index--) { sink->AddLine(right[index]); }
            sink->EndFigure(D2D1_FIGURE_END.D2D1_FIGURE_END_CLOSED);
            sink->Close();
            _target->FillGeometry((ID2D1Geometry*)path, (ID2D1Brush*)_brush, null);
            Dot(segment.Start, width * segment.StartWidthFactor);
            Dot(segment.End, width * segment.EndWidthFactor);
        } finally {
            if (sink != null) { sink->Release(); }
            if (path != null) { path->Release(); }
        }
    }

    internal uint ReadPixel(int x, int y)
    {
        Verify();
        if (x < 0 || y < 0 || x >= Width || y >= Height) { throw new ArgumentOutOfRangeException(nameof(x)); }
        return unchecked((uint)Marshal.ReadInt32(_bits, (y * Width + x) * 4));
    }

    private void Verify()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread != NativeMethods.GetCurrentThreadId()) { throw new InvalidOperationException("Ink surfaces belong to their creating thread."); }
    }
    public void Dispose()
    {
        if (_disposed) { return; }
        Verify();
        if (_roundStroke != null) { _roundStroke->Release(); _roundStroke = null; }
        if (_brush != null) { _brush->Release(); _brush = null; }
        if (_target != null) { _target->Release(); _target = null; }
        if (_factory != null) { _factory->Release(); _factory = null; }
        if (_previousBitmap != 0 && _previousBitmap != new nint(-1)) { NativeMethods.SelectObject(_dc, _previousBitmap); }
        if (_bitmap != 0) { NativeMethods.DeleteObject(_bitmap); }
        if (_dc != 0) { NativeMethods.DeleteDC(_dc); }
        _disposed = true;
    }
}
