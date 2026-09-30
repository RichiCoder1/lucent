namespace Lucent.IssueBrowser;

internal enum NavigationPersistenceNotice
{
    ReadUnavailable,
    SnapshotRejected,
    CaptureUnavailable,
    WriteUnavailable,
    RestoreUnavailable,
}

/// <summary>Opt-in, application-owned navigation persistence for the offline Issue Browser.</summary>
public sealed class IssueBrowserNavigationPersistence
{
    internal const string Scope = "issue-browser-fixture-routes-v1";
    private readonly IssueBrowserNavigationStorage _storage;
    private readonly Action<NavigationPersistenceNotice> _report;
    private readonly TaskCompletionSource<bool> _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly NavigationRestoration _restoration = new(
        IssueBrowserRouting.Table,
        Scope,
        IssueBrowserRoutes.Issues(),
        static match =>
            match.DefinitionId.Value == "issues"
            || (match.DefinitionId.Value == "issue" && match.GetValue(0).Signed32 > 0),
        options: new(
            NavigationRestorationMode.Journal,
            maximumEntries: 32,
            stateCodecs: NavigationRestorationStateCodecs.Interaction
        )
    );
    private ReactiveScope? _owner;
    private IssueBrowserViewState? _view;
    private SynchronizationContext? _applicationContext;
    private long _captureGeneration;
    private bool _captureQueued;
    private bool _bound;
    private bool _mounted;
    private bool _started;
    private bool _closing;
    private bool _stopped;

    /// <summary>Configures explicit storage without reading or creating files until startup.</summary>
    public IssueBrowserNavigationPersistence(string path)
        : this(
            new IssueBrowserNavigationStorage(path),
            static notice => Console.Error.WriteLine("navigation-persistence status=" + notice)
        ) { }

    internal IssueBrowserNavigationPersistence(
        IssueBrowserNavigationStorage storage,
        Action<NavigationPersistenceNotice> report
    )
    {
        _storage = storage;
        _report = report;
    }

    internal bool AllowsNavigation => !_closing && !_stopped;

    /// <summary>Installs lifecycle callbacks; pass this same owner to <see cref="IssueBrowserStructure.CreateHosted"/>.</summary>
    public void Configure(LucentApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.OnMounted(RestoreAsync).OnPrepareClose(PrepareCloseAsync).OnStop(StopAsync);
    }

    internal void Bind(ReactiveScope owner, IssueBrowserViewState view, IssueBrowserState browser)
    {
        if (_bound)
            throw new InvalidOperationException("Navigation persistence may bind only once.");
        _bound = true;
        _owner = owner;
        _view = view;
        _ = view.Navigation.RegisterCommitted(owner, _ => QueueCapture());
        _ = owner.Effect(
            () =>
            {
                if (!browser.IsLoading)
                    _ready.TrySetResult(true);
            },
            "issue-browser.navigation-ready"
        );
        owner.OnDispose(() =>
        {
            CancelCapture();
            _ready.TrySetResult(false);
            _owner = null;
            _view = null;
        });
    }

    // This callback runs through ApplicationSession's owner-thread synchronization context.
    internal async ValueTask RestoreAsync(ApplicationSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_mounted || _view is not { } view)
            throw new InvalidOperationException(
                "Navigation persistence requires one mounted view."
            );
        _mounted = true;
        _applicationContext =
            SynchronizationContext.Current
            ?? throw new InvalidOperationException(
                "Navigation persistence requires the application owner context."
            );
        var input = await _storage.ReadAsync();
        if (!await _ready.Task || !ReferenceEquals(view, _view) || _stopped)
            return;

        if (input.Status == NavigationStorageStatus.Unavailable)
            _report(NavigationPersistenceNotice.ReadUnavailable);
        else if (input.Status == NavigationStorageStatus.TooLarge)
            _report(NavigationPersistenceNotice.SnapshotRejected);

        // A user action during loading remains authoritative. Replay is startup-only.
        if (view.Navigation.Current is null && view.Navigation.Pending is null)
        {
            var plan = _restoration.Decode(input.Utf8.Span);
            if (
                plan.Status
                is not NavigationRestorationStatus.Ready
                    and not NavigationRestorationStatus.NoSnapshot
            )
                _report(NavigationPersistenceNotice.SnapshotRejected);
            var outcome = await view.Navigation.Restore(plan).Completion;
            if (outcome.Kind is NavigationOutcomeKind.Failed or NavigationOutcomeKind.Stayed)
                _report(NavigationPersistenceNotice.RestoreUnavailable);
        }
        if (!ReferenceEquals(view, _view) || _stopped)
            return;
        _started = true;
        QueueCapture();
    }

    internal async ValueTask<bool> PrepareCloseAsync(
        ApplicationCloseContext context,
        CancellationToken cancellationToken
    )
    {
        _ = cancellationToken;
        _closing = true;
        CancelCapture();
        context.OnDeclined(() =>
        {
            if (_stopped)
                return;
            _closing = false;
            QueueCapture();
        });
        // This is an independent view snapshot, never a document/status-save acknowledgement.
        var write = Capture();
        if (write is not null)
            ReportWrite(await write);
        await _storage.DrainAsync();
        return true;
    }

    internal async ValueTask StopAsync(ApplicationCleanupContext context)
    {
        _ = context;
        _stopped = true;
        CancelCapture();
        await _storage.StopAsync();
    }

    private void QueueCapture()
    {
        if (!_started || !AllowsNavigation || _captureQueued || _owner is not { } owner)
            return;
        _captureQueued = true;
        var generation = ++_captureGeneration;
        // A scope post can run during the navigation batch's retiring phase. The
        // application queue runs after the synchronous transaction returns to its host.
        _applicationContext!.Post(
            _ =>
            {
                if (generation != _captureGeneration)
                    return;
                _captureQueued = false;
                if (!AllowsNavigation || !ReferenceEquals(owner, _owner) || owner.IsDisposed)
                    return;
                if (Capture() is { } write)
                    _ = ObserveWriteAsync(owner, write);
            },
            null
        );
    }

    private Task<NavigationStorageStatus>? Capture()
    {
        if (!_started || _view is not { } view || view.Navigation.IsDisposed)
            return null;
        var capture = _restoration.Capture(view.Navigation);
        if (capture.Status == NavigationRestorationStatus.Ready)
            return _storage.Submit(capture.Utf8);
        if (capture.Status == NavigationRestorationStatus.NoSnapshot)
            return _storage.Submit(null);
        _report(NavigationPersistenceNotice.CaptureUnavailable);
        return null;
    }

    private async Task ObserveWriteAsync(ReactiveScope owner, Task<NavigationStorageStatus> write)
    {
        var status = await write.ConfigureAwait(false);
        if (status == NavigationStorageStatus.Unavailable)
            _ = owner.Post(() => ReportWrite(status));
    }

    private void ReportWrite(NavigationStorageStatus status)
    {
        if (status == NavigationStorageStatus.Unavailable)
            _report(NavigationPersistenceNotice.WriteUnavailable);
    }

    private void CancelCapture()
    {
        _captureGeneration++;
        _captureQueued = false;
    }
}
