using System.Collections.ObjectModel;
using System.Numerics;

namespace ScreenInk.Core.Models;

public readonly record struct InkStyle(float Width, byte Red, byte Green, byte Blue, byte Alpha = 255)
{
    public static InkStyle MousePen => new(4, 255, 80, 80);
    public static InkStyle TabletPen => new(8, 255, 80, 80);
}

// Samples and width are in physical desktop pixels, independent of WinUI's DIP coordinates.
public sealed class InkStroke
{
    private readonly List<InkSample> _samples = [];
    public Guid Id { get; } = Guid.NewGuid();
    public InkStyle Style { get; }
    public InkInputKind InputKind { get; }
    public AnnotationTool Tool { get; }
    public ReadOnlyCollection<InkSample> Samples { get; }

    public InkStroke(InkStyle style, InkInputKind inputKind = InkInputKind.Mouse, AnnotationTool tool = AnnotationTool.Pen)
    {
        if (!float.IsFinite(style.Width) || style.Width <= 0 || style.Width > 256) {
            throw new ArgumentOutOfRangeException(nameof(style));
        }
        Style = style;
        if (!Enum.IsDefined(inputKind)) { throw new ArgumentOutOfRangeException(nameof(inputKind)); }
        InputKind = inputKind;
        if (tool != AnnotationTool.Pen && !tool.IsShape()) { throw new ArgumentOutOfRangeException(nameof(tool)); }
        Tool = tool;
        Samples = _samples.AsReadOnly();
    }

    public bool AddSample(Vector2 point) => AddSample(new InkSample(point));

    public void SetShapeEnd(Vector2 point)
    {
        if (!Tool.IsShape() || _samples.Count == 0) { throw new InvalidOperationException("Only a shape draft has a replaceable endpoint."); }
        var sample = new InkSample(point);
        sample.Validate();
        if (_samples.Count == 1) { _samples.Add(sample); }
        else { _samples[1] = sample; }
    }

    public bool AddSample(InkSample sample)
    {
        sample.Validate();
        if (_samples.Count > 0) {
            var previous = _samples[^1];
            if (Vector2.DistanceSquared(previous.Position, sample.Position) < .25f &&
                previous.Pressure == sample.Pressure && previous.TiltX == sample.TiltX &&
                previous.TiltY == sample.TiltY && previous.Rotation == sample.Rotation && previous.Buttons == sample.Buttons) { return false; }
        }
        _samples.Add(sample);
        return true;
    }
}
