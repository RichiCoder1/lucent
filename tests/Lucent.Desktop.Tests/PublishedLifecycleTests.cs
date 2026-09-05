using System.Diagnostics;
using System.IO;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace Lucent.Desktop.Tests;

[TestClass]
public sealed class PublishedLifecycleTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [TestMethod]
    public void FlaUiRejectedCloseShowsRecoveryAndLuiRetryDrainsTheAcceptedWrite()
    {
        using var process = StartApplication();
        try
        {
            var window = WaitForWindow(process);
            using var automation = new UIA3Automation();
            var root = automation.FromHandle(window);
            WaitUntil(
                process,
                () =>
                    root.FindFirstDescendant(condition =>
                        condition
                            .ByControlType(ControlType.Button)
                            .And(condition.ByName("Retry close"))
                    )
                        is not null,
                "The asynchronous startup did not mount the .lui retry action."
            );
            var retry =
                root.FindFirstDescendant(condition =>
                    condition.ByControlType(ControlType.Button).And(condition.ByName("Retry close"))
                )
                ?? throw new InvalidOperationException(
                    "The mounted .lui retry button disappeared."
                );

            Assert.IsTrue(
                process.CloseMainWindow(),
                "The ordinary close request was not delivered."
            );
            WaitUntil(
                process,
                () =>
                    root.FindFirstDescendant(condition =>
                        condition
                            .ByControlType(ControlType.StatusBar)
                            .And(condition.ByName("Running: save failed; retry close"))
                    )
                        is not null,
                "The rejected save did not redraw an actionable recovery status."
            );

            retry.Patterns.Invoke.Pattern.Invoke();
            WaitUntil(
                process,
                () => process.HasExited,
                "The authored .lui retry action did not drain the accepted write and close."
            );
            Assert.AreEqual(0, process.ExitCode, "The retried lifecycle did not exit cleanly.");
        }
        finally
        {
            StopApplication(process);
        }
    }

    private static Process StartApplication()
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
        start.ArgumentList.Add("--lifecycle-fixture");
        return Process.Start(start)
            ?? throw new InvalidOperationException("Could not launch the lifecycle fixture.");
    }

    private static nint WaitForWindow(Process process)
    {
        nint handle = 0;
        WaitUntil(
            process,
            () =>
            {
                process.Refresh();
                handle = process.MainWindowHandle;
                return handle != 0;
            },
            "The lifecycle fixture did not expose a window."
        );
        return handle;
    }

    private static void WaitUntil(Process process, Func<bool> condition, string message)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            process.Refresh();
            if (process.HasExited)
                throw new InvalidOperationException(
                    $"The lifecycle fixture exited {process.ExitCode}: {message}"
                );
            if (elapsed.Elapsed >= Timeout)
                throw new TimeoutException(message);
            Thread.Sleep(50);
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
}
