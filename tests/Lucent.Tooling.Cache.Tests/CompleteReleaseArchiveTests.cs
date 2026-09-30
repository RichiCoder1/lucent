using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucent.Tooling.Cache;

namespace Lucent.Tooling.Cache.Tests;

[TestClass]
public sealed class CompleteReleaseArchiveTests
{
    [TestMethod]
    public void ExtractsOnlyTheApprovedServerZipFromExactCompleteDescriptor()
    {
        using var fixture = new Fixture();
        var extracted = CompleteReleaseArchive.Extract(fixture.Request, CancellationToken.None);
        Assert.IsTrue(File.Exists(extracted.ServerArchivePath));
        CollectionAssert.AreEqual(
            fixture.ServerBytes,
            File.ReadAllBytes(extracted.ServerArchivePath)
        );
        Assert.AreEqual(1, Directory.EnumerateFiles(extracted.StageDirectory).Count());
        Assert.IsFalse(File.Exists(Path.Combine(extracted.StageDirectory, "unrelated.nupkg")));
    }

    [TestMethod]
    public void RejectsChangedDescriptorAndOuterDigestWithoutPromotion()
    {
        using var fixture = new Fixture();
        var wrongDescriptor = fixture.Request with { DescriptorSha256 = new string('0', 64) };
        var failure = Assert.ThrowsExactly<CacheException>(() =>
            CompleteReleaseArchive.Extract(wrongDescriptor, CancellationToken.None)
        );
        Assert.AreEqual("outer_descriptor", failure.Code);
        Assert.AreEqual(
            0,
            Directory.EnumerateFileSystemEntries(fixture.Request.StagingRoot).Count()
        );
        var wrongDigest = fixture.Request with { ArtifactDigest = "sha256:" + new string('0', 64) };
        failure = Assert.ThrowsExactly<CacheException>(() =>
            CompleteReleaseArchive.Extract(wrongDigest, CancellationToken.None)
        );
        Assert.AreEqual("outer_digest", failure.Code);
    }

    [TestMethod]
    public void CancellationBeforeExtractionDoesNotCreateStage()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            CompleteReleaseArchive.Extract(fixture.Request, cancellation.Token)
        );
        Assert.AreEqual(
            0,
            Directory.EnumerateFileSystemEntries(fixture.Request.StagingRoot).Count()
        );
    }

    [TestMethod]
    public void DescriptorWithDifferentSdkOrUnsafeEntryCannotExtractServer()
    {
        using var forgedSdk = new Fixture(descriptorSdkHash: new string('0', 64));
        var failure = Assert.ThrowsExactly<CacheException>(() =>
            CompleteReleaseArchive.Extract(forgedSdk.Request, CancellationToken.None)
        );
        Assert.AreEqual("outer_descriptor", failure.Code);
        using var unsafePath = new Fixture(extraArchiveEntry: "../escape.txt");
        failure = Assert.ThrowsExactly<CacheException>(() =>
            CompleteReleaseArchive.Extract(unsafePath.Request, CancellationToken.None)
        );
        Assert.AreEqual("archive_path", failure.Code);
        Assert.IsFalse(File.Exists(Path.Combine(unsafePath.Root, "escape.txt")));
    }

    [TestMethod]
    public async Task InstallReleaseFailureCleansExtractedInnerStage()
    {
        using var fixture = new Fixture();
        var dotnetPath = Path.Combine(fixture.Root, "dotnet.exe");
        File.WriteAllText(dotnetPath, "fixture host");
        var outer = fixture.Request;
        var request = new CacheRequest
        {
            SchemaVersion = 1,
            Operation = "install-release",
            CacheRoot = Path.Combine(fixture.Root, "cache"),
            DotnetPath = dotnetPath,
            Expected = outer.ExpectedServer,
            CompleteRelease = outer,
            Anchor = new CallerAnchor
            {
                Kind = "bundled-catalog",
                SourceCommit = outer.SourceCommit,
                DescriptorSha256 = outer.DescriptorSha256,
                RunId = outer.RunId,
                RunAttempt = outer.RunAttempt,
                ArtifactDigest = outer.ArtifactDigest,
            },
        };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            new CacheInstaller(new NeverIdentity()).ExecuteAsync(request, CancellationToken.None)
        );
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(outer.StagingRoot).Count());
        Assert.AreEqual(
            0,
            Directory
                .EnumerateFileSystemEntries(Path.Combine(fixture.Root, "cache", "generations"))
                .Count()
        );
    }

    private sealed class NeverIdentity : IServerIdentityRunner
    {
        public Task<JsonElement> IdentifyAsync(
            string dotnetPath,
            string serverPath,
            CancellationToken cancellationToken
        ) => throw new AssertFailedException("Invalid inner archive must never execute identity.");
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly string Source = new('a', 40);
        private static readonly string SdkHash = new('b', 64);

        public Fixture(string? descriptorSdkHash = null, string? extraArchiveEntry = null)
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "lucent-complete-test-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(Root);
            var staging = Path.Combine(Root, "cache", ".staging");
            Directory.CreateDirectory(staging);
            ServerBytes = Encoding.UTF8.GetBytes("fixture server zip bytes");
            var identity = JsonSerializer.SerializeToElement(new { sourceCommit = Source });
            var approved = new ApprovedServer
            {
                Artifact = new ArtifactBytes
                {
                    Bytes = ServerBytes.Length,
                    Sha256 = Hash(ServerBytes),
                },
                FilesSha256 = new string('c', 64),
                Identity = identity,
            };
            var descriptor = JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    schemaVersion = 1,
                    status = "complete",
                    releaseSet = new
                    {
                        version = "0.3.0-dev.101.1",
                        sourceCommit = Source,
                        sourceState = "clean",
                    },
                    provenance = new
                    {
                        repository = "RichiCoder1/lucent",
                        workflow = ".github/workflows/tests.yml",
                        sourceCommit = Source,
                        runId = 123L,
                        runAttempt = 1L,
                    },
                    packages = new[]
                    {
                        new
                        {
                            id = "Lucent.Lui.Sdk",
                            version = "0.3.0-dev.101.1",
                            repositoryCommit = Source,
                            artifact = new { sha256 = descriptorSdkHash ?? SdkHash },
                        },
                    },
                    server = new
                    {
                        artifact = new
                        {
                            fileName = "server.zip",
                            bytes = ServerBytes.Length,
                            sha256 = Hash(ServerBytes),
                        },
                        identity,
                    },
                }
            );
            var archivePath = Path.Combine(Root, "complete-release.zip");
            using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                Write(zip, "complete.json", descriptor);
                Write(zip, "server.zip", ServerBytes);
                Write(zip, "unrelated.nupkg", Encoding.UTF8.GetBytes("should not extract"));
                if (extraArchiveEntry is not null)
                    Write(zip, extraArchiveEntry, Encoding.UTF8.GetBytes("unsafe"));
            }
            Request = new CompleteReleaseRequest
            {
                ArchivePath = archivePath,
                StagingRoot = staging,
                ArtifactBytes = new FileInfo(archivePath).Length,
                ArtifactDigest = "sha256:" + Hash(File.ReadAllBytes(archivePath)),
                DescriptorSha256 = Hash(descriptor),
                ReleaseVersion = "0.3.0-dev.101.1",
                SourceCommit = Source,
                SdkPackageSha256 = SdkHash,
                RunId = 123,
                RunAttempt = 1,
                ExpectedServer = approved,
            };
        }

        public string Root { get; }
        public byte[] ServerBytes { get; }
        public CompleteReleaseRequest Request { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private static void Write(ZipArchive zip, string name, byte[] bytes)
        {
            using var output = zip.CreateEntry(name).Open();
            output.Write(bytes);
        }

        private static string Hash(byte[] bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
