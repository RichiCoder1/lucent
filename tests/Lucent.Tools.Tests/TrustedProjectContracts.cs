using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lucent.Tools;

namespace Lucent.Tools.Tests;

[TestClass]
public sealed class TrustedProjectContracts
{
    private static readonly string[] InventoryArguments = ["--list-sdks", "--list-runtimes"];

    [TestMethod]
    public async Task ForcedCancellationWaitsForRunningProducerExit()
    {
        var directory = Directory.CreateTempSubdirectory("lucent-producer-cancel-");
        var marker = Path.Combine(directory.FullName, "producer.csproj");
        var server = Path.Combine(
            AppContext.BaseDirectory,
            "CancellationProducer",
            "CancellationProducer.dll"
        );
        Assert.IsTrue(
            File.Exists(server),
            "The cancellation producer DLL was not copied to the test output."
        );
        Assert.IsTrue(
            File.Exists(Path.ChangeExtension(server, "runtimeconfig.json")),
            "The cancellation producer runtime configuration was not copied to the test output."
        );
        Assert.IsTrue(
            File.Exists(Path.ChangeExtension(server, "deps.json")),
            "The cancellation producer dependency manifest was not copied to the test output."
        );
        using var cancel = new CancellationTokenSource();
        var running = new TrustedProjectProcessProbe().RunAsync(server, marker, cancel.Token);
        try
        {
            using var ready = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!File.Exists(marker))
            {
                if (running.IsCompleted)
                {
                    var result = await running;
                    Assert.Fail(
                        $"Cancellation producer exited before readiness (success={result.Success})."
                    );
                }
                Assert.IsFalse(
                    ready.IsCancellationRequested,
                    "Cancellation producer did not publish readiness within ten seconds."
                );
                await Task.WhenAny(running, Task.Delay(20));
            }
            var pid = int.Parse(
                await File.ReadAllTextAsync(marker),
                System.Globalization.CultureInfo.InvariantCulture
            );
            using var producer = System.Diagnostics.Process.GetProcessById(pid);
            Assert.IsFalse(producer.HasExited);
            cancel.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => running);
            Assert.IsTrue(producer.HasExited, "Cancellation returned before the producer exited.");
        }
        finally
        {
            cancel.Cancel();
            try
            {
                await running;
            }
            catch (OperationCanceledException) { }
            directory.Delete(true);
        }
    }

    [TestMethod]
    public async Task OrdinaryCliDoctorRetainsStaticScopeAndNeverInvokesTrustedServer()
    {
        using var fixture = new Fixture();
        var output = new StringWriter();
        var inventory = new InventoryProbe();
        Assert.AreEqual(
            0,
            await Program.RunAsync(
                ["doctor", "--workspace", fixture.Directory.FullName, "--json"],
                output,
                new StringWriter(),
                inventory,
                fixture.Probe
            )
        );
        using var json = JsonDocument.Parse(output.ToString());
        Assert.AreEqual("environment-doctor", json.RootElement.GetProperty("kind").GetString());
        Assert.AreEqual("static-offline", json.RootElement.GetProperty("scope").GetString());
        Assert.AreEqual(0, fixture.Probe.Projects.Count);
        CollectionAssert.AreEqual(InventoryArguments, inventory.Arguments);
        Assert.IsFalse(json.RootElement.TryGetProperty("target", out _));
    }

    [TestMethod]
    public async Task ExplicitTrustedReportUsesServerEvidenceAndExcludesPathsAndCredentials()
    {
        using var fixture = new Fixture();
        fixture.Requirements["credentials"] = "SEEDED_SECRET";
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await Program.RunAsync(
            ["doctor", "--trusted-project", fixture.Project, "--server", fixture.Server, "--json"],
            output,
            error,
            trustedProbe: fixture.Probe
        );
        Assert.AreEqual(0, exit);
        Assert.AreEqual("", error.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        var report = json.RootElement;
        Assert.AreEqual("trusted-project-doctor", report.GetProperty("kind").GetString());
        Assert.AreEqual("trusted-project", report.GetProperty("scope").GetString());
        Assert.AreEqual("explicit-override", report.GetProperty("delivery").GetString());
        Assert.AreEqual("notChecked", report.GetProperty("releaseAuthentication").GetString());
        Assert.AreEqual(
            "net10.0-windows10.0.26100.0",
            report.GetProperty("target").GetProperty("framework").GetString()
        );
        Assert.AreEqual(
            "win-x64",
            report.GetProperty("target").GetProperty("runtimeIdentifier").GetString()
        );
        foreach (
            var key in new[]
            {
                "semanticReadiness",
                "managedBuildReadiness",
                "nativeReadiness",
                "feedAccess",
            }
        )
            Assert.AreEqual("notChecked", report.GetProperty(key).GetString());
        Assert.AreEqual(
            fixture.CompilerHash,
            report.GetProperty("compiler").GetProperty("sha256").GetString()
        );
        CollectionAssert.AreEqual(new string?[] { null, fixture.Project }, fixture.Probe.Projects);
        Assert.IsTrue(fixture.Probe.Servers.All(path => path == fixture.Server));
        Assert.IsFalse(output.ToString().Contains("SEEDED_SECRET", StringComparison.Ordinal));
        Assert.IsFalse(output.ToString().Contains("projectPath", StringComparison.Ordinal));
        Assert.IsFalse(
            output.ToString().Contains(fixture.Directory.FullName, StringComparison.Ordinal)
        );
    }

    [TestMethod]
    public async Task OlderProducerWithoutTargetLeavesTargetUncheckedAndTextLabelsUnauthenticatedOverride()
    {
        using var fixture = new Fixture();
        fixture.Requirements["projects"]![0]!.AsObject().Remove("target");
        var report = await TrustedProjectDoctor.RunAsync(
            fixture.Project,
            fixture.Server,
            fixture.Probe
        );
        Assert.AreEqual("notChecked", report.Target.Status);
        Assert.IsNull(report.Target.Framework);
        var output = new StringWriter();
        Assert.AreEqual(
            0,
            await Program.RunAsync(
                ["doctor", "--trusted-project", fixture.Project, "--server", fixture.Server],
                output,
                new StringWriter(),
                trustedProbe: fixture.Probe
            )
        );
        StringAssert.Contains(output.ToString(), "unauthenticated as a release artifact");
        StringAssert.Contains(output.ToString(), "Target: notChecked");
    }

    [TestMethod]
    public async Task DevelopmentSourceWithoutRuntimeIdentifierDoesNotImplyNativeOrReleaseReadiness()
    {
        using var fixture = new Fixture();
        fixture.Requirements["state"] = "development-source";
        fixture.Requirements["compiler"]!["sourceCommit"] = null;
        fixture.Requirements["projects"]![0]!["state"] = "development-source";
        fixture.Requirements["projects"]![0]!["sdk"] = null;
        fixture.Requirements["projects"]![0]!["target"]!["runtimeIdentifier"] = null;
        var result = await TrustedProjectDoctor.RunAsync(
            fixture.Project,
            fixture.Server,
            fixture.Probe
        );
        Assert.AreEqual("development-source", result.ProjectState);
        Assert.IsNull(result.Compiler.SourceCommit);
        Assert.AreEqual("observed", result.Target.Status);
        Assert.IsNull(result.Target.RuntimeIdentifier);
        Assert.AreEqual("notChecked", result.NativeReadiness);
        Assert.AreEqual("notChecked", result.ReleaseAuthentication);
    }

    [TestMethod]
    [DataRow("--trusted-project")]
    [DataRow("--server")]
    [DataRow("workspace")]
    [DataRow("relative")]
    public async Task TrustedOptionsRequireBothAbsolutePathsAndRejectWorkspace(string variant)
    {
        using var fixture = new Fixture();
        string[] args = variant switch
        {
            "--trusted-project" => ["doctor", "--trusted-project", fixture.Project, "--json"],
            "--server" => ["doctor", "--server", fixture.Server, "--json"],
            "workspace" =>
            [
                "doctor",
                "--workspace",
                fixture.Directory.FullName,
                "--trusted-project",
                fixture.Project,
                "--server",
                fixture.Server,
                "--json",
            ],
            _ =>
            [
                "doctor",
                "--trusted-project",
                "relative.csproj",
                "--server",
                fixture.Server,
                "--json",
            ],
        };
        var output = new StringWriter();
        Assert.AreEqual(
            2,
            await Program.RunAsync(args, output, new StringWriter(), trustedProbe: fixture.Probe)
        );
        Assert.AreEqual(0, fixture.Probe.Projects.Count);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.AreEqual("unavailable", json.RootElement.GetProperty("status").GetString());
    }

    [TestMethod]
    [DataRow("protocol")]
    [DataRow("protocol-minor")]
    [DataRow("identity-hash")]
    [DataRow("compiler")]
    [DataRow("target")]
    [DataRow("null-target")]
    [DataRow("semantic")]
    [DataRow("mixed")]
    [DataRow("duplicate")]
    [DataRow("missing-selected")]
    [DataRow("json")]
    [DataRow("oversized")]
    public async Task UnsupportedOrIncompatibleEvidenceFailsClosedWithoutRawErrors(string variant)
    {
        using var fixture = new Fixture();
        switch (variant)
        {
            case "protocol":
                fixture.Identity["protocol"]!["major"] = 2;
                break;
            case "protocol-minor":
                fixture.Identity["protocol"]!["minor"] = 1;
                break;
            case "identity-hash":
                fixture.Identity["server"]!["sha256"] = new string('a', 64);
                break;
            case "compiler":
                fixture.Requirements["compiler"]!["sha256"] = new string('b', 64);
                break;
            case "target":
                fixture.Requirements["projects"]![0]!["target"]!["framework"] =
                    "net10.0\nSEEDED_SECRET";
                break;
            case "null-target":
                fixture.Requirements["projects"]![0]!["target"] = null;
                break;
            case "semantic":
                fixture.Requirements["semanticReady"] = true;
                break;
            case "mixed":
                fixture.Requirements["projects"]![0]!["state"] = "development-source";
                break;
            case "duplicate":
                fixture.Requirements["inputs"]!
                    .AsArray()
                    .Add(fixture.Requirements["inputs"]![0]!.DeepClone());
                break;
            case "missing-selected":
                fixture.Requirements["inputs"]![0]!["path"] = fixture.Server;
                break;
            case "json":
                fixture.Probe.Malformed = true;
                break;
            case "oversized":
                fixture.Requirements["diagnostics"] = new string('x', 2 * 1024 * 1024);
                break;
        }
        var output = new StringWriter();
        Assert.AreEqual(
            1,
            await Program.RunAsync(
                [
                    "doctor",
                    "--trusted-project",
                    fixture.Project,
                    "--server",
                    fixture.Server,
                    "--json",
                ],
                output,
                new StringWriter(),
                trustedProbe: fixture.Probe
            )
        );
        Assert.IsFalse(output.ToString().Contains("SEEDED_SECRET", StringComparison.Ordinal));
        Assert.IsFalse(
            output.ToString().Contains(fixture.Directory.FullName, StringComparison.Ordinal)
        );
        using var json = JsonDocument.Parse(output.ToString());
        Assert.AreEqual("unavailable", json.RootElement.GetProperty("status").GetString());
        if (variant is "protocol" or "protocol-minor" or "identity-hash")
            Assert.AreEqual(
                1,
                fixture.Probe.Projects.Count,
                "Unverified identity must prevent project evaluation."
            );
    }

    [TestMethod]
    [DataRow("project")]
    [DataRow("tool")]
    [DataRow("missing-created")]
    public async Task ChangedInputsOrToolBytesRejectStaleResults(string variant)
    {
        using var fixture = new Fixture();
        var missing = Path.Combine(fixture.Directory.FullName, "Missing.props");
        fixture.Requirements["inputs"]!
            .AsArray()
            .Add(new JsonObject { ["path"] = missing, ["sha256"] = null });
        fixture.Probe.BeforeRequirements = () =>
            File.WriteAllText(
                variant switch
                {
                    "project" => fixture.Project,
                    "tool" => fixture.Server,
                    _ => missing,
                },
                "changed"
            );
        var failure = await Assert.ThrowsAsync<TrustedProjectFailure>(() =>
            TrustedProjectDoctor.RunAsync(fixture.Project, fixture.Server, fixture.Probe)
        );
        Assert.AreEqual(variant == "tool" ? "tool-changed" : "project-changed", failure.Code);
    }

    [TestMethod]
    public async Task FailedProbeDiscardsSeededServerDiagnosticsAndCancellationNeverReturnsSuccess()
    {
        using var fixture = new Fixture();
        fixture.Probe.Failed = true;
        var output = new StringWriter();
        Assert.AreEqual(
            1,
            await Program.RunAsync(
                [
                    "doctor",
                    "--trusted-project",
                    fixture.Project,
                    "--server",
                    fixture.Server,
                    "--json",
                ],
                output,
                new StringWriter(),
                trustedProbe: fixture.Probe
            )
        );
        Assert.IsFalse(output.ToString().Contains("SEEDED_SECRET", StringComparison.Ordinal));
        fixture.Probe.Failed = false;
        using var cancel = new CancellationTokenSource();
        fixture.Probe.BeforeRequirements = cancel.Cancel;
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            TrustedProjectDoctor.RunAsync(
                fixture.Project,
                fixture.Server,
                fixture.Probe,
                cancel.Token
            )
        );
        Assert.IsTrue(Path.IsPathFullyQualified(DotnetProcessProbe.HostPath));
        Assert.IsFalse(
            DotnetProcessProbe.HostPath.StartsWith(
                fixture.Directory.FullName,
                StringComparison.Ordinal
            )
        );
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new TrustedProjectProcessProbe().RunAsync(fixture.Server, fixture.Project, cancel.Token)
        );
    }

    [TestMethod]
    public async Task SharedBoundedReaderRejectsExcessOutputAndHonorsCancellation()
    {
        using var stream = new MemoryStream(new byte[17]);
        using var reader = new StreamReader(stream);
        await Assert.ThrowsAsync<IOException>(() =>
            DotnetProcessProbe.ReadBoundedAsync(reader, CancellationToken.None, 16)
        );
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        using var secondStream = new MemoryStream(new byte[1]);
        using var secondReader = new StreamReader(secondStream);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            DotnetProcessProbe.ReadBoundedAsync(secondReader, canceled.Token)
        );
    }

    private sealed class Fixture : IDisposable
    {
        private const string Commit = "0123456789012345678901234567890123456789";
        public DirectoryInfo Directory { get; } =
            System.IO.Directory.CreateTempSubdirectory("lucent-trusted-SEEDED_SECRET-");
        public string Project { get; }
        public string Server { get; }
        public string CompilerHash { get; }
        public JsonObject Identity { get; }
        public JsonObject Requirements { get; }
        public StubProbe Probe { get; }

        public Fixture()
        {
            Project = Path.Combine(Directory.FullName, "App.csproj");
            Server = Path.Combine(Directory.FullName, "Server.dll");
            var compiler = Path.Combine(Directory.FullName, "Lucent.Lui.Compiler.dll");
            File.WriteAllText(Project, "<Project />");
            File.WriteAllText(Server, "trusted-server");
            File.WriteAllText(compiler, "compiler");
            CompilerHash = Hash(compiler);
            Identity = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["sourceCommit"] = Commit,
                ["server"] = new JsonObject
                {
                    ["sha256"] = Hash(Server),
                    ["informationalVersion"] = "1+" + Commit,
                },
                ["compiler"] = new JsonObject
                {
                    ["sha256"] = CompilerHash,
                    ["informationalVersion"] = "1+" + Commit,
                },
                ["protocol"] = new JsonObject
                {
                    ["id"] = "lucent-lui",
                    ["major"] = 1,
                    ["minor"] = 0,
                },
                ["language"] = new JsonObject
                {
                    ["id"] = "lui",
                    ["version"] = "preview",
                    ["featureLevel"] = "preview-1",
                },
            };
            Requirements = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["kind"] = "project-requirements",
                ["state"] = "package",
                ["semanticReady"] = false,
                ["projectPath"] = Project,
                ["compiler"] = new JsonObject
                {
                    ["sha256"] = CompilerHash,
                    ["informationalVersion"] = "1+" + Commit,
                    ["sourceCommit"] = Commit,
                },
                ["packages"] = new JsonArray(),
                ["projects"] = new JsonArray(
                    new JsonObject
                    {
                        ["projectPath"] = Project,
                        ["state"] = "package",
                        ["target"] = new JsonObject
                        {
                            ["framework"] = "net10.0-windows10.0.26100.0",
                            ["runtimeIdentifier"] = "win-x64",
                        },
                        ["sdk"] = new JsonObject
                        {
                            ["id"] = "Lucent.Lui.Sdk",
                            ["version"] = "1",
                            ["repositoryCommit"] = Commit,
                            ["packageSha256"] = new string('c', 64),
                        },
                    }
                ),
                ["inputs"] = new JsonArray(
                    new JsonObject { ["path"] = Project, ["sha256"] = Hash(Project) }
                ),
            };
            Probe = new StubProbe(this);
        }

        private static string Hash(string path) =>
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

        public void Dispose() => Directory.Delete(true);
    }

    private sealed class StubProbe(Fixture fixture) : ITrustedProjectProbe
    {
        public List<string?> Projects { get; } = [];
        public List<string> Servers { get; } = [];
        public Action? BeforeRequirements { get; set; }
        public bool Malformed { get; set; }
        public bool Failed { get; set; }

        public Task<DotnetProbeResult> RunAsync(
            string serverPath,
            string? projectPath,
            CancellationToken cancellationToken
        )
        {
            Projects.Add(projectPath);
            Servers.Add(serverPath);
            if (projectPath is not null)
                BeforeRequirements?.Invoke();
            return Task.FromResult(
                new DotnetProbeResult(
                    !Failed,
                    Failed ? "SEEDED_SECRET raw failure"
                        : Malformed ? "SEEDED_SECRET malformed JSON"
                        : (
                            projectPath is null ? fixture.Identity : fixture.Requirements
                        ).ToJsonString()
                )
            );
        }
    }

    private sealed class InventoryProbe : IDotnetProbe
    {
        public List<string> Arguments { get; } = [];

        public Task<DotnetProbeResult> RunAsync(
            string workingDirectory,
            string argument,
            CancellationToken cancellationToken
        )
        {
            Arguments.Add(argument);
            return Task.FromResult(
                new DotnetProbeResult(
                    true,
                    argument == "--list-sdks"
                        ? "10.0.401 [host]"
                        : "Microsoft.NETCore.App 10.0.12 [host]"
                )
            );
        }
    }
}
