using System.Runtime.InteropServices;

namespace ScreenInk.Native;

internal static class NativeMethods
{
    internal const uint Popup = 0x80000000;
    internal const uint Topmost = 0x00000008;
    internal const uint ToolWindow = 0x00000080;
    internal const uint NoActivate = 0x08000000;
    internal const uint NoRedirectionBitmap = 0x00200000;
    internal const uint Layered = 0x00080000;
    internal const uint Transparent = 0x00000020;
    internal const uint ShowWindowFlag = 0x0040;
    internal const uint NoActivatePosition = 0x0010;
    internal const uint NoMove = 0x0002;
    internal const uint NoSize = 0x0001;
    internal const uint MouseActivate = 0x0021;
    internal const uint NcHitTest = 0x0084;
    internal const uint Paint = 0x000F;
    internal const uint EraseBackground = 0x0014;
    internal const uint LeftButtonDown = 0x0201;
    internal const uint LeftButtonUp = 0x0202;
    internal const uint MouseMove = 0x0200;
    internal const uint CaptureChanged = 0x0215;
    internal const uint CancelMode = 0x001F;
    internal const uint PointerUpdate = 0x0245;
    internal const uint PointerDown = 0x0246;
    internal const uint PointerUp = 0x0247;
    internal const uint PointerLeave = 0x024A;
    internal const uint PointerEnter = 0x0249;
    internal const uint PointerActivate = 0x024B;
    internal const uint PointerCaptureChanged = 0x024C;
    internal const uint TabletQuerySystemGestureStatus = 0x02CC;
    internal const uint HotKey = 0x0312;
    internal const uint DisplayChange = 0x007E;
    internal const uint DpiChanged = 0x02E0;
    internal const uint Close = 0x0010;
    internal const uint Destroy = 0x0002;
    internal static readonly nint TopmostHandle = new(-1);
    internal static readonly nint NotTopmostHandle = new(-2);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClass
    {
        internal uint Size, Style;
        internal nint Procedure;
        internal int ClassExtra, WindowExtra;
        internal nint Instance, Icon, Cursor, Background;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)] internal string ClassName;
        internal nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        internal int X, Y;
        internal Point(int x, int y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Size
    {
        internal int Width, Height;
        internal Size(int width, int height) { Width = width; Height = height; }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { internal int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        internal uint Size;
        internal Rect Monitor, Work;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        internal uint Size;
        internal int Width, Height;
        internal ushort Planes, BitCount;
        internal uint Compression, SizeImage;
        internal int XPelsPerMeter, YPelsPerMeter;
        internal uint ColorsUsed, ColorsImportant, Color;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct BlendFunction
    {
        internal byte Operation, Flags, ConstantAlpha, AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        internal nint Window;
        internal uint Id;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal Point Point;
        internal uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PointerInfo
    {
        internal uint PointerType, PointerId, FrameId, Flags;
        internal nint SourceDevice, Target;
        internal Point PixelLocation, HimetricLocation, PixelLocationRaw, HimetricLocationRaw;
        internal uint Time, HistoryCount;
        internal int InputData;
        internal uint KeyStates;
        internal ulong PerformanceCount;
        internal uint ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PointerPenInfo
    {
        internal PointerInfo Pointer;
        internal uint PenFlags, PenMask, Pressure, Rotation;
        internal int TiltX, TiltY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPointerType(uint pointerId, out uint pointerType);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPointerPenInfo(uint pointerId, out PointerPenInfo penInfo);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPointerPenInfoHistory(uint pointerId, ref uint entriesCount, [Out] PointerPenInfo[] penInfo);
    [DllImport("user32.dll")]
    internal static extern nint GetMessageExtraInfo();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetModuleHandleW(string? moduleName);
    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassExW(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowExW(uint extendedStyle, string className, string title,
        uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint DefWindowProcW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint LoadCursorW(nint instance, nint cursorName);
    [DllImport("user32.dll")]
    internal static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    internal static extern nint GetDC(nint window);
    [DllImport("user32.dll")]
    internal static extern nint SetCapture(nint window);
    [DllImport("user32.dll")]
    internal static extern nint GetCapture();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BitBlt(nint destination, int x, int y, int width, int height,
        nint source, int sourceX, int sourceY, uint operation);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GdiFlush();
    [DllImport("dwmapi.dll")]
    internal static extern int DwmFlush();
    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")]
    internal static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(nint dc);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateLayeredWindow(nint window, nint destinationDc, ref Point destination,
        ref Size size, nint sourceDc, ref Point source, uint colorKey, ref BlendFunction blend, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")]
    internal static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetMessageW(out Message message, nint window, uint minimum, uint maximum);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint DispatchMessageW(ref Message message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")]
    internal static extern void PostQuitMessage(int exitCode);
}
