using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lucent.Core;

namespace Lucent.Platform.Windows;

/// <summary>Keeps a tooltip's shadow from taking pointer input away from its trigger.</summary>
internal sealed unsafe partial class WindowsPopupInputRegion : IDisposable
{
    private const uint WmNcHitTest = 0x0084,
        WmNcDestroy = 0x0082;
    private static long _nextId;
    private static readonly nint Procedure = (nint)
        (delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nint, nint>)&SubclassProc;
    private readonly nint _window;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly nuint _id = checked((nuint)Interlocked.Increment(ref _nextId));
    private GCHandle _handle;
    private LayoutRect _bounds;
    private bool _destroyed;
    private bool _disposed;

    internal WindowsPopupInputRegion(nint window)
    {
        ArgumentOutOfRangeException.ThrowIfZero(window);
        _window = window;
        _handle = GCHandle.Alloc(this);
        if (!SetWindowSubclass(window, Procedure, _id, GCHandle.ToIntPtr(_handle)))
        {
            _handle.Free();
            throw new InvalidOperationException("SetWindowSubclass(popup input region) failed.");
        }
    }

    internal void Update(LayoutRect bounds, float scale)
    {
        CheckThread();
        _bounds = new(
            bounds.X * scale,
            bounds.Y * scale,
            bounds.Width * scale,
            bounds.Height * scale
        );
    }

    public void Dispose()
    {
        CheckThread();
        if (_disposed)
            return;
        if (!_destroyed && !RemoveWindowSubclass(_window, Procedure, _id))
            throw new InvalidOperationException("RemoveWindowSubclass(popup input region) failed.");
        _disposed = true;
        Release();
    }

    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException(
                "Popup input region requires its SDL owner thread."
            );
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint SubclassProc(
        nint window,
        uint message,
        nint wParam,
        nint lParam,
        nuint id,
        nint data
    )
    {
        try
        {
            var region = (WindowsPopupInputRegion)GCHandle.FromIntPtr(data).Target!;
            if (message == WmNcHitTest)
            {
                // Signed screen coordinates also support displays left/above the primary.
                var point = new Point
                {
                    X = (short)((long)lParam & 0xffff),
                    Y = (short)(((long)lParam >> 16) & 0xffff),
                };
                if (
                    ScreenToClient(window, ref point)
                    && !WindowsPopupHost.ContainsMenuPoint(region._bounds, point.X, point.Y)
                )
                    return -1; // HTTRANSPARENT: continue hit testing the owner on this thread.
            }
            if (message == WmNcDestroy)
            {
                region._destroyed = true;
                region.Release();
            }
        }
        catch { }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    private void Release()
    {
        if (_handle.IsAllocated)
            _handle.Free();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ScreenToClient(nint window, ref Point point);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(nint window, nint procedure, nuint id, nint data);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveWindowSubclass(nint window, nint procedure, nuint id);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(
        nint window,
        uint message,
        nint wParam,
        nint lParam
    );
}
