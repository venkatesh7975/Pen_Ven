using System.Numerics;
using ScreenInk.Core.Models;
using ScreenInk.Drawing.StrokeEngine;

namespace ScreenInk.Drawing.Geometry;

public static class StrokeHitTester
{
    private const float Tolerance = .2f;

    public static bool Intersects(InkStroke stroke, Vector2 from, Vector2 to, float radius)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        if (!float.IsFinite(radius) || radius < 0 || !Finite(from) || !Finite(to)) {
            throw new ArgumentOutOfRangeException(nameof(radius));
        }
        if (stroke.Samples.Count == 0) { return false; }
        var first = stroke.Samples[0];
        var dotRadius = radius + stroke.Style.Width * first.WidthFactor / 2;
        if (!stroke.Tool.IsShape() && PointDistanceSquared(first.Position, from, to) <= dotRadius * dotRadius) { return true; }
        foreach (var segment in stroke.Tool.IsShape() ? ShapeGeometry.Replay(stroke) : StrokeBuilder.Replay(stroke)) {
            if (HitCurve(segment, stroke.Style.Width, from, to, radius, 0)) { return true; }
        }
        return false;
    }

    private static bool HitCurve(InkSegment curve, float width, Vector2 from, Vector2 to, float radius, int depth)
    {
        var maximum = MathF.Max(curve.StartWidthFactor, MathF.Max(curve.ControlWidthFactor, curve.EndWidthFactor));
        var reach = radius + width * maximum / 2 + Tolerance;
        var minimumPoint = Vector2.Min(curve.Start, Vector2.Min(curve.Control, curve.End));
        var maximumPoint = Vector2.Max(curve.Start, Vector2.Max(curve.Control, curve.End));
        var sweepMinimum = Vector2.Min(from, to);
        var sweepMaximum = Vector2.Max(from, to);
        if (sweepMaximum.X < minimumPoint.X - reach || sweepMinimum.X > maximumPoint.X + reach ||
            sweepMaximum.Y < minimumPoint.Y - reach || sweepMinimum.Y > maximumPoint.Y + reach) { return false; }

        var flat = PointDistanceSquared(curve.Control, curve.Start, curve.End) <= Tolerance * Tolerance;
        var minimum = MathF.Min(curve.StartWidthFactor, MathF.Min(curve.ControlWidthFactor, curve.EndWidthFactor));
        if (depth >= 16 || (flat && width * (maximum - minimum) <= .4f)) {
            return SegmentDistanceSquared(from, to, curve.Start, curve.End) <= reach * reach;
        }
        var a = (curve.Start + curve.Control) / 2;
        var b = (curve.Control + curve.End) / 2;
        var middle = (a + b) / 2;
        var wa = (curve.StartWidthFactor + curve.ControlWidthFactor) / 2;
        var wb = (curve.ControlWidthFactor + curve.EndWidthFactor) / 2;
        var wm = (wa + wb) / 2;
        return HitCurve(new(curve.Start, a, middle, curve.StartWidthFactor, wa, wm), width, from, to, radius, depth + 1) ||
            HitCurve(new(middle, b, curve.End, wm, wb, curve.EndWidthFactor), width, from, to, radius, depth + 1);
    }

    private static bool Finite(Vector2 point) => float.IsFinite(point.X) && float.IsFinite(point.Y);
    private static float PointDistanceSquared(Vector2 point, Vector2 start, Vector2 end)
    {
        var direction = end - start;
        var length = direction.LengthSquared();
        if (length < .000001f) { return Vector2.DistanceSquared(point, start); }
        var t = Math.Clamp(Vector2.Dot(point - start, direction) / length, 0, 1);
        return Vector2.DistanceSquared(point, start + t * direction);
    }
    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    private static float SegmentDistanceSquared(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        var ab = b - a;
        var cd = d - c;
        var denominator = Cross(ab, cd);
        if (MathF.Abs(denominator) > .000001f) {
            var t = Cross(c - a, cd) / denominator;
            var u = Cross(c - a, ab) / denominator;
            if (t >= 0 && t <= 1 && u >= 0 && u <= 1) { return 0; }
        }
        return MathF.Min(MathF.Min(PointDistanceSquared(a, c, d), PointDistanceSquared(b, c, d)),
            MathF.Min(PointDistanceSquared(c, a, b), PointDistanceSquared(d, a, b)));
    }
}
