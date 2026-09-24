using System.Security.Cryptography;
using System.Text.Json;

namespace Lucent.Performance.Tests;

[TestClass]
public sealed class PublishedIdentityTests
{
    [TestMethod]
    public void MatchingManifestBindsConfigurationAndSourceToBinary()
    {
        using var fixture = new IdentityFixture();
        fixture.WriteManifest(schemaVersion: 1, hash: fixture.Hash);
        var identity = PublishedIdentity.Read(fixture.App);
        Assert.AreEqual("verified", identity.Status);
        Assert.AreEqual("Release", identity.Configuration);
        Assert.AreEqual("NativeAOT", identity.Execution);
        Assert.AreEqual("test-revision", identity.SourceRevision);
        Assert.AreEqual(false, identity.SourceDirty);
    }

    [TestMethod]
    public void MismatchedHashAndUnknownSchemaCannotClaimBuildIdentity()
    {
        using var fixture = new IdentityFixture();
        fixture.WriteManifest(schemaVersion: 1, hash: new string('0', 64));
        var mismatch = PublishedIdentity.Read(fixture.App);
        Assert.AreEqual("invalid", mismatch.Status);
        Assert.IsNull(mismatch.Configuration);
        Assert.IsTrue(mismatch.Error!.Contains("hash", StringComparison.Ordinal));
        fixture.WriteManifest(schemaVersion: 2, hash: fixture.Hash);
        var unsupported = PublishedIdentity.Read(fixture.App);
        Assert.AreEqual("invalid", unsupported.Status);
        Assert.IsTrue(unsupported.Error!.Contains("schema", StringComparison.Ordinal));
    }

    private sealed class IdentityFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "lucent-identity-test-" + Guid.NewGuid().ToString("N")
        );
        internal string App => Path.Combine(_directory, "fixture.exe");
        internal string Hash => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(App)));

        internal IdentityFixture()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(App, [1, 2, 3, 4]);
        }

        internal void WriteManifest(int schemaVersion, string hash)
        {
            using var stream = File.Create(App + ".benchmark.json");
            using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", schemaVersion);
            writer.WriteString("executable", "fixture.exe");
            writer.WriteString("appSha256", hash);
            writer.WriteString("configuration", "Release");
            writer.WriteString("execution", "NativeAOT");
            writer.WriteString("targetFramework", "net10.0-windows10.0.26100.0");
            writer.WriteString("runtimeIdentifier", "win-x64");
            writer.WriteString("sdk", "10.0.401");
            writer.WriteString("sourceRevision", "test-revision");
            writer.WriteBoolean("sourceDirty", false);
            writer.WriteString("publishedUtc", "2026-09-24T00:00:00Z");
            writer.WriteStartArray("binaries");
            writer.WriteStartObject();
            writer.WriteString("name", "fixture.exe");
            writer.WriteString("sha256", Hash);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
