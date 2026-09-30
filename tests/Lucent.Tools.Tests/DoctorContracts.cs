using System.Text.Json;
using Lucent.Tools;

namespace Lucent.Tools.Tests;

[TestClass]
public sealed class DoctorContracts
{
    private static readonly string[] ExpectedProbeArguments = ["--list-sdks", "--list-runtimes"];

    [TestMethod]
    public async Task StaticDoctorLeavesWorkspaceUntouchedAndDoesNotExposeFeedCredentials()
    {
        var directory = Directory.CreateTempSubdirectory("lucent-doctor-");
        try
        {
            var root = directory.FullName;
            var source = Path.Combine(root, "App.csproj");
            await File.WriteAllTextAsync(source, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            await File.WriteAllTextAsync(
                Path.Combine(root, "global.json"),
                """{"sdk":{"version":"10.0.401","rollForward":"disable"}}"""
            );
            await File.WriteAllTextAsync(
                Path.Combine(root, "NuGet.Config"),
                """<configuration><packageSources><add key="local" value="https://user:SEEDED_SECRET@example.test/feed?token=SEEDED_SECRET" /></packageSources></configuration>"""
            );
            var before = Snapshot(root);
            var probe = new StubProbe();

            var result = await Doctor.RunAsync(root, probe);

            CollectionAssert.AreEqual(before, Snapshot(root));
            CollectionAssert.AreEqual(ExpectedProbeArguments, probe.Arguments);
            Assert.IsTrue(probe.Directories.All(item => item != root));
            Assert.AreEqual("static-offline", result.Scope);
            Assert.AreEqual("available", result.Status);
            Assert.AreEqual(
                "notChecked",
                result.Capabilities.Single(item => item.Name == "restore").Status
            );
            Assert.AreEqual(
                "notChecked",
                result.Checks.Single(item => item.Code == "project-requirements").Status
            );
            Assert.AreEqual(
                "local",
                result.Checks.Single(item => item.Code == "feed-config").Evidence
            );
            var json = JsonSerializer.Serialize(result);
            Assert.IsFalse(json.Contains("SEEDED_SECRET", StringComparison.Ordinal));
            Assert.IsFalse(json.Contains("example.test", StringComparison.Ordinal));
            Assert.IsFalse(json.Contains("<Project", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public async Task MissingSdkInventoryBlocksBuildButLeavesPinSelectionUnchecked()
    {
        var directory = Directory.CreateTempSubdirectory("lucent-doctor-");
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory.FullName, "global.json"),
                """{"sdk":{"version":"10.0.401","rollForward":"disable"}}"""
            );
            var result = await Doctor.RunAsync(
                directory.FullName,
                new StubProbe(sdkAvailable: false)
            );

            Assert.AreEqual("blocked", result.Status);
            Assert.AreEqual("fail", result.Checks.Single(item => item.Code == "dotnet-sdk").Status);
            Assert.AreEqual(
                "blocked",
                result.Capabilities.Single(item => item.Name == "build").Status
            );
            Assert.AreEqual(
                "notChecked",
                result.Checks.Single(item => item.Code == "sdk-selection").Status
            );
            Assert.AreEqual(
                "notChecked",
                result.Capabilities.Single(item => item.Name == "restore").Status
            );
            Assert.AreEqual(
                "notChecked",
                result.Checks.Single(item => item.Code == "feed-reachability").Status
            );
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public async Task FailedInventoryIsReportedAsInspectionFailureRatherThanMissingInstallation()
    {
        var directory = Directory.CreateTempSubdirectory("lucent-doctor-");
        try
        {
            var sdkFailure = await Doctor.RunAsync(
                directory.FullName,
                new StubProbe(sdkProbeFails: true)
            );
            var sdkCheck = sdkFailure.Checks.Single(item => item.Code == "dotnet-sdk");
            Assert.AreEqual("fail", sdkCheck.Status);
            StringAssert.Contains(sdkCheck.Summary, "could not be inspected");
            StringAssert.Contains(sdkCheck.Remedy!, "Retry");
            Assert.IsFalse(sdkCheck.Remedy!.Contains("Install an SDK", StringComparison.Ordinal));

            var runtimeFailure = await Doctor.RunAsync(
                directory.FullName,
                new StubProbe(runtimeProbeFails: true)
            );
            var runtimeCheck = runtimeFailure.Checks.Single(item => item.Code == "dotnet-runtime");
            Assert.AreEqual("fail", runtimeCheck.Status);
            StringAssert.Contains(runtimeCheck.Summary, "could not be inspected");
            StringAssert.Contains(runtimeCheck.Remedy!, "Retry");
            Assert.IsFalse(
                runtimeCheck.Remedy!.Contains(
                    "Install the .NET 10 runtime",
                    StringComparison.Ordinal
                )
            );

            var emptyRuntime = await Doctor.RunAsync(
                directory.FullName,
                new StubProbe(runtimeAvailable: false)
            );
            StringAssert.Contains(
                emptyRuntime.Checks.Single(item => item.Code == "dotnet-runtime").Remedy!,
                "Install the .NET 10 runtime"
            );
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public async Task UnsafeFeedNameIsRedactedWithoutReadingItsAddressIntoTheReport()
    {
        var directory = Directory.CreateTempSubdirectory("lucent-doctor-");
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory.FullName, "NuGet.Config"),
                """<configuration><packageSources><add key="token-SEEDED_SECRET" value="https://example.test/?password=SEEDED_SECRET" /></packageSources></configuration>"""
            );
            var result = await Doctor.RunAsync(directory.FullName, new StubProbe());
            var json = JsonSerializer.Serialize(result);
            Assert.IsFalse(json.Contains("SEEDED_SECRET", StringComparison.Ordinal));
            Assert.AreEqual(
                "[redacted]",
                result.Checks.Single(item => item.Code == "feed-config").Evidence
            );
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public async Task GlobalJsonWithoutSdkPinIsNotAnInvalidConfiguration()
    {
        var directory = Directory.CreateTempSubdirectory("lucent-doctor-");
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory.FullName, "global.json"),
                """{"msbuild-sdks":{"MSTest.Sdk":"4.4.0"}}"""
            );
            var result = await Doctor.RunAsync(directory.FullName, new StubProbe());
            Assert.AreEqual(
                "pass",
                result.Checks.Single(item => item.Code == "global-json").Status
            );
            Assert.AreEqual(
                "notChecked",
                result.Checks.Single(item => item.Code == "sdk-selection").Status
            );
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [TestMethod]
    public async Task GlobalJsonCommentsAreValidButMalformedConfigurationStillFails()
    {
        var directory = Directory.CreateTempSubdirectory("lucent-doctor-");
        try
        {
            var file = Path.Combine(directory.FullName, "global.json");
            await File.WriteAllTextAsync(
                file,
                """
                {
                  // The SDK pin is static input, not a request to execute this SDK.
                  "sdk": { "version": "10.0.401" /* accepted comment */ }
                }
                """
            );
            var commented = await Doctor.RunAsync(directory.FullName, new StubProbe());
            Assert.AreEqual(
                "pass",
                commented.Checks.Single(item => item.Code == "global-json").Status
            );
            Assert.AreEqual(
                "notChecked",
                commented.Checks.Single(item => item.Code == "sdk-selection").Status
            );

            await File.WriteAllBytesAsync(
                file,
                [
                    0xef,
                    0xbb,
                    0xbf,
                    .. System.Text.Encoding.UTF8.GetBytes("""{"sdk":{"version":"10.0.401"}}"""),
                ]
            );
            var withBom = await Doctor.RunAsync(directory.FullName, new StubProbe());
            Assert.AreEqual(
                "pass",
                withBom.Checks.Single(item => item.Code == "global-json").Status
            );

            await File.WriteAllTextAsync(file, """{"sdk":{"version":"10.0.401",}}""");
            var trailingComma = await Doctor.RunAsync(directory.FullName, new StubProbe());
            Assert.AreEqual(
                "fail",
                trailingComma.Checks.Single(item => item.Code == "global-json").Status
            );

            await File.WriteAllTextAsync(file, """{"sdk": /* unterminated""");
            var malformed = await Doctor.RunAsync(directory.FullName, new StubProbe());
            Assert.AreEqual(
                "fail",
                malformed.Checks.Single(item => item.Code == "global-json").Status
            );
            Assert.AreEqual("blocked", malformed.Status);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static string[] Snapshot(string root) =>
        Directory
            .GetFiles(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(file =>
                Path.GetRelativePath(root, file)
                + ":"
                + Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))
                )
            )
            .ToArray();

    private sealed class StubProbe(
        bool sdkAvailable = true,
        bool runtimeAvailable = true,
        bool sdkProbeFails = false,
        bool runtimeProbeFails = false
    ) : IDotnetProbe
    {
        public List<string> Arguments { get; } = [];
        public List<string> Directories { get; } = [];

        public Task<DotnetProbeResult> RunAsync(
            string workingDirectory,
            string argument,
            CancellationToken cancellationToken
        )
        {
            Arguments.Add(argument);
            Directories.Add(workingDirectory);
            return Task.FromResult(
                argument switch
                {
                    "--list-sdks" => new DotnetProbeResult(
                        !sdkProbeFails,
                        sdkAvailable ? "10.0.401 [C:\\dotnet\\sdk]\n" : ""
                    ),
                    "--list-runtimes" => new DotnetProbeResult(
                        !runtimeProbeFails,
                        runtimeAvailable
                            ? "Microsoft.NETCore.App 10.0.0 [C:\\dotnet\\shared\\Microsoft.NETCore.App]\n"
                            : ""
                    ),
                    _ => throw new InvalidOperationException(
                        "The doctor requested a nonstatic probe."
                    ),
                }
            );
        }
    }
}
