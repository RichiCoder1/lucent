using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lucent.Lui.LanguageServer.Tests;

[TestClass]
public sealed class ServerIdentityContracts
{
    [TestMethod]
    public async Task IdentityRunsWithoutProjectEvaluationOrMSBuildDeployment()
    {
        var root = Path.GetFullPath("../../../../..", AppContext.BaseDirectory);
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var built = Path.Combine(
            root,
            "src",
            "Lucent.Lui.LanguageServer",
            "bin",
            configuration,
            "net10.0"
        );
        var published = Environment.GetEnvironmentVariable("LUCENT_LSP_SERVER_DLL");
        if (!string.IsNullOrWhiteSpace(published))
        {
            Assert.IsTrue(File.Exists(published), "The configured published server is missing.");
            built = Path.GetDirectoryName(Path.GetFullPath(published))!;
        }
        var directory = Path.Combine(
            root,
            "artifacts",
            "server-identity-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        foreach (
            var name in new[]
            {
                "Lucent.Lui.LanguageServer.dll",
                "Lucent.Lui.LanguageServer.runtimeconfig.json",
                "Lucent.Lui.Compiler.dll",
            }
        )
            File.Copy(Path.Combine(built, name), Path.Combine(directory, name));
        // The isolated deployment intentionally has no MSBuild, Roslyn or deps file.
        await File.WriteAllTextAsync(
            Path.Combine(directory, "Poison.csproj"),
            "<Project Sdk=\"Unavailable.Sdk/99.0.0\"><Import Project=\"must-not-evaluate.targets\" /></Project>"
        );
        var before = Directory.GetFiles(directory).Order(StringComparer.Ordinal).ToArray();
        var info = await Run("--identity");
        Assert.AreEqual(0, info.ExitCode, info.Error);
        Assert.AreEqual("", info.Error);
        using var identity = JsonDocument.Parse(info.Output);
        var value = identity.RootElement;
        Assert.AreEqual(1, value.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual(
            "preview",
            value.GetProperty("language").GetProperty("version").GetString()
        );
        Assert.AreEqual("lucent-lui", value.GetProperty("protocol").GetProperty("id").GetString());
        Assert.AreEqual("net10.0", value.GetProperty("runtime").GetProperty("tfm").GetString());
        Assert.AreEqual(40, value.GetProperty("sourceCommit").GetString()!.Length);
        foreach (
            var (property, file) in new[]
            {
                ("server", "Lucent.Lui.LanguageServer.dll"),
                ("compiler", "Lucent.Lui.Compiler.dll"),
            }
        )
            Assert.AreEqual(
                Convert
                    .ToHexString(
                        SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(directory, file)))
                    )
                    .ToLowerInvariant(),
                value.GetProperty(property).GetProperty("sha256").GetString()
            );
        var rejected = await Run("--identity", "Poison.csproj");
        Assert.AreEqual(2, rejected.ExitCode);
        Assert.AreEqual("", rejected.Output);
        StringAssert.Contains(rejected.Error, "Usage:");
        CollectionAssert.AreEqual(
            before,
            Directory.GetFiles(directory).Order(StringComparer.Ordinal).ToArray()
        );

        async Task<(int ExitCode, string Output, string Error)> Run(params string[] arguments)
        {
            var dotnet = Path.Combine(root, ".dotnet", "dotnet.exe");
            var start = new ProcessStartInfo(File.Exists(dotnet) ? dotnet : "dotnet")
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add(Path.Combine(directory, "Lucent.Lui.LanguageServer.dll"));
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);
            using var process =
                Process.Start(start)
                ?? throw new InvalidOperationException("Cannot start identity probe.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                return (process.ExitCode, await output, await error);
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
        }
    }
}
