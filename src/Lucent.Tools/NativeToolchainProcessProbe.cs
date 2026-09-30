using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Lucent.Tools;

/// <summary>Runs only the installed Visual Studio discovery executable, never a workspace tool.</summary>
public sealed class NativeToolchainProcessProbe : INativeToolchainProbe
{
    private static readonly string[] InventoryArguments =
    [
        "-products",
        "*",
        "-version",
        "[17.0,)",
        "-prerelease",
        "-format",
        "json",
        "-utf8",
        "-nologo",
    ];
    internal static string DiscoveryPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio",
            "Installer",
            "vswhere.exe"
        );

    public async Task<NativeToolchainProbeResult> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            return new(NativeToolchainProbeStatus.UnsupportedHost);
        var executable = DiscoveryPath;
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            return new(NativeToolchainProbeStatus.DiscoveryUnavailable);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var version = FileVersionInfo.GetVersionInfo(executable);
            if (
                new Version(version.FileMajorPart, version.FileMinorPart, version.FileBuildPart)
                < new Version(3, 1, 1)
            )
                return new(NativeToolchainProbeStatus.DiscoveryUnsupported);
            var inventory = await QueryAsync(executable, [], budget.Token).ConfigureAwait(false);
            var cpp = await QueryAsync(
                    executable,
                    ["-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64"],
                    budget.Token
                )
                .ConfigureAwait(false);
            var sdk = await QueryAsync(
                    executable,
                    [
                        "-requiresAny",
                        "-requires",
                        "Microsoft.VisualStudio.Component.Windows10SDK.*",
                        "Microsoft.VisualStudio.Component.Windows11SDK.*",
                    ],
                    budget.Token
                )
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new(NativeToolchainProbeStatus.Observed, inventory, cpp, sdk);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(NativeToolchainProbeStatus.Failed);
        }
        catch (Exception error)
            when (error is IOException or UnauthorizedAccessException or Win32Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(NativeToolchainProbeStatus.Failed);
        }
    }

    private static async Task<string> QueryAsync(
        string executable,
        string[] componentArguments,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in InventoryArguments.Concat(componentArguments))
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException();
        using var reads = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var output = DotnetProcessProbe.ReadBoundedAsync(
            process.StandardOutput,
            reads.Token,
            64 * 1024
        );
        var errors = DotnetProcessProbe.ReadBoundedAsync(
            process.StandardError,
            reads.Token,
            16 * 1024
        );
        Exception? failure = null;
        string? result = null;
        var cleanupConfirmed = true;
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var values = await Task.WhenAll(output, errors).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new IOException();
            result = values[0];
        }
        catch (Exception error)
            when (error is OperationCanceledException or IOException or Win32Exception)
        {
            failure = error;
        }
        finally
        {
            reads.Cancel();
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited) { }
            catch (Win32Exception)
            {
                cleanupConfirmed = false;
            }
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
                await Task.WhenAll(output, errors).WaitAsync(cleanup.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cleanupConfirmed = false;
            }
            catch (IOException) when (process.HasExited)
            {
                // The owned process is reaped; failed bounded output reads remain inconclusive.
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!cleanupConfirmed)
            throw new IOException(
                "Discovery process cleanup could not be confirmed within its deadline."
            );
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result!;
    }
}
