using System.Diagnostics;
using Lucent.Core;
using SDL3;

namespace Lucent.Platform.Windows.TestHost;

/// <summary>Explicit desktop-only proof of the generated native dialog ABI and STA cancellation pump.</summary>
internal static class FilePickerProof
{
    internal static int Run()
    {
        if (!SDL.Init(SDL.InitFlags.Video))
            return 1;
        var window = SDL.CreateWindow("Lucent file picker proof", 320, 180, SDL.WindowFlags.Hidden);
        try
        {
            if (window == 0)
                return 1;
            var hwnd = SDL.GetPointerProperty(
                SDL.GetWindowProperties(window),
                SDL.Props.WindowWin32HWNDPointer,
                0
            );
            if (hwnd == 0)
                return 1;
            foreach (
                var kind in new[]
                {
                    WindowsFilePickerKind.Open,
                    WindowsFilePickerKind.Save,
                    WindowsFilePickerKind.Folder,
                }
            )
            {
                var clock = Stopwatch.StartNew();
                var cancellationChecks = 0;
                var canceledInsideShow = false;
                bool CancelFromNativePump()
                {
                    // The first two reads precede Show. Require a real native timer callback,
                    // even when shell initialization itself takes longer than the delay.
                    if (++cancellationChecks <= 2 || clock.ElapsedMilliseconds < 500)
                        return false;
                    canceledInsideShow = true;
                    return true;
                }
                var request = new WindowsFilePickerRequest(
                    kind,
                    "Lucent cancellation proof",
                    kind == WindowsFilePickerKind.Save ? "lucent-proof.txt" : null,
                    kind == WindowsFilePickerKind.Folder ? null : [new("Text", ["txt"])],
                    new Uri(Path.GetTempPath()),
                    kind == WindowsFilePickerKind.Open
                );
                var result = WindowsFilePickerNative.Show(hwnd, request, CancelFromNativePump);
                if (
                    result.Status != FilePickerStatus.Canceled
                    || result.Items.Count != 0
                    || !canceledInsideShow
                )
                {
                    Console.Error.WriteLine($"FILE-PICKER {kind}: {result.Status}");
                    return 1;
                }
                Console.WriteLine($"FILE-PICKER {kind}: Canceled");
            }
            return 0;
        }
        finally
        {
            if (window != 0)
                SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }
}
