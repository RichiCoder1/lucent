using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL3;

namespace Lucent.Platform.Windows;

/// <summary>Host-owned comctl32 subclass that wakes SDL for Windows settings messages and always chains other subclasses.</summary>
internal sealed unsafe partial class WindowsSettingsListener : IDisposable
{
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint WM_THEMECHANGED = 0x031A;
    private const uint WM_NCDESTROY = 0x0082;
    private static long _nextId;
    private static readonly nint Procedure = (nint)
        (delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nint, nint>)&SubclassProc;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly nint _window;
    private readonly nuint _id = checked((nuint)Interlocked.Increment(ref _nextId));
    private GCHandle _handle;
    private bool _pending;
    private bool _destroyed;
    private bool _disposed;

    internal WindowsSettingsListener(nint window)
    {
        ArgumentOutOfRangeException.ThrowIfZero(window, nameof(window));
        _window = window;
        _handle = GCHandle.Alloc(this);
        if (!SetWindowSubclass(window, Procedure, _id, GCHandle.ToIntPtr(_handle)))
        {
            _handle.Free();
            throw new InvalidOperationException("SetWindowSubclass(settings listener) failed.");
        }
    }

    internal bool TakePending()
    {
        CheckThread();
        var pending = _pending;
        _pending = false;
        return pending;
    }

    public void Dispose()
    {
        CheckThread();
        if (_disposed)
            return;
        _disposed = true;
        if (_destroyed)
            return;
        if (!RemoveWindowSubclass(_window, Procedure, _id))
            throw new InvalidOperationException("RemoveWindowSubclass(settings listener) failed.");
        ReleaseHandle();
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
        WindowsSettingsListener? listener = null;
        try
        {
            listener = GCHandle.FromIntPtr(data).Target as WindowsSettingsListener;
            if (listener is not null && message is WM_SETTINGCHANGE or WM_THEMECHANGED)
            {
                listener._pending = true;
                var @event = new SDL.Event { Type = (uint)SDL.EventType.User };
                _ = SDL.PushEvent(ref @event);
            }
        }
        catch { }

        nint result;
        try
        {
            result = DefSubclassProc(window, message, wParam, lParam);
        }
        catch
        {
            result = 0;
        }
        if (message == WM_NCDESTROY && listener is not null)
            try
            {
                listener.Destroyed();
            }
            catch { }
        return result;
    }

    private void Destroyed()
    {
        _destroyed = true;
        ReleaseHandle();
    }

    private void ReleaseHandle()
    {
        if (_handle.IsAllocated)
            _handle.Free();
    }

    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException(
                "Windows settings listener access must remain on its owner thread."
            );
    }

    [LibraryImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(nint window, nint procedure, nuint id, nint data);

    [LibraryImport("comctl32.dll", SetLastError = true)]
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
