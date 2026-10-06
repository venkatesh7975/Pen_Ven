using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using ScreenInk.Core.Models;
using ScreenInk.Native;

namespace ScreenInk.Input.PenInput;

public enum PointerDeviceKind { Unknown, Touch, Pen, Mouse }
public readonly record struct PenFrame(uint PointerId, InkSample Sample, bool InContact, bool Canceled,
    uint FrameId = 0, ulong PerformanceCount = 0);

public interface IPenInputReader
{
    PointerDeviceKind GetDeviceKind(uint pointerId);
    bool TryRead(uint pointerId, bool history, out PenFrame[] frames, out string? error);
}

public sealed class PenInputReader : IPenInputReader
{
    private readonly NativeMethods.PointerPenInfo[] _history = new NativeMethods.PointerPenInfo[256];

    public PointerDeviceKind GetDeviceKind(uint pointerId) =>
        NativeMethods.GetPointerType(pointerId, out var kind) ? kind switch {
            2 => PointerDeviceKind.Touch, 3 => PointerDeviceKind.Pen, 4 => PointerDeviceKind.Mouse,
            _ => PointerDeviceKind.Unknown
        } : PointerDeviceKind.Unknown;

    public bool TryRead(uint pointerId, bool history, out PenFrame[] frames, out string? error)
    {
        frames = [];
        error = null;
        if (!NativeMethods.GetPointerPenInfo(pointerId, out var latest)) {
            error = new Win32Exception(Marshal.GetLastWin32Error(), "Windows pen data is unavailable.").Message;
            return false;
        }
        if (history && latest.Pointer.HistoryCount > 1) {
            uint count = (uint)_history.Length;
            if (NativeMethods.GetPointerPenInfoHistory(pointerId, ref count, _history)) {
                var available = (int)Math.Min(count, (uint)_history.Length);
                frames = ConvertNewestFirst(_history.AsSpan(0, available));
                if (available > 0) { return true; }
            }
            // A history failure must not discard the successfully retrieved current sample.
        }
        frames = [Convert(latest)];
        return true;
    }

    internal static PenFrame[] ConvertNewestFirst(ReadOnlySpan<NativeMethods.PointerPenInfo> history)
    {
        var frames = new PenFrame[history.Length];
        for (var index = 0; index < history.Length; index++) {
            // Windows returns newest first; the stroke engine requires oldest first.
            frames[index] = Convert(history[history.Length - 1 - index]);
        }
        return frames;
    }

    internal static PenFrame Convert(NativeMethods.PointerPenInfo pen)
    {
        var pointer = pen.Pointer;
        var sample = new InkSample(new Vector2(pointer.PixelLocation.X, pointer.PixelLocation.Y),
            (pen.PenMask & 1) != 0 ? Math.Clamp(pen.Pressure, 0u, 1024u) / 1024f : null,
            (pen.PenMask & 4) != 0 ? Math.Clamp(pen.TiltX, -90, 90) : null,
            (pen.PenMask & 8) != 0 ? Math.Clamp(pen.TiltY, -90, 90) : null,
            (pen.PenMask & 2) != 0 ? Math.Min(pen.Rotation, 359u) : null,
            (PenButtons)(pen.PenFlags & 7));
        return new PenFrame(pointer.PointerId, sample, (pointer.Flags & 4) != 0,
            (pointer.Flags & 0x8000) != 0, pointer.FrameId, pointer.PerformanceCount);
    }

    public static bool IsPromotedMouse(nint extraInfo) =>
        (unchecked((ulong)(long)extraInfo) & 0xFFFFFF00UL) == 0xFF515700UL;
}
