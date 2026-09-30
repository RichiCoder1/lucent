using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lucent.Platform.Windows;

/// <summary>The separate, observable results of a single Windows attention request.</summary>
public readonly record struct WindowsAttentionResult(
    bool WindowIsRestored,
    bool ForegroundAcquired,
    bool TaskbarAttentionRequested
);

/// <summary>Requests native attention for the currently owned Lucent top-level window.</summary>
/// <remarks>Windows may deny foreground access. This object is valid only on its creating host
/// thread and until the host begins native-window teardown.</remarks>
public sealed partial class WindowsWindowAttention
{
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private nint _hwnd;

    internal WindowsWindowAttention(nint hwnd)
    {
        if (hwnd == 0)
            throw new ArgumentException("A live window handle is required.", nameof(hwnd));
        _hwnd = hwnd;
    }

    /// <summary>Restores a minimized window, requests foreground once, and flashes the taskbar if denied.</summary>
    public WindowsAttentionResult Request()
    {
        VerifyOwner();
        ObjectDisposedException.ThrowIf(_hwnd == 0, this);
        var restored = true;
        if (IsIconic(_hwnd))
        {
            _ = ShowWindow(_hwnd, 9); // SW_RESTORE
            restored = !IsIconic(_hwnd);
        }
        _ = SetForegroundWindow(_hwnd);
        var foreground = GetForegroundWindow() == _hwnd;
        var flashed = false;
        if (!foreground)
        {
            var info = new FlashInfo
            {
                Size = (uint)Unsafe.SizeOf<FlashInfo>(),
                Window = _hwnd,
                Flags = 0x00000002, // FLASHW_TRAY
                Count = 1,
            };
            _ = FlashWindowEx(ref info);
            flashed = true;
        }
        return new(restored, foreground, flashed);
    }

    internal void Invalidate()
    {
        VerifyOwner();
        _hwnd = 0;
    }

    private void VerifyOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException(
                "Windows window attention requires the host owner thread."
            );
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public nint Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlashWindowEx(ref FlashInfo info);
}
