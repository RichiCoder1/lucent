using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using FlaUI.Core.Capturing;

namespace Lucent.Desktop.Tests;

[TestClass]
public sealed partial class PublishedMotionTests
{
    private const uint TargetPixel = 0x00323BE3;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [TestMethod]
    public void PublishedHostSchedulesChangingPixelsWithoutInputAndClosesDuringMotion()
    {
        using var process = StartApplication("--motion-fixture");
        try
        {
            var window = WaitForWindow(process);
            var distinct = new HashSet<uint>();
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < TimeSpan.FromSeconds(3) && distinct.Count < 3)
            {
                var pixel = ReadCenterPixel(window);
                if (pixel is not (0 or uint.MaxValue))
                    distinct.Add(pixel);
                Thread.Sleep(25);
            }
            Assert.IsGreaterThanOrEqualTo(
                3,
                distinct.Count,
                "The published host did not schedule intermediate presentation pixels."
            );

            Assert.IsTrue(process.CloseMainWindow(), "The active-motion close was not delivered.");
            Assert.IsTrue(
                process.WaitForExit((int)Timeout.TotalMilliseconds),
                "Active presentation demand kept the published process alive after close."
            );
            Assert.AreEqual(0, process.ExitCode);
        }
        finally
        {
            StopApplication(process);
        }
    }

    [TestMethod]
    public void PublishedHostSnapsActiveMotionAcrossMinimizeAndRestore()
    {
        using var process = StartApplication("--motion-fixture");
        try
        {
            var window = WaitForWindow(process);
            WaitUntil(
                process,
                () => ReadCenterPixel(window) != 0 && ReadCenterPixel(window) != TargetPixel,
                "The motion fixture did not expose its initial surface."
            );
            Thread.Sleep(900);
            Assert.IsTrue(ShowWindow(window, 6), "The fixture could not be minimized.");
            Thread.Sleep(100);
            _ = ShowWindow(window, 9);
            WaitUntil(
                process,
                () => ReadCenterPixel(window) == TargetPixel,
                "Restore did not present the snapped motion target."
            );
        }
        finally
        {
            StopApplication(process);
        }
    }

    [TestMethod]
    public void PublishedHostHonorsApplicationReducedMotionPreference()
    {
        using var process = StartApplication("--motion-reduced-fixture");
        try
        {
            var window = WaitForWindow(process);
            WaitUntil(
                process,
                () => ReadCenterPixel(window) == TargetPixel,
                "Reduced motion did not snap the scheduled target in the published host."
            );
        }
        finally
        {
            StopApplication(process);
        }
    }

    private static Process StartApplication(string fixture)
    {
        var path = Environment.GetEnvironmentVariable("LUCENT_DESKTOP_HOST");
        if (String.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException(
                "LUCENT_DESKTOP_HOST must name the published Windows TestHost executable."
            );
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException(
                "The published Windows TestHost executable does not exist.",
                fullPath
            );
        var start = new ProcessStartInfo(fullPath)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(fullPath)!,
        };
        start.ArgumentList.Add(fixture);
        return Process.Start(start)
            ?? throw new InvalidOperationException("Could not launch the motion fixture.");
    }

    private static nint WaitForWindow(Process process)
    {
        nint window = 0;
        WaitUntil(
            process,
            () =>
            {
                process.Refresh();
                window = process.MainWindowHandle;
                return window != 0;
            },
            "The published motion fixture did not expose a window."
        );
        return window;
    }

    private static uint ReadCenterPixel(nint window)
    {
        if (!GetWindowRect(window, out var bounds))
            return 0;
        var width = Math.Max(0, bounds.Right - bounds.Left);
        var height = Math.Max(0, bounds.Bottom - bounds.Top);
        if (width == 0 || height == 0)
            return 0;
        using var capture = Capture.Rectangle(
            new Rectangle(bounds.Left, bounds.Top, width, height)
        );
        var pixel = capture.Bitmap.GetPixel(width / 2, height / 2);
        return (uint)(pixel.R | pixel.G << 8 | pixel.B << 16);
    }

    private static void WaitUntil(Process process, Func<bool> condition, string message)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            process.Refresh();
            if (process.HasExited)
                throw new InvalidOperationException(
                    $"The motion fixture exited {process.ExitCode}: {message}"
                );
            if (elapsed.Elapsed >= Timeout)
                throw new TimeoutException(message);
            Thread.Sleep(25);
        }
    }

    private static void StopApplication(Process process)
    {
        process.Refresh();
        if (process.HasExited)
            return;
        _ = process.CloseMainWindow();
        if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out Rect bounds);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);
}
