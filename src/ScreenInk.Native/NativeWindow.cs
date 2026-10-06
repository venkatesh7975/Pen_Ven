using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ScreenInk.Native;

internal sealed class NativeWindow : IDisposable
{
    private const string ClassName = "ScreenInk.NativeSurface.v1";
    private static readonly NativeMethods.WindowProcedure Procedure = Dispatch;
    private static readonly Dictionary<nint, NativeWindow> Windows = new();
    private static readonly object RegistrationLock = new();
    private static bool _registered;
    private readonly uint _threadId = NativeMethods.GetCurrentThreadId();

    internal nint Handle { get; private set; }
    internal Func<uint, nuint, nint, nint?>? MessageHandler { get; set; }
    internal event Action<Exception>? CallbackFailed;

    internal NativeWindow(string title, uint extendedStyle, int x, int y, int width, int height)
    {
        RegisterClass();
        Handle = NativeMethods.CreateWindowExW(extendedStyle, ClassName, title, NativeMethods.Popup,
            x, y, width, height, 0, 0, NativeMethods.GetModuleHandleW(null), 0);
        if (Handle == 0) { throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create an overlay surface."); }
        lock (Windows) { Windows.Add(Handle, this); }
    }

    internal void VerifyThread()
    {
        if (NativeMethods.GetCurrentThreadId() != _threadId) {
            throw new InvalidOperationException("Native windows must be accessed from their creating thread.");
        }
    }

    internal void Position(int x, int y, int width, int height, bool show)
    {
        VerifyThread();
        var flags = NativeMethods.NoActivatePosition | (show ? NativeMethods.ShowWindowFlag : 0);
        if (!NativeMethods.SetWindowPos(Handle, NativeMethods.TopmostHandle, x, y, width, height, flags)) {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not position the overlay.");
        }
    }

    internal void Hide()
    {
        VerifyThread();
        NativeMethods.ShowWindow(Handle, 0);
    }

    public void Dispose()
    {
        if (Handle == 0) { return; }
        VerifyThread();
        if (!NativeMethods.DestroyWindow(Handle)) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
        lock (Windows) { Windows.Remove(Handle); }
        Handle = 0;
    }

    private static void RegisterClass()
    {
        lock (RegistrationLock) {
            if (_registered) { return; }
            var windowClass = new NativeMethods.WindowClass {
                Size = (uint)Marshal.SizeOf<NativeMethods.WindowClass>(),
                Procedure = Marshal.GetFunctionPointerForDelegate(Procedure),
                Instance = NativeMethods.GetModuleHandleW(null),
                Cursor = NativeMethods.LoadCursorW(0, new nint(32512)),
                ClassName = ClassName
            };
            if (NativeMethods.RegisterClassExW(ref windowClass) == 0) {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register the overlay window class.");
            }
            _registered = true;
        }
    }

    private static nint Dispatch(nint window, uint message, nuint wParam, nint lParam)
    {
        NativeWindow? surface;
        lock (Windows) { Windows.TryGetValue(window, out surface); }
        try {
            var result = surface?.MessageHandler?.Invoke(message, wParam, lParam);
            if (result.HasValue) { return result.Value; }
        } catch (Exception error) {
            // Never allow a managed exception to escape a reverse P/Invoke callback.
            NativeMethods.ShowWindow(window, 0);
            try { surface?.CallbackFailed?.Invoke(error); } catch { }
        }
        return NativeMethods.DefWindowProcW(window, message, wParam, lParam);
    }
}
