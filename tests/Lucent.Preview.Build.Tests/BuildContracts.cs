using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using Lucent.Preview.Build;

namespace Lucent.Preview.Build.Tests;

[TestClass]
public sealed class BuildContracts
{
    [TestMethod]
    public async Task RealSdkBuildRechecksSourceImportAssetGlobAndClosureWithoutLaunching()
    {
        var feed = Environment.GetEnvironmentVariable("LUCENT_PREVIEW_BUILD_FEED");
        var version = Environment.GetEnvironmentVariable("LUCENT_PREVIEW_BUILD_VERSION");
        Assert.IsFalse(
            String.IsNullOrWhiteSpace(feed),
            "Set LUCENT_PREVIEW_BUILD_FEED to the explicit CI or local candidate package directory."
        );
        Assert.IsFalse(
            String.IsNullOrWhiteSpace(version),
            "Set LUCENT_PREVIEW_BUILD_VERSION to its exact package version."
        );
        var root = Directory.CreateTempSubdirectory("lucent-preview-build-contract-").FullName;
        var launchMarker = Path.Combine(root, "unexpected-worker-launch.txt");
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "NuGet.Config"),
            $"<configuration><packageSources><clear/><add key=\"lucent\" value=\"{SecurityElement.Escape(feed)}\"/><add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\"/></packageSources><packageSourceMapping><clear/><packageSource key=\"lucent\"><package pattern=\"Lucent.*\"/></packageSource><packageSource key=\"nuget.org\"><package pattern=\"*\"/></packageSource></packageSourceMapping></configuration>"
        );
        var project = Path.Combine(source, "Fixture.csproj");
        var customLock = Path.Combine(root, "controls", "restore.lock.json");
        Directory.CreateDirectory(Path.GetDirectoryName(customLock)!);
        await File.WriteAllTextAsync(customLock, "{\"version\":2,\"dependencies\":{}}");
        await File.WriteAllTextAsync(
            project,
            $"""
            <Project Sdk="Microsoft.NET.Sdk;Lucent.Lui.Sdk/{version}">
              <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows10.0.26100.0</TargetFramework><RuntimeIdentifier>win-x64</RuntimeIdentifier><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RootNamespace>PreviewBuildFixture</RootNamespace><LucentLuiNamedComponents>true</LucentLuiNamedComponents></PropertyGroup>
              <PropertyGroup><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile><NuGetLockFilePath>{SecurityElement.Escape(
                customLock
            )}</NuGetLockFilePath></PropertyGroup>
              <Import Project="extra.props" />
              <ItemGroup><PackageReference Include="Lucent.Core" Version="[{version}]"/><LucentAsset Include="mark.svg" Accessor="Mark" Path="mark.svg" /><ProjectReference Include="../library/Library.csproj" /></ItemGroup>
            </Project>
            """
        );
        var library = Path.Combine(root, "library");
        Directory.CreateDirectory(library);
        await File.WriteAllTextAsync(
            Path.Combine(library, "Library.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile></PropertyGroup></Project>"
        );
        var referencedSource = Path.Combine(library, "Value.cs");
        await File.WriteAllTextAsync(
            referencedSource,
            "public static class Value { public const int Number = 7; }"
        );
        var sharedAssets = Path.Combine(root, "shared-assets");
        Directory.CreateDirectory(sharedAssets);
        await File.WriteAllTextAsync(
            project,
            (await File.ReadAllTextAsync(project)).Replace(
                "</ItemGroup>",
                "<LucentAsset Include=\"../shared-assets/**/*.svg\" Path=\"shared/%(Filename)%(Extension)\" /></ItemGroup>",
                StringComparison.Ordinal
            )
        );
        var imported = Path.Combine(source, "extra.props");
        await File.WriteAllTextAsync(
            imported,
            "<Project><PropertyGroup><DefineConstants>PREVIEW_FIXTURE</DefineConstants></PropertyGroup></Project>"
        );
        var authored = Path.Combine(source, "Card.lui");
        await File.WriteAllTextAsync(
            authored,
            "namespace PreviewBuildFixture; public component Card() { <Text>Real preview build</Text> }"
        );
        var asset = Path.Combine(source, "mark.svg");
        await File.WriteAllTextAsync(
            asset,
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><rect width=\"16\" height=\"16\" fill=\"blue\"/></svg>"
        );
        await File.WriteAllTextAsync(
            Path.Combine(source, "Program.cs"),
            "File.WriteAllText("
                + JsonSerializer.Serialize(launchMarker)
                + ", \"Main ran\"); Console.WriteLine(PreviewBuildFixture.Card.Create());"
        );
        var output = Path.Combine(root, "generation");
        var report = Path.Combine(output, "report.json");
        var requestPath = Path.Combine(root, "request.json");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        await File.WriteAllTextAsync(
            requestPath,
            JsonSerializer.Serialize(
                new PreviewBuildRequest(
                    1,
                    "contract",
                    "first",
                    "build",
                    project,
                    "Release",
                    "net10.0-windows10.0.26100.0",
                    "win-x64",
                    output,
                    []
                ),
                options
            )
        );
        var sourceDirectories = new[] { source, library, Path.GetDirectoryName(customLock)! };
        var sourceSnapshot = SnapshotSources(sourceDirectories);
        var built = await Invoke(root, "build", "--request", requestPath, "--report", report);
        Assert.AreEqual(0, built.Exit, built.Output);
        CollectionAssert.AreEqual(
            sourceSnapshot,
            SnapshotSources(sourceDirectories),
            "Successful preview restore/build must not change any source-tree files or directories."
        );
        Assert.IsFalse(
            File.Exists(Path.Combine(library, "packages.lock.json")),
            "A missing conventional lock must remain absent in the source tree."
        );
        Assert.IsFalse(
            File.Exists(launchMarker),
            "Build must never execute the authored entry point."
        );
        var payload = JsonSerializer.Deserialize<PreviewBuildReport>(
            await File.ReadAllTextAsync(report),
            options
        )!;
        Assert.IsTrue(
            payload.Consumption.Any(item => item.Stage == "preparation"),
            "The actual SDK prepared-emitter path must run."
        );
        Assert.IsTrue(
            payload.Consumption.Any(item =>
                item.Stage == "assets"
                && item.Inputs.Any(input =>
                    input.Path.EndsWith("MARK.SVG", StringComparison.Ordinal)
                )
            ),
            "The authored asset must be captured at consumption."
        );
        Assert.IsTrue(File.Exists(payload.EntryPoint));
        Assert.IsTrue(
            payload.Projects.Any(node =>
                node.Inputs.Any(input =>
                    String.Equals(input.Path, customLock, StringComparison.OrdinalIgnoreCase)
                )
            ),
            "A custom external NuGet lock file must be an exact watched input."
        );
        CollectionAssert.Contains(
            payload.GlobWatchRoots,
            sharedAssets.ToUpperInvariant(),
            "An empty external asset glob needs its actual recursive watch root."
        );
        Assert.IsTrue(
            payload.Projects.Any(node =>
                node.Properties["TargetFramework"] == "net10.0"
                && node.ProjectPath.EndsWith("LIBRARY.CSPROJ", StringComparison.Ordinal)
            ),
            "Reference target selection must preserve its net10.0 framework."
        );
        Assert.AreEqual(0, (await Invoke(root, "verify", "--report", report)).Exit);
        foreach (var path in new[] { authored, imported, asset, referencedSource, customLock })
        {
            var original = await File.ReadAllTextAsync(path);
            await File.AppendAllTextAsync(path, "\n ");
            Assert.AreEqual(
                1,
                (await Invoke(root, "verify", "--report", report)).Exit,
                "Changed authored input must be stale: " + path
            );
            await File.WriteAllTextAsync(path, original);
        }
        var lockBytes = await File.ReadAllBytesAsync(customLock);
        File.Delete(customLock);
        Assert.AreEqual(
            1,
            (await Invoke(root, "verify", "--report", report)).Exit,
            "Custom lock presence is part of freshness."
        );
        await File.WriteAllBytesAsync(customLock, lockBytes);
        var added = Path.Combine(source, "Added.cs");
        await File.WriteAllTextAsync(added, "internal static class Added { }");
        Assert.AreEqual(
            1,
            (await Invoke(root, "verify", "--report", report)).Exit,
            "A new selected glob member must be stale."
        );
        File.Delete(added);
        var addedAsset = Path.Combine(sharedAssets, "added.svg");
        await File.WriteAllTextAsync(addedAsset, await File.ReadAllTextAsync(asset));
        Assert.AreEqual(
            1,
            (await Invoke(root, "verify", "--report", report)).Exit,
            "A new external asset glob member must be stale."
        );
        File.Delete(addedAsset);
        Assert.AreEqual(0, (await Invoke(root, "verify", "--report", report)).Exit);
        var originalProject = await File.ReadAllTextAsync(project);
        foreach (var destination in new[] { "RestoreOutputPath", "OutDir", "TargetPath" })
        {
            var external = Path.Combine(root, "escaped-" + destination);
            var value =
                destination == "TargetPath"
                    ? Path.Combine(external, "escape.dll")
                    : external + Path.DirectorySeparatorChar;
            await File.WriteAllTextAsync(
                project,
                originalProject.Replace(
                    "</Project>",
                    $"<PropertyGroup><{destination}>{SecurityElement.Escape(value)}</{destination}></PropertyGroup></Project>",
                    StringComparison.Ordinal
                )
            );
            var escapedOutput = Path.Combine(root, "rejected-" + destination);
            var escapedReport = Path.Combine(escapedOutput, "report.json");
            await File.WriteAllTextAsync(
                requestPath,
                JsonSerializer.Serialize(
                    payload.Request with
                    {
                        Generation = destination,
                        OutputDirectory = escapedOutput,
                    },
                    options
                )
            );
            var escapedSnapshot = SnapshotSources(sourceDirectories);
            Assert.AreEqual(
                1,
                (
                    await Invoke(root, "build", "--request", requestPath, "--report", escapedReport)
                ).Exit
            );
            CollectionAssert.AreEqual(escapedSnapshot, SnapshotSources(sourceDirectories));
            Assert.IsFalse(
                Directory.Exists(external),
                "Escaping outputs must be rejected before writes."
            );
            Assert.IsFalse(
                File.Exists(Path.Combine(escapedOutput, "restore.stdout.log")),
                "Escaping outputs must be rejected before restore starts."
            );
            Assert.IsFalse(File.Exists(escapedReport));
        }
        await File.WriteAllTextAsync(project, originalProject);
        await File.WriteAllTextAsync(
            project,
            originalProject.Replace(
                "</Project>",
                "<PropertyGroup><RestoreLockedMode>true</RestoreLockedMode></PropertyGroup></Project>",
                StringComparison.Ordinal
            )
        );
        var lockedOutput = Path.Combine(root, "rejected-locked-mode");
        var lockedReport = Path.Combine(lockedOutput, "report.json");
        await File.WriteAllTextAsync(
            requestPath,
            JsonSerializer.Serialize(
                payload.Request with
                {
                    Generation = "locked",
                    OutputDirectory = lockedOutput,
                },
                options
            )
        );
        var lockedSnapshot = SnapshotSources(sourceDirectories);
        Assert.AreEqual(
            1,
            (await Invoke(root, "build", "--request", requestPath, "--report", lockedReport)).Exit
        );
        StringAssert.Contains(
            await File.ReadAllTextAsync(Path.Combine(lockedOutput, "restore.stdout.log")),
            "NU1004"
        );
        CollectionAssert.AreEqual(
            lockedSnapshot,
            SnapshotSources(sourceDirectories),
            "Authored locked-mode rejection must preserve the complete source tree."
        );
        Assert.IsFalse(File.Exists(lockedReport));
        await File.WriteAllTextAsync(project, originalProject);
        var raceOutput = Path.Combine(root, "rejected-lock-selection");
        var raceReport = Path.Combine(raceOutput, "report.json");
        var ready = Path.Combine(root, "restore-ready");
        var release = Path.Combine(root, "restore-release");
        var pauseScript = Path.Combine(root, "pause-restore.ps1");
        await File.WriteAllTextAsync(
            pauseScript,
            "param($Ready,$Release)\n[IO.File]::WriteAllText($Ready,'ready')\n$deadline=[DateTime]::UtcNow.AddSeconds(30)\nwhile (!(Test-Path -LiteralPath $Release)) { if ([DateTime]::UtcNow -ge $deadline) { exit 1 }; Start-Sleep -Milliseconds 25 }\n"
        );
        var command =
            $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{pauseScript}\" -Ready \"{ready}\" -Release \"{release}\"";
        var raceProject = originalProject
            .Replace(
                $"<NuGetLockFilePath>{SecurityElement.Escape(customLock)}</NuGetLockFilePath>",
                "",
                StringComparison.Ordinal
            )
            .Replace(
                "</Project>",
                $"<Target Name=\"PausePreviewRestore\" BeforeTargets=\"_GenerateRestoreGraphProjectEntry\" Condition=\"'$(MSBuildProjectName)' == 'Fixture'\"><Exec Command=\"{SecurityElement.Escape(command)}\" /></Target></Project>",
                StringComparison.Ordinal
            );
        await File.WriteAllTextAsync(project, raceProject);
        var conventionalLock = Path.Combine(source, "packages.lock.json");
        var preferredLock = Path.Combine(source, "packages.Fixture.lock.json");
        await File.WriteAllBytesAsync(conventionalLock, await File.ReadAllBytesAsync(customLock));
        await File.WriteAllTextAsync(
            requestPath,
            JsonSerializer.Serialize(
                payload.Request with
                {
                    Generation = "lock-selection",
                    OutputDirectory = raceOutput,
                },
                options
            )
        );
        var raceTask = Invoke(root, "build", "--request", requestPath, "--report", raceReport);
        string[] raceSnapshot;
        try
        {
            var readyWait = Stopwatch.StartNew();
            while (
                !File.Exists(ready)
                && !raceTask.IsCompleted
                && readyWait.Elapsed < TimeSpan.FromSeconds(30)
            )
                await Task.Delay(25);
            Assert.IsTrue(File.Exists(ready), "The real restore must reach the controlled pause.");
            await File.WriteAllBytesAsync(
                preferredLock,
                await File.ReadAllBytesAsync(conventionalLock)
            );
            raceSnapshot = SnapshotSources(sourceDirectories);
        }
        finally
        {
            await File.WriteAllTextAsync(release, "release");
        }
        var raceResult = await raceTask;
        Assert.AreEqual(
            1,
            raceResult.Exit,
            "A newly preferred lock must not be accepted as a fresh baseline."
        );
        StringAssert.Contains(raceResult.Output, "lock selection changed");
        Assert.IsFalse(File.Exists(raceReport));
        CollectionAssert.AreEqual(
            raceSnapshot,
            SnapshotSources(sourceDirectories),
            "Only the deliberate authored preferred-lock creation may change the source tree."
        );
        Assert.IsFalse(File.Exists(launchMarker));
        File.Delete(preferredLock);
        File.Delete(conventionalLock);
        await File.WriteAllTextAsync(project, originalProject);
        await File.AppendAllTextAsync(payload.EntryPoint, "changed");
        Assert.AreEqual(
            1,
            (await Invoke(root, "verify", "--report", report)).Exit,
            "Executable bytes must be independently verified."
        );
        Assert.IsFalse(File.Exists(launchMarker));
        await File.WriteAllTextAsync(
            authored,
            "namespace PreviewBuildFixture; public component Card() { string Broken() => MissingGeneratedValue; <Text>Real preview build</Text> }"
        );
        var rejectedOutput = Path.Combine(root, "rejected-generation");
        var rejectedReport = Path.Combine(rejectedOutput, "report.json");
        await File.WriteAllTextAsync(
            requestPath,
            JsonSerializer.Serialize(
                payload.Request with
                {
                    Generation = "rejected",
                    OutputDirectory = rejectedOutput,
                },
                options
            )
        );
        var rejectedSnapshot = SnapshotSources(sourceDirectories);
        var rejected = await Invoke(
            root,
            "build",
            "--request",
            requestPath,
            "--report",
            rejectedReport
        );
        Assert.AreEqual(1, rejected.Exit);
        CollectionAssert.AreEqual(
            rejectedSnapshot,
            SnapshotSources(sourceDirectories),
            "Rejected generated code must not change any source-tree files or directories."
        );
        Assert.IsFalse(
            File.Exists(rejectedReport),
            "Failed generated C# compilation must never publish executable build evidence."
        );
        var compilationLog = await File.ReadAllTextAsync(
            Path.Combine(rejectedOutput, "publish.stdout.log")
        );
        StringAssert.Contains(compilationLog, "LUI2000");
        StringAssert.Contains(compilationLog, "Card.lui");
        StringAssert.Contains(compilationLog, "MissingGeneratedValue");
        Assert.IsFalse(File.Exists(launchMarker));
        Console.WriteLine("Retained preview build evidence: " + root);
    }

    private static string[] SnapshotSources(string[] directories) =>
        directories
            .SelectMany(directory =>
                Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories)
            )
            .Select(path =>
                Directory.Exists(path)
                    ? "D:" + path
                    : "F:"
                        + path
                        + ":"
                        + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            )
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static async Task<(int Exit, string Output)> Invoke(
        string evidence,
        params string[] arguments
    )
    {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var start = new ProcessStartInfo(dotnet)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        start.ArgumentList.Add(
            Path.Combine(AppContext.BaseDirectory, "build-tool", "Lucent.Preview.Build.dll")
        );
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(cleanup.Token);
            }
        }
        var output = await stdout + await stderr;
        await File.WriteAllTextAsync(
            Path.Combine(evidence, "invocation-" + Guid.NewGuid().ToString("N") + ".log"),
            output
        );
        return (process.ExitCode, output);
    }
}
