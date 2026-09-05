using System.Diagnostics;
using System.IO;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace Lucent.Desktop.Tests;

[TestClass]
public sealed class PublishedAutoSizedTextFieldTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [TestMethod]
    public void FlaUiAutoSizedLuiTextFieldRetainsBoundsAcrossFocusEditAndBlur()
    {
        using var process = StartApplication();
        try
        {
            var window = WaitForWindow(process);
            using var automation = new UIA3Automation();
            var root = automation.FromHandle(window);
            var field =
                root.FindFirstDescendant(condition =>
                    condition
                        .ByControlType(ControlType.Edit)
                        .And(condition.ByName("Autosized field"))
                )
                ?? throw new InvalidOperationException("FlaUI could not find the autosized field.");
            var other =
                root.FindFirstDescendant(condition =>
                    condition.ByControlType(ControlType.Edit).And(condition.ByName("Other field"))
                ) ?? throw new InvalidOperationException("FlaUI could not find the blur target.");
            var initial = field.BoundingRectangle;
            Assert.IsGreaterThan(0, initial.Width, "The default field had no intrinsic width.");
            Assert.IsGreaterThan(0, initial.Height, "The default field had no intrinsic height.");

            field.Focus();
            WaitUntil(
                process,
                () => field.Properties.HasKeyboardFocus.Value,
                "The autosized field rejected focus."
            );
            WaitUntil(
                process,
                () => field.BoundingRectangle == initial,
                "The empty focused field changed its intrinsic bounds."
            );

            field.Patterns.Value.Pattern.SetValue("x");
            WaitUntil(
                process,
                () =>
                    field.Patterns.Value.Pattern.Value.Value == "x"
                    && field.BoundingRectangle == initial,
                "The first edit changed the field's intrinsic bounds."
            );
            field.Patterns.Value.Pattern.SetValue("");
            other.Focus();
            WaitUntil(
                process,
                () => other.Properties.HasKeyboardFocus.Value && field.BoundingRectangle == initial,
                "Blur changed the empty field's intrinsic bounds."
            );
        }
        finally
        {
            StopApplication(process);
        }
    }

    private static Process StartApplication()
    {
        var path = Environment.GetEnvironmentVariable("LUCENT_DESKTOP_HOST");
        if (string.IsNullOrWhiteSpace(path))
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
        start.ArgumentList.Add("--autosized-text-field-fixture");
        return Process.Start(start)
            ?? throw new InvalidOperationException("Could not launch the TextField fixture.");
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
            "The TextField fixture did not expose a window."
        );
        return handle;
    }

    private static void WaitUntil(Process process, Func<bool> condition, string message)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (process.HasExited)
                throw new InvalidOperationException(
                    $"The TextField fixture exited {process.ExitCode}: {message}"
                );
            if (elapsed.Elapsed >= Timeout)
                throw new TimeoutException(message);
            Thread.Sleep(25);
        }
    }

    private static void StopApplication(Process process)
    {
        if (process.HasExited)
            return;
        process.CloseMainWindow();
        if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
    }
}
