using System.Numerics;
using ScreenInk.Native;

namespace ScreenInk.Input.MouseInput;

public sealed class MouseCapture(nint window) : IDisposable
{
    public bool IsCaptured => NativeMethods.GetCapture() == window;

    public bool Begin()
    {
        NativeMethods.SetCapture(window);
        return IsCaptured;
    }

    public void Release()
    {
        if (IsCaptured) { NativeMethods.ReleaseCapture(); }
    }

    public static Vector2 DesktopPoint(nint lParam, int monitorLeft, int monitorTop) => new(
        monitorLeft + unchecked((short)(long)lParam),
        monitorTop + unchecked((short)((long)lParam >> 16)));

    public void Dispose() => Release();
}
