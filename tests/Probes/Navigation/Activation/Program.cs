using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

string[] uris =
[
    "lucent-probe://navigation/items/%2e%2e/admin?q=a%2Fb",
    "lucent-probe://navigation/a/../b?x=one+two",
    "lucent-probe://navigation/items/%252e%252e?q=%E2%9C%93",
    "lucent-probe://navigation//items/1",
];
foreach (var raw in uris)
{
    using var marshaler = ABI.System.Uri.CreateMarshaler(new Uri(raw));
    var uri = ABI.System.Uri.FromAbi(ABI.System.Uri.GetAbi(marshaler));
    if (!string.Equals(uri.OriginalString, raw, StringComparison.Ordinal))
        throw new InvalidOperationException("Raw URI fidelity failed.");
}

var current = AppInstance.GetCurrent();
var activation = current.GetActivatedEventArgs();
var redirected = new TaskCompletionSource<string>(
    TaskCreationOptions.RunContinuationsAsynchronously
);
void OnActivated(object? sender, AppActivationArguments delivery)
{
    if (
        delivery.Kind != ExtendedActivationKind.Launch
        || delivery.Data is not ILaunchActivatedEventArgs launch
    )
        redirected.TrySetException(new InvalidOperationException("Unexpected activation payload."));
    else
        redirected.TrySetResult(launch.Arguments);
}
current.Activated += OnActivated;
var key = args.Length == 0 ? "Lucent.ActivationProbe." + Guid.NewGuid().ToString("N") : args[1];
var owner = AppInstance.FindOrRegisterForKey(key);
var registeringThread = Environment.CurrentManagedThreadId;
try
{
    if (!owner.IsCurrent)
    {
        owner
            .RedirectActivationToAsync(activation)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5))
            .GetAwaiter()
            .GetResult();
        Console.WriteLine("redirect-delivered");
        return 0;
    }
    if (args.Length > 0 && args[0] == "--primary")
    {
        File.WriteAllText(args[2], "ready");
        var delivered = redirected
            .Task.WaitAsync(TimeSpan.FromSeconds(10))
            .GetAwaiter()
            .GetResult();
        if (!delivered.Contains("--secondary", StringComparison.Ordinal))
            throw new InvalidOperationException("Redirected arguments missing.");
        File.WriteAllText(
            args[3],
            "PASS: primary received secondary launch; raw URI fidelity passed"
        );
    }
    Console.WriteLine(
        $"kind={activation.Kind}; ownsKey={owner.IsCurrent}; raw-uri-cases={uris.Length}"
    );
    return 0;
}
finally
{
    current.Activated -= OnActivated;
    if (owner.IsCurrent)
    {
        Console.WriteLine(
            $"registration-thread={registeringThread}; cleanup-thread={Environment.CurrentManagedThreadId}"
        );
        current.UnregisterKey();
    }
}
