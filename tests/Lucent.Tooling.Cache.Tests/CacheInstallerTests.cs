using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucent.Tooling.Cache;

namespace Lucent.Tooling.Cache.Tests;

[TestClass]
public sealed class CacheInstallerTests
{
    [TestMethod]
    public async Task AbsentCacheVerificationDoesNotCreateDirectoriesOrRunServer()
    {
        using var fixture = new ServerFixture();
        var runner = new FakeIdentity(fixture.Identity);
        Assert.IsFalse(Directory.Exists(fixture.CacheRoot));
        var failure = await Assert.ThrowsExactlyAsync<CacheException>(() =>
            new CacheInstaller(runner).ExecuteAsync(
                fixture.Request("verify"),
                CancellationToken.None
            )
        );
        Assert.AreEqual("cache_missing", failure.Code);
        Assert.IsFalse(Directory.Exists(fixture.CacheRoot));
        Assert.AreEqual(0, runner.Calls);
    }

    [TestMethod]
    public async Task InstallAndOfflineVerifyPreserveImmutableServerPayload()
    {
        using var fixture = new ServerFixture();
        var runner = new FakeIdentity(fixture.Identity);
        var installer = new CacheInstaller(runner);
        var installed = await installer.ExecuteAsync(fixture.Request(), CancellationToken.None);
        Assert.AreEqual("verified", installed.Status);
        Assert.IsTrue(File.Exists(installed.ServerPath));
        Assert.IsTrue(File.Exists(Path.Combine(installed.GenerationPath, "receipt.json")));
        Assert.IsFalse(
            File.Exists(Path.Combine(installed.GenerationPath, "server", "receipt.json"))
        );
        File.Delete(fixture.ArchivePath);
        var verified = await installer.ExecuteAsync(
            fixture.Request("verify"),
            CancellationToken.None
        );
        Assert.AreEqual(installed.ServerPath, verified.ServerPath);
        var otherAnchor = fixture.Request("verify");
        otherAnchor = otherAnchor with
        {
            Anchor = otherAnchor.Anchor! with
            {
                Kind = "github-actions-receipt",
                DescriptorSha256 = new string('d', 64),
            },
        };
        var reused = await installer.ExecuteAsync(otherAnchor, CancellationToken.None);
        Assert.AreEqual(installed.ServerPath, reused.ServerPath);
        Assert.AreEqual(3, runner.Calls);
        Assert.AreEqual(
            0,
            Directory
                .EnumerateFileSystemEntries(Path.Combine(fixture.CacheRoot, ".staging"))
                .Count()
        );
    }

    [TestMethod]
    public async Task MissingNoticeAndTraversalNeverPromoteOrEscapeStaging()
    {
        using var missing = new ServerFixture(omit: "notices/Roslyn-LICENSE.txt");
        var runner = new FakeIdentity(missing.Identity);
        var failure = await Assert.ThrowsExactlyAsync<CacheException>(() =>
            new CacheInstaller(runner).ExecuteAsync(missing.Request(), CancellationToken.None)
        );
        Assert.AreEqual("deployment", failure.Code);
        Assert.AreEqual(0, runner.Calls);
        Assert.AreEqual(
            0,
            Directory
                .EnumerateFileSystemEntries(Path.Combine(missing.CacheRoot, "generations"))
                .Count()
        );
        Assert.AreEqual(
            0,
            Directory
                .EnumerateFileSystemEntries(Path.Combine(missing.CacheRoot, ".staging"))
                .Count()
        );

        using var traversal = new ServerFixture(extraArchiveEntry: "../outside.txt");
        failure = await Assert.ThrowsExactlyAsync<CacheException>(() =>
            new CacheInstaller(new FakeIdentity(traversal.Identity)).ExecuteAsync(
                traversal.Request(),
                CancellationToken.None
            )
        );
        Assert.AreEqual("archive_path", failure.Code);
        Assert.IsFalse(File.Exists(Path.Combine(traversal.Root, "outside.txt")));
        Assert.AreEqual(
            0,
            Directory
                .EnumerateFileSystemEntries(Path.Combine(traversal.CacheRoot, ".staging"))
                .Count()
        );

        foreach (
            var alias in new[]
            {
                "CON.txt",
                "notices/Roslyn-LICENSE.txt.",
                "LUCENT.LUI.LANGUAGESERVER.DLL",
            }
        )
        {
            using var device = new ServerFixture(extraArchiveEntry: alias);
            failure = await Assert.ThrowsExactlyAsync<CacheException>(() =>
                new CacheInstaller(new FakeIdentity(device.Identity)).ExecuteAsync(
                    device.Request(),
                    CancellationToken.None
                )
            );
            Assert.AreEqual(
                alias.StartsWith("LUCENT", StringComparison.Ordinal)
                    ? "archive_entry"
                    : "archive_path",
                failure.Code
            );
            Assert.AreEqual(
                0,
                Directory
                    .EnumerateFileSystemEntries(Path.Combine(device.CacheRoot, ".staging"))
                    .Count()
            );
        }
        using var link = new ServerFixture(
            extraArchiveEntry: "link",
            extraAttributes: (int)FileAttributes.ReparsePoint
        );
        failure = await Assert.ThrowsExactlyAsync<CacheException>(() =>
            new CacheInstaller(new FakeIdentity(link.Identity)).ExecuteAsync(
                link.Request(),
                CancellationToken.None
            )
        );
        Assert.AreEqual("archive_entry", failure.Code);
    }

    [TestMethod]
    public async Task WrongExecutableIdentityDoesNotPromoteAndCancelCleansOnlyOwnStage()
    {
        using var wrong = new ServerFixture();
        var altered = JsonSerializer.SerializeToElement(
            new { schemaVersion = 1, sourceCommit = new string('b', 40) }
        );
        var failure = await Assert.ThrowsExactlyAsync<CacheException>(() =>
            new CacheInstaller(new FakeIdentity(altered)).ExecuteAsync(
                wrong.Request(),
                CancellationToken.None
            )
        );
        Assert.AreEqual("identity", failure.Code);
        Assert.AreEqual(
            0,
            Directory
                .EnumerateFileSystemEntries(Path.Combine(wrong.CacheRoot, "generations"))
                .Count()
        );

        using var canceled = new ServerFixture();
        using var cancellation = new CancellationTokenSource();
        var blocking = new FakeIdentity(canceled.Identity) { Block = true };
        var operation = new CacheInstaller(blocking).ExecuteAsync(
            canceled.Request(),
            cancellation.Token
        );
        await blocking.Entered.Task;
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => operation);
        Assert.AreEqual(
            0,
            Directory
                .EnumerateFileSystemEntries(Path.Combine(canceled.CacheRoot, "generations"))
                .Count()
        );
        Assert.AreEqual(
            0,
            Directory
                .EnumerateFileSystemEntries(Path.Combine(canceled.CacheRoot, ".staging"))
                .Count()
        );
    }

    [TestMethod]
    public async Task ConcurrentWindowsReuseOneGenerationAndRejectChangedCachedByte()
    {
        using var fixture = new ServerFixture();
        var runner = new FakeIdentity(fixture.Identity);
        var installer = new CacheInstaller(runner);
        var results = await Task.WhenAll(
            installer.ExecuteAsync(fixture.Request(), CancellationToken.None),
            installer.ExecuteAsync(fixture.Request(), CancellationToken.None)
        );
        Assert.AreEqual(results[0].GenerationPath, results[1].GenerationPath);
        Assert.AreEqual(
            1,
            Directory.EnumerateDirectories(Path.Combine(fixture.CacheRoot, "generations")).Count()
        );
        File.WriteAllText(results[0].ServerPath, "changed server");
        var failure = await Assert.ThrowsExactlyAsync<CacheException>(() =>
            installer.ExecuteAsync(fixture.Request("verify"), CancellationToken.None)
        );
        Assert.AreEqual("inventory_hash", failure.Code);
    }

    [TestMethod]
    public async Task FailedReplacementLeavesPreviousVerifiedGenerationAvailable()
    {
        using var previous = new ServerFixture();
        var installer = new CacheInstaller(new FakeIdentity(previous.Identity));
        var active = await installer.ExecuteAsync(previous.Request(), CancellationToken.None);
        using var replacement = new ServerFixture(
            cacheRoot: previous.CacheRoot,
            serverText: "different server bytes"
        );
        var wrongIdentity = JsonSerializer.SerializeToElement(
            new { sourceCommit = new string('b', 40) }
        );
        var failure = await Assert.ThrowsExactlyAsync<CacheException>(() =>
            new CacheInstaller(new FakeIdentity(wrongIdentity)).ExecuteAsync(
                replacement.Request(),
                CancellationToken.None
            )
        );
        Assert.AreEqual("identity", failure.Code);
        Assert.IsTrue(File.Exists(active.ServerPath));
        Assert.AreEqual(
            1,
            Directory.EnumerateDirectories(Path.Combine(previous.CacheRoot, "generations")).Count()
        );
        var reused = await installer.ExecuteAsync(
            previous.Request("verify"),
            CancellationToken.None
        );
        Assert.AreEqual(active.ServerPath, reused.ServerPath);
    }

    [TestMethod]
    public async Task CliRejectsInvalidRequestWithoutWaitingForStdinEof()
    {
        using var process = StartHelper();
        await process.StandardInput.WriteLineAsync("{}");
        await process.StandardInput.FlushAsync();
        try
        {
            var response = await process
                .StandardOutput.ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
            using var parsed = JsonDocument.Parse(response!);
            Assert.AreEqual("error", parsed.RootElement.GetProperty("status").GetString());
            Assert.AreEqual("request", parsed.RootElement.GetProperty("code").GetString());
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    [TestMethod]
    public async Task CliCancelWhileAnotherProcessHoldsInstallLockLeavesNoStage()
    {
        using var fixture = new ServerFixture();
        var request = fixture.Request();
        var lockDirectory = Path.Combine(fixture.CacheRoot, "locks");
        Directory.CreateDirectory(lockDirectory);
        using var held = new FileStream(
            Path.Combine(lockDirectory, request.Expected!.Artifact!.Sha256 + ".lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        );
        using var process = StartHelper();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));
        await process.StandardInput.FlushAsync();
        await Task.Delay(200);
        Assert.IsFalse(process.HasExited);
        await process.StandardInput.WriteLineAsync("cancel");
        await process.StandardInput.FlushAsync();
        try
        {
            var response = await process
                .StandardOutput.ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
            using var parsed = JsonDocument.Parse(response!);
            Assert.AreEqual("cancelled", parsed.RootElement.GetProperty("code").GetString());
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(4, process.ExitCode);
            Assert.AreEqual(
                0,
                Directory
                    .EnumerateFileSystemEntries(Path.Combine(fixture.CacheRoot, ".staging"))
                    .Count()
            );
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    [TestMethod]
    public async Task AbandonedStageDoesNotBlockNewGenerationOrGetDeleted()
    {
        using var fixture = new ServerFixture();
        var orphan = Path.Combine(fixture.CacheRoot, ".staging", "abandoned-stage");
        Directory.CreateDirectory(orphan);
        File.WriteAllText(Path.Combine(orphan, "marker"), "previous process");
        var result = await new CacheInstaller(new FakeIdentity(fixture.Identity)).ExecuteAsync(
            fixture.Request(),
            CancellationToken.None
        );
        Assert.AreEqual("verified", result.Status);
        Assert.AreEqual("previous process", File.ReadAllText(Path.Combine(orphan, "marker")));
        Assert.AreEqual(
            1,
            Directory.EnumerateDirectories(Path.Combine(fixture.CacheRoot, ".staging")).Count()
        );
    }

    private static Process StartHelper()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(typeof(CacheInstaller).Assembly.Location);
        return Process.Start(start)!;
    }

    private sealed class FakeIdentity(JsonElement identity) : IServerIdentityRunner
    {
        public bool Block { get; init; }
        public int Calls { get; private set; }
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<JsonElement> IdentifyAsync(
            string dotnetPath,
            string serverPath,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            Assert.IsTrue(File.Exists(serverPath));
            Entered.TrySetResult();
            if (Block)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return identity;
        }
    }

    private sealed class ServerFixture : IDisposable
    {
        private static readonly string[] Required =
        [
            "Lucent.Lui.LanguageServer.dll",
            "Lucent.Lui.Compiler.dll",
            "Lucent.Lui.Preparation.dll",
            "Lucent.Lui.LanguageServer.deps.json",
            "Lucent.Lui.LanguageServer.runtimeconfig.json",
            "BuildHost-netcore/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll",
            "BuildHost-netcore/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.deps.json",
            "BuildHost-netcore/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.runtimeconfig.json",
            "LICENSE",
            "notices/Roslyn-LICENSE.txt",
            "notices/dotnet-LICENSE.txt",
            "notices/Roslyn-Common-NOTICES.rtf",
            "notices/Roslyn-CSharp-NOTICES.rtf",
            "notices/Roslyn-Features-NOTICES.rtf",
            "notices/Roslyn-CSharp-Features-NOTICES.rtf",
            "notices/Roslyn-CSharp-Workspaces-NOTICES.rtf",
            "notices/Roslyn-Workspaces-Common-NOTICES.rtf",
            "notices/Roslyn-Workspaces-MSBuild-NOTICES.rtf",
            "notices/MSBuild-NOTICES.txt",
            "notices/dotnet-Cryptography-NOTICES.txt",
        ];
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly byte[] server;
        private readonly byte[] compiler = Encoding.UTF8.GetBytes("test compiler dll");
        private readonly CallerAnchor anchor;
        private readonly ApprovedServer approved;

        public ServerFixture(
            string? omit = null,
            string? extraArchiveEntry = null,
            int extraAttributes = 0,
            string? cacheRoot = null,
            string serverText = "test server dll"
        )
        {
            server = Encoding.UTF8.GetBytes(serverText);
            Root = Path.Combine(
                Path.GetTempPath(),
                "lucent-cache-test-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(Root);
            CacheRoot = cacheRoot ?? Path.Combine(Root, "cache");
            ArchivePath = Path.Combine(Root, "server.zip");
            DotnetPath = Path.Combine(Root, "dotnet.exe");
            File.WriteAllText(DotnetPath, "fake dotnet for injected runner");
            var source = new string('a', 40);
            var runtime = new
            {
                tfm = "net10.0",
                framework = new { name = "Microsoft.NETCore.App", version = "10.0.0" },
                configProperties = new { },
            };
            Identity = JsonSerializer.SerializeToElement(
                new
                {
                    schemaVersion = 1,
                    sourceCommit = source,
                    server = new
                    {
                        sha256 = Hash(server),
                        informationalVersion = "1.0.0+" + source,
                    },
                    compiler = new
                    {
                        sha256 = Hash(compiler),
                        informationalVersion = "1.0.0+" + source,
                    },
                    language = new
                    {
                        id = "lui",
                        version = "preview",
                        featureLevel = "preview-1",
                    },
                    protocol = new
                    {
                        id = "lucent-lui",
                        major = 1,
                        minor = 0,
                    },
                    runtime,
                },
                JsonOptions
            );
            var runtimeConfig = JsonSerializer.SerializeToUtf8Bytes(
                new { runtimeOptions = runtime },
                JsonOptions
            );
            var deps = Encoding.UTF8.GetBytes(
                "{\"runtimeTarget\":{\"name\":\"net10.0\"},\"targets\":{\"net10.0\":{\"fixture/1\":{}}}}"
            );
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["Lucent.Lui.LanguageServer.dll"] = server,
                ["Lucent.Lui.Compiler.dll"] = compiler,
                ["Lucent.Lui.LanguageServer.runtimeconfig.json"] = runtimeConfig,
                ["Lucent.Lui.LanguageServer.deps.json"] = deps,
                [
                    "BuildHost-netcore/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.deps.json"
                ] = deps,
                ["lucent-server.json"] = JsonSerializer.SerializeToUtf8Bytes(Identity, JsonOptions),
            };
            foreach (var name in Required)
                if (name != omit && !files.ContainsKey(name))
                    files.Add(name, Encoding.UTF8.GetBytes("fixture " + name));
            var inventory = new
            {
                files = files
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new
                    {
                        bytes = pair.Value.Length,
                        fileName = pair.Key,
                        sha256 = Hash(pair.Value),
                    })
                    .ToArray(),
                schemaVersion = 1,
            };
            var inventoryBytes = JsonSerializer.SerializeToUtf8Bytes(inventory, JsonOptions);
            using (var zip = ZipFile.Open(ArchivePath, ZipArchiveMode.Create))
            {
                foreach (
                    var (name, bytes) in files.Append(
                        new KeyValuePair<string, byte[]>("lucent-server-files.json", inventoryBytes)
                    )
                )
                {
                    var entry = zip.CreateEntry(name);
                    using var output = entry.Open();
                    output.Write(bytes);
                }
                if (extraArchiveEntry is not null)
                {
                    var entry = zip.CreateEntry(extraArchiveEntry);
                    entry.ExternalAttributes = extraAttributes;
                    using var output = entry.Open();
                    output.WriteByte(1);
                }
            }
            approved = new ApprovedServer
            {
                Artifact = new ArtifactBytes
                {
                    Bytes = new FileInfo(ArchivePath).Length,
                    Sha256 = Hash(File.ReadAllBytes(ArchivePath)),
                },
                FilesSha256 = Hash(inventoryBytes),
                Identity = Identity,
            };
            anchor = new CallerAnchor
            {
                Kind = "bundled-catalog",
                SourceCommit = source,
                DescriptorSha256 = new string('c', 64),
            };
        }

        public string Root { get; }
        public string CacheRoot { get; }
        public string ArchivePath { get; }
        public string DotnetPath { get; }
        public JsonElement Identity { get; }

        public CacheRequest Request(string operation = "install") =>
            new()
            {
                SchemaVersion = 1,
                Operation = operation,
                CacheRoot = CacheRoot,
                ArchivePath = operation == "install" ? ArchivePath : null,
                DotnetPath = DotnetPath,
                Expected = approved,
                Anchor = anchor,
            };

        public void Dispose()
        {
            var prefix =
                Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Root);
            if (!full.StartsWith(prefix + "lucent-cache-test-", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Refusing to delete cache fixture outside its temporary prefix."
                );
            Directory.Delete(full, recursive: true);
        }

        private static string Hash(byte[] bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
