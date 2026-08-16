using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Lucent.LanguageServer;

internal static partial class LanguageServerLog
{
    private static readonly ILoggerFactory Factory = LoggerFactory.Create(builder =>
    {
        var level = Environment.GetEnvironmentVariable("LUCENT_LANGUAGE_SERVER_TRACE") switch
        {
            "off" => LogLevel.None,
            "verbose" => LogLevel.Debug,
            _ => LogLevel.Information,
        };
        builder.SetMinimumLevel(level);
        builder.AddJsonConsole(options =>
        {
            options.TimestampFormat = "O";
            options.UseUtcTimestamp = true;
        });
        builder.Services.Configure<ConsoleLoggerOptions>(options =>
            options.LogToStandardErrorThreshold = LogLevel.Trace);
    });

    public static ILogger Logger { get; } = Factory.CreateLogger("Lucent.LanguageServer");

    [LoggerMessage(1, LogLevel.Information, "Workspace configured with roots {Roots}")]
    public static partial void WorkspaceConfigured(ILogger logger, string roots);

    [LoggerMessage(2, LogLevel.Warning, "No project found for {Source}")]
    public static partial void ProjectNotFound(ILogger logger, string source);

    [LoggerMessage(3, LogLevel.Information, "Selected project {Project} for {Source}")]
    public static partial void ProjectSelected(ILogger logger, string project, string source);

    [LoggerMessage(4, LogLevel.Debug, "Using cached project context for {Project}")]
    public static partial void ProjectCacheHit(ILogger logger, string project);

    [LoggerMessage(5, LogLevel.Error, "Failed to load project context for {Project}")]
    public static partial void ProjectLoadFailed(ILogger logger, string project, Exception exception);

    [LoggerMessage(6, LogLevel.Information, "Loaded project {Project} with {SourceCount} sources and {ReferenceCount} references")]
    public static partial void ProjectLoaded(ILogger logger, string project, int sourceCount, int referenceCount);

    [LoggerMessage(7, LogLevel.Debug, "Starting design-time MSBuild for {Project}")]
    public static partial void MsBuildStarted(ILogger logger, string project);

    [LoggerMessage(8, LogLevel.Warning, "Design-time MSBuild failed for {Project} with exit code {ExitCode}: {StandardError}")]
    public static partial void MsBuildFailed(ILogger logger, string project, int exitCode, string standardError);

    [LoggerMessage(9, LogLevel.Warning, "Design-time MSBuild returned invalid output for {Project}")]
    public static partial void MsBuildInvalidOutput(ILogger logger, string project);

    [LoggerMessage(10, LogLevel.Warning, "Using fallback context for {Project} with {SourceCount} sources")]
    public static partial void ProjectFallback(ILogger logger, string project, int sourceCount);

    [LoggerMessage(11, LogLevel.Information, "Analyzed {Source} in {Project} with {SourceCount} sources, {ReferenceCount} references, and diagnostics {Diagnostics}")]
    public static partial void DocumentAnalyzed(ILogger logger, string source, string? project, int sourceCount, int referenceCount, string diagnostics);

    [LoggerMessage(12, LogLevel.Warning, "Ignored malformed JSON-RPC payload")]
    public static partial void MalformedPayload(ILogger logger, Exception exception);

    [LoggerMessage(13, LogLevel.Error, "JSON-RPC transport failed")]
    public static partial void TransportFailed(ILogger logger, Exception exception);

    [LoggerMessage(14, LogLevel.Error, "Unhandled failure while processing {Method}")]
    public static partial void RequestFailed(ILogger logger, string method, Exception exception);
}
