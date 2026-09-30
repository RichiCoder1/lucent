using System.Text;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Lucent.Platform.Windows.Activation;

/// <summary>Result of claiming the application instance.</summary>
public enum ActivationRunKind
{
    /// <summary>The process owned the key and ran the application.</summary>
    Primary,

    /// <summary>The process redirected its activation and did not create a session.</summary>
    Redirected,

    /// <summary>The request was rejected or the bounded redirect did not complete.</summary>
    RedirectFailed,
}

/// <summary>Result of the synchronous activation wrapper.</summary>
public readonly record struct ActivationRunResult(
    ActivationRunKind Kind,
    int ExitCode,
    ActivationRedirectFailure Failure = ActivationRedirectFailure.None
);

/// <summary>Finite reason a secondary process did not deliver its request.</summary>
public enum ActivationRedirectFailure
{
    /// <summary>Delivery succeeded or this process owns the key.</summary>
    None,

    /// <summary>The external input could not meet the bounded envelope contract.</summary>
    RejectedInput,

    /// <summary>Windows did not acknowledge the redirect within five seconds.</summary>
    TimedOut,

    /// <summary>Windows reported a redirect failure.</summary>
    Failed,
}

/// <summary>Owns Windows activation before application/session/window startup.</summary>
public static class WindowsActivation
{
    private static readonly TimeSpan RedirectTimeout = TimeSpan.FromSeconds(5);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// Runs on the application's STA entry thread. The primary callback creates and runs the
    /// Lucent application synchronously; secondary processes return without creating it.
    /// </summary>
    /// <remarks>
    /// Keep this method on the entry thread until the application has shut down. Windows App SDK
    /// owns a native mutex for the instance key, which must be released on the registering thread.
    /// The application must bind the inbox to its owner dispatcher before processing warm delivery.
    /// </remarks>
    public static ActivationRunResult Run(
        WindowsActivationOptions options,
        Func<ActivationInbox, int> runPrimary
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runPrimary);
        options.Validate();
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Windows activation requires an STA entry thread.");
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            throw new PlatformNotSupportedException(
                "Windows activation requires Windows 10 1809 or later."
            );

        WinRT.ComWrappersSupport.InitializeComWrappers();
        var current = AppInstance.GetCurrent();
        var cold = current.GetActivatedEventArgs();
        using var inbox = new ActivationInbox();
        var initial = CopyOrReject(cold, options, ActivationDelivery.Cold);
        inbox.Offer(initial);

        void OnActivated(object? _, AppActivationArguments args)
        {
            // WinRT argument objects are transient and may arrive on another thread.
            // Copy only bounded managed text before posting to the UI owner.
            try
            {
                inbox.Offer(CopyOrReject(args, options, ActivationDelivery.Redirected));
            }
            catch
            {
                // WinRT event callbacks must never unwind through the native dispatcher.
                // A failed owner post is terminal to that delivery; the owner remains responsible
                // for its own dispatcher failure handling.
            }
        }

        current.Activated += OnActivated;
        AppInstance? claimed = null;
        try
        {
            claimed = AppInstance.FindOrRegisterForKey(options.InstanceKey);
            if (!claimed.IsCurrent)
            {
                if (initial.Kind == ActivationKind.Rejected)
                    return new(
                        ActivationRunKind.RedirectFailed,
                        2,
                        ActivationRedirectFailure.RejectedInput
                    );
                try
                {
                    claimed
                        .RedirectActivationToAsync(cold)
                        .AsTask()
                        .WaitAsync(RedirectTimeout)
                        .GetAwaiter()
                        .GetResult();
                    return new(ActivationRunKind.Redirected, 0);
                }
                catch (TimeoutException)
                {
                    return new(
                        ActivationRunKind.RedirectFailed,
                        3,
                        ActivationRedirectFailure.TimedOut
                    );
                }
                catch
                {
                    return new(
                        ActivationRunKind.RedirectFailed,
                        4,
                        ActivationRedirectFailure.Failed
                    );
                }
            }
            return new(ActivationRunKind.Primary, runPrimary(inbox));
        }
        finally
        {
            inbox.Dispose();
            current.Activated -= OnActivated;
            if (claimed?.IsCurrent == true)
                current.UnregisterKey();
        }
    }

    private static ActivationEnvelope CopyOrReject(
        AppActivationArguments args,
        WindowsActivationOptions options,
        ActivationDelivery delivery
    )
    {
        try
        {
            if (args.Kind == ExtendedActivationKind.Launch)
            {
                if (
                    args.Data is not ILaunchActivatedEventArgs launch
                    || launch.Arguments.Length > WindowsActivationEnvelope.MaxRawUriBytes
                    || StrictUtf8.GetByteCount(launch.Arguments)
                        > WindowsActivationEnvelope.MaxRawUriBytes
                )
                    return WindowsActivationEnvelope.Rejected(
                        delivery,
                        ActivationRejection.UnreadableArguments
                    );
                return WindowsActivationEnvelope.Launch(delivery);
            }
            if (args.Kind != ExtendedActivationKind.Protocol)
                return WindowsActivationEnvelope.Rejected(
                    delivery,
                    ActivationRejection.UnsupportedKind
                );
            if (args.Data is not IProtocolActivatedEventArgs protocol)
                return WindowsActivationEnvelope.Rejected(
                    delivery,
                    ActivationRejection.UnreadableArguments
                );
            return WindowsActivationEnvelope.TryProtocol(
                protocol.Uri.OriginalString,
                options,
                delivery,
                out var envelope
            )
                ? envelope!
                : WindowsActivationEnvelope.Rejected(delivery, ActivationRejection.InvalidProtocol);
        }
        catch
        {
            return WindowsActivationEnvelope.Rejected(
                delivery,
                ActivationRejection.UnreadableArguments
            );
        }
    }
}
