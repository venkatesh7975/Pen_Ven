using System.Numerics;
using ScreenInk.Core.Models;
using ScreenInk.Drawing.StrokeEngine;

namespace ScreenInk.Drawing.Geometry;

public static class ShapeGeometry
{
    public static IEnumerable<InkSegment> Replay(InkStroke stroke)
    {
        if (!stroke.Tool.IsShape()) { throw new ArgumentException("Expected a shape stroke.", nameof(stroke)); }
        if (stroke.Samples.Count < 2) { yield break; }
        var start = stroke.Samples[0].Position;
        var end = stroke.Samples[^1].Position;
        if (stroke.Tool is AnnotationTool.Line or AnnotationTool.Arrow) {
            if (Vector2.DistanceSquared(start, end) < .0001f) { yield break; }
            yield return InkSegment.Line(start, end);
            if (stroke.Tool == AnnotationTool.Arrow) {
                var direction = Vector2.Normalize(end - start);
                var normal = new Vector2(-direction.Y, direction.X);
                var length = MathF.Min(Vector2.Distance(start, end) * .4f, MathF.Max(12, stroke.Style.Width * 4));
                yield return InkSegment.Line(end, end - direction * length + normal * length * .5f);
                yield return InkSegment.Line(end, end - direction * length - normal * length * .5f);
            }
            yield break;
        }
        var min = Vector2.Min(start, end);
        var max = Vector2.Max(start, end);
        if (max.X - min.X < .001f || max.Y - min.Y < .001f) { yield break; }
        if (stroke.Tool == AnnotationTool.Rectangle) {
            Vector2[] corners = [min, new(max.X, min.Y), max, new(min.X, max.Y)];
            for (var i = 0; i < 4; i++) { yield return InkSegment.Line(corners[i], corners[(i + 1) % 4]); }
        } else {
            var center = (min + max) / 2;
            var radius = (max - min) / 2;
            var steps = Math.Clamp((int)MathF.Ceiling(MathF.PI * MathF.Max(max.X - min.X, max.Y - min.Y) / 8), 64, 512);
            Vector2 Point(int i) {
                var angle = 2 * MathF.PI * (i % steps) / steps;
                return center + new Vector2(MathF.Cos(angle) * radius.X, MathF.Sin(angle) * radius.Y);
            }
            for (var i = 0; i < steps; i++) { yield return InkSegment.Line(Point(i), Point(i + 1)); }
        }
    }
}
