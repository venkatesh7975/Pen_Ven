using System.Numerics;
using ScreenInk.Core.Models;
using ScreenInk.Drawing.Geometry;

namespace ScreenInk.Tools.Eraser;

public sealed record RemovedStroke(int Index, InkStroke Stroke);

public static class StrokeEraser
{
    public static IReadOnlyList<RemovedStroke> Erase(IList<InkStroke> strokes, Vector2 from, Vector2 to, float diameter)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        if (!float.IsFinite(diameter) || diameter < 4 || diameter > 128) { throw new ArgumentOutOfRangeException(nameof(diameter)); }
        List<RemovedStroke> removed = [];
        for (var index = strokes.Count - 1; index >= 0; index--) {
            if (!StrokeHitTester.Intersects(strokes[index], from, to, diameter / 2)) { continue; }
            removed.Add(new RemovedStroke(index, strokes[index]));
            strokes.RemoveAt(index);
        }
        removed.Reverse();
        return removed;
    }
}
