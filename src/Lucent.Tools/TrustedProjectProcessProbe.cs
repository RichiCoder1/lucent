using System.Diagnostics;

namespace Lucent.Tools;

public interface ITrustedProjectProbe
{
    Task<DotnetProbeResult> RunAsync(
        string serverPath,
        string? projectPath,
        CancellationToken cancellationToken
    );
}

public sealed class TrustedProjectProcessProbe : ITrustedProjectProbe
{
    private const int MaximumOutputCharacters = 2 * 1024 * 1024;

    public async Task<DotnetProbeResult> RunAsync(
        string serverPath,
        string? projectPath,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(DotnetProcessProbe.HostPath))
            return new(false, "");
        var start = new ProcessStartInfo(DotnetProcessProbe.HostPath)
        {
            WorkingDirectory = projectPath is null
                ? Path.GetTempPath()
                : Path.GetDirectoryName(projectPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        start.Environment["DOTNET_HOST_PATH"] = DotnetProcessProbe.HostPath;
        start.Environment["LUCENT_REQUIREMENTS_CANCEL_STDIN"] = "1";
        start.ArgumentList.Add(serverPath);
        if (projectPath is null)
            start.ArgumentList.Add("--identity");
        else
        {
            start.ArgumentList.Add("--project-requirements");
            start.ArgumentList.Add("--trusted-project");
            start.ArgumentList.Add(projectPath);
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(projectPath is null ? 10 : 60));
        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return new(false, "");
            var output = ReadAsync(process.StandardOutput, deadline);
            var errors = ReadAsync(process.StandardError, deadline);
            try
            {
                await Task.WhenAll(output, errors, process.WaitForExitAsync(deadline.Token))
                    .WaitAsync(deadline.Token)
                    .ConfigureAwait(false);
                return new(process.ExitCode == 0, await output.ConfigureAwait(false));
            }
            finally
            {
                if (!process.HasExited)
                {
                    // Allow the requirements producer to terminate its own MSBuild descendants.
                    using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    try
                    {
                        await process
                            .StandardInput.WriteLineAsync("cancel".AsMemory(), grace.Token)
                            .ConfigureAwait(false);
                        process.StandardInput.Close();
                        await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
                    }
                    catch (Exception error)
                        when (error
                                is IOException
                                    or OperationCanceledException
                                    or InvalidOperationException
                        )
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                            // Kill is asynchronous. Establish producer exit independently of the
                            // caller/deadline token; this does not guarantee descendant exit.
                            using var cleanup = new CancellationTokenSource(
                                TimeSpan.FromSeconds(5)
                            );
                            await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
                        }
                    }
                }
                // Observe stream failures without retaining or displaying server diagnostics.
                deadline.Cancel();
                try
                {
                    await Task.WhenAll(output, errors).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or OperationCanceledException)
                { }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, "");
        }
        catch (Exception error)
            when (error
                    is System.ComponentModel.Win32Exception
                        or IOException
                        or InvalidOperationException
            )
        {
            return new(false, "");
        }
    }

    private static async Task<string> ReadAsync(
        StreamReader reader,
        CancellationTokenSource deadline
    )
    {
        try
        {
            return await DotnetProcessProbe
                .ReadBoundedAsync(reader, deadline.Token, MaximumOutputCharacters)
                .ConfigureAwait(false);
        }
        catch (IOException)
        {
            await deadline.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }
}
