using ContextInjection.Library;
using Lucent.Core;

var events = new List<string>();
var lifecycle = new ProbeLifecycle(events);
var application = LucentApplication.CreateBuilder().UseHost(new ProbeHost()).Build();
if (application.Run(lifecycle) != 0)
    throw new InvalidOperationException("The context/injection probe returned a failure code.");
const string Expected = "resolve:IProbeService,setup:42:7,stop,component-dispose,service-dispose";
if (string.Join(",", events) != Expected)
    throw new InvalidOperationException(
        "Unexpected context/injection lifecycle: " + string.Join(",", events)
    );
Console.WriteLine("package-context-injection-native-aot=pass");

internal sealed class ProbeHost : IApplicationHost
{
    public int Run(ApplicationSession session)
    {
        session.Start();
        Pump(session, () => session.Status.Phase == ApplicationPhase.Running);
        session.RequestClose();
        Pump(session, () => session.IsCompleted);
        return 0;
    }

    private static void Pump(ApplicationSession session, Func<bool> complete)
    {
        var deadline = Environment.TickCount64 + 5_000;
        while (!complete())
        {
            session.ProcessEvents();
            if (!session.Composition.IsDisposed)
                session.Composition.Flush();
            if (Environment.TickCount64 >= deadline)
                throw new TimeoutException("The context/injection probe did not complete.");
            Thread.Sleep(1);
        }
    }
}

internal sealed class ProbeLifecycle(List<string> events) : IApplicationLifecycle
{
    private readonly ProbeService _service = new(events);
    private ComponentServiceBinding? _binding;

    public ValueTask<ComponentRecipe> StartAsync(ApplicationSession session)
    {
        _binding = session.CreateServiceBinding(new ProbeServiceSource(_service, events));
        var recipe = Context.Provide(new ProbeContext(42), RequiredComponent.Create(events));
        return ValueTask.FromResult(_binding.Attach(recipe));
    }

    public ValueTask<bool> PrepareCloseAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(true);

    public ValueTask StopAsync()
    {
        events.Add("stop");
        _binding!.StopAccepting();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _binding!.Revoke();
        _service.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class ProbeServiceSource(ProbeService service, List<string> events)
    : IComponentServiceSource
{
    public T Resolve<T>()
        where T : class
    {
        events.Add("resolve:" + typeof(T).Name);
        if (typeof(T) == typeof(IProbeService))
            return (T)(object)service;
        throw new InvalidOperationException("Unexpected exact service type: " + typeof(T).FullName);
    }
}

internal sealed class ProbeService(List<string> events) : IProbeService, IDisposable
{
    public int Value => 7;

    public void Dispose() => events.Add("service-dispose");
}
