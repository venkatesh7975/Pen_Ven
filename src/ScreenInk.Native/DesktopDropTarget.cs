using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace ScreenInk.Native;

[ComVisible(true), Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDesktopDropTarget
{
    [PreserveSig] int DragEnter([MarshalAs(UnmanagedType.Interface)] IDataObject data, uint keys, DropPoint point, ref uint effect);
    [PreserveSig] int DragOver(uint keys, DropPoint point, ref uint effect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int Drop([MarshalAs(UnmanagedType.Interface)] IDataObject data, uint keys, DropPoint point, ref uint effect);
}
[StructLayout(LayoutKind.Sequential)]
public struct DropPoint { public int X, Y; }
public sealed record DesktopMediaDrop(string Content, int X, int Y);

// Visible only during a drag originating in our media library. No pixels are drawn.
// Other applications remain usable when there is no drag in progress.
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class DesktopDropTarget : IDesktopDropTarget, IDisposable
{
    private readonly NativeWindow _window;
    private readonly short _format;
    private bool _accepted, _disposed;
    public event Action<DesktopMediaDrop>? Dropped;
    public event Action<Exception>? Failed;
    public DesktopDropTarget(string format)
    {
        Marshal.ThrowExceptionForHR(OleInitialize(0));
        NativeWindow? window = null;
        try {
            // The library also supplies this JSON as standard Unicode text. Its OLE
            // representation is documented and works across WinRT/COM boundaries.
            _format = 13;
            window = new NativeWindow("ScreenInk media drop", NativeMethods.NoRedirectionBitmap | NativeMethods.Topmost | NativeMethods.ToolWindow | NativeMethods.NoActivate, 0, 0, 1, 1);
            var pointer = Marshal.GetComInterfaceForObject(this, typeof(IDesktopDropTarget));
            try { Marshal.ThrowExceptionForHR(RegisterDragDrop(window.Handle, pointer)); }
            finally { Marshal.Release(pointer); }
            _window = window;
        } catch { window?.Dispose(); OleUninitialize(); throw; }
    }
    public void Show(CaptureBounds bounds, params nint[] controls)
    {
        _window.Position(bounds.Left, bounds.Top, bounds.Width, bounds.Height, true);
        foreach (var control in controls) {
            if (control != 0) { NativeMethods.SetWindowPos(control, NativeMethods.TopmostHandle, 0, 0, 0, 0, NativeMethods.NoActivatePosition | NativeMethods.NoMove | NativeMethods.NoSize); }
        }
    }
    public void Hide() { if (!_disposed) { _window.Hide(); } }
    public int DragEnter(IDataObject data, uint keys, DropPoint point, ref uint effect)
    {
        try { var format = Format(); _accepted = data.QueryGetData(ref format) == 0; effect &= _accepted ? 1u : 0; }
        catch { _accepted = false; effect = 0; }
        return 0;
    }
    public int DragOver(uint keys, DropPoint point, ref uint effect) { effect &= _accepted ? 1u : 0; return 0; }
    public int DragLeave() { _accepted = false; return 0; }
    public int Drop(IDataObject data, uint keys, DropPoint point, ref uint effect)
    {
        try {
            var format = Format();
            data.GetData(ref format, out var medium);
            string text;
            try {
                if (medium.tymed != TYMED.TYMED_HGLOBAL) { throw new ArgumentException("Unsupported drag data."); }
                var length = checked((int)GlobalSize(medium.unionmember));
                if (length is <= 0 or > 80000) { throw new ArgumentException("Media drop is too large."); }
                var pointer = GlobalLock(medium.unionmember);
                if (pointer == 0) { throw new InvalidOperationException("Could not read drag data."); }
                try {
                    var bytes = new byte[length]; Marshal.Copy(pointer, bytes, 0, length);
                    // WinRT string custom formats use UTF-16 HGLOBAL, including the terminator.
                    text = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
                } finally { GlobalUnlock(medium.unionmember); }
            } finally { ReleaseStgMedium(ref medium); }
            Hide(); _accepted = false;
            Dropped?.Invoke(new(text, point.X, point.Y)); effect &= 1;
        } catch (Exception error) { effect = 0; Hide(); try { Failed?.Invoke(error); } catch { } }
        return 0;
    }
    private FORMATETC Format() => new() { cfFormat = _format, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
    public void Dispose()
    {
        if (_disposed) { return; }
        _window.VerifyThread(); RevokeDragDrop(_window.Handle); _window.Dispose(); _disposed = true; OleUninitialize();
    }
    [DllImport("ole32.dll")] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();
    [DllImport("ole32.dll")] private static extern int RegisterDragDrop(nint window, nint target);
    [DllImport("ole32.dll")] private static extern int RevokeDragDrop(nint window);
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string format);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(nint memory);
}
