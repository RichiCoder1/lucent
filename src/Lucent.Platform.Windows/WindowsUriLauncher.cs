using System.ComponentModel;
using System.Diagnostics;
using Lucent.Core;

namespace Lucent.Platform.Windows;

/// <summary>Launches an explicitly permitted URI using the Windows registered handler.</summary>
public sealed class WindowsUriLauncher : IUriLauncher
{
    private readonly UriLaunchPolicy _policy;
    private readonly Action<Uri> _launch;

    /// <summary>Creates a launcher with a required application scheme policy.</summary>
    public WindowsUriLauncher(UriLaunchPolicy policy)
        : this(policy, Launch) { }

    internal WindowsUriLauncher(UriLaunchPolicy policy, Action<Uri> launch)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _launch = launch ?? throw new ArgumentNullException(nameof(launch));
    }

    /// <inheritdoc />
    public ValueTask<UriLaunchResult> LaunchAsync(
        Uri uri,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromResult(new UriLaunchResult(UriLaunchStatus.Canceled));
        if (!_policy.Allows(uri))
            return ValueTask.FromResult(new UriLaunchResult(UriLaunchStatus.Denied));
        try
        {
            _launch(uri);
            return ValueTask.FromResult(new UriLaunchResult(UriLaunchStatus.Launched));
        }
        catch (PlatformNotSupportedException)
        {
            return ValueTask.FromResult(new UriLaunchResult(UriLaunchStatus.Unsupported));
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            return ValueTask.FromResult(
                new UriLaunchResult(
                    UriLaunchStatus.Failed,
                    "The operating system could not open this link."
                )
            );
        }
    }

    private static void Launch(Uri uri)
    {
        using var process = Process.Start(
            new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }
        );
    }
}
