using System.Text.Json;

namespace Lucent.Preview.Supervisor;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var requestId = "unavailable";
        SupervisorResult result;
        if (!OperatingSystem.IsWindows() || args.Length != 0)
            result = SupervisorResult.Failure(requestId, "launch-failed", true);
        else
        {
            using var inputDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                // Console's redirected reader may block synchronously; use a separate thread.
                var line = await Task.Run(() =>
                        SupervisorProtocol.ReadLineAsync(
                            Console.In,
                            SupervisorRequest.MaximumRequestBytes / 2,
                            inputDeadline.Token
                        )
                    )
                    .WaitAsync(inputDeadline.Token)
                    .ConfigureAwait(false);
                if (
                    line is null
                    || System.Text.Encoding.UTF8.GetByteCount(line)
                        > SupervisorRequest.MaximumRequestBytes
                )
                    throw new InvalidDataException("A bounded supervisor request is required.");
                var request =
                    JsonSerializer.Deserialize<SupervisorRequest>(
                        line,
                        SupervisorProtocol.JsonOptions
                    ) ?? throw new InvalidDataException("The supervisor request is empty.");
                request.Validate();
                requestId = request.RequestId;
                using var stop = new CancellationTokenSource();
                void RequestStop()
                {
                    try
                    {
                        stop.Cancel();
                    }
                    catch (ObjectDisposedException) { }
                }
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // EOF is a stop even if the parent never sent an explicit control record.
                        var command = await SupervisorProtocol
                            .ReadLineAsync(Console.In, 64, CancellationToken.None)
                            .ConfigureAwait(false);
                        // EOF, stop and unsupported control records all initiate fail-closed cleanup.
                        _ = command;
                        RequestStop();
                    }
                    catch (Exception error) when (error is IOException or ObjectDisposedException)
                    {
                        RequestStop();
                    }
                });
                result = await ProcessSupervisor
                    .RunAsync(
                        request,
                        started =>
                        {
                            Console.Out.WriteLine(
                                JsonSerializer.Serialize(started, SupervisorProtocol.JsonOptions)
                            );
                            Console.Out.Flush();
                        },
                        stop.Token
                    )
                    .ConfigureAwait(false);
            }
            catch (Exception error)
                when (error
                        is ArgumentException
                            or IOException
                            or JsonException
                            or OperationCanceledException
                            or UnauthorizedAccessException
                )
            {
                result = SupervisorResult.Failure(requestId, "launch-failed", true);
            }
        }
        Console.Out.WriteLine(JsonSerializer.Serialize(result, SupervisorProtocol.JsonOptions));
        return result.TreeReaped ? 0 : 3;
    }
}
