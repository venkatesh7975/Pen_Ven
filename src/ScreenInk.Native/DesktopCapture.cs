using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ScreenInk.Native;

public readonly record struct CaptureBounds(int Left, int Top, int Width, int Height);
public sealed record CapturedPixels(int Width, int Height, byte[] Bgra);

public static class DesktopCapture
{
    public static CaptureBounds MonitorForWindow(nint window)
    {
        var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfoW(NativeMethods.MonitorFromWindow(window, 2), ref info)) {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not find the screen to capture.");
        }
        return new(info.Monitor.Left, info.Monitor.Top,
            info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
    }

    public static CapturedPixels Capture(CaptureBounds bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) { throw new ArgumentOutOfRangeException(nameof(bounds)); }
        _ = NativeMethods.DwmFlush();
        var screen = NativeMethods.GetDC(0);
        if (screen == 0) { throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the screen."); }
        try { return CaptureFromDc(screen, bounds, 0x00CC0020 | 0x40000000); }
        finally { NativeMethods.ReleaseDC(0, screen); }
    }

    // Shared with native tests, which capture a synthetic memory DC rather than desktop data.
    internal static CapturedPixels CaptureFromDc(nint source, CaptureBounds bounds, uint operation = 0x00CC0020)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) { throw new ArgumentOutOfRangeException(nameof(bounds)); }
        var length = checked(bounds.Width * bounds.Height * 4);
        nint dc = 0, bitmap = 0, previous = 0;
        try {
            dc = NativeMethods.CreateCompatibleDC(source);
            if (dc == 0) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
            var info = new NativeMethods.BitmapInfo { Size = 40, Width = bounds.Width, Height = -bounds.Height, Planes = 1, BitCount = 32 };
            bitmap = NativeMethods.CreateDIBSection(dc, ref info, 0, out var pixels, 0, 0);
            if (bitmap == 0 || pixels == 0) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
            previous = NativeMethods.SelectObject(dc, bitmap);
            if (previous == 0 || previous == new nint(-1)) { throw new Win32Exception("Could not prepare the screenshot bitmap."); }
            if (!NativeMethods.BitBlt(dc, 0, 0, bounds.Width, bounds.Height, source, bounds.Left, bounds.Top, operation)) {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not capture this screen.");
            }
            if (!NativeMethods.GdiFlush()) { throw new Win32Exception("Could not finish the screenshot."); }
            var data = new byte[length];
            Marshal.Copy(pixels, data, 0, length);
            // GDI's reserved alpha byte is undefined. A desktop screenshot is opaque.
            for (var i = 3; i < data.Length; i += 4) { data[i] = 255; }
            return new(bounds.Width, bounds.Height, data);
        } finally {
            if (previous != 0 && previous != new nint(-1)) { NativeMethods.SelectObject(dc, previous); }
            if (bitmap != 0) { NativeMethods.DeleteObject(bitmap); }
            if (dc != 0) { NativeMethods.DeleteDC(dc); }
        }
    }
}
