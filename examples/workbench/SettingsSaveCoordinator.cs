namespace Lucent.Examples.Workbench;

internal sealed class SettingsSaveCoordinator : IDisposable
{
    private readonly ISettingsRepository _repository;
    private readonly CancellationToken _lifetime;
    private readonly object _gate = new();
    private Task _queue = Task.CompletedTask;
    private Task _tail = Task.CompletedTask;
    private bool _disposed;

    public SettingsSaveCoordinator(ISettingsRepository repository, CancellationToken lifetime)
    {
        _repository = repository;
        _lifetime = lifetime;
    }

    public Task Tail { get { lock (_gate) return _tail; } }

    public void Save(WorkbenchSettings settings)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _queue = _queue.ContinueWith(_ => _repository.SaveAsync(settings, _lifetime),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default).Unwrap();
            _tail = AccumulateFailuresAsync(_tail, _queue);
        }
    }

    private static async Task AccumulateFailuresAsync(Task previous, Task current)
    {
        var failures = new List<Exception>();
        try { await previous.ConfigureAwait(false); }
        catch (Exception error) { Add(error); }
        try { await current.ConfigureAwait(false); }
        catch (Exception error) { Add(error); }

        if (failures.Count == 1) throw failures[0];
        if (failures.Count > 1) throw new AggregateException(failures);
        return;

        void Add(Exception error)
        {
            if (error is AggregateException aggregate)
                failures.AddRange(aggregate.Flatten().InnerExceptions);
            else
                failures.Add(error);
        }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
    }
}
