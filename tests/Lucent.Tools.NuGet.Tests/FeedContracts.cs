using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Lucent.Tools.NuGet;
using NuGet.Common;
using NuGet.Configuration;

namespace Lucent.Tools.NuGet.Tests;

[TestClass]
public sealed class FeedContracts
{
    private const string ChildRootVariable = "LUCENT_FEED_HIERARCHY_CHILD_ROOT";
    private static readonly string[] ExpectedSelection =
    [
        "disabled",
        "eligible",
        "mapping-excluded",
    ];
    private static readonly string[] ExpectedSourceNames = ["child", "parent"];

    [TestMethod]
    public async Task ConfigurationHierarchyDisabledAndMostSpecificMappingAreAuthoritativeAndReadOnly()
    {
        using var fixture = new ConfigurationFixture();
        fixture.Write(
            "<packageSources><clear/><add key='parent' value='https://parent.invalid/v3/index.json'/><add key='wild' value='https://wild.invalid/index.json'/></packageSources><packageSourceMapping><packageSource key='wild'><package pattern='*'/></packageSource><packageSource key='parent'><package pattern='Lucent.*'/></packageSource></packageSourceMapping>",
            false
        );
        fixture.Write(
            "<packageSources><add key='disabled' value='https://disabled.invalid/index.json'/></packageSources><disabledPackageSources><add key='disabled' value='true'/></disabledPackageSources>",
            true
        );
        var before = fixture.Snapshot();
        var result = await fixture.Doctor.ObserveAsync(fixture.Request(false));
        Assert.AreEqual("observed", result.Status);
        CollectionAssert.AreEqual(
            ExpectedSelection,
            result.Sources.Select(s => s.Selection).Order(StringComparer.Ordinal).ToArray(),
            JsonSerializer.Serialize(result)
        );
        Assert.IsTrue(result.Sources.All(s => s.Reachability == "notChecked"));
        Assert.AreEqual("generation1", result.Generation);
        CollectionAssert.AreEqual(before, fixture.Snapshot());
    }

    [TestMethod]
    [DataRow(200, "available", "notExercised")]
    [DataRow(404, "notFoundInAnonymousView", "notExercised")]
    [DataRow(401, "unknown", "authRequired")]
    [DataRow(403, "unknown", "forbidden")]
    public async Task ExactReleaseAbsenceAndAuthenticationFailureAreSeparate(
        int status,
        string release,
        string authentication
    )
    {
        await using var server = new FeedFixture(status);
        using var fixture = new ConfigurationFixture();
        fixture.Online(server.Url);
        var before = fixture.Snapshot();
        var result = await fixture.Doctor.ObserveAsync(fixture.Request(true));
        var source = result.Sources.Single();
        Assert.AreEqual("reachable", source.Reachability);
        Assert.AreEqual(release, source.Release);
        Assert.AreEqual(authentication, source.Authentication);
        Assert.AreEqual("notChecked", result.RestoreReadiness);
        Assert.AreEqual("anonymous-no-credentials", result.AuthenticationPolicy);
        Assert.IsFalse(server.SawAuthorization);
        CollectionAssert.AreEqual(before, fixture.Snapshot());
        Assert.IsFalse(
            JsonSerializer.Serialize(result).Contains("SEEDED_SECRET", StringComparison.Ordinal)
        );
        Assert.IsFalse(
            JsonSerializer.Serialize(result).Contains(server.Url, StringComparison.Ordinal)
        );
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CrossOriginRedirectAndAdvertisedResourceNeverReceiveCredentials(bool resource)
    {
        await using var foreign = new FeedFixture();
        await using var server = new FeedFixture { Foreign = foreign.Url, Redirect = !resource };
        using var fixture = new ConfigurationFixture();
        fixture.Online(server.Url);
        var report = await fixture.Doctor.ObserveAsync(fixture.Request(true));
        Assert.AreEqual("unknown", report.Sources.Single().Release);
        Assert.AreEqual(
            resource ? "unsupported-resource" : "unsupported-redirect",
            report.Sources.Single().Reason
        );
        Assert.AreEqual(0, foreign.Requests);
        Assert.IsFalse(server.SawAuthorization);
    }

    [TestMethod]
    public async Task CallerCancellationDoesNotPublishSuccessAndConfigMutationRejectsStaleResult()
    {
        await using var server = new FeedFixture { Block = true };
        using var fixture = new ConfigurationFixture();
        fixture.Online(server.Url);
        using var cancellation = new CancellationTokenSource();
        var operation = fixture.Doctor.ObserveAsync(fixture.Request(true), cancellation.Token);
        await server.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
        server.Block = false;
        server.Release.TrySetResult();
        server.BeforeResponse = fixture.AddUserConfig;
        var stale = await fixture.Doctor.ObserveAsync(fixture.Request(true));
        Assert.AreEqual("stale", stale.Status);
        Assert.AreEqual(0, stale.Sources.Count);
    }

    [TestMethod]
    public async Task OversizedResponseAndMalformedConfigurationNeverExposeDiagnostics()
    {
        await using var server = new FeedFixture { Oversized = true };
        using var fixture = new ConfigurationFixture();
        fixture.Online(server.Url);
        var report = await fixture.Doctor.ObserveAsync(fixture.Request(true));
        Assert.AreEqual("inconclusive", report.Sources.Single().Reachability);
        File.WriteAllText(fixture.ChildConfig, "<SEEDED_SECRET>");
        report = await fixture.Doctor.ObserveAsync(fixture.Request(false));
        Assert.AreEqual("configuration-unavailable", report.Status);
        Assert.IsFalse(
            JsonSerializer.Serialize(report).Contains("SEEDED_SECRET", StringComparison.Ordinal)
        );
    }

    [TestMethod]
    public void PinnedOwnershipDeniesMutationAndMissingUserConfigIsNeverCreated()
    {
        using var fixture = new ConfigurationFixture();
        var before = fixture.FileNames();
        using (var owned = fixture.Open())
        {
            Assert.ThrowsExactly<IOException>(() => File.Delete(fixture.ChildConfig));
            Assert.ThrowsExactly<IOException>(() =>
                File.WriteAllText(fixture.ChildConfig, "changed")
            );
            Assert.IsTrue(owned.IsCurrent(CancellationToken.None));
        }
        CollectionAssert.AreEqual(before, fixture.FileNames());
        File.WriteAllText(fixture.ChildConfig, "<configuration/>");
    }

    [TestMethod]
    public async Task InheritedCredentialsCannotRebindToWorkspaceControlledSource()
    {
        await using var server = new FeedFixture();
        using var fixture = new ConfigurationFixture();
        fixture.Write(
            "<packageSources><clear/><add key='same' value='https://official.invalid/index.json'/></packageSources><packageSourceCredentials><same><add key='Username' value='user'/><add key='Password' value='SEEDED_SECRET_INVALID_ENCRYPTED'/><add key='ValidAuthenticationTypes' value='negotiate'/></same></packageSourceCredentials>",
            false
        );
        fixture.Write(
            $"<packageSources><add key='same' value='{server.Url}' allowInsecureConnections='true'/></packageSources>",
            true
        );
        var report = await fixture.Doctor.ObserveAsync(fixture.Request(true));
        Assert.AreEqual("available", report.Sources.Single().Release);
        Assert.IsFalse(server.SawAuthorization);
        Assert.AreEqual("notChecked", report.ConfiguredAuthentication);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TokenQueriesAreUnsupportedBeforeNetworkOrResourceRequest(bool resource)
    {
        await using var server = new FeedFixture();
        using var fixture = new ConfigurationFixture();
        if (resource)
            server.Foreign = server.Url + "?token=SEEDED_SECRET";
        fixture.Online(resource ? server.Url : server.Url + "?token=SEEDED_SECRET");
        var report = await fixture.Doctor.ObserveAsync(fixture.Request(true));
        Assert.AreEqual(
            resource ? "unsupported-resource" : "unsupported-source",
            report.Sources.Single().Reason
        );
        Assert.AreEqual(resource ? 1 : 0, server.Requests);
    }

    [TestMethod]
    public async Task SameOriginRedirectIsBoundedAndAnonymousReleaseAbsenceIsNotPrivateAbsence()
    {
        await using var server = new FeedFixture
        {
            SameOriginRedirect = true,
            Versions = "{\"versions\":[\"0.3.0-dev.100.1\"]}",
        };
        using var fixture = new ConfigurationFixture();
        fixture.Online(server.Url);
        var report = await fixture.Doctor.ObserveAsync(fixture.Request(true));
        Assert.AreEqual("notFoundInAnonymousView", report.Sources.Single().Release);
        Assert.AreEqual("notChecked", report.PrivateAvailability);
        Assert.AreEqual(3, server.Requests);
        Assert.IsFalse(server.SawAuthorization);
    }

    [TestMethod]
    public async Task DisabledMappedSourcesNeverContactNetwork()
    {
        await using var server = new FeedFixture();
        using var fixture = new ConfigurationFixture();
        fixture.Online(server.Url);
        fixture.Write(
            $"<packageSources><clear/><add key='off' value='{server.Url}' allowInsecureConnections='true'/><add key='excluded' value='{server.Url}' allowInsecureConnections='true'/></packageSources><disabledPackageSources><add key='off' value='true'/></disabledPackageSources><packageSourceMapping><packageSource key='excluded'><package pattern='Different.*'/></packageSource></packageSourceMapping>",
            true
        );
        var report = await fixture.Doctor.ObserveAsync(fixture.Request(true));
        Assert.AreEqual(0, server.Requests);
        Assert.IsTrue(report.Sources.All(source => source.Reachability == "notChecked"));
    }

    [TestMethod]
    public async Task RedirectCycleStopsAtBoundWithoutCredentialOrReleaseClaim()
    {
        await using var server = new FeedFixture { Redirect = true };
        server.Foreign = server.Url;
        using var fixture = new ConfigurationFixture();
        fixture.Online(server.Url);
        var report = await fixture.Doctor.ObserveAsync(fixture.Request(true));
        Assert.AreEqual("unsupported-redirect", report.Sources.Single().Reason);
        Assert.AreEqual(4, server.Requests);
        Assert.IsFalse(server.SawAuthorization);
    }

    [TestMethod]
    public async Task SourceDeadlineReturnsObservationAndLeavesPrivateReadinessUnchecked()
    {
        await using var server = new FeedFixture { Block = true };
        using var fixture = new ConfigurationFixture();
        fixture.Online(server.Url);
        var report = await fixture
            .Doctor.ObserveAsync(fixture.Request(true))
            .WaitAsync(TimeSpan.FromSeconds(12));
        Assert.AreEqual("observed", report.Status);
        Assert.AreEqual("timed-out", report.Sources.Single().Reachability);
        Assert.AreEqual("unknown", report.Sources.Single().Release);
        Assert.AreEqual("notChecked", report.PrivateAvailability);
    }

    [TestMethod]
    public async Task MalformedVersionMetadataCannotProduceAvailableRelease()
    {
        await using var server = new FeedFixture
        {
            Versions = "{\"versions\":[\"0.3.0-dev.101.1\",42]}",
        };
        using var fixture = new ConfigurationFixture();
        fixture.Online(server.Url);
        var report = await fixture.Doctor.ObserveAsync(fixture.Request(true));
        Assert.AreEqual("inconclusive", report.Sources.Single().Reachability);
        Assert.AreEqual("unknown", report.Sources.Single().Release);
    }

    [TestMethod]
    public async Task UnreachableFeedIsDistinctFromAuthenticationAndAnonymousReleaseAbsence()
    {
        var server = new FeedFixture();
        var url = server.Url;
        await server.DisposeAsync();
        using var fixture = new ConfigurationFixture();
        fixture.Online(url);
        var report = await fixture.Doctor.ObserveAsync(fixture.Request(true));
        Assert.AreEqual("unreachable", report.Sources.Single().Reachability);
        Assert.AreEqual("unknown", report.Sources.Single().Authentication);
        Assert.AreEqual("unknown", report.Sources.Single().Release);
    }

    [TestMethod]
    public async Task FalseDisabledEntryAndClearFollowOfficialNuGetPresenceSemantics()
    {
        using var fixture = new ConfigurationFixture();
        fixture.Write(
            "<packageSources><clear/><add key='source' value='https://source.invalid/'/></packageSources><disabledPackageSources><add key='source' value='true'/></disabledPackageSources>",
            false
        );
        fixture.Write(
            "<disabledPackageSources><add key='source' value='false'/></disabledPackageSources>",
            true
        );
        using (var owned = fixture.Open())
        {
            Assert.IsFalse(
                new PackageSourceProvider(owned.Settings).IsPackageSourceEnabled("source")
            );
        }
        var report = await fixture.Doctor.ObserveAsync(fixture.Request(false));
        Assert.AreEqual("disabled", report.Sources.Single().Selection);
        fixture.Write("<disabledPackageSources><clear/></disabledPackageSources>", true);
        using (var owned = fixture.Open())
        {
            Assert.IsTrue(
                new PackageSourceProvider(owned.Settings).IsPackageSourceEnabled("source")
            );
        }
        report = await fixture.Doctor.ObserveAsync(fixture.Request(false));
        Assert.AreEqual("eligible", report.Sources.Single().Selection);
    }

    [TestMethod]
    public async Task StableHierarchyMatchesOfficialDefaultLoaderIncludingMachineSettings()
    {
        var childRoot = Environment.GetEnvironmentVariable(ChildRootVariable);
        if (childRoot is not null)
        {
            CompareIsolatedHierarchy(childRoot);
            return;
        }
        await RunIsolatedTestAsync(
            nameof(StableHierarchyMatchesOfficialDefaultLoaderIncludingMachineSettings)
        );
    }

    [TestMethod]
    public async Task AdapterInvocationOwnsAndCleansScratchWithoutTouchingInheritedScratch()
    {
        var childRoot = Environment.GetEnvironmentVariable(ChildRootVariable);
        if (childRoot is null)
        {
            await RunIsolatedTestAsync(
                nameof(AdapterInvocationOwnsAndCleansScratchWithoutTouchingInheritedScratch)
            );
            return;
        }
        var inherited = Environment.GetEnvironmentVariable("NUGET_SCRATCH")!;
        Assert.AreEqual(Path.Combine(childRoot, "NUGET_SCRATCH"), inherited);
        var sentinel = Path.Combine(inherited, "sentinel");
        File.WriteAllText(sentinel, "inherited-scratch-must-remain-unchanged");
        using var fixture = new ConfigurationFixture();
        fixture.Online("https://example.invalid/index.json");
        var before = Directory.GetFiles(inherited, "*", SearchOption.AllDirectories);
        var input = Console.In;
        var output = Console.Out;
        var captured = new StringWriter();
        int exitCode;
        try
        {
            Console.SetIn(new StringReader(JsonSerializer.Serialize(fixture.Request(false))));
            Console.SetOut(captured);
            exitCode = await Lucent.Tools.NuGet.Program.Main();
        }
        finally
        {
            Console.SetIn(input);
            Console.SetOut(output);
        }
        Assert.AreEqual(0, exitCode);
        using var report = JsonDocument.Parse(captured.ToString());
        Assert.AreEqual("observed", report.RootElement.GetProperty("status").GetString());
        var owned = Environment.GetEnvironmentVariable("NUGET_SCRATCH")!;
        Assert.AreNotEqual(inherited, owned);
        Assert.AreEqual(
            Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetDirectoryName(owned)
        );
        Assert.IsTrue(
            Path.GetFileName(owned).StartsWith("lucent-nuget-scratch-", StringComparison.Ordinal)
        );
        Assert.IsFalse(Directory.Exists(owned));
        CollectionAssert.AreEqual(
            before,
            Directory.GetFiles(inherited, "*", SearchOption.AllDirectories)
        );
        Assert.AreEqual("inherited-scratch-must-remain-unchanged", File.ReadAllText(sentinel));
    }

    private static async Task RunIsolatedTestAsync(string testName)
    {
        var root = Directory.CreateTempSubdirectory("lucent-feed-child-");
        try
        {
            var start = new ProcessStartInfo(
                Path.GetFullPath(
                    Path.Combine(
                        RuntimeEnvironment.GetRuntimeDirectory(),
                        "..",
                        "..",
                        "..",
                        "dotnet.exe"
                    )
                )
            )
            {
                WorkingDirectory = root.FullName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add(typeof(FeedContracts).Assembly.Location);
            start.ArgumentList.Add("--filter");
            start.ArgumentList.Add("FullyQualifiedName~" + testName);
            start.ArgumentList.Add("--report-trx");
            start.ArgumentList.Add("--results-directory");
            var results = Path.Combine(root.FullName, "results");
            start.ArgumentList.Add(results);
            start.Environment[ChildRootVariable] = root.FullName;
            foreach (
                var variable in new[]
                {
                    "APPDATA",
                    "LOCALAPPDATA",
                    "PROGRAMFILES",
                    "PROGRAMFILES(X86)",
                    "DOTNET_CLI_HOME",
                    "TEMP",
                    "TMP",
                    "NUGET_SCRATCH",
                }
            )
                start.Environment[variable] = Directory
                    .CreateDirectory(
                        Path.Combine(root.FullName, variable.Replace('(', '_').Replace(')', '_'))
                    )
                    .FullName;
            start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            start.Environment["TESTINGPLATFORM_TELEMETRY_OPTOUT"] = "1";
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var process = Process.Start(start)!;
            var output = DrainBoundedAsync(process.StandardOutput, deadline.Token);
            var errors = DrainBoundedAsync(process.StandardError, deadline.Token);
            try
            {
                await Task.WhenAll(output, errors, process.WaitForExitAsync(deadline.Token))
                    .WaitAsync(deadline.Token);
                Assert.AreEqual(0, process.ExitCode, "Isolated adapter contract failed.");
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await process.WaitForExitAsync(cleanup.Token);
                }
                await deadline.CancelAsync();
                try
                {
                    await Task.WhenAll(output, errors);
                }
                catch (Exception error) when (error is OperationCanceledException or IOException)
                { }
            }
            var report = XDocument.Load(Directory.GetFiles(results, "*.trx").Single());
            var counters = report
                .Descendants()
                .Single(element => element.Name.LocalName == "Counters");
            Assert.AreEqual("1", counters.Attribute("total")?.Value);
            Assert.AreEqual("1", counters.Attribute("executed")?.Value);
            Assert.AreEqual("1", counters.Attribute("passed")?.Value);
            var result = report
                .Descendants()
                .Single(element => element.Name.LocalName == "UnitTestResult");
            Assert.AreEqual(testName, result.Attribute("testName")?.Value);
            Assert.AreEqual("Passed", result.Attribute("outcome")?.Value);
        }
        finally
        {
            root.Delete(true);
        }
    }

    private static void CompareIsolatedHierarchy(string childRoot)
    {
        var user = NuGetEnvironment.GetFolderPath(NuGetFolderPath.UserSettingsDirectory);
        var machine = NuGetEnvironment.GetFolderPath(NuGetFolderPath.MachineWideConfigDirectory);
        Assert.AreEqual(Path.Combine(childRoot, "APPDATA", "NuGet"), user);
        Assert.AreEqual(Path.Combine(childRoot, "PROGRAMFILES_X86_", "NuGet", "Config"), machine);
        Directory.CreateDirectory(user);
        Directory.CreateDirectory(machine);
        File.WriteAllText(
            Path.Combine(user, Settings.DefaultSettingsFileName),
            "<configuration><config><add key='user-policy' value='fixture'/></config></configuration>"
        );
        var additional = Directory
            .CreateDirectory(Path.Combine(user, ConfigurationConstants.Config))
            .FullName;
        File.WriteAllText(
            Path.Combine(additional, "a.config"),
            "<configuration><config><add key='additional-policy' value='a'/></config></configuration>"
        );
        File.WriteAllText(
            Path.Combine(additional, "z.config"),
            "<configuration><config><add key='additional-policy' value='z'/></config></configuration>"
        );
        File.WriteAllText(
            Path.Combine(additional, Settings.DefaultSettingsFileName),
            "<intentionally-invalid/>"
        );
        using var fixture = new ConfigurationFixture();
        fixture.Write(
            "<packageSources><clear/><add key='parent' value='https://parent.invalid/'/></packageSources>",
            false
        );
        fixture.Write(
            "<packageSources><add key='child' value='https://child.invalid/'/></packageSources>",
            true
        );
        File.WriteAllText(
            Path.Combine(machine, "test.config"),
            "<configuration><config><add key='machine-policy' value='fixture'/></config><packageSources><add key='machine' value='https://machine.invalid/'/></packageSources></configuration>"
        );
        var official = Settings.LoadDefaultSettings(
            fixture.Workspace,
            null,
            new XPlatMachineWideSetting()
        );
        using var owned = PinnedNuGetSettings.Open(fixture.Workspace, CancellationToken.None);
        CollectionAssert.AreEqual(SourceNames(official), SourceNames(owned.Settings));
        CollectionAssert.AreEqual(ExpectedSourceNames, SourceNames(owned.Settings));
        foreach (var key in new[] { "user-policy", "additional-policy", "machine-policy" })
            Assert.AreEqual(
                SettingsUtility.GetConfigValue(official, key),
                SettingsUtility.GetConfigValue(owned.Settings, key)
            );
        Assert.AreEqual("fixture", SettingsUtility.GetConfigValue(owned.Settings, "user-policy"));
        Assert.AreEqual("a", SettingsUtility.GetConfigValue(owned.Settings, "additional-policy"));
        Assert.AreEqual(
            "fixture",
            SettingsUtility.GetConfigValue(owned.Settings, "machine-policy")
        );
    }

    private static async Task DrainBoundedAsync(
        StreamReader reader,
        CancellationToken cancellationToken
    )
    {
        var buffer = new char[1024];
        var length = 0;
        int count;
        while ((count = await reader.ReadAsync(buffer, cancellationToken)) > 0)
            if ((length += count) > 32768)
                throw new IOException("Child output bound exceeded.");
    }

    [TestMethod]
    public void UserAdditionalOrderingAndIgnoredDefaultFilenameAreIndependentlyAsserted()
    {
        using var fixture = new ConfigurationFixture();
        fixture.Write("", false);
        fixture.Write("", true);
        fixture.AddUserConfig();
        var additional = Directory
            .CreateDirectory(Path.Combine(fixture.User, ConfigurationConstants.Config))
            .FullName;
        File.WriteAllText(
            Path.Combine(additional, "a.config"),
            "<configuration><packageSources><add key='additional' value='https://a.invalid/'/></packageSources></configuration>"
        );
        File.WriteAllText(
            Path.Combine(additional, "z.config"),
            "<configuration><packageSources><add key='additional' value='https://z.invalid/'/></packageSources></configuration>"
        );
        File.WriteAllText(
            Path.Combine(additional, Settings.DefaultSettingsFileName),
            "<intentionally-invalid/>"
        );
        using var owned = fixture.Open();
        var source = owned
            .Settings.GetSection(ConfigurationConstants.PackageSources)!
            .Items.OfType<SourceItem>()
            .Single();
        Assert.AreEqual("https://a.invalid/", source.GetValueAsPath());
        var official = Settings.LoadSettingsGivenConfigPaths([
            fixture.ChildConfig,
            fixture.ParentConfig,
            Path.Combine(fixture.User, Settings.DefaultSettingsFileName),
            Path.Combine(additional, "a.config"),
            Path.Combine(additional, "z.config"),
        ]);
        Assert.AreEqual(
            official
                .GetSection(ConfigurationConstants.PackageSources)!
                .Items.OfType<SourceItem>()
                .Single()
                .GetValueAsPath(),
            source.GetValueAsPath()
        );
    }

    [TestMethod]
    public async Task UnsupportedDefaultsAndMalformedLoadReleaseEveryOwnedHandle()
    {
        using var fixture = new ConfigurationFixture();
        File.WriteAllText(fixture.Defaults, "<configuration/>");
        var report = await fixture.Doctor.ObserveAsync(fixture.Request(false));
        Assert.AreEqual("unsupported-defaults", report.Reason);
        File.Delete(fixture.Defaults);
        File.WriteAllText(fixture.ChildConfig, "<broken");
        report = await fixture.Doctor.ObserveAsync(fixture.Request(false));
        Assert.AreEqual("configuration-unavailable", report.Status);
        using var exclusive = new FileStream(
            fixture.ParentConfig,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None
        );
        using var child = new FileStream(
            fixture.ChildConfig,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None
        );
    }

    private static string[] SourceNames(ISettings settings) =>
        settings
            .GetSection(ConfigurationConstants.PackageSources)!
            .Items.OfType<SourceItem>()
            .Select(item => item.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private sealed class ConfigurationFixture : IDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("lucent-feed-");
        private readonly string _parentConfig;
        public string ChildConfig { get; }
        private readonly string _workspace;
        private readonly string _user;
        private readonly string _machine;
        public string Workspace => _workspace;
        public string User => _user;
        public string Machine => _machine;
        public string ParentConfig => _parentConfig;
        public string Defaults =>
            Path.Combine(_root.FullName, ConfigurationConstants.ConfigurationDefaultsFile);
        public FeedDoctor Doctor { get; }

        public ConfigurationFixture()
        {
            _workspace = Directory
                .CreateDirectory(Path.Combine(_root.FullName, "workspace"))
                .FullName;
            _parentConfig = Path.Combine(_root.FullName, "NuGet.Config");
            ChildConfig = Path.Combine(_workspace, "NuGet.Config");
            _user = Directory.CreateDirectory(Path.Combine(_root.FullName, "user")).FullName;
            _machine = Directory.CreateDirectory(Path.Combine(_root.FullName, "machine")).FullName;
            Doctor = new FeedDoctor(
                (workspace, token) => new PinnedNuGetSettings(workspace, _user, _machine, token)
            );
            Write("", false);
            Write("", true);
        }

        public void Write(string contents, bool child) =>
            File.WriteAllText(
                child ? ChildConfig : _parentConfig,
                "<configuration>" + contents + "</configuration>"
            );

        public FeedRequest Request(bool online) =>
            new(_workspace, "Lucent.Core", "0.3.0-dev.101.1", online, "generation1");

        public void Online(string url, string name = "source") =>
            Write(
                $"<packageSources><clear/><add key='{name}' value='{url}' allowInsecureConnections='true'/></packageSources><packageSourceCredentials><{name}><add key='Username' value='user'/><add key='ClearTextPassword' value='SEEDED_SECRET'/></{name}></packageSourceCredentials>",
                true
            );

        public string[] Snapshot() =>
            [File.ReadAllText(_parentConfig), File.ReadAllText(ChildConfig)];

        public void AddUserConfig() =>
            File.WriteAllText(
                Path.Combine(_user, Settings.DefaultSettingsFileName),
                "<configuration/>"
            );

        public PinnedNuGetSettings Open() =>
            new(_workspace, _user, _machine, CancellationToken.None);

        public string[] FileNames() =>
            Directory
                .GetFiles(_root.FullName, "*", SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        public void Dispose() => _root.Delete(true);
    }

    private sealed class FeedFixture : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        private readonly int _packageStatus;
        public string Url { get; }
        public string? Foreign { get; set; }
        public bool Redirect { get; set; }
        public bool Block { get; set; }
        public bool Oversized { get; set; }
        public bool SameOriginRedirect { get; set; }
        public string Versions { get; set; } = "{\"versions\":[\"0.3.0-dev.101.1\"]}";
        public Action? BeforeResponse { get; set; }
        public bool SawAuthorization { get; private set; }
        public int Requests { get; private set; }
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FeedFixture(int packageStatus = 200)
        {
            _packageStatus = packageStatus;
            using var port = new TcpListener(IPAddress.Loopback, 0);
            port.Start();
            var number = ((IPEndPoint)port.LocalEndpoint).Port;
            port.Stop();
            Url = $"http://127.0.0.1:{number}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
            _loop = LoopAsync();
        }

        private async Task LoopAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync().WaitAsync(_stop.Token);
                    Requests++;
                    SawAuthorization |= context.Request.Headers["Authorization"] is not null;
                    Started.TrySetResult();
                    if (Block)
                    {
                        await Release.Task.WaitAsync(_stop.Token);
                        context.Response.Abort();
                        continue;
                    }
                    BeforeResponse?.Invoke();
                    var index = context.Request.RawUrl is "/" or "/moved";
                    if (Redirect && index || SameOriginRedirect && context.Request.RawUrl == "/")
                    {
                        context.Response.StatusCode = 302;
                        context.Response.RedirectLocation = SameOriginRedirect
                            ? Url + "moved"
                            : Foreign;
                    }
                    else
                    {
                        context.Response.StatusCode = index ? 200 : _packageStatus;
                        var text =
                            Oversized ? new string('x', 1024 * 1024 + 1)
                            : index
                                ? $"{{\"version\":\"3.0.0\",\"resources\":[{{\"@id\":\"{Foreign ?? Url}flat/\",\"@type\":\"PackageBaseAddress/3.0.0\"}}]}}"
                            : Versions;
                        var bytes = Encoding.UTF8.GetBytes(text);
                        context.Response.ContentLength64 = bytes.Length;
                        await context.Response.OutputStream.WriteAsync(bytes, _stop.Token);
                    }
                    context.Response.Close();
                }
            }
            catch (Exception error)
                when (error
                        is OperationCanceledException
                            or HttpListenerException
                            or ObjectDisposedException
                            or IOException
                ) { }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _loop;
            _listener.Close();
            _stop.Dispose();
        }
    }
}
