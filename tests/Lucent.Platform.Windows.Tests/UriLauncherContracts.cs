using System.ComponentModel;
using Lucent.Core;

namespace Lucent.Platform.Windows.Tests;

[TestClass]
public sealed class UriLauncherContracts
{
    [TestMethod]
    public async Task LauncherChecksCancellationAndPolicyBeforeCallingExternalHandler()
    {
        var called = 0;
        var launcher = new WindowsUriLauncher(new UriLaunchPolicy(["https"]), _ => called++);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.AreEqual(
            UriLaunchStatus.Canceled,
            (await launcher.LaunchAsync(new("https://example.test/"), canceled.Token)).Status
        );
        Assert.AreEqual(
            UriLaunchStatus.Denied,
            (await launcher.LaunchAsync(new("file:///C:/notes.txt"))).Status
        );
        Assert.AreEqual(0, called);
        Assert.AreEqual(
            UriLaunchStatus.Launched,
            (await launcher.LaunchAsync(new("https://example.test/"))).Status
        );
        Assert.AreEqual(1, called);
    }

    [TestMethod]
    public async Task HandlerFailureDoesNotEchoUriOrExceptionDetails()
    {
        var launcher = new WindowsUriLauncher(
            new UriLaunchPolicy(["https"]),
            _ => throw new Win32Exception("sensitive address")
        );
        var result = await launcher.LaunchAsync(new("https://example.test/"));
        Assert.AreEqual(UriLaunchStatus.Failed, result.Status);
        Assert.IsFalse(result.Error!.Contains("sensitive", StringComparison.Ordinal));
    }
}
