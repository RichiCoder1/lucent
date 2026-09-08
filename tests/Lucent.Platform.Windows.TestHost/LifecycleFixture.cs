using Lucent.Hosting;
using Lucent.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lucent.Platform.Windows.TestHost;

/// <summary>Published public-surface fixture for hosted startup, close retry, and cleanup.</summary>
internal static class LifecycleFixture
{
    internal static int Run(LifecycleFixtureMode mode)
    {
        var recorder = new LifecycleRecorder();
        try
        {
            recorder.RecordUi("entry");
            var builder = HostedApplication.CreateBuilder();
            builder.Services.AddSingleton(recorder);
            builder.Services.AddSingleton<IHostedService>(_ => new LifecycleHostedService(
                recorder,
                mode
            ));
            builder.Services.AddScoped(services => new LifecycleFixtureModel(
                recorder,
                mode,
                (LifecycleHostedService)services.GetRequiredService<IHostedService>()
            ));
            var lifecycle = new HostedApplication(
                _ =>
                {
                    recorder.RecordUi("host-factory");
                    IHost host = builder.Build();
                    return mode == LifecycleFixtureMode.CleanupFailure
                        ? new FailingDisposeHost(host, recorder)
                        : host;
                },
                (services, session) =>
                {
                    var model = services.GetRequiredService<LifecycleFixtureModel>();
                    model.Attach(session);
                    recorder.RecordUi("root-factory");
                    session.Scope.OnDispose(() => recorder.RecordUi("ui-dispose"));
                    return LuiFixtures.Components.LifecycleFixtureView(model);
                },
                (services, cancellationToken) =>
                    services
                        .GetRequiredService<LifecycleFixtureModel>()
                        .PrepareCloseAsync(cancellationToken)
            );

            var result = LucentApplication
                .CreateBuilder()
                .UseWindows()
                .SetTitle("Lucent Lifecycle Fixture")
                .SetTheme(_ => ControlThemes.Light)
                .Build()
                .Run(lifecycle);
            recorder.RecordUi("run-complete");
            return result;
        }
        catch (Exception error)
        {
            recorder.Record(
                "failure:" + String.Join("|", Flatten(error).Select(item => item.Message))
            );
            return 1;
        }
    }

    private static IEnumerable<Exception> Flatten(Exception error) =>
        error is AggregateException aggregate
            ? aggregate.InnerExceptions.SelectMany(Flatten)
            : [error];

    private sealed class FailingDisposeHost(IHost inner, LifecycleRecorder recorder)
        : IHost,
            IAsyncDisposable
    {
        public IServiceProvider Services => inner.Services;

        public Task StartAsync(CancellationToken cancellationToken = default) =>
            inner.StartAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken = default) =>
            inner.StopAsync(cancellationToken);

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        public async ValueTask DisposeAsync()
        {
            recorder.Record("host-dispose");
            if (inner is IAsyncDisposable asyncInner)
                await asyncInner.DisposeAsync();
            else
                inner.Dispose();
            throw new InvalidOperationException("host dispose failed");
        }
    }
}

internal enum LifecycleFixtureMode
{
    Normal,
    StartupFailure,
    CleanupFailure,
}

public sealed class LifecycleFixtureModel : IAsyncDisposable
{
    private readonly LifecycleRecorder _recorder;
    private readonly LifecycleFixtureMode _mode;
    private readonly LifecycleHostedService _service;
    private ApplicationSession? _session;
    private int _statusObserved;

    internal LifecycleFixtureModel(
        LifecycleRecorder recorder,
        LifecycleFixtureMode mode,
        LifecycleHostedService service
    )
    {
        _recorder = recorder;
        _mode = mode;
        _service = service;
        recorder.RecordUi("model-create");
    }

    public string Status
    {
        get
        {
            if (Interlocked.Exchange(ref _statusObserved, 1) == 0)
                _recorder.RecordUi("ui-mounted");
            var status = RequireSession().Status;
            var error = status.Error?.Message ?? _service.CloseError;
            return error is null ? status.Phase.ToString() : status.Phase + ": " + error;
        }
    }

    public void Retry() => RequireSession().RequestClose();

    internal void Attach(ApplicationSession session)
    {
        if (Interlocked.CompareExchange(ref _session, session, null) is not null)
            throw new InvalidOperationException("The lifecycle model was attached more than once.");
    }

    internal ValueTask<bool> PrepareCloseAsync(CancellationToken cancellationToken) =>
        _service.PrepareCloseAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _recorder.Record("model-dispose-start");
        await Task.Delay(200);
        _recorder.Record("model-dispose");
        if (_mode == LifecycleFixtureMode.CleanupFailure)
            throw new InvalidOperationException("scope dispose failed");
    }

    private ApplicationSession RequireSession() =>
        _session ?? throw new InvalidOperationException("The lifecycle session is not attached.");
}

internal sealed class LifecycleHostedService(LifecycleRecorder recorder, LifecycleFixtureMode mode)
    : IHostedService,
        IDisposable
{
    private readonly TaskCompletionSource _finishAcceptedWrite = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private Task? _acceptedWrite;
    private int _prepareCount;

    internal string? CloseError { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        recorder.RecordUi("service-start");
        await Task.Delay(150, cancellationToken);
        recorder.RecordUi("service-start-continued");
        if (mode == LifecycleFixtureMode.StartupFailure)
            throw new InvalidOperationException("startup failed");
        _acceptedWrite = AcceptedWriteAsync();
    }

    internal async ValueTask<bool> PrepareCloseAsync(CancellationToken cancellationToken)
    {
        var attempt = Interlocked.Increment(ref _prepareCount);
        recorder.RecordUi("prepare-" + attempt + "-start");
        await Task.Delay(300, cancellationToken);
        recorder.RecordUi("prepare-" + attempt + "-continued");
        if (attempt == 1)
        {
            CloseError = "save failed; retry close";
            recorder.RecordUi("prepare-1-rejected");
            return false;
        }

        CloseError = null;
        _finishAcceptedWrite.TrySetResult();
        await (
            _acceptedWrite ?? throw new InvalidOperationException("No write was accepted.")
        ).WaitAsync(cancellationToken);
        recorder.RecordUi("prepare-2-accepted");
        return true;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        recorder.RecordUi("service-stop");
        _finishAcceptedWrite.TrySetResult();
        if (_acceptedWrite is { } acceptedWrite)
            await acceptedWrite;
        await Task.Delay(200, cancellationToken);
        recorder.RecordUi("service-stop-continued");
        if (mode == LifecycleFixtureMode.CleanupFailure)
            throw new InvalidOperationException("stop failed");
    }

    public void Dispose() => recorder.Record("service-dispose");

    private async Task AcceptedWriteAsync()
    {
        recorder.RecordUi("write-accepted");
        await _finishAcceptedWrite.Task;
        await Task.Delay(400);
        recorder.RecordUi("write-complete");
    }
}

internal sealed class LifecycleRecorder
{
    private readonly object _gate = new();
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;

    internal void RecordUi(string marker)
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException(marker + " left the lifecycle owner thread");
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException(marker + " did not run in STA");
        Record(marker + ":thread=" + Environment.CurrentManagedThreadId + ":sta=true");
    }

    internal void Record(string marker)
    {
        lock (_gate)
        {
            Console.WriteLine("lifecycle:" + marker);
            Console.Out.Flush();
        }
    }
}
