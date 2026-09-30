using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lucent.Lui.LanguageServer.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ProjectRequirementsContracts
{
    [TestMethod]
    public async Task TrustedRequirementsTrackExternalImportAndRejectStaleCentralPin()
    {
        var root = Path.GetFullPath("../../../../..", AppContext.BaseDirectory);
        var fixture = Path.Combine(
            Path.GetTempPath(),
            "lucent-project-requirements-tests",
            Guid.NewGuid().ToString("N")
        );
        var application = Path.Combine(fixture, "Application");
        var shared = Path.Combine(fixture, "Shared");
        Directory.CreateDirectory(application);
        Directory.CreateDirectory(shared);
        var project = Path.Combine(application, "Application.csproj");
        var imported = Path.Combine(shared, "Custom.props");
        var customLock = Path.Combine(shared, "custom.lock.json");
        var raceArm = Path.Combine(shared, "race-arm.txt");
        var raceCount = Path.Combine(shared, "race-count.txt");
        var raceStarted = Path.Combine(shared, "race-started.txt");
        var raceRelease = Path.Combine(shared, "race-release.txt");
        var racePid = Path.Combine(shared, "race-pid.txt");
        var central = Path.Combine(fixture, "Directory.Packages.props");
        var compilerProject = Path.Combine(
            root,
            "src",
            "Lucent.Lui.Compiler",
            "Lucent.Lui.Compiler.csproj"
        );
        await File.WriteAllTextAsync(imported, ImportedProperties("FIRST", "win-x64"));
        await File.WriteAllTextAsync(central, Central("17.14.28"));
        await File.WriteAllTextAsync(
            Path.Combine(fixture, "NuGet.Config"),
            """
            <configuration><packageSources><clear />
              <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
            </packageSources></configuration>
            """
        );
        var waitCommand =
            $"powershell -NoProfile -NonInteractive -Command &quot;[System.IO.File]::WriteAllText('{racePid}', [string]$PID); while (-not [System.IO.File]::Exists('{raceRelease}')) {{ Start-Sleep -Milliseconds 20 }}&quot;";
        await File.WriteAllTextAsync(
            project,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <Import Project="../Shared/Custom.props" />
              <PropertyGroup>
                <SelfContained>false</SelfContained>
                <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
                <NuGetAudit>false</NuGetAudit>
                <NuGetLockFilePath>../Shared/custom.lock.json</NuGetLockFilePath>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.Build" />
                <ProjectReference Include="{compilerProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
              <Target Name="RequirementsRaceGate" BeforeTargets="GenerateRestoreGraphFile" Condition="Exists('{raceArm}')">
                <PropertyGroup><_RequirementsSecondGraph Condition="Exists('{raceCount}')">true</_RequirementsSecondGraph></PropertyGroup>
                <WriteLinesToFile File="{raceCount}" Lines="first" Overwrite="true" Condition="'$(_RequirementsSecondGraph)' != 'true'" />
                <WriteLinesToFile File="{raceStarted}" Lines="ready" Overwrite="true" Condition="'$(_RequirementsSecondGraph)' == 'true'" />
                <Exec Command="{waitCommand}" Condition="'$(_RequirementsSecondGraph)' == 'true'" />
              </Target>
            </Project>
            """
        );

        var restore = await Run(root, fixture, "restore", project, "--ignore-failed-sources");
        Assert.AreEqual(0, restore.ExitCode, restore.Error);
        Assert.IsTrue(
            File.Exists(customLock),
            "NuGet did not create the project-relative custom lock file."
        );
        var first = await Requirements(root, fixture, project);
        Assert.AreEqual(0, first.ExitCode, first.Error + first.Output);
        using var firstJson = JsonDocument.Parse(first.Output);
        Assert.AreEqual(
            "development-source",
            firstJson.RootElement.GetProperty("state").GetString()
        );
        AssertTarget(firstJson.RootElement, project, "win-x64");
        var firstHash = InputHash(firstJson.RootElement, imported);
        Assert.AreEqual(64, firstHash.Length);
        Assert.AreEqual(
            Convert
                .ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(customLock)))
                .ToLowerInvariant(),
            InputHash(firstJson.RootElement, customLock)
        );

        var originalLock = await File.ReadAllTextAsync(customLock);
        const string marker = "\"contentHash\": \"";
        var hashStart = originalLock.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        Assert.IsTrue(hashStart >= marker.Length, "The NuGet lock has no package content hash.");
        var wrongFirstCharacter = originalLock[hashStart] == 'A' ? 'B' : 'A';
        await File.WriteAllTextAsync(
            customLock,
            originalLock[..hashStart] + wrongFirstCharacter + originalLock[(hashStart + 1)..]
        );
        var wrongLock = await Requirements(root, fixture, project);
        Assert.AreEqual(3, wrongLock.ExitCode, wrongLock.Error + wrongLock.Output);
        using (var wrongLockJson = JsonDocument.Parse(wrongLock.Output))
            Assert.AreEqual(
                "stale-restore",
                wrongLockJson.RootElement.GetProperty("error").GetProperty("code").GetString()
            );
        await File.WriteAllTextAsync(customLock, originalLock);
        var savedLock = customLock + ".saved";
        File.Move(customLock, savedLock);
        try
        {
            var missingLock = await Requirements(root, fixture, project);
            Assert.AreEqual(3, missingLock.ExitCode, missingLock.Error + missingLock.Output);
            using var missingLockJson = JsonDocument.Parse(missingLock.Output);
            Assert.AreEqual(
                "restore-unavailable",
                missingLockJson.RootElement.GetProperty("error").GetProperty("code").GetString()
            );
        }
        finally
        {
            File.Move(savedLock, customLock);
        }

        await File.WriteAllTextAsync(imported, ImportedProperties("OTHER", "win-x64"));
        var second = await Requirements(root, fixture, project);
        Assert.AreEqual(0, second.ExitCode, second.Error + second.Output);
        using var secondJson = JsonDocument.Parse(second.Output);
        AssertTarget(secondJson.RootElement, project, "win-x64");
        Assert.AreNotEqual(firstHash, InputHash(secondJson.RootElement, imported));

        await File.WriteAllTextAsync(imported, ImportedProperties("OTHER", null));
        var staleTarget = await Requirements(root, fixture, project);
        Assert.AreEqual(3, staleTarget.ExitCode, staleTarget.Error + staleTarget.Output);
        using (var staleTargetJson = JsonDocument.Parse(staleTarget.Output))
            Assert.AreEqual(
                "stale-restore",
                staleTargetJson.RootElement.GetProperty("error").GetProperty("code").GetString()
            );
        var targetRestore = await Run(root, fixture, "restore", project, "--ignore-failed-sources");
        Assert.AreEqual(0, targetRestore.ExitCode, targetRestore.Error);
        var noRuntime = await Requirements(root, fixture, project);
        Assert.AreEqual(0, noRuntime.ExitCode, noRuntime.Error + noRuntime.Output);
        using (var noRuntimeJson = JsonDocument.Parse(noRuntime.Output))
            AssertTarget(noRuntimeJson.RootElement, project, null);

        // A property can change between restore-graph and direct evaluation. Invalid
        // identities must be rejected before they become authoritative report data.
        await File.WriteAllTextAsync(
            imported,
            ImportedProperties("OTHER", null)
                .Replace(
                    "<RuntimeIdentifier></RuntimeIdentifier>",
                    "<RuntimeIdentifier Condition=\"'$(RestoreGraphOutputPath)' == ''\">win x64</RuntimeIdentifier>",
                    StringComparison.Ordinal
                )
        );
        var malformedTarget = await Requirements(root, fixture, project);
        Assert.AreEqual(
            3,
            malformedTarget.ExitCode,
            malformedTarget.Error + malformedTarget.Output
        );
        using (var malformedTargetJson = JsonDocument.Parse(malformedTarget.Output))
            Assert.AreEqual(
                "project-unsupported",
                malformedTargetJson.RootElement.GetProperty("error").GetProperty("code").GetString()
            );
        await File.WriteAllTextAsync(imported, ImportedProperties("OTHER", null));

        await File.WriteAllTextAsync(central, Central("17.14.29"));
        var stale = await Requirements(root, fixture, project);
        Assert.AreEqual(3, stale.ExitCode, stale.Error + stale.Output);
        using var staleJson = JsonDocument.Parse(stale.Output);
        Assert.AreEqual("unavailable", staleJson.RootElement.GetProperty("state").GetString());
        Assert.AreEqual(
            "stale-restore",
            staleJson.RootElement.GetProperty("error").GetProperty("code").GetString()
        );

        await File.WriteAllTextAsync(central, Central("17.14.28"));
        await File.WriteAllTextAsync(raceArm, "armed");
        using (var race = StartRequirements(root, fixture, project, cancelOnStdin: false))
        {
            try
            {
                await WaitForFile(raceStarted, race);
                await File.WriteAllTextAsync(imported, ImportedProperties("THIRD", "win-arm64"));
                await File.WriteAllTextAsync(raceRelease, "release");
                var result = await Finish(race);
                Assert.AreEqual(3, result.ExitCode, result.Error + result.Output);
                using var resultJson = JsonDocument.Parse(result.Output);
                Assert.AreEqual(
                    "project-changed",
                    resultJson.RootElement.GetProperty("error").GetProperty("code").GetString()
                );
            }
            finally
            {
                await File.WriteAllTextAsync(raceRelease, "release");
                if (!race.HasExited)
                    race.Kill(entireProcessTree: true);
            }
        }

        File.Delete(raceRelease);
        File.Delete(raceStarted);
        File.Delete(racePid);
        await File.WriteAllTextAsync(imported, ImportedProperties("OTHER", null));
        using (var cancelled = StartRequirements(root, fixture, project, cancelOnStdin: true))
        {
            try
            {
                await WaitForFile(racePid, cancelled);
                var childPid = int.Parse(
                    await File.ReadAllTextAsync(racePid),
                    System.Globalization.CultureInfo.InvariantCulture
                );
                await cancelled.StandardInput.WriteLineAsync("cancel");
                cancelled.StandardInput.Close();
                var result = await Finish(cancelled);
                Assert.AreEqual(3, result.ExitCode, result.Error + result.Output);
                using var resultJson = JsonDocument.Parse(result.Output);
                Assert.AreEqual(
                    "cancelled",
                    resultJson.RootElement.GetProperty("error").GetProperty("code").GetString()
                );
                Assert.IsFalse(
                    IsAlive(childPid),
                    "Cancellation left the graph's PowerShell child running."
                );
            }
            finally
            {
                await File.WriteAllTextAsync(raceRelease, "release");
                if (!cancelled.HasExited)
                    cancelled.Kill(entireProcessTree: true);
            }
        }
    }

    private static string ImportedProperties(string constant, string? runtimeIdentifier) =>
        $"""
            <Project><PropertyGroup>
              <TargetFramework>net10.0</TargetFramework>
              <DefineConstants>{constant}</DefineConstants>
              <RuntimeIdentifier>{runtimeIdentifier}</RuntimeIdentifier>
              <RuntimeIdentifiers>{(
                runtimeIdentifier is null ? "win-x64;win-arm64" : ""
            )}</RuntimeIdentifiers>
            </PropertyGroup></Project>
            """;

    private static void AssertTarget(JsonElement root, string project, string? runtimeIdentifier)
    {
        var target = root.GetProperty("projects")
            .EnumerateArray()
            .Single(item => item.GetProperty("projectPath").GetString() == project)
            .GetProperty("target");
        Assert.AreEqual("net10.0", target.GetProperty("framework").GetString());
        Assert.AreEqual(runtimeIdentifier, target.GetProperty("runtimeIdentifier").GetString());
        Assert.IsFalse(root.GetProperty("semanticReady").GetBoolean());
    }

    private static string Central(string version) =>
        $"""
            <Project>
              <PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>
              <ItemGroup><PackageVersion Include="Microsoft.Build" Version="{version}" /></ItemGroup>
            </Project>
            """;

    private static string InputHash(JsonElement root, string path) =>
        root.GetProperty("inputs")
            .EnumerateArray()
            .Single(input =>
                input
                    .GetProperty("path")
                    .GetString()!
                    .Equals(path, StringComparison.OrdinalIgnoreCase)
            )
            .GetProperty("sha256")
            .GetString()!;

    private static Task<(int ExitCode, string Output, string Error)> Requirements(
        string root,
        string directory,
        string project
    )
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var server = Path.Combine(
            root,
            "src",
            "Lucent.Lui.LanguageServer",
            "bin",
            configuration,
            "net10.0",
            "Lucent.Lui.LanguageServer.dll"
        );
        return Run(root, directory, server, "--project-requirements", "--trusted-project", project);
    }

    private static Process StartRequirements(
        string root,
        string directory,
        string project,
        bool cancelOnStdin
    )
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var server = Path.Combine(
            root,
            "src",
            "Lucent.Lui.LanguageServer",
            "bin",
            configuration,
            "net10.0",
            "Lucent.Lui.LanguageServer.dll"
        );
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = cancelOnStdin,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["DOTNET_HOST_PATH"] = "dotnet";
        if (cancelOnStdin)
            start.Environment["LUCENT_REQUIREMENTS_CANCEL_STDIN"] = "1";
        foreach (
            var argument in new[] { server, "--project-requirements", "--trusted-project", project }
        )
            start.ArgumentList.Add(argument);
        return Process.Start(start)
            ?? throw new InvalidOperationException("Cannot start requirements race probe.");
    }

    private static async Task WaitForFile(string path, Process process)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        while (!File.Exists(path))
        {
            Assert.IsFalse(
                process.HasExited,
                "The requirements probe exited before the graph gate opened."
            );
            await Task.Delay(25, timeout.Token);
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)> Finish(Process process)
    {
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await output, await error);
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)> Run(
        string root,
        string directory,
        params string[] arguments
    )
    {
        const string dotnet = "dotnet";
        var start = new ProcessStartInfo(dotnet)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["DOTNET_HOST_PATH"] = dotnet;
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process =
            Process.Start(start)
            ?? throw new InvalidOperationException(
                "Cannot start trusted project requirements probe."
            );
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
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
