namespace Lucent.Platform.Windows.Activation;

/// <summary>One latest valid protocol, one finite rejection and one coalesced launch.</summary>
public sealed class ActivationInbox : IDisposable
{
    private readonly object _gate = new();
    private ActivationEnvelope? _protocol;
    private ActivationEnvelope? _rejected;
    private ActivationEnvelope? _launch;
    private long _protocolOrder;
    private long _rejectedOrder;
    private long _launchOrder;
    private long _nextOrder;
    private long _deliveredProtocolOrder;
    private long _protocolWaitVersion;
    private TaskCompletionSource? _protocolDelivered;
    private Action<Action>? _post;
    private Action<ActivationEnvelope>? _receiver;
    private bool _posted;
    private bool _closing;
    private bool _disposed;

    internal long Version
    {
        get
        {
            lock (_gate)
                return _nextOrder;
        }
    }

    internal long ProtocolVersion
    {
        get
        {
            lock (_gate)
                return _protocolOrder;
        }
    }

    internal long LaunchVersion
    {
        get
        {
            lock (_gate)
                return _launchOrder;
        }
    }

    internal Task WaitForProtocolDeliveryAsync(long version)
    {
        lock (_gate)
        {
            if (_closing || _disposed || _deliveredProtocolOrder >= version)
                return Task.CompletedTask;
            _protocolWaitVersion = Math.Max(_protocolWaitVersion, version);
            _protocolDelivered ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            return _protocolDelivered.Task;
        }
    }

    /// <summary>Whether new deliveries are rejected during close preparation.</summary>
    public bool IsClosing
    {
        get
        {
            lock (_gate)
                return _closing || _disposed;
        }
    }

    /// <summary>Copies a delivery; returns false when the owner is closing or disposed.</summary>
    public bool Offer(ActivationEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        Action<Action>? post = null;
        lock (_gate)
        {
            if (_closing || _disposed)
                return false;
            var order = ++_nextOrder;
            envelope = envelope.WithSequence(order);
            if (envelope.Kind == ActivationKind.Protocol)
            {
                _protocol = envelope;
                _protocolOrder = order;
            }
            else if (envelope.Kind == ActivationKind.Rejected)
            {
                _rejected = envelope;
                _rejectedOrder = order;
            }
            else
            {
                _launch = envelope;
                _launchOrder = order;
            }
            if (_receiver is not null && !_posted)
            {
                _posted = true;
                post = _post;
            }
        }
        try
        {
            post?.Invoke(DrainOnOwner);
        }
        catch
        {
            BeginClose();
            return false;
        }
        return true;
    }

    /// <summary>Consumes the latest pre-mount request. Call on the owner before attaching a receiver.</summary>
    public ActivationEnvelope? TakeStartup()
    {
        lock (_gate)
        {
            if (_receiver is not null || _disposed)
                throw new InvalidOperationException(
                    "Startup activation is available only before owner attachment."
                );
            // A protocol wins over a plain launch. A plain launch only requests attention.
            var request = _protocol ?? _rejected ?? _launch;
            if (request?.Kind == ActivationKind.Protocol)
                _protocol = null;
            else if (request?.Kind == ActivationKind.Rejected)
                _rejected = null;
            else if (request?.Kind == ActivationKind.Launch)
                _launch = null;
            return request;
        }
    }

    /// <summary>Attaches the owner dispatcher and delivery policy exactly once.</summary>
    public void Attach(Action<Action> postToOwner, Action<ActivationEnvelope> receive)
    {
        ArgumentNullException.ThrowIfNull(postToOwner);
        ArgumentNullException.ThrowIfNull(receive);
        bool schedule;
        lock (_gate)
        {
            if (_receiver is not null || _disposed)
                throw new InvalidOperationException(
                    "Activation owner is already attached or disposed."
                );
            _post = postToOwner;
            _receiver = receive;
            schedule =
                (_protocol is not null || _rejected is not null || _launch is not null)
                && !_closing;
            _posted = schedule;
        }
        if (schedule)
        {
            try
            {
                postToOwner(DrainOnOwner);
            }
            catch
            {
                BeginClose();
                throw;
            }
        }
    }

    /// <summary>Rejects deliveries while application close preparation runs.</summary>
    public void BeginClose()
    {
        lock (_gate)
        {
            _closing = true;
            _protocol = null;
            _rejected = null;
            _launch = null;
            _deliveredProtocolOrder = _protocolOrder;
            _protocolDelivered?.TrySetResult();
            _protocolDelivered = null;
            _protocolWaitVersion = 0;
        }
    }

    /// <summary>Resumes delivery after the application declines close.</summary>
    public void CancelClose()
    {
        lock (_gate)
            if (!_disposed)
                _closing = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _closing = true;
            _protocol = null;
            _rejected = null;
            _launch = null;
            _receiver = null;
            _post = null;
            _deliveredProtocolOrder = _protocolOrder;
            _protocolDelivered?.TrySetResult();
            _protocolDelivered = null;
            _protocolWaitVersion = 0;
        }
    }

    private void DrainOnOwner()
    {
        while (true)
        {
            ActivationEnvelope? next;
            Action<ActivationEnvelope>? receiver;
            lock (_gate)
            {
                if (_closing || _disposed || _receiver is null)
                {
                    _posted = false;
                    return;
                }
                if (_protocol is null && _rejected is null && _launch is null)
                {
                    _posted = false;
                    return;
                }
                if (
                    _protocol is not null
                    && (_rejected is null || _protocolOrder <= _rejectedOrder)
                    && (_launch is null || _protocolOrder <= _launchOrder)
                )
                {
                    next = _protocol;
                    _protocol = null;
                }
                else if (
                    _rejected is not null
                    && (_launch is null || _rejectedOrder <= _launchOrder)
                )
                {
                    next = _rejected;
                    _rejected = null;
                }
                else
                {
                    next = _launch;
                    _launch = null;
                }
                receiver = _receiver;
            }
            try
            {
                receiver(next!);
            }
            finally
            {
                if (next!.Kind == ActivationKind.Protocol)
                {
                    lock (_gate)
                    {
                        _deliveredProtocolOrder = next.Sequence;
                        if (_deliveredProtocolOrder >= _protocolWaitVersion)
                        {
                            _protocolDelivered?.TrySetResult();
                            _protocolDelivered = null;
                            _protocolWaitVersion = 0;
                        }
                    }
                }
            }
        }
    }
}
