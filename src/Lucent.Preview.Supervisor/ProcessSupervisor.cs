using System.ComponentModel;
using System.Diagnostics;

namespace Lucent.Preview.Supervisor;

public static class ProcessSupervisor
{
    private const int ReapTimeoutMs = 5_000;

    public static Task<SupervisorResult> RunAsync(
        SupervisorRequest request,
        CancellationToken stop = default
    ) => RunAsync(request, canObserveJob: null, stop);

    internal static async Task<SupervisorResult> RunAsync(
        SupervisorRequest request,
        Func<bool>? canObserveJob,
        CancellationToken stop
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        if (!OperatingSystem.IsWindows())
            return SupervisorResult.Failure(request.RequestId, "launch-failed", true);
        if (stop.IsCancellationRequested)
            return SupervisorResult.Failure(request.RequestId, "cancelled", true);
        OwnedProcessJob? job = null;
        FileStream? output = null;
        FileStream? error = null;
        try
        {
            output = NewLog(request.LogDirectory, "stdout.log");
            error = NewLog(request.LogDirectory, "stderr.log");
            job = OwnedProcessJob.Start(request, stop, canObserveJob);
            var limit = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var outputTask = Task.Run(() =>
                Capture(job.StandardOutput, output, request.MaxOutputBytes, limit)
            );
            var errorTask = Task.Run(() =>
                Capture(job.StandardError, error, request.MaxOutputBytes, limit)
            );
            var logs = Task.WhenAll(outputTask, errorTask);
            _ = logs.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default
            );
            var started = Stopwatch.StartNew();
            var status = "completed";
            var termination = "natural";
            while (!job.HasExited)
            {
                if (stop.IsCancellationRequested)
                {
                    status = "cancelled";
                    break;
                }
                if (limit.Task.IsCompleted)
                {
                    status = "output-limit";
                    break;
                }
                if (outputTask.IsFaulted || errorTask.IsFaulted)
                {
                    status = "launch-failed";
                    break;
                }
                if (started.ElapsedMilliseconds >= request.TimeoutMs)
                {
                    status = "timeout";
                    break;
                }
                await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);
            }
            if (status != "completed")
            {
                try
                {
                    // Five bytes cannot fill a fresh pipe even when a build tool ignores stdin.
                    job.StandardInput.Write("stop\n"u8);
                    job.StandardInput.Flush();
                }
                catch (IOException) { }
                job.StandardInput.Dispose();
                var grace = Stopwatch.StartNew();
                while (grace.ElapsedMilliseconds < request.GraceMs)
                {
                    if (job.TryActiveCount(out var active) && active == 0)
                        break;
                    await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);
                }
                termination = "cooperative";
            }
            var reaped = job.TryActiveCount(out var count) && count == 0;
            if (!reaped)
            {
                termination = "forced";
                if (job.Terminate())
                    reaped = await WaitForEmptyJobAsync(job).ConfigureAwait(false);
            }
            if (!reaped)
                return new(
                    1,
                    "preview-supervisor-result",
                    request.RequestId,
                    "termination-failed",
                    job.ExitCode,
                    "unconfirmed",
                    false,
                    output.Length,
                    error.Length
                );
            try
            {
                var counts = await logs.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None)
                    .ConfigureAwait(false);
                if (limit.Task.IsCompleted)
                    status = "output-limit";
                return new(
                    1,
                    "preview-supervisor-result",
                    request.RequestId,
                    status,
                    job.ExitCode,
                    termination,
                    true,
                    counts[0],
                    counts[1]
                );
            }
            catch (TimeoutException)
            {
                // A pipe held outside the owned job makes complete output cleanup uncertain.
                return new(
                    1,
                    "preview-supervisor-result",
                    request.RequestId,
                    "termination-failed",
                    job.ExitCode,
                    "unconfirmed",
                    false,
                    output.Length,
                    error.Length
                );
            }
        }
        catch (TerminationUnconfirmedException)
        {
            return SupervisorResult.Failure(request.RequestId, "termination-failed", false);
        }
        catch (OperationCanceledException)
        {
            var reaped =
                job is null
                || (job.Terminate() && await WaitForEmptyJobAsync(job).ConfigureAwait(false));
            return SupervisorResult.Failure(
                request.RequestId,
                reaped ? "cancelled" : "termination-failed",
                reaped
            );
        }
        catch (Exception failure)
            when (failure
                    is IOException
                        or UnauthorizedAccessException
                        or Win32Exception
                        or ArgumentException
                        or InvalidOperationException
            )
        {
            var reaped =
                job is null
                || (job.Terminate() && await WaitForEmptyJobAsync(job).ConfigureAwait(false));
            return SupervisorResult.Failure(
                request.RequestId,
                reaped ? "launch-failed" : "termination-failed",
                reaped
            );
        }
        finally
        {
            job?.Dispose();
            output?.Dispose();
            error?.Dispose();
        }
    }

    private static FileStream NewLog(string directory, string name) =>
        new(
            Path.Combine(directory, name),
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            4096,
            FileOptions.SequentialScan
        );

    private static long Capture(
        Stream source,
        Stream destination,
        int maximum,
        TaskCompletionSource limit
    )
    {
        var buffer = new byte[4096];
        long observed = 0;
        var stored = 0;
        while (true)
        {
            var read = source.Read(buffer);
            if (read == 0)
                break;
            observed += read;
            var allowed = Math.Min(read, maximum - stored);
            if (allowed > 0)
            {
                destination.Write(buffer, 0, allowed);
                stored += allowed;
            }
            if (observed > maximum)
                limit.TrySetResult();
        }
        destination.Flush();
        return observed;
    }

    private static async Task<bool> WaitForEmptyJobAsync(OwnedProcessJob job)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < ReapTimeoutMs)
        {
            if (!job.TryActiveCount(out var count))
                return false;
            if (count == 0)
                return true;
            await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);
        }
        return false;
    }
}
