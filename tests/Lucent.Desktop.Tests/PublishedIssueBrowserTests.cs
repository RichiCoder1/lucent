using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using Axe.Windows.Automation;
using Axe.Windows.Automation.Data;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace Lucent.Desktop.Tests;

[TestClass]
public sealed class PublishedIssueBrowserTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [TestMethod]
    public void AxeScanFindsACompleteTreeAndNoRuleErrors()
    {
        using var process = StartApplication();
        try
        {
            var window = WaitForWindow(process);
            var root = AutomationElement.FromHandle(window);
            var nodes = root.FindAll(
                System.Windows.Automation.TreeScope.Descendants,
                Automation.ControlViewCondition
            );
            Assert.IsGreaterThanOrEqualTo(
                3,
                nodes.Count,
                "The published app UIA tree was empty or incomplete."
            );

            var outputRoot = Environment.GetEnvironmentVariable("LUCENT_ACCESSIBILITY_OUTPUT");
            if (string.IsNullOrWhiteSpace(outputRoot))
                throw new InvalidOperationException(
                    "LUCENT_ACCESSIBILITY_OUTPUT must select the Axe evidence directory."
                );
            var output = Path.Combine(
                Path.GetFullPath(outputRoot),
                "axe-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(output);
            var config = Config
                .Builder.ForProcessId(process.Id)
                .WithOutputFileFormat(OutputFileFormat.A11yTest)
                .WithOutputDirectory(output)
                .WithAlwaysSaveTestFile()
                .Build();
            var scan = ScannerFactory
                .CreateScanner(config)
                .Scan(new ScanOptions("issue-browser", window));

            Assert.IsTrue(scan.WindowScanOutputs.Count > 0, "Axe produced no window scan results.");
            Assert.AreEqual(
                0,
                scan.WindowScanOutputs.Sum(result => result.ErrorCount),
                "Axe reported accessibility rule errors: "
                    + string.Join(
                        "; ",
                        scan.WindowScanOutputs.SelectMany(result => result.Errors)
                            .Select(error => $"{error.Rule.ID}: {error.Rule.Description}")
                    )
            );
            Assert.IsTrue(
                Directory.GetFiles(output, "*.a11ytest").Length > 0,
                "Axe did not write its requested .a11ytest evidence."
            );
        }
        finally
        {
            StopApplication(process);
        }
    }

    [TestMethod]
    public void FlaUiFiltersSelectsAndRestoresVirtualizedIssues()
    {
        using var process = StartApplication();
        try
        {
            var window = WaitForWindow(process);
            using var automation = new UIA3Automation();
            var root = automation.FromHandle(window);
            var search =
                root.FindFirstDescendant(condition =>
                    condition.ByControlType(FlaUI.Core.Definitions.ControlType.Edit)
                ) ?? throw new InvalidOperationException("FlaUI could not find the Search field.");
            var list =
                root.FindFirstDescendant(condition =>
                    condition
                        .ByControlType(FlaUI.Core.Definitions.ControlType.List)
                        .And(condition.ByName("Issues"))
                ) ?? throw new InvalidOperationException("FlaUI could not find the Issues list.");
            WaitUntil(
                process,
                () => list.FindAllChildren().Length > 0,
                "Issue rows did not appear."
            );

            search.Focus();
            WaitUntil(
                process,
                () => search.Properties.HasKeyboardFocus.Value,
                "Search rejected focus."
            );
            search.Patterns.Value.Pattern.SetValue("10000");
            WaitUntil(
                process,
                () =>
                    list.FindAllChildren()
                        .Any(row => row.Name.Contains("10000", StringComparison.Ordinal)),
                "Filtering did not expose issue 10000."
            );
            var row = list.FindAllChildren()
                .First(item => item.Name.Contains("10000", StringComparison.Ordinal));
            row.Patterns.SelectionItem.Pattern.Select();
            WaitUntil(
                process,
                () => row.Patterns.SelectionItem.Pattern.IsSelected.Value,
                "Issue selection was rejected."
            );
            search.Patterns.Value.Pattern.SetValue("");
            WaitUntil(
                process,
                () => list.FindAllChildren().Length > 1,
                "Clearing Search did not restore issue rows."
            );
        }
        finally
        {
            StopApplication(process);
        }
    }

    private static Process StartApplication()
    {
        var path = Environment.GetEnvironmentVariable("LUCENT_DESKTOP_APP");
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException(
                "LUCENT_DESKTOP_APP must name the published Issue Browser executable."
            );
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException(
                "The published Issue Browser executable does not exist.",
                fullPath
            );
        return Process.Start(
                new ProcessStartInfo(fullPath)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(fullPath)!,
                }
            )
            ?? throw new InvalidOperationException("Could not launch the published Issue Browser.");
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
            "The published Issue Browser did not expose a window."
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
                    $"The published app exited {process.ExitCode}: {message}"
                );
            if (elapsed.Elapsed >= Timeout)
                throw new TimeoutException(message);
            Thread.Sleep(100);
        }
    }

    private static void StopApplication(Process process)
    {
        if (process.HasExited)
            return;
        _ = process.CloseMainWindow();
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true);
            if (!process.WaitForExit(10_000))
                throw new TimeoutException("The published app did not exit after termination.");
        }
    }
}
