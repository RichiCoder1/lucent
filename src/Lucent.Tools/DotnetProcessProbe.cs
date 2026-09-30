using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Lucent.Tools;

public sealed class DotnetProcessProbe : IDotnetProbe
{
    private const int MaximumOutputCharacters = 16 * 1024;
    private readonly string host = Path.Combine(
        Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..")),
        OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"
    );

    public async Task<DotnetProbeResult> RunAsync(
        string workingDirectory,
        string argument,
        CancellationToken cancellationToken
    )
    {
        if (argument is not ("--list-sdks" or "--list-runtimes"))
            throw new ArgumentException("Unsupported static .NET probe.", nameof(argument));
        if (!File.Exists(host))
            return new(false, "");
        var start = new ProcessStartInfo(host)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        start.ArgumentList.Add(argument);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return new(false, "");
            try
            {
                var output = ReadBoundedAsync(process.StandardOutput, timeout.Token);
                var error = ReadBoundedAsync(process.StandardError, timeout.Token);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                var values = await Task.WhenAll(output, error).ConfigureAwait(false);
                return new(process.ExitCode == 0, values[0]);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                return new(false, "");
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException)
        {
            return new(false, "");
        }
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        CancellationToken cancellationToken
    )
    {
        var buffer = new char[1024];
        var text = new StringBuilder();
        while (true)
        {
            var count = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0)
                return text.ToString();
            if (text.Length + count > MaximumOutputCharacters)
                throw new IOException("The .NET probe returned too much output.");
            text.Append(buffer, 0, count);
        }
    }
}
