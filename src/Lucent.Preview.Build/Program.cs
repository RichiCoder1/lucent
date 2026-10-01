using System.Text.Json;
using System.Text.RegularExpressions;
using Lucent.Preview.Build;
using Microsoft.Build.Locator;

using var cancellation = new ParentLifetime();
try
{
    if (args.Length is not (3 or 5) || args[0] is not ("build" or "verify"))
        throw new InvalidOperationException(
            "Use build --request <absolute.json> --report <absolute.json>, or verify --report <absolute.json>."
        );
    var command = args[0];
    var reportPath = Value("--report");
    if (!Path.IsPathFullyQualified(reportPath))
        throw new InvalidOperationException("The report path must be absolute.");
    PreviewBuildRequest request;
    PreviewBuildReport? report = null;
    if (command == "build")
        request = Read<PreviewBuildRequest>(Value("--request"));
    else
    {
        report = Read<PreviewBuildReport>(reportPath);
        request = report.Request;
    }
    Validate(request);
    var sdk =
        MSBuildLocator
            .QueryVisualStudioInstances(
                new VisualStudioInstanceQueryOptions
                {
                    DiscoveryTypes = DiscoveryType.DotNetSdk,
                    WorkingDirectory = Path.GetDirectoryName(request.ProjectPath)!,
                }
            )
            .FirstOrDefault()
        ?? throw new InvalidOperationException("The selected preview SDK is unavailable.");
    MSBuildLocator.RegisterInstance(sdk);
    var dotnet = Path.GetFullPath(Path.Combine(sdk.MSBuildPath, "..", "..", "dotnet.exe"));
    if (command == "build")
    {
        if (Directory.Exists(request.OutputDirectory))
            throw new InvalidOperationException("A preview generation directory must be new.");
        Directory.CreateDirectory(request.OutputDirectory);
        BuildData.RequireInside(reportPath, request.OutputDirectory);
        report = await BuildEngine.BuildAsync(request, sdk.MSBuildPath, dotnet, cancellation.Token);
        await BuildData.WriteAsync(reportPath, report, cancellation.Token);
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    protocolVersion = 1,
                    kind = "preview-build",
                    status = "succeeded",
                    reportPath,
                },
                BuildData.Json
            )
        );
    }
    else
    {
        if (report!.SdkPath != sdk.MSBuildPath || report.DotnetPath != dotnet)
            throw new InvalidOperationException(
                "The selected SDK changed after the preview build."
            );
        BuildEngine.Verify(report);
        cancellation.Token.ThrowIfCancellationRequested();
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    protocolVersion = 1,
                    kind = "preview-build-verification",
                    status = "fresh",
                    request.SessionId,
                    request.Generation,
                    request.RequestId,
                    report.ProjectTargetDigest,
                    report.InputDigest,
                    report.ArtifactDigest,
                },
                BuildData.Json
            )
        );
    }
    return 0;
}
catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
{
    Console.Error.WriteLine(error.ToString());
    Console.WriteLine(
        JsonSerializer.Serialize(
            new
            {
                protocolVersion = 1,
                kind = "preview-build-failure",
                status = "unavailable",
                code = error is OperationCanceledException ? "cancelled" : "build-or-freshness",
                message = error.Message[..Math.Min(error.Message.Length, 1024)],
            },
            BuildData.Json
        )
    );
    return 1;
}

string Value(string name)
{
    var index = Array.IndexOf(args, name);
    if (index < 1 || index + 1 >= args.Length)
        throw new InvalidOperationException("A required preview command argument is missing.");
    return args[index + 1];
}

static T Read<T>(string path)
{
    if (!Path.IsPathFullyQualified(path) || new FileInfo(path).Length > 16 * 1024 * 1024)
        throw new InvalidOperationException("The preview request/report is unsupported.");
    return JsonSerializer.Deserialize<T>(File.ReadAllText(path), BuildData.Json)
        ?? throw new InvalidOperationException("The preview request/report is empty.");
}

static void Validate(PreviewBuildRequest request)
{
    const string id = "^[A-Za-z0-9_-]{1,128}$";
    const string token = "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$";
    if (
        request.ProtocolVersion != 1
        || !Regex.IsMatch(request.SessionId, id)
        || !Regex.IsMatch(request.Generation, id)
        || !Regex.IsMatch(request.RequestId, id)
        || !Regex.IsMatch(request.Configuration, token)
        || !Regex.IsMatch(request.TargetFramework, token)
        || request.RuntimeIdentifier != "win-x64"
        || !Path.IsPathFullyQualified(request.ProjectPath)
        || !File.Exists(request.ProjectPath)
        || !request.ProjectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
        || !Path.IsPathFullyQualified(request.OutputDirectory)
        || request.ExtraInputs is null
        || request.ExtraInputs.Length > 128
        || request.ExtraInputs.Any(path => !Path.IsPathFullyQualified(path))
    )
        throw new InvalidOperationException("The preview build request is unsupported.");
    if (!OperatingSystem.IsWindows())
        throw new InvalidOperationException("Preview build supports local Windows only.");
    foreach (
        var path in request.ExtraInputs.Append(request.ProjectPath).Append(request.OutputDirectory)
    )
        if (new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed)
            throw new InvalidOperationException("Preview build requires local fixed-drive paths.");
}
