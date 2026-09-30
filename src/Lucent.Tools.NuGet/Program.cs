using System.Text.Json;

namespace Lucent.Tools.NuGet;

internal static class Program
{
    public static async Task<int> Main()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(65));
        ConsoleCancelEventHandler cancel = (_, args) =>
        {
            args.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        DirectoryInfo? scratch = null;
        var report = Unavailable("invocation-failed");
        try
        {
            // Set this before NuGet's lazily cached scratch policy is initialized.
            scratch = Directory.CreateTempSubdirectory("lucent-nuget-scratch-");
            Environment.SetEnvironmentVariable("NUGET_SCRATCH", scratch.FullName);
            var input = new char[16385];
            var length = 0;
            int read;
            while (
                (
                    read = await Console
                        .In.ReadAsync(input.AsMemory(length), cancellation.Token)
                        .ConfigureAwait(false)
                ) > 0
            )
            {
                length += read;
                if (length > 16384)
                    throw new IOException("Request bound exceeded.");
            }
            var request =
                JsonSerializer.Deserialize<FeedRequest>(input.AsSpan(0, length), JsonOptions)
                ?? throw new IOException("Invalid request.");
            report = await new FeedDoctor()
                .ObserveAsync(request, cancellation.Token)
                .ConfigureAwait(false);
        }
        catch (Exception error)
        {
            report = Unavailable(
                error is OperationCanceledException ? "cancelled" : "invocation-failed"
            );
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
            // ObserveAsync has disposed its settings/handles before control reaches here.
            try
            {
                scratch?.Delete(recursive: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                report = Unavailable("invocation-failed");
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        return report.Status == "observed" ? 0 : 1;
    }

    private static FeedReport Unavailable(string reason) =>
        new(1, "nuget-feed-doctor", "invocation", "", "unavailable", [], reason);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
}
