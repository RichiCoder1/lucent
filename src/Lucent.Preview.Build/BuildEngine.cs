using System.Diagnostics;
using System.Text.Json;

namespace Lucent.Preview.Build;

internal static class BuildEngine
{
    internal static async Task<PreviewBuildReport> BuildAsync(
        PreviewBuildRequest request,
        string sdkPath,
        string dotnetPath,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = EvaluatedInputs.Capture(request, isolated: false);
        var globals = EvaluatedInputs.Globals(request, isolated: true);
        _ = EvaluatedInputs.Capture(request, isolated: true);
        var restoreGraph = EvaluatedInputs.Capture(request, isolated: true, restoreContext: true);
        var lockSelection = LockSelectionDigest(restoreGraph);
        var originalLocks = StageLocks(request, restoreGraph);
        cancellationToken.ThrowIfCancellationRequested();
        await InvokeAsync("restore", request, dotnetPath, globals, cancellationToken);
        foreach (var originalLock in originalLocks)
            if (BuildData.Snapshot(originalLock.Path).Sha256 != originalLock.Sha256)
                throw new InvalidOperationException(
                    "An authored NuGet lock changed during preview restore."
                );
        var restoredGraph = EvaluatedInputs.Capture(request, isolated: true, restoreContext: true);
        if (LockSelectionDigest(restoredGraph) != lockSelection)
            throw new InvalidOperationException(
                "The authored NuGet lock selection changed during preview restore."
            );
        var before = EvaluatedInputs.Capture(request, isolated: true);
        var root = before.Single(project =>
            project.ProjectPath == BuildData.CanonicalPath(request.ProjectPath)
        );
        if (root.Properties["OutputType"] is not ("Exe" or "WinExe"))
            throw new InvalidOperationException(
                "Preview requires an explicit development executable."
            );
        await InvokeAsync("publish", request, dotnetPath, globals, cancellationToken);
        var after = EvaluatedInputs.Capture(request, isolated: true);
        cancellationToken.ThrowIfCancellationRequested();
        if (BuildData.Digest(before) != BuildData.Digest(after))
            throw new InvalidOperationException(
                "Evaluated preview inputs or membership changed during the build."
            );
        var consumption = BuildData
            .EnumerateOwnedFiles(Path.Combine(request.OutputDirectory, "artifacts"), 32768)
            .Where(path =>
                Path.GetExtension(path) == ".json"
                && Path.GetFileName(Path.GetDirectoryName(path)) == "preview-consumption"
            )
            .Order(StringComparer.Ordinal)
            .Select(path =>
                JsonSerializer.Deserialize<ConsumedSnapshot>(File.ReadAllText(path), BuildData.Json)
                ?? throw new InvalidOperationException("A preview consumption witness is empty.")
            )
            .ToArray();
        foreach (var project in after)
            if (
                !consumption.Any(snapshot =>
                    snapshot.ProjectPath == project.ProjectPath && snapshot.Stage == "compile"
                )
            )
                throw new InvalidOperationException(
                    "A preview graph project lacks a compiler consumption witness."
                );
        CheckConsumption(consumption);
        var publishDirectory = Path.GetFullPath(
            root.Properties["PublishDir"],
            Path.GetDirectoryName(request.ProjectPath)!
        );
        BuildData.RequireInside(publishDirectory, request.OutputDirectory);
        var artifacts = ArtifactClosure(publishDirectory);
        var publish = consumption.Single(snapshot =>
            snapshot.ProjectPath == root.ProjectPath && snapshot.Stage == "publish"
        );
        var declared = publish
            .Items.Select(item =>
                item.Metadata.TryGetValue("RelativePath", out var relative)
                    ? relative.Replace('\\', '/')
                    : throw new InvalidOperationException("A publish input has no relative path.")
            )
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (
            !declared.SequenceEqual(artifacts.Select(file => file.FileName), StringComparer.Ordinal)
        )
            throw new InvalidOperationException(
                "The published preview closure differs from SDK-declared files."
            );
        var entryPoint = Path.Combine(publishDirectory, root.Properties["AssemblyName"] + ".exe");
        if (!File.Exists(entryPoint))
            throw new InvalidOperationException("The managed preview apphost is missing.");
        var globRoots = after
            .SelectMany(project => project.Globs)
            .Select(glob => glob.WatchRoot)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (globRoots.Length > 512)
            throw new InvalidOperationException(
                "Preview recursive glob watch roots exceed their supported bound."
            );
        return new(
            1,
            "preview-build-report",
            "succeeded",
            request,
            sdkPath,
            dotnetPath,
            root.NodeDigest,
            BuildData.Digest(new { projects = after, consumption }),
            BuildData.Digest(artifacts),
            entryPoint,
            after
                .SelectMany(project => project.Inputs)
                .Select(input => Path.GetDirectoryName(input.Path)!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            globRoots,
            after,
            consumption,
            artifacts
        );
    }

    private static string LockSelectionDigest(ProjectSnapshot[] projects) =>
        BuildData.Digest(
            projects
                .OrderBy(project => project.NodeDigest, StringComparer.Ordinal)
                .Select(project =>
                {
                    var directory = Path.GetDirectoryName(project.ProjectPath)!;
                    var conventional = BuildData.CanonicalPath(
                        Path.Combine(directory, "packages.lock.json")
                    );
                    var preferred = BuildData.CanonicalPath(
                        Path.Combine(
                            directory,
                            "packages."
                                + Path.GetFileNameWithoutExtension(project.ProjectPath)
                                    .Replace(' ', '_')
                                + ".lock.json"
                        )
                    );
                    return new
                    {
                        project.NodeDigest,
                        original = project.Properties["LucentPreviewOriginalNuGetLockFilePath"],
                        enabled = project.Properties["RestorePackagesWithLockFile"],
                        locked = project.Properties["RestoreLockedMode"],
                        candidates = project
                            .Inputs.Where(input =>
                                input.Path == conventional || input.Path == preferred
                            )
                            .ToArray(),
                    };
                })
                .ToArray()
        );

    private static FileInput[] StageLocks(PreviewBuildRequest request, ProjectSnapshot[] projects)
    {
        var originals = new Dictionary<string, FileInput>(StringComparer.Ordinal);
        var destinations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var project in projects)
        {
            var original = BuildData.CanonicalPath(
                project.Properties["LucentPreviewOriginalNuGetLockFilePath"]
            );
            var destination = BuildData.CanonicalPath(
                Path.GetFullPath(
                    project.Properties["NuGetLockFilePath"],
                    Path.GetDirectoryName(project.ProjectPath)!
                )
            );
            BuildData.RequireInside(destination, request.OutputDirectory);
            if (destinations.TryGetValue(destination, out var previous))
            {
                if (previous != original)
                    throw new InvalidOperationException("Preview lock destinations are ambiguous.");
                continue;
            }
            destinations.Add(destination, original);
            var snapshot = project.Inputs.Single(input => input.Path == original);
            originals[original] = snapshot;
            if (snapshot.Sha256 is null)
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(original, destination, overwrite: false);
            if (BuildData.Snapshot(destination).Sha256 != snapshot.Sha256)
                throw new InvalidOperationException(
                    "An authored NuGet lock changed while staging."
                );
        }
        return originals.Values.ToArray();
    }

    internal static void Verify(PreviewBuildReport report)
    {
        if (
            report.ProtocolVersion != 1
            || report.Kind != "preview-build-report"
            || report.Status != "succeeded"
            || report.Projects.Length is 0 or > 128
            || report.Consumption.Length > 512
        )
            throw new InvalidOperationException("The preview build report is unsupported.");
        var evaluated = EvaluatedInputs.Capture(report.Request, isolated: true);
        CheckConsumption(report.Consumption);
        if (
            BuildData.Digest(new { projects = evaluated, consumption = report.Consumption })
            != report.InputDigest
        )
            throw new InvalidOperationException(
                "Preview inputs or evaluated membership are stale."
            );
        var root = evaluated.Single(project =>
            project.ProjectPath == BuildData.CanonicalPath(report.Request.ProjectPath)
        );
        if (root.NodeDigest != report.ProjectTargetDigest)
            throw new InvalidOperationException("Preview project/target identity is stale.");
        BuildData.RequireInside(report.EntryPoint, report.Request.OutputDirectory);
        var files = ArtifactClosure(Path.GetDirectoryName(report.EntryPoint)!);
        if (
            BuildData.Digest(files) != report.ArtifactDigest
            || BuildData.Digest(report.Artifacts) != report.ArtifactDigest
        )
            throw new InvalidOperationException("The preview executable closure is stale.");
    }

    private static void CheckConsumption(ConsumedSnapshot[] snapshots)
    {
        if (snapshots.Sum(snapshot => snapshot.Inputs.Length) > 32768)
            throw new InvalidOperationException(
                "Consumed preview input witnesses exceed their bound."
            );
        foreach (var input in snapshots.SelectMany(snapshot => snapshot.Inputs))
            if (input.Sha256 is null || BuildData.Snapshot(input.Path) != input)
                throw new InvalidOperationException(
                    "A consumed preview input changed after consumption."
                );
    }

    private static ArtifactFile[] ArtifactClosure(string directory)
    {
        var paths = BuildData.EnumerateOwnedFiles(directory, 4096);
        if (paths.Length is 0 or > 4096)
            throw new InvalidOperationException(
                "The preview runtime closure exceeds its supported bound."
            );
        return paths
            .Select(path =>
            {
                BuildData.RequireInside(path, directory);
                var input = BuildData.Snapshot(path);
                return new ArtifactFile(
                    Path.GetRelativePath(directory, path).Replace('\\', '/'),
                    new FileInfo(path).Length,
                    input.Sha256!
                );
            })
            .OrderBy(file => file.FileName, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task InvokeAsync(
        string command,
        PreviewBuildRequest request,
        string dotnetPath,
        Dictionary<string, string> globals,
        CancellationToken cancellationToken
    )
    {
        var start = new ProcessStartInfo(dotnetPath)
        {
            WorkingDirectory = Path.GetDirectoryName(request.ProjectPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.ArgumentList.Add(command);
        start.ArgumentList.Add(request.ProjectPath);
        if (command == "publish")
        {
            start.ArgumentList.Add("--disable-build-servers");
            start.ArgumentList.Add("--no-restore");
        }
        foreach (var property in globals)
        {
            // Restore computes the authored graph's frameworks. A root framework global
            // would force that framework onto every dependency before SDK selection.
            if (command == "restore" && property.Key == "TargetFramework")
                continue;
            start.ArgumentList.Add(
                "-p:"
                    + property.Key
                    + "="
                    + Microsoft.Build.Evaluation.ProjectCollection.Escape(property.Value)
            );
        }
        start.ArgumentList.Add("-nr:false");
        using var process =
            Process.Start(start)
            ?? throw new InvalidOperationException("Preview SDK process did not start.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        var stdout = DrainAsync(
            process.StandardOutput,
            Path.Combine(request.OutputDirectory, command + ".stdout.log"),
            deadline.Token
        );
        var stderr = DrainAsync(
            process.StandardError,
            Path.Combine(request.OutputDirectory, command + ".stderr.log"),
            deadline.Token
        );
        try
        {
            var exit = process.WaitForExitAsync(deadline.Token);
            var pending = new List<Task> { exit, stdout, stderr };
            while (!exit.IsCompleted)
            {
                var completed = await Task.WhenAny(pending);
                await completed;
                pending.Remove(completed);
            }
            await exit;
            await Task.WhenAll(stdout, stderr);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    "The real SDK " + command + " failed; see the retained build log."
                );
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
    }

    private static async Task DrainAsync(
        StreamReader reader,
        string path,
        CancellationToken cancellationToken
    )
    {
        await using var writer = File.Create(path);
        var buffer = new char[4096];
        long total = 0;
        while (true)
        {
            var count = await reader.ReadAsync(buffer, cancellationToken);
            if (count == 0)
                return;
            total += count;
            if (total > 32 * 1024 * 1024)
                throw new InvalidOperationException(
                    "Preview SDK output exceeded its bounded log size."
                );
            var bytes = System.Text.Encoding.UTF8.GetBytes(buffer.AsSpan(0, count).ToArray());
            await writer.WriteAsync(bytes, cancellationToken);
        }
    }
}
