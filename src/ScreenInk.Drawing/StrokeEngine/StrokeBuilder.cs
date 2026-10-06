using System.Numerics;
using ScreenInk.Core.Models;

namespace ScreenInk.Drawing.StrokeEngine;

public readonly record struct InkSegment(Vector2 Start, Vector2 Control, Vector2 End,
    float StartWidthFactor = 1, float ControlWidthFactor = 1, float EndWidthFactor = 1)
{
    public static InkSegment Line(Vector2 start, Vector2 end) => new(start, (start + end) / 2, end);
}

public sealed class StrokeBuilder
{
    private Vector2 _tail;
    private float _tailWidth;
    public InkStroke Stroke { get; }

    public StrokeBuilder(Vector2 first, InkStyle style) : this(new InkSample(first), style, InkInputKind.Mouse) { }

    public StrokeBuilder(InkSample first, InkStyle style, InkInputKind kind)
    {
        Stroke = new InkStroke(style, kind);
        Stroke.AddSample(first);
        _tail = first.Position;
        _tailWidth = first.WidthFactor;
    }

    // Midpoint quadratic interpolation has one sample of latency, with no overshoot at corners.
    public InkSegment? Append(Vector2 point) => Append(new InkSample(point));

    public InkSegment? Append(InkSample point)
    {
        var previous = Stroke.Samples[^1];
        if (!Stroke.AddSample(point)) { return null; }
        var midpoint = (previous.Position + point.Position) / 2;
        var width = (previous.WidthFactor + point.WidthFactor) / 2;
        var segment = new InkSegment(_tail, previous.Position, midpoint, _tailWidth, previous.WidthFactor, width);
        _tail = midpoint;
        _tailWidth = width;
        return segment;
    }

    public InkSegment Finish()
    {
        var last = Stroke.Samples[^1];
        return new InkSegment(_tail, (_tail + last.Position) / 2, last.Position,
            _tailWidth, (_tailWidth + last.WidthFactor) / 2, last.WidthFactor);
    }

    public static IEnumerable<InkSegment> Replay(InkStroke stroke)
    {
        if (stroke.Samples.Count == 0) { yield break; }
        var tail = stroke.Samples[0].Position;
        var tailWidth = stroke.Samples[0].WidthFactor;
        for (var index = 1; index < stroke.Samples.Count; index++) {
            var previous = stroke.Samples[index - 1];
            var midpoint = (previous.Position + stroke.Samples[index].Position) / 2;
            var width = (previous.WidthFactor + stroke.Samples[index].WidthFactor) / 2;
            yield return new InkSegment(tail, previous.Position, midpoint, tailWidth, previous.WidthFactor, width);
            tail = midpoint;
            tailWidth = width;
        }
        var last = stroke.Samples[^1];
        yield return new InkSegment(tail, (tail + last.Position) / 2, last.Position,
            tailWidth, (tailWidth + last.WidthFactor) / 2, last.WidthFactor);
    }
}
