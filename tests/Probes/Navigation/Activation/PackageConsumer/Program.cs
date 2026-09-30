using System.Collections.Concurrent;
using Lucent.Platform.Windows.Activation;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 2 || args[0] is not ("--primary" or "--secondary"))
            return 10;
        var options = new WindowsActivationOptions(args[1], "lucent-package-probe", "navigation");
        var primaryCalls = 0;
        var result = WindowsActivation.Run(
            options,
            inbox =>
            {
                primaryCalls++;
                if (args[0] != "--primary" || args.Length != 4)
                    throw new InvalidOperationException(
                        "A secondary process created the primary session."
                    );
                var cold = inbox.TakeStartup();
                if (cold is not { Kind: ActivationKind.Launch, Delivery: ActivationDelivery.Cold })
                    throw new InvalidOperationException("Cold launch was not captured.");
                var queue = new ConcurrentQueue<Action>();
                using var wake = new AutoResetEvent(false);
                ActivationEnvelope? redirected = null;
                inbox.Attach(
                    action =>
                    {
                        queue.Enqueue(action);
                        wake.Set();
                    },
                    envelope => redirected = envelope
                );
                File.WriteAllText(args[2], "ready");
                var deadline = Environment.TickCount64 + 10_000;
                while (redirected is null && Environment.TickCount64 < deadline)
                {
                    while (queue.TryDequeue(out var action))
                        action();
                    if (redirected is null)
                        wake.WaitOne(25);
                }
                if (
                    redirected
                    is not {
                        Kind: ActivationKind.Launch,
                        Delivery: ActivationDelivery.Redirected,
                        Provenance: ActivationProvenance.UntrustedExternal
                    }
                )
                    throw new InvalidOperationException(
                        "The primary did not receive one redirected launch."
                    );
                File.WriteAllText(
                    args[3],
                    "PASS: package-only NativeAOT primary received redirect"
                );
                return 0;
            }
        );
        if (
            args[0] == "--primary"
            && (result.Kind != ActivationRunKind.Primary || primaryCalls != 1)
        )
            return 11;
        if (args[0] == "--secondary")
        {
            if (primaryCalls != 0)
                return 12;
            if (
                result.Kind == ActivationRunKind.RedirectFailed
                && result.Failure == ActivationRedirectFailure.RejectedInput
            )
                return result.ExitCode;
            if (result.Kind != ActivationRunKind.Redirected)
                return 12;
        }
        return result.ExitCode;
    }
}
