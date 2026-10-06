using System.Numerics;
using ScreenInk.Core.Models;

namespace ScreenInk.Tools.Shapes;

public sealed class ShapeDraft
{
    public InkStroke Stroke { get; }
    public bool IsMeaningful {
        get {
            var delta = Stroke.Samples[^1].Position - Stroke.Samples[0].Position;
            return Stroke.Tool is AnnotationTool.Rectangle or AnnotationTool.Ellipse
                ? MathF.Abs(delta.X) >= 2 && MathF.Abs(delta.Y) >= 2 : delta.LengthSquared() >= 4;
        }
    }
    public ShapeDraft(AnnotationTool tool, Vector2 start, InkStyle style, InkInputKind inputKind)
    {
        if (!tool.IsShape()) { throw new ArgumentOutOfRangeException(nameof(tool)); }
        Stroke = new InkStroke(style, inputKind, tool);
        Stroke.AddSample(start);
        Stroke.SetShapeEnd(start);
    }
    public void Update(Vector2 end) => Stroke.SetShapeEnd(end);
}
