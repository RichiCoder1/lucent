using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lucent.Lui.Compiler;

namespace Lucent.Lui.LanguageServer;

internal static partial class LuiServerIdentity
{
    internal static int Write()
    {
        try
        {
            var server = typeof(LuiServerIdentity).Assembly;
            var compiler = typeof(LuiCompiler).Assembly;
            var serverVersion = Version(server);
            var commit = CommitPattern().Match(serverVersion);
            if (!commit.Success)
                throw new InvalidOperationException("The server has no source commit identity.");
            using var policyStream =
                server.GetManifestResourceStream("Lucent.Lui.ReleasePolicy.json")
                ?? throw new InvalidOperationException("The server has no release policy.");
            using var policy = JsonDocument.Parse(policyStream);
            using var runtime = JsonDocument.Parse(
                File.ReadAllText(Path.ChangeExtension(server.Location, ".runtimeconfig.json"))
            );
            Console.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = 1,
                        sourceCommit = commit.Groups[1].Value.ToLowerInvariant(),
                        server = Identity(server),
                        compiler = Identity(compiler),
                        language = policy.RootElement.GetProperty("language"),
                        protocol = policy.RootElement.GetProperty("protocol"),
                        runtime = runtime.RootElement.GetProperty("runtimeOptions"),
                    }
                )
            );
            return 0;
        }
        catch (Exception exception)
            when (exception is IOException or InvalidOperationException or JsonException)
        {
            Console.Error.WriteLine("Cannot describe the language server: " + exception.Message);
            return 2;
        }
    }

    private static object Identity(Assembly assembly) =>
        new
        {
            informationalVersion = Version(assembly),
            sha256 = Convert
                .ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location)))
                .ToLowerInvariant(),
        };

    private static string Version(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? throw new InvalidOperationException(
            "The tooling assembly has no informational version."
        );

    [GeneratedRegex(@"\+([0-9a-fA-F]{40})(?:\.|$)")]
    private static partial Regex CommitPattern();
}
