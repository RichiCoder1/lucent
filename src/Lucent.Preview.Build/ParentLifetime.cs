namespace Lucent.Preview.Build;

internal sealed class ParentLifetime : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _source = new();
    private readonly ConsoleCancelEventHandler _handler;

    internal ParentLifetime()
    {
        Token = _source.Token;
        _handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            Cancel();
        };
        Console.CancelKeyPress += _handler;
        _ = Task.Run(ObserveParentAsync);
    }

    internal CancellationToken Token { get; }

    private async Task ObserveParentAsync()
    {
        try
        {
            _ = await Console.In.ReadLineAsync();
        }
        catch (IOException) { }
        finally
        {
            Cancel();
        }
    }

    private void Cancel()
    {
        lock (_gate)
            _source?.Cancel();
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= _handler;
        lock (_gate)
        {
            var source = _source;
            _source = null;
            source?.Dispose();
        }
    }
}
