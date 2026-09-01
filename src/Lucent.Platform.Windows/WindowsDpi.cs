using System.Runtime.InteropServices;

namespace Lucent.Platform.Windows;

internal static partial class WindowsDpi
{
    internal static readonly nint PerMonitorV2 = (nint)(-4);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetProcessDpiAwarenessContext(nint value);

    [LibraryImport("user32.dll")]
    internal static partial nint GetThreadDpiAwarenessContext();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AreDpiAwarenessContextsEqual(nint left, nint right);

    internal static bool IsPerMonitorV2() =>
        AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), PerMonitorV2);

    internal static bool EnsurePerMonitorV2() =>
        SetProcessDpiAwarenessContext(PerMonitorV2) || IsPerMonitorV2();
}
