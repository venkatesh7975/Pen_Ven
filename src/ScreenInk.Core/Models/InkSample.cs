using System.Numerics;

namespace ScreenInk.Core.Models;

public enum InkInputKind { Mouse, Pen }

[Flags]
public enum PenButtons { None = 0, Barrel = 1, Inverted = 2, Eraser = 4 }

public readonly record struct InkSample(Vector2 Position, float? Pressure = null,
    int? TiltX = null, int? TiltY = null, uint? Rotation = null, PenButtons Buttons = PenButtons.None)
{
    public float WidthFactor => Pressure.HasValue ? .2f + .8f * Pressure.Value : 1;

    public void Validate()
    {
        if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y) ||
            (Pressure.HasValue && (!float.IsFinite(Pressure.Value) || Pressure < 0 || Pressure > 1)) ||
            TiltX is < -90 or > 90 || TiltY is < -90 or > 90 || Rotation is > 359) {
            throw new ArgumentOutOfRangeException(nameof(InkSample), "Ink coordinates and pen measurements must be finite and in range.");
        }
    }
}
