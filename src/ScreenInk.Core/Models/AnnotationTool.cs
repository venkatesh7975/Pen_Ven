namespace ScreenInk.Core.Models;

public enum AnnotationTool { Pen, StrokeEraser, Line, Arrow, Rectangle, Ellipse, Laser }

public static class AnnotationToolExtensions
{
    public static bool IsShape(this AnnotationTool tool) => tool is AnnotationTool.Line or AnnotationTool.Arrow or AnnotationTool.Rectangle or AnnotationTool.Ellipse;
}

public readonly record struct InkColor(byte Red, byte Green, byte Blue)
{
    public static InkColor Default => new(255, 80, 80);
    public string Hex => $"#{Red:X2}{Green:X2}{Blue:X2}";
}

public sealed record StrokeSummary(Guid Id, int Number, InkInputKind InputKind, int SampleCount);
