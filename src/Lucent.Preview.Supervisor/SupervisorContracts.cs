using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lucent.Preview.Supervisor;

public sealed record SupervisorRequest(
    int ProtocolVersion,
    string Kind,
    string Mode,
    string RequestId,
    string Program,
    string[] Args,
    string WorkingDirectory,
    string LogDirectory,
    int TimeoutMs,
    int GraceMs,
    Dictionary<string, string>? Environment = null,
    int MaxOutputBytes = 1024 * 1024
)
{
    public const int MaximumRequestBytes = 128 * 1024;
    public const int MaximumOutputBytes = 16 * 1024 * 1024;

    public void Validate()
    {
        if (
            ProtocolVersion != 2
            || Kind != "preview-supervisor-request"
            || Mode is not ("bounded" or "live")
        )
            throw new ArgumentException("Unsupported supervisor protocol.");
        if (!ValidText(RequestId, 128) || RequestId.Any(char.IsControl))
            throw new ArgumentException("A bounded request identity is required.");
        if (
            !Absolute(Program)
            || !File.Exists(Program)
            || !Absolute(WorkingDirectory)
            || !Directory.Exists(WorkingDirectory)
            || !Absolute(LogDirectory)
            || !Directory.Exists(LogDirectory)
        )
            throw new ArgumentException(
                "Existing absolute executable, working and log paths are required."
            );
        if (
            Args is null
            || Args.Length > 128
            || Args.Any(value => !ValidText(value, 16 * 1024, empty: true))
        )
            throw new ArgumentException("Process arguments exceed the supported bound.");
        if (
            TimeoutMs is < 1 or > 600_000
            || GraceMs is < 0 or > 10_000
            || MaxOutputBytes is < 1 or > MaximumOutputBytes
        )
            throw new ArgumentException("Supervisor deadlines or log bounds are unsupported.");
        if (
            Environment is not null
            && (
                Environment.Count > 128
                || Environment.Any(pair =>
                    !ValidText(pair.Key, 256)
                    || pair.Key.Contains('=')
                    || pair.Key.Any(char.IsControl)
                    || !ValidText(pair.Value, 16 * 1024, empty: true)
                )
            )
        )
            throw new ArgumentException("Environment overrides exceed the supported bound.");
        // Logs must not follow a directory link into another generation or overwrite an existing file.
        for (
            var directory = new DirectoryInfo(LogDirectory);
            directory is not null;
            directory = directory.Parent
        )
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("The log directory must not traverse a reparse point.");
    }

    private static bool Absolute(string? value) =>
        ValidText(value, 32_000) && Path.IsPathFullyQualified(value!);

    private static bool ValidText(string? value, int maximum, bool empty = false) =>
        value is not null
        && value.Length <= maximum
        && !value.Contains('\0')
        && (empty || value.Length > 0);
}

public sealed record SupervisorStarted(int ProtocolVersion, string Kind, string RequestId)
{
    internal static SupervisorStarted For(SupervisorRequest request) =>
        new(2, "preview-supervisor-started", request.RequestId);
}

public sealed record SupervisorResult(
    int ProtocolVersion,
    string Kind,
    string RequestId,
    string Status,
    int? ExitCode,
    string Termination,
    bool TreeReaped,
    long StdoutBytes,
    long StderrBytes
)
{
    internal static SupervisorResult Failure(string requestId, string status, bool treeReaped) =>
        new(
            2,
            "preview-supervisor-result",
            requestId,
            status,
            null,
            treeReaped ? "natural" : "unconfirmed",
            treeReaped,
            0,
            0
        );
}

internal static class SupervisorProtocol
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    internal static async Task<string?> ReadLineAsync(
        TextReader reader,
        int maximum,
        CancellationToken token
    )
    {
        var text = new System.Text.StringBuilder();
        var buffer = new char[1];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (count == 0)
                return text.Length == 0 ? null : text.ToString();
            if (buffer[0] == '\n')
                return text.ToString().TrimEnd('\r');
            if (text.Length >= maximum)
                throw new InvalidDataException("Supervisor input exceeds its bound.");
            text.Append(buffer[0]);
        }
    }
}
