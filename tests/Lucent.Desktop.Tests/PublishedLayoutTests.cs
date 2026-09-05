using System.Diagnostics;
using System.IO;

namespace Lucent.Desktop.Tests;

[TestClass]
public sealed class PublishedLayoutTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [TestMethod]
    public void PublishedFixtureProvesResponsiveParagraphResizeDpiAndPaint()
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

        using var process = Start(fullPath);
        try
        {
            if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                throw new TimeoutException(
                    "The published layout fixture did not finish within the smoke timeout."
                );
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            Assert.IsTrue(
                process.ExitCode == 0
                    && output.Contains("layout-fixture: PASS", StringComparison.Ordinal),
                "Published layout fixture failed with exit "
                    + process.ExitCode
                    + ". stdout: "
                    + output
                    + " stderr: "
                    + error
            );
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
    }

    private static Process Start(string path)
    {
        var start = new ProcessStartInfo(path)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(path)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("--layout-fixture");
        return Process.Start(start)
            ?? throw new InvalidOperationException(
                "Could not launch the published layout fixture."
            );
    }
}
