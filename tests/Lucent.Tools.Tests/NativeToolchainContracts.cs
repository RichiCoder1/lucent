using Lucent.Tools;

namespace Lucent.Tools.Tests;

[TestClass]
public sealed class NativeToolchainContracts
{
    private const string A =
        """[{"instanceId":"a1","isComplete":true,"isLaunchable":true,"installationVersion":"17.14.1","installationPath":"C:\\private\\user\\VisualStudio"}]""";
    private const string B =
        """[{"instanceId":"b2","isComplete":true,"isLaunchable":true,"installationVersion":"18.0.0"}]""";
    private const string Both =
        """[{"instanceId":"a1","isComplete":true,"isLaunchable":true,"installationVersion":"17.14.1"},{"instanceId":"b2","isComplete":true,"isLaunchable":true,"installationVersion":"18.0.0"}]""";

    [TestMethod]
    public async Task RegisteredPrerequisitesObserveWindowsX64WithoutCertifyingPublicationOrLeakingPaths()
    {
        var result = await NativeToolchainDoctor.RunAsync(
            new Probe(new(NativeToolchainProbeStatus.Observed, A, A, A))
        );
        Assert.AreEqual("installed-windows-x64-toolchain", result.Scope);
        Assert.AreEqual("observed", result.Capabilities.Single().Status);
        Assert.AreEqual("native", result.Capabilities.Single().Name);
        Assert.AreEqual("pass", result.Checks.Single(c => c.Code == "native-prerequisites").Status);
        Assert.AreEqual("notChecked", result.Checks.Single(c => c.Code == "native-publish").Status);
        Assert.IsTrue(result.Checks.All(c => c.Capability == "native"));
        var report = string.Join(" ", result.Checks.Select(c => c.ToString()));
        Assert.DoesNotContain("private", report);
        Assert.DoesNotContain("a1", report);
        Assert.DoesNotContain("C:\\", report);
    }

    [TestMethod]
    public async Task ComponentsInDifferentInstallationsCannotCreateASuccessfulCombinedObservation()
    {
        var result = await NativeToolchainDoctor.RunAsync(
            new Probe(new(NativeToolchainProbeStatus.Observed, Both, A, B))
        );
        Assert.AreEqual("fail", result.Checks.Single(c => c.Code == "native-prerequisites").Status);
        Assert.AreEqual(
            "Matching installations: 0",
            result.Checks.Single(c => c.Code == "native-prerequisites").Evidence
        );
        Assert.AreEqual("notChecked", result.Capabilities.Single().Status);
    }

    [TestMethod]
    public async Task EmptySuccessfulInventoryIsScopedMissingEvidenceRatherThanDiscoveryFailure()
    {
        var result = await NativeToolchainDoctor.RunAsync(
            new Probe(new(NativeToolchainProbeStatus.Observed, "[]", "[]", "[]"))
        );
        Assert.AreEqual("blocked", result.Status);
        Assert.AreEqual("fail", result.Checks.Single(c => c.Code == "native-windows-sdk").Status);
        Assert.Contains(
            "registration",
            result.Checks.Single(c => c.Code == "native-windows-sdk").Summary
        );
        Assert.AreEqual("notChecked", result.Checks.Single(c => c.Code == "native-publish").Status);
    }

    [TestMethod]
    [DataRow(NativeToolchainProbeStatus.DiscoveryUnavailable)]
    [DataRow(NativeToolchainProbeStatus.DiscoveryUnsupported)]
    [DataRow(NativeToolchainProbeStatus.UnsupportedHost)]
    [DataRow(NativeToolchainProbeStatus.Failed)]
    public async Task InconclusiveDiscoveryNeverClaimsMissingComponents(
        NativeToolchainProbeStatus status
    )
    {
        var result = await NativeToolchainDoctor.RunAsync(new Probe(new(status, "C:\\sensitive")));
        Assert.AreEqual("unavailable", result.Status);
        Assert.AreEqual("notChecked", result.Checks.Single().Status);
        Assert.AreEqual("notChecked", result.Capabilities.Single().Status);
        Assert.DoesNotContain("sensitive", result.Checks.Single().ToString());
    }

    [TestMethod]
    [DataRow("not JSON")]
    [DataRow("{}")]
    [DataRow(
        "[{\"instanceId\":\"x\",\"isComplete\":false,\"isLaunchable\":true,\"installationVersion\":\"17.1\"}]"
    )]
    [DataRow(
        "[{\"instanceId\":\"x\",\"isComplete\":true,\"isLaunchable\":true,\"installationVersion\":\"16.1\"}]"
    )]
    public async Task InvalidInventoryFailsClosed(string inventory)
    {
        var result = await NativeToolchainDoctor.RunAsync(
            new Probe(new(NativeToolchainProbeStatus.Observed, inventory, "[]", "[]"))
        );
        Assert.AreEqual("unavailable", result.Status);
        Assert.AreEqual("notChecked", result.Checks.Single().Status);
    }

    [TestMethod]
    public async Task InconsistentInventoryAndCancellationDiscardSuccessfulObservation()
    {
        var inconsistent = await NativeToolchainDoctor.RunAsync(
            new Probe(new(NativeToolchainProbeStatus.Observed, A, B, A))
        );
        Assert.AreEqual("unavailable", inconsistent.Status);
        using var cancel = new CancellationTokenSource();
        var probe = new Probe(new(NativeToolchainProbeStatus.Observed, A, A, A), cancel.Cancel);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            NativeToolchainDoctor.RunAsync(probe, cancel.Token)
        );
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new NativeToolchainProcessProbe().RunAsync(cancel.Token)
        );
    }

    [TestMethod]
    public async Task ExplicitNativeCliUsesExclusiveModeAndDistinctObservationExitStates()
    {
        foreach (
            var observation in new[]
            {
                new NativeToolchainProbeResult(NativeToolchainProbeStatus.Observed, A, A, A),
                new NativeToolchainProbeResult(
                    NativeToolchainProbeStatus.Observed,
                    "[]",
                    "[]",
                    "[]"
                ),
                new NativeToolchainProbeResult(NativeToolchainProbeStatus.DiscoveryUnavailable),
            }
        )
        {
            var probe = new Probe(observation);
            var output = new StringWriter();
            var exit = await Program.RunAsync(
                ["doctor", "--native-prerequisites", "--json"],
                output,
                new StringWriter(),
                nativeProbe: probe
            );
            using var document = System.Text.Json.JsonDocument.Parse(output.ToString());
            Assert.AreEqual(
                "native-prerequisites-doctor",
                document.RootElement.GetProperty("kind").GetString()
            );
            Assert.AreEqual(
                "installed-windows-x64-toolchain",
                document.RootElement.GetProperty("scope").GetString()
            );
            Assert.AreEqual(
                observation.Status == NativeToolchainProbeStatus.DiscoveryUnavailable ? 2
                    : observation.Instances == "[]" ? 1
                    : 0,
                exit
            );
            Assert.AreEqual(1, probe.Calls);
        }
        foreach (
            var extra in new[]
            {
                new[] { "--workspace", "not-an-absolute-workspace" },
                new[] { "--trusted-project", "private-project", "--server", "private-server" },
                new[] { "--native-prerequisites" },
            }
        )
        {
            var probe = new Probe(new(NativeToolchainProbeStatus.Observed, A, A, A));
            Assert.AreEqual(
                2,
                await Program.RunAsync(
                    ["doctor", "--native-prerequisites", .. extra],
                    new StringWriter(),
                    new StringWriter(),
                    nativeProbe: probe
                )
            );
            Assert.AreEqual(0, probe.Calls);
        }
    }

    private sealed class Probe(NativeToolchainProbeResult result, Action? beforeReturn = null)
        : INativeToolchainProbe
    {
        public int Calls { get; private set; }

        public Task<NativeToolchainProbeResult> RunAsync(CancellationToken cancellationToken)
        {
            Calls++;
            beforeReturn?.Invoke();
            return Task.FromResult(result);
        }
    }
}
