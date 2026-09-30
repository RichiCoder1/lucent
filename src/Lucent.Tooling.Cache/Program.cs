using System.Text.Json;

namespace Lucent.Tooling.Cache;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> Main()
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        try
        {
            var line = await ReadLineBoundedAsync(Console.In, 128 * 1024, cancellation.Token);
            if (line is null)
                throw new CacheException("request", "Cache request is missing or too large.");
            var request =
                JsonSerializer.Deserialize<CacheRequest>(line, JsonOptions)
                ?? throw new CacheException("request", "Cache request is empty.");
            if (
                string.IsNullOrWhiteSpace(request.DotnetPath)
                && Environment.ProcessPath is { } host
                && Path.GetFileName(host) is "dotnet" or "dotnet.exe"
            )
                request = request with { DotnetPath = host };
            // Console.In may complete ReadAsync synchronously while waiting on a pipe.
            // Keep that blocking read off the operation's execution thread.
            _ = Task.Run(() => WatchCancellationAsync(cancellation));
            var result = await new CacheInstaller(new DotnetIdentityRunner()).ExecuteAsync(
                request,
                cancellation.Token
            );
            Console.Out.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return 0;
        }
        catch (OperationCanceledException)
        {
            WriteError("cancelled", "Cache operation was cancelled.");
            return 4;
        }
        catch (CacheException error)
        {
            WriteError(error.Code, error.Message);
            return 2;
        }
        catch (Exception error)
        {
            WriteError("invalid", error.Message);
            return 3;
        }
    }

    private static async Task WatchCancellationAsync(CancellationTokenSource cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            var line = await ReadLineBoundedAsync(Console.In, 64, cancellation.Token);
            if (line is null)
                return;
            if (line == "cancel")
            {
                cancellation.Cancel();
                return;
            }
        }
    }

    private static async Task<string?> ReadLineBoundedAsync(
        TextReader reader,
        int maxChars,
        CancellationToken cancellationToken
    )
    {
        var buffer = new char[1];
        var text = new System.Text.StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
                return text.Length == 0 ? null : text.ToString();
            if (buffer[0] == '\n')
                return text.ToString().TrimEnd('\r');
            if (text.Length == maxChars)
                throw new CacheException("request", "Cache control line exceeds its size limit.");
            text.Append(buffer[0]);
        }
    }

    private static void WriteError(string code, string message) =>
        Console.Out.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    schemaVersion = 1,
                    status = "error",
                    code,
                    message,
                }
            )
        );
}
