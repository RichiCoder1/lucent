using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lucent.Platform.Windows;

/// <summary>Chains WM_GETOBJECT and returns only the already-created UIA root. It never runs Core work in WndProc.</summary>
internal sealed unsafe partial class WindowsUiaListener : IDisposable
{
    private const uint WmGetObject = 0x003d, WmNcDestroy = 0x0082;
    private const nint UiaRootObjectId = -25;
    private static long _nextId;
    private static readonly nint Procedure = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nint, nint>)&SubclassProc;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly nint _window; private readonly nuint _id = checked((nuint)Interlocked.Increment(ref _nextId));
    private GCHandle _handle; private bool _destroyed; private bool _disposed;
    internal int RootDeliveryCount { get; private set; }
    internal WindowsUiaListener(nint window, WindowsUiaProvider provider)
    {
        if (window == 0) throw new ArgumentOutOfRangeException(nameof(window)); ArgumentNullException.ThrowIfNull(provider);
        _window = window; _handle = GCHandle.Alloc(new State(this, provider));
        if (!SetWindowSubclass(window, Procedure, _id, GCHandle.ToIntPtr(_handle))) { _handle.Free(); throw new InvalidOperationException("SetWindowSubclass(UIA) failed."); }
    }
    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread) throw new InvalidOperationException("UIA subclass disposal must remain on the SDL owner thread.");
        if (_disposed) return; _disposed = true; if (!_destroyed && !RemoveWindowSubclass(_window, Procedure, _id)) throw new InvalidOperationException("RemoveWindowSubclass(UIA) failed."); Release();
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint SubclassProc(nint window, uint message, nint wParam, nint lParam, nuint id, nint data)
    {
        State? state = null;
        try
        {
            state = GCHandle.FromIntPtr(data).Target as State;
            if (message == WmGetObject && lParam == UiaRootObjectId)
            {
                if (state is null) return 0;
                var delivery = WindowsUiaProvider.UiaReturnRawElementProvider(window, wParam, lParam, state.Provider.InterfacePointer);
                state.Listener.RootDeliveryCount++; state.Provider.RecordRootDelivery();
                return delivery;
            }
            var result = DefSubclassProc(window, message, wParam, lParam);
            if (message == WmNcDestroy && state is not null) { state.Listener._destroyed = true; state.Provider.MarkUnavailable(); state.Listener.Release(); }
            return result;
        }
        catch { try { state?.Provider.RecordListenerFailure(); } catch { } return 0; }
    }
    private void Release() { if (_handle.IsAllocated) _handle.Free(); }
    private sealed record State(WindowsUiaListener Listener, WindowsUiaProvider Provider);
    [LibraryImport("comctl32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowSubclass(nint window, nint procedure, nuint id, nint data);
    [LibraryImport("comctl32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool RemoveWindowSubclass(nint window, nint procedure, nuint id);
    [LibraryImport("comctl32.dll")] private static partial nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);
}
