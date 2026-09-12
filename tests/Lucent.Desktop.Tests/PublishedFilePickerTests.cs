using System.Diagnostics;

namespace Lucent.Desktop.Tests;

[TestClass]
public sealed class PublishedFilePickerTests
{
    [TestMethod]
    public void NativeOpenSaveAndFolderDialogsCancelThroughTheirOwnerStaPump()
    {
        var host = Environment.GetEnvironmentVariable("LUCENT_DESKTOP_HOST");
        Assert.IsFalse(
            string.IsNullOrWhiteSpace(host),
            "Set LUCENT_DESKTOP_HOST to the published Windows TestHost."
        );
        using var process =
            Process.Start(
                new ProcessStartInfo(host!, "--file-picker-proof")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            ) ?? throw new InvalidOperationException("Could not start the native picker proof.");
        try
        {
            Assert.IsTrue(
                process.WaitForExit(30_000),
                "Native picker cancellation did not finish within 30 seconds."
            );
            var output = process.StandardOutput.ReadToEnd();
            var errors = process.StandardError.ReadToEnd();
            Assert.AreEqual(0, process.ExitCode, errors);
            foreach (var kind in new[] { "Open", "Save", "Folder" })
                StringAssert.Contains(output, $"FILE-PICKER {kind}: Canceled");
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
}
