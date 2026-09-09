using System.Runtime.ExceptionServices;

namespace Lucent.Core;

/// <summary>Describes the current owner-visible state of an image load.</summary>
public enum ImageLoadStatus
{
    /// <summary>The resource is queued or being prepared.</summary>
    Loading,

    /// <summary>The resource is prepared and can be leased.</summary>
    Ready,

    /// <summary>The resource failed with an expected loading error.</summary>
    Failed,

    /// <summary>The resource could not be admitted within configured budgets.</summary>
    BudgetDeclined,
}

/// <summary>Describes the terminal result of an image preload.</summary>
public enum ImagePreloadStatus
{
    /// <summary>The resource is ready in the cache.</summary>
    Ready,

    /// <summary>The resource failed with an expected loading error.</summary>
    Failed,

    /// <summary>The preload demand was canceled.</summary>
    Canceled,

    /// <summary>The resource could not be admitted within configured budgets.</summary>
    BudgetDeclined,
}

/// <summary>Reports the terminal result of an image preload.</summary>
public sealed record ImagePreloadOutcome
{
    /// <summary>Initializes a preload outcome.</summary>
    public ImagePreloadOutcome(ImagePreloadStatus status, ImageLoadException? error = null) =>
        (Status, Error) = (status, error);

    /// <summary>Gets the terminal status.</summary>
    public ImagePreloadStatus Status { get; }

    /// <summary>Gets the expected failure, when applicable.</summary>
    public ImageLoadException? Error { get; }
}

/// <summary>Reports a point-in-time image-cache resource snapshot.</summary>
public sealed record ImageCacheMetrics
{
    /// <summary>Initializes an image-cache metrics snapshot.</summary>
    public ImageCacheMetrics(
        int queued,
        int active,
        int readyEntries,
        long cachedBytes,
        long leasedBytes,
        long temporaryBytes,
        long hits,
        long misses,
        long evictions,
        long budgetDeclines
    ) =>
        (
            Queued,
            Active,
            ReadyEntries,
            CachedBytes,
            LeasedBytes,
            TemporaryBytes,
            Hits,
            Misses,
            Evictions,
            BudgetDeclines
        ) = (
            queued,
            active,
            readyEntries,
            cachedBytes,
            leasedBytes,
            temporaryBytes,
            hits,
            misses,
            evictions,
            budgetDeclines
        );

    /// <summary>Gets the number of preparations waiting for a worker.</summary>
    public int Queued { get; }

    /// <summary>Gets the number of active preparations.</summary>
    public int Active { get; }

    /// <summary>Gets the number of ready cache entries.</summary>
    public int ReadyEntries { get; }

    /// <summary>Gets bytes retained only by unleased cache entries.</summary>
    public long CachedBytes { get; }

    /// <summary>Gets bytes retained by one or more active leases.</summary>
    public long LeasedBytes { get; }

    /// <summary>Gets temporary bytes reserved by active preparations.</summary>
    public long TemporaryBytes { get; }

    /// <summary>Gets ready-entry cache hits.</summary>
    public long Hits { get; }

    /// <summary>Gets newly created cache entries.</summary>
    public long Misses { get; }

    /// <summary>Gets unleased entries evicted from the cache.</summary>
    public long Evictions { get; }

    /// <summary>Gets requests declined by a configured budget.</summary>
    public long BudgetDeclines { get; }
}

/// <summary>An application-scoped, bounded cache of prepared portable images.</summary>
public sealed class ImageCache : IDisposable
{
    private readonly object _gate = new();
    private readonly IImagePreparer _preparer;
    private readonly ImageLoadLimits _limits;
    private readonly Dictionary<ImageCacheKey, CacheEntry> _entries = [];
    private readonly Dictionary<ReactiveScope, OwnerRegistration> _owners = new(
        ReferenceEqualityComparer.Instance
    );
    private readonly LinkedList<CacheEntry> _queue = [];
    private bool _disposed;
    private int _active;
    private long _cachedBytes;
    private long _leasedBytes;
    private long _reservedLeaseBytes;
    private long _temporaryBytes;
    private long _hits;
    private long _misses;
    private long _evictions;
    private long _budgetDeclines;
    private long _sequence;

    /// <summary>Initializes an application-scoped cache using the supplied preparer.</summary>
    public ImageCache(IImagePreparer preparer, ImageLoadLimits? limits = null)
    {
        _preparer = preparer ?? throw new ArgumentNullException(nameof(preparer));
        _limits = limits ?? ImageLoadLimits.Default;
    }

    /// <summary>Gets the limits enforced by this cache.</summary>
    public ImageLoadLimits Limits => _limits;

    /// <summary>Gets a thread-safe point-in-time metrics snapshot.</summary>
    public ImageCacheMetrics Metrics
    {
        get
        {
            lock (_gate)
            {
                return new ImageCacheMetrics(
                    _queue.Count,
                    _active,
                    _entries.Values.Count(entry => entry.State == EntryState.Ready),
                    _cachedBytes,
                    _leasedBytes,
                    _temporaryBytes,
                    _hits,
                    _misses,
                    _evictions,
                    _budgetDeclines
                );
            }
        }
    }

    /// <summary>Acquires owner-scoped demand for a source and rendition.</summary>
    public ImageLoadHandle Acquire(
        ReactiveScope owner,
        ImageSource source,
        ImageRendition rendition
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rendition);
        owner.Graph.CheckThread();
        ObjectDisposedException.ThrowIf(owner.IsDisposed, owner);

        rendition =
            _preparer.GetCacheRendition(source, rendition)
            ?? throw new InvalidOperationException(
                "The image preparer returned no cache rendition."
            );
        ImageLoadHandle handle;
        lock (_gate)
        {
            ThrowIfDisposed();
            var registration = GetOwnerRegistration_NoLock(owner);
            var entry = FindOrCreateEntry_NoLock(source, rendition);
            entry.FailureOwner = owner;
            handle = new ImageLoadHandle(this, owner, registration, entry);
            registration.Handles.Add(handle);
            entry.LastAccess = ++_sequence;
            if (entry.State == EntryState.Ready)
                _hits++;
            entry.Handles.Add(handle);
            if (entry.Shared is { } shared && !EnsureLeaseReservation_NoLock(shared))
            {
                _budgetDeclines++;
                handle.PublishDirect(
                    ImageLoadStatus.BudgetDeclined,
                    Budget("Live scene images exhaust the configured leased-memory budget."),
                    null,
                    entry.Generation
                );
            }
            else
                PublishInitial_NoLock(handle, entry);
        }
        return handle;
    }

    /// <summary>Prepares a source without retaining owner demand after completion.</summary>
    public Task<ImagePreloadOutcome> PreloadAsync(
        ReactiveScope owner,
        ImageSource source,
        ImageRendition rendition,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rendition);
        owner.Graph.CheckThread();
        ObjectDisposedException.ThrowIf(owner.IsDisposed, owner);

        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult(new ImagePreloadOutcome(ImagePreloadStatus.Canceled));
        rendition =
            _preparer.GetCacheRendition(source, rendition)
            ?? throw new InvalidOperationException(
                "The image preparer returned no cache rendition."
            );
        var demand = new PreloadDemand(this, cancellationToken);
        ImagePreloadOutcome? immediate = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (demand.IsCompleted)
                return demand.Task;
            var registration = GetOwnerRegistration_NoLock(owner);
            demand.SetOwner(registration);
            registration.Preloads.Add(demand);
            var entry = FindOrCreateEntry_NoLock(source, rendition);
            entry.FailureOwner = owner;
            demand.Attach(entry);
            entry.Preloads.Add(demand);
            entry.LastAccess = ++_sequence;
            if (entry.State == EntryState.Ready)
            {
                _hits++;
                entry.Preloads.Remove(demand);
                registration.Preloads.Remove(demand);
                immediate = new ImagePreloadOutcome(ImagePreloadStatus.Ready);
            }
            else if (entry.State == EntryState.Failed)
            {
                entry.Preloads.Remove(demand);
                registration.Preloads.Remove(demand);
                immediate = new ImagePreloadOutcome(ImagePreloadStatus.Failed, entry.Error);
            }
            else if (entry.State == EntryState.BudgetDeclined)
            {
                entry.Preloads.Remove(demand);
                registration.Preloads.Remove(demand);
                immediate = new ImagePreloadOutcome(ImagePreloadStatus.BudgetDeclined, entry.Error);
            }
        }
        if (immediate is not null)
            demand.Complete(immediate);
        return demand.Task;
    }

    /// <summary>Stops new work and releases unleased cached resources.</summary>
    public void Dispose()
    {
        List<PreparedImage>? release = null;
        List<PreloadDemand>? cancel = null;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (var entry in _entries.Values)
            {
                RequestCancellation_NoLock(entry);
                if (entry.Preloads.Count != 0)
                    (cancel ??= []).AddRange(entry.Preloads);
                entry.Preloads.Clear();
                entry.Handles.Clear();
                if (entry.Shared is { } shared && ReleaseCacheReference_NoLock(shared) is { } image)
                    (release ??= []).Add(image);
                entry.Shared = null;
                entry.State = EntryState.Abandoned;
            }
            _entries.Clear();
            foreach (var owner in _owners.Values)
                owner.MarkCacheDisposed();
            _owners.Clear();
            _queue.Clear();
        }
        if (cancel is not null)
            foreach (var demand in cancel)
                demand.Complete(new ImagePreloadOutcome(ImagePreloadStatus.Canceled));
        DisposeAll(release, "Image cache cleanup failed.");
    }

    internal void ReleaseHandle(
        ImageLoadHandle handle,
        OwnerRegistration registration,
        CacheEntry entry
    )
    {
        lock (_gate)
        {
            registration.Handles.Remove(handle);
            if (!entry.Handles.Remove(handle))
                return;
            ReleaseUnusedLeaseReservation_NoLock(entry);
            ReleaseDemand_NoLock(entry);
        }
    }

    internal void Retry(ImageLoadHandle sourceHandle, CacheEntry entry)
    {
        var publishGeneration = 0;
        var publishStatus = ImageLoadStatus.Loading;
        ImageLoadException? publishError = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            ObjectDisposedException.ThrowIf(!entry.Handles.Contains(sourceHandle), sourceHandle);
            if (
                entry.State == EntryState.Ready
                && sourceHandle.CurrentStatus == ImageLoadStatus.BudgetDeclined
            )
            {
                publishGeneration = entry.Generation;
                if (entry.Shared is not { } shared || !EnsureLeaseReservation_NoLock(shared))
                {
                    publishStatus = ImageLoadStatus.BudgetDeclined;
                    publishError = Budget(
                        "Live scene images exhaust the configured leased-memory budget."
                    );
                }
                else
                    publishStatus = ImageLoadStatus.Ready;
            }
            else if (entry.State is not (EntryState.Failed or EntryState.BudgetDeclined))
                return;
            else
            {
                entry.Error = null;
                entry.State = EntryState.Loading;
                entry.Generation++;
                publishGeneration = entry.Generation;
                Schedule_NoLock(entry);
                if (entry.State == EntryState.BudgetDeclined)
                {
                    publishStatus = ImageLoadStatus.BudgetDeclined;
                    publishError = entry.Error;
                    foreach (var handle in entry.Handles)
                        if (!ReferenceEquals(handle, sourceHandle))
                            handle.SchedulePublish(
                                publishStatus,
                                publishError,
                                null,
                                entry.Generation
                            );
                }
                else
                    foreach (var handle in entry.Handles)
                        if (!ReferenceEquals(handle, sourceHandle))
                            handle.SchedulePublish(
                                ImageLoadStatus.Loading,
                                null,
                                null,
                                entry.Generation
                            );
            }
        }
        if (publishGeneration != 0)
            sourceHandle.PublishDirect(publishStatus, publishError, null, publishGeneration);
    }

    internal ImageLease? AcquireLease(ImageLoadHandle handle, CacheEntry entry)
    {
        lock (_gate)
        {
            if (_disposed || !entry.Handles.Contains(handle) || entry.Shared is not { } shared)
                return null;
            entry.LastAccess = ++_sequence;
            return RetainLease_NoLock(shared);
        }
    }

    internal PreparedImage GetResource(SharedPreparedImage shared)
    {
        lock (_gate)
            return shared.Resource ?? throw new ObjectDisposedException(nameof(ImageLease));
    }

    internal ImageLease RetainLease(SharedPreparedImage shared)
    {
        lock (_gate)
            return RetainLease_NoLock(shared);
    }

    internal void ReleaseLease(SharedPreparedImage shared)
    {
        List<PreparedImage>? release = null;
        List<ImageLoadHandle>? declined = null;
        ImageLoadException? decline = null;
        var declineGeneration = 0;
        lock (_gate)
        {
            if (shared.Leases <= 0 || shared.References <= 0)
                return;
            shared.Leases--;
            shared.References--;
            if (shared.Leases == 0)
            {
                _leasedBytes -= shared.ByteCount;
                if (shared.Entry.Handles.Count != 0)
                    _ = EnsureLeaseReservation_NoLock(shared);
                if (shared.HasCacheReference)
                {
                    if (MakeCacheRoom_NoLock(shared.ByteCount, ref release, shared))
                        _cachedBytes += shared.ByteCount;
                    else
                    {
                        if (shared.Entry.Handles.Count != 0)
                        {
                            decline = Budget(
                                "Live image resources exhaust the configured cache budget."
                            );
                            shared.Entry.State = EntryState.BudgetDeclined;
                            shared.Entry.Error = decline;
                            shared.Entry.Shared = null;
                            shared.HasCacheReference = false;
                            shared.References--;
                            ReleaseLeaseReservation_NoLock(shared);
                            _budgetDeclines++;
                            declineGeneration = shared.Entry.Generation;
                            declined = [.. shared.Entry.Handles];
                        }
                        else
                        {
                            _entries.Remove(shared.Entry.Key);
                            shared.Entry.State = EntryState.Abandoned;
                            shared.Entry.Shared = null;
                            shared.HasCacheReference = false;
                            shared.References--;
                        }
                    }
                }
            }
            if (shared.References == 0)
                AddRelease(ref release, TakeResource_NoLock(shared));
        }
        DisposeAll(release, "Image lease cleanup failed.");
        if (declined is not null)
            foreach (var handle in declined)
                handle.SchedulePublish(
                    ImageLoadStatus.BudgetDeclined,
                    decline,
                    null,
                    declineGeneration
                );
    }

    internal void CancelPreload(PreloadDemand demand)
    {
        lock (_gate)
        {
            if (demand.Entry is { } entry && entry.Preloads.Remove(demand))
                ReleaseDemand_NoLock(entry);
            DetachPreload_NoLock(demand);
        }
        demand.Complete(new ImagePreloadOutcome(ImagePreloadStatus.Canceled));
    }

    private OwnerRegistration GetOwnerRegistration_NoLock(ReactiveScope owner)
    {
        if (_owners.TryGetValue(owner, out var found))
            return found;
        var registration = new OwnerRegistration(this, owner);
        _owners.Add(owner, registration);
        owner.OnDispose(registration.Dispose);
        return registration;
    }

    private void ReleaseOwner(OwnerRegistration registration)
    {
        List<PreloadDemand>? preloads = null;
        lock (_gate)
        {
            if (!_owners.Remove(registration.Owner))
                return;
            foreach (var handle in registration.Handles.ToArray())
            {
                handle.MarkOwnerDisposed();
                if (handle.Entry.Handles.Remove(handle))
                {
                    ReleaseUnusedLeaseReservation_NoLock(handle.Entry);
                    ReleaseDemand_NoLock(handle.Entry);
                }
            }
            registration.Handles.Clear();
            if (registration.Preloads.Count != 0)
            {
                preloads = [.. registration.Preloads];
                foreach (var preload in preloads)
                {
                    if (preload.Entry is { } entry && entry.Preloads.Remove(preload))
                        ReleaseDemand_NoLock(entry);
                    preload.Detach();
                }
                registration.Preloads.Clear();
            }
        }
        if (preloads is not null)
            foreach (var preload in preloads)
                preload.Complete(new ImagePreloadOutcome(ImagePreloadStatus.Canceled));
    }

    private static void DetachPreload_NoLock(PreloadDemand demand)
    {
        demand.Owner?.Preloads.Remove(demand);
        demand.Detach();
    }

    private CacheEntry FindOrCreateEntry_NoLock(ImageSource source, ImageRendition rendition)
    {
        var key = ImageCacheKey.Create(source, rendition);
        if (_entries.TryGetValue(key, out var found))
        {
            if (found.State is not EntryState.Abandoned)
                return found;
            _entries.Remove(key);
        }

        _misses++;
        var entry = new CacheEntry(key, source, rendition) { Generation = 1 };
        _entries.Add(key, entry);
        if (ValidateSource(source, rendition) is { } error)
        {
            Decline_NoLock(entry, error);
            return entry;
        }
        Schedule_NoLock(entry);
        return entry;
    }

    private ImageLoadException? ValidateSource(ImageSource source, ImageRendition rendition)
    {
        var asset = source.PackagedAsset;
        if (asset is not null && asset.ByteLength > _limits.MaximumEncodedBytes)
            return Budget("The encoded image exceeds the configured byte budget.");
        if (asset is not null && asset.Format is AssetFormat.Png or AssetFormat.Jpeg)
        {
            var metadata = source.Metadata;
            var pixels =
                (double)metadata.Width * metadata.Density * metadata.Height * metadata.Density;
            if (!double.IsFinite(pixels) || pixels > _limits.MaximumSourcePixels)
                return Budget("The encoded image exceeds the configured source-pixel budget.");
        }
        long output;
        try
        {
            output = checked((long)rendition.PixelWidth * rendition.PixelHeight * 4);
        }
        catch (OverflowException)
        {
            return Budget("The requested image rendition exceeds the configured output budget.");
        }
        return output > _limits.MaximumOutputBytes
            ? Budget("The requested image rendition exceeds the configured output budget.")
            : null;
    }

    private void Schedule_NoLock(CacheEntry entry)
    {
        if (_active < _limits.MaximumConcurrentPreparations)
        {
            Start_NoLock(entry);
            return;
        }
        if (_queue.Count >= _limits.MaximumQueuedRequests)
        {
            Decline_NoLock(entry, Budget("The image preparation queue is full."));
            return;
        }
        entry.QueueNode = _queue.AddLast(entry);
    }

    private void Start_NoLock(CacheEntry entry)
    {
        entry.QueueNode = null;
        entry.Cancellation = new CancellationTokenSource();
        entry.CancellationCallbacks = null;
        entry.PreparationFinished = false;
        var cancellation = entry.Cancellation;
        entry.State = EntryState.Loading;
        _active++;
        var generation = entry.Generation;
        var request = new ImagePreparationRequest(
            entry.Source,
            entry.Rendition,
            _limits,
            ReserveTemporary
        );
        _ = Task.Run(async () =>
        {
            PreparedImage? image = null;
            Exception? error = null;
            try
            {
                image = await _preparer
                    .PrepareAsync(request, cancellation.Token)
                    .ConfigureAwait(false);
                if (image is null)
                    throw new InvalidOperationException("The image preparer returned no resource.");
            }
            catch (Exception exception)
            {
                error = exception;
            }
            Task? cancellationCallbacks;
            lock (_gate)
            {
                entry.PreparationFinished = true;
                cancellationCallbacks = entry.CancellationCallbacks;
            }
            if (cancellationCallbacks is not null)
            {
                try
                {
                    await cancellationCallbacks.ConfigureAwait(false);
                }
                catch (Exception cancellationError)
                {
                    error = error is null
                        ? cancellationError
                        : new AggregateException(error, cancellationError);
                }
            }
            Complete(entry, generation, image, error);
        });
    }

    private void Complete(CacheEntry entry, int generation, PreparedImage? image, Exception? error)
    {
        List<ImageLoadHandle>? handles = null;
        List<PreloadDemand>? preloads = null;
        ImageLoadStatus status = ImageLoadStatus.Loading;
        ImageLoadException? expected = null;
        Exception? fatal = null;
        ReactiveScope? failureOwner = null;
        List<PreparedImage>? release = null;
        lock (_gate)
        {
            _active--;
            var cancellation = entry.Cancellation;
            entry.Cancellation = null;
            cancellation?.Dispose();
            if (
                error is not null
                && error is not ImageLoadException
                && (error is not OperationCanceledException || HasDemand(entry))
            )
            {
                fatal = error;
                failureOwner = entry.FailureOwner;
            }
            if (_disposed || entry.State == EntryState.Abandoned || entry.Generation != generation)
                AddRelease(ref release, image);
            else if (error is OperationCanceledException && !HasDemand(entry))
            {
                Abandon_NoLock(entry);
                AddRelease(ref release, image);
            }
            else if (error is ImageLoadException loadError)
            {
                expected = loadError;
                status =
                    loadError.Kind == ImageLoadFailureKind.BudgetDeclined
                        ? ImageLoadStatus.BudgetDeclined
                        : ImageLoadStatus.Failed;
                entry.State =
                    status == ImageLoadStatus.BudgetDeclined
                        ? EntryState.BudgetDeclined
                        : EntryState.Failed;
                entry.Error = loadError;
                if (status == ImageLoadStatus.BudgetDeclined)
                    _budgetDeclines++;
            }
            else if (fatal is not null)
            {
                entry.State = EntryState.Abandoned;
                _entries.Remove(entry.Key);
                AddRelease(ref release, image);
            }
            else if (image!.ByteCount > _limits.MaximumOutputBytes)
            {
                expected = Budget("The prepared image exceeds the configured output budget.");
                status = ImageLoadStatus.BudgetDeclined;
                entry.State = EntryState.BudgetDeclined;
                entry.Error = expected;
                _budgetDeclines++;
                AddRelease(ref release, image);
            }
            else if (!MakeCacheRoom_NoLock(image.ByteCount, ref release))
            {
                expected = Budget("Live image resources exhaust the configured cache budget.");
                status = ImageLoadStatus.BudgetDeclined;
                entry.State = EntryState.BudgetDeclined;
                entry.Error = expected;
                _budgetDeclines++;
                AddRelease(ref release, image);
            }
            else
            {
                entry.Shared = new SharedPreparedImage(this, entry, image);
                if (entry.Handles.Count != 0 && !EnsureLeaseReservation_NoLock(entry.Shared))
                {
                    expected = Budget(
                        "Live scene images exhaust the configured leased-memory budget."
                    );
                    status = ImageLoadStatus.BudgetDeclined;
                    entry.State = EntryState.BudgetDeclined;
                    entry.Error = expected;
                    entry.Shared.HasCacheReference = false;
                    entry.Shared.References = 0;
                    entry.Shared = null;
                    _budgetDeclines++;
                    AddRelease(ref release, image);
                }
                else
                {
                    entry.State = EntryState.Ready;
                    entry.Error = null;
                    entry.LastAccess = ++_sequence;
                    _cachedBytes += image.ByteCount;
                    status = ImageLoadStatus.Ready;
                }
            }

            if (entry.Handles.Count != 0)
                handles = [.. entry.Handles];
            if (entry.Preloads.Count != 0)
            {
                preloads = [.. entry.Preloads];
                entry.Preloads.Clear();
                foreach (var preload in preloads)
                    DetachPreload_NoLock(preload);
            }
            PumpQueue_NoLock();
        }

        var cleanupFailure = DisposeReleased(release);
        if (cleanupFailure is not null)
            fatal = fatal is null ? cleanupFailure : new AggregateException(fatal, cleanupFailure);
        if (fatal is not null)
        {
            if (handles is not null)
                foreach (var handle in handles)
                    handle.ScheduleFatal(fatal);
            if (preloads is not null)
                foreach (var preload in preloads)
                    preload.Fail(fatal);
            if (handles is null && preloads is null && failureOwner is not null)
            {
                var escapingFailure = fatal;
                failureOwner.Post(() => ExceptionDispatchInfo.Capture(escapingFailure).Throw());
            }
            return;
        }
        if (handles is not null)
            foreach (var handle in handles)
                handle.SchedulePublish(status, expected, entry.Shared, generation);
        if (preloads is not null)
        {
            var outcome = status switch
            {
                ImageLoadStatus.Ready => new ImagePreloadOutcome(ImagePreloadStatus.Ready),
                ImageLoadStatus.Failed => new ImagePreloadOutcome(
                    ImagePreloadStatus.Failed,
                    expected
                ),
                _ => new ImagePreloadOutcome(ImagePreloadStatus.BudgetDeclined, expected),
            };
            foreach (var preload in preloads)
                preload.Complete(outcome);
        }
    }

    private void PumpQueue_NoLock()
    {
        while (_active < _limits.MaximumConcurrentPreparations && _queue.First is { } node)
        {
            _queue.RemoveFirst();
            var entry = node.Value;
            entry.QueueNode = null;
            if (entry.State == EntryState.Loading && HasDemand(entry))
                Start_NoLock(entry);
        }
    }

    private static void PublishInitial_NoLock(ImageLoadHandle handle, CacheEntry entry)
    {
        switch (entry.State)
        {
            case EntryState.Ready:
                handle.PublishDirect(ImageLoadStatus.Ready, null, entry.Shared, entry.Generation);
                break;
            case EntryState.Failed:
                handle.PublishDirect(ImageLoadStatus.Failed, entry.Error, null, entry.Generation);
                break;
            case EntryState.BudgetDeclined:
                handle.PublishDirect(
                    ImageLoadStatus.BudgetDeclined,
                    entry.Error,
                    null,
                    entry.Generation
                );
                break;
            default:
                handle.PublishDirect(ImageLoadStatus.Loading, null, null, entry.Generation);
                break;
        }
    }

    private void Decline_NoLock(CacheEntry entry, ImageLoadException error)
    {
        entry.State = EntryState.BudgetDeclined;
        entry.Error = error;
        _budgetDeclines++;
    }

    private void ReleaseDemand_NoLock(CacheEntry entry)
    {
        if (HasDemand(entry))
            return;
        switch (entry.State)
        {
            case EntryState.Loading when entry.QueueNode is not null:
                _queue.Remove(entry.QueueNode);
                entry.QueueNode = null;
                Abandon_NoLock(entry);
                break;
            case EntryState.Loading:
                entry.State = EntryState.Abandoned;
                _entries.Remove(entry.Key);
                RequestCancellation_NoLock(entry);
                break;
            case EntryState.Failed:
            case EntryState.BudgetDeclined:
                _entries.Remove(entry.Key);
                entry.State = EntryState.Abandoned;
                break;
        }
    }

    private void Abandon_NoLock(CacheEntry entry)
    {
        entry.State = EntryState.Abandoned;
        _entries.Remove(entry.Key);
        RequestCancellation_NoLock(entry);
    }

    private bool MakeCacheRoom_NoLock(
        long required,
        ref List<PreparedImage>? release,
        SharedPreparedImage? excluded = null
    )
    {
        if (required > _limits.MaximumCachedBytes)
            return false;
        while (_cachedBytes + required > _limits.MaximumCachedBytes)
        {
            var candidate = _entries
                .Values.Where(entry =>
                    entry.State == EntryState.Ready
                    && !HasDemand(entry)
                    && entry.Shared is { Leases: 0 }
                    && !ReferenceEquals(entry.Shared, excluded)
                )
                .OrderBy(entry => entry.LastAccess)
                .FirstOrDefault();
            if (candidate is null)
                return false;
            _entries.Remove(candidate.Key);
            candidate.State = EntryState.Abandoned;
            var releasedImage = ReleaseCacheReference_NoLock(candidate.Shared!);
            candidate.Shared = null;
            _evictions++;
            AddRelease(ref release, releasedImage);
        }
        return true;
    }

    private TemporaryReservation ReserveTemporary(long byteCount)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_temporaryBytes + byteCount > _limits.MaximumTemporaryBytes)
                throw Budget("Concurrent image preparation exhausts the temporary-memory budget.");
            _temporaryBytes += byteCount;
            return new TemporaryReservation(this, byteCount);
        }
    }

    private void ReleaseTemporary(long byteCount)
    {
        lock (_gate)
            _temporaryBytes -= byteCount;
    }

    private ImageLease RetainLease_NoLock(SharedPreparedImage shared)
    {
        ObjectDisposedException.ThrowIf(shared.Resource is null || shared.References <= 0, shared);
        if (shared.Leases == 0)
        {
            if (!EnsureLeaseReservation_NoLock(shared))
                throw new InvalidOperationException(
                    "A ready image did not reserve its configured lease budget."
                );
            shared.LeaseReserved = false;
            _reservedLeaseBytes -= shared.ByteCount;
            if (shared.HasCacheReference)
                _cachedBytes -= shared.ByteCount;
            _leasedBytes += shared.ByteCount;
        }
        shared.Leases++;
        shared.References++;
        return new ImageLease(shared);
    }

    private PreparedImage? ReleaseCacheReference_NoLock(SharedPreparedImage shared)
    {
        if (!shared.HasCacheReference || shared.References <= 0)
            return null;
        shared.HasCacheReference = false;
        shared.References--;
        ReleaseLeaseReservation_NoLock(shared);
        if (shared.Leases == 0)
            _cachedBytes -= shared.ByteCount;
        return shared.References == 0 ? TakeResource_NoLock(shared) : null;
    }

    private static PreparedImage? TakeResource_NoLock(SharedPreparedImage shared)
    {
        var resource = shared.Resource;
        shared.Resource = null;
        return resource;
    }

    private static bool HasDemand(CacheEntry entry) =>
        entry.Handles.Count != 0 || entry.Preloads.Count != 0;

    private bool EnsureLeaseReservation_NoLock(SharedPreparedImage shared)
    {
        if (shared.Leases != 0 || shared.LeaseReserved)
            return true;
        if (_leasedBytes + _reservedLeaseBytes + shared.ByteCount > _limits.MaximumLeasedBytes)
            return false;
        shared.LeaseReserved = true;
        _reservedLeaseBytes += shared.ByteCount;
        return true;
    }

    private void ReleaseUnusedLeaseReservation_NoLock(CacheEntry entry)
    {
        if (entry.Handles.Count == 0 && entry.Shared is { } shared)
            ReleaseLeaseReservation_NoLock(shared);
    }

    private void ReleaseLeaseReservation_NoLock(SharedPreparedImage shared)
    {
        if (!shared.LeaseReserved)
            return;
        shared.LeaseReserved = false;
        _reservedLeaseBytes -= shared.ByteCount;
    }

    private static void RequestCancellation_NoLock(CacheEntry entry)
    {
        var cancellation = entry.Cancellation;
        if (
            cancellation is null
            || cancellation.IsCancellationRequested
            || entry.PreparationFinished
        )
            return;
        try
        {
            entry.CancellationCallbacks = cancellation.CancelAsync();
        }
        catch (Exception error)
        {
            entry.CancellationCallbacks = Task.FromException(error);
        }
    }

    private static ImageLoadException Budget(string message) =>
        new(ImageLoadFailureKind.BudgetDeclined, message);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static void DisposeAll(List<PreparedImage>? images, string message)
    {
        if (images is null)
            return;
        List<Exception>? errors = null;
        foreach (var image in images)
        {
            try
            {
                image.Dispose();
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        }
        if (errors is not null)
            throw new AggregateException(message, errors);
    }

    private static void AddRelease(ref List<PreparedImage>? images, PreparedImage? image)
    {
        if (image is not null)
            (images ??= []).Add(image);
    }

    private static AggregateException? DisposeReleased(List<PreparedImage>? images)
    {
        if (images is null)
            return null;
        List<Exception>? errors = null;
        foreach (var image in images)
        {
            try
            {
                image.Dispose();
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        }
        return errors is null
            ? null
            : new AggregateException("Prepared image cleanup failed.", errors);
    }

    internal sealed class CacheEntry(
        ImageCacheKey key,
        ImageSource source,
        ImageRendition rendition
    )
    {
        public ImageCacheKey Key { get; } = key;
        public ImageSource Source { get; } = source;
        public ImageRendition Rendition { get; } = rendition;
        public HashSet<ImageLoadHandle> Handles { get; } = [];
        public HashSet<PreloadDemand> Preloads { get; } = [];
        public EntryState State { get; set; } = EntryState.Loading;
        public ImageLoadException? Error { get; set; }
        public SharedPreparedImage? Shared { get; set; }
        public CancellationTokenSource? Cancellation { get; set; }
        public Task? CancellationCallbacks { get; set; }
        public bool PreparationFinished { get; set; }
        public LinkedListNode<CacheEntry>? QueueNode { get; set; }
        public int Generation { get; set; }
        public long LastAccess { get; set; }
        public ReactiveScope? FailureOwner { get; set; }
    }

    internal sealed class SharedPreparedImage(
        ImageCache owner,
        CacheEntry entry,
        PreparedImage resource
    )
    {
        public ImageCache Owner { get; } = owner;
        public CacheEntry Entry { get; } = entry;
        public PreparedImage? Resource { get; set; } = resource;
        public long ByteCount { get; } = resource.ByteCount;
        public int References { get; set; } = 1;
        public int Leases { get; set; }
        public bool HasCacheReference { get; set; } = true;
        public bool LeaseReserved { get; set; }
    }

    internal sealed class PreloadDemand : IDisposable
    {
        private readonly ImageCache _cache;
        private readonly TaskCompletionSource<ImagePreloadOutcome> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private CancellationTokenRegistration _cancellation;
        private int _completed;

        public PreloadDemand(ImageCache cache, CancellationToken cancellationToken)
        {
            _cache = cache;
            if (cancellationToken.CanBeCanceled)
                _cancellation = cancellationToken.Register(
                    static state =>
                    {
                        var demand = (PreloadDemand)state!;
                        demand._cache.CancelPreload(demand);
                    },
                    this
                );
        }

        public CacheEntry? Entry { get; private set; }
        public OwnerRegistration? Owner { get; private set; }
        public Task<ImagePreloadOutcome> Task => _completion.Task;
        public bool IsCompleted => Volatile.Read(ref _completed) != 0;

        public void Attach(CacheEntry entry) => Entry = entry;

        public void SetOwner(OwnerRegistration owner) => Owner = owner;

        public void Detach()
        {
            Entry = null;
            Owner = null;
        }

        public void Complete(ImagePreloadOutcome outcome)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
                return;
            Entry = null;
            _cancellation.Unregister();
            _completion.TrySetResult(outcome);
        }

        public void Fail(Exception error)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
                return;
            Entry = null;
            _cancellation.Unregister();
            _completion.TrySetException(error);
        }

        public void Dispose() => _cache.CancelPreload(this);
    }

    internal sealed class OwnerRegistration(ImageCache cache, ReactiveScope owner) : IDisposable
    {
        private ImageCache? _cache = cache;

        public ReactiveScope Owner { get; } = owner;
        public HashSet<ImageLoadHandle> Handles { get; } = [];
        public HashSet<PreloadDemand> Preloads { get; } = [];

        public void Dispose() => Interlocked.Exchange(ref _cache, null)?.ReleaseOwner(this);

        public void MarkCacheDisposed()
        {
            Interlocked.Exchange(ref _cache, null);
            foreach (var handle in Handles)
                handle.MarkOwnerDisposed();
            Handles.Clear();
            foreach (var preload in Preloads)
                preload.Detach();
            Preloads.Clear();
        }
    }

    internal enum EntryState
    {
        Loading,
        Ready,
        Failed,
        BudgetDeclined,
        Abandoned,
    }

    internal readonly record struct ImageCacheKey(
        string ContentHash,
        AssetFormat Format,
        ImageSource? UniqueSource,
        int PixelWidth,
        int PixelHeight
    )
    {
        public static ImageCacheKey Create(ImageSource source, ImageRendition rendition)
        {
            var asset = source.PackagedAsset;
            return asset is null
                ? new ImageCacheKey(
                    string.Empty,
                    default,
                    source,
                    rendition.PixelWidth,
                    rendition.PixelHeight
                )
                : new ImageCacheKey(
                    asset.ContentHash,
                    asset.Format,
                    null,
                    rendition.PixelWidth,
                    rendition.PixelHeight
                );
        }
    }

    private sealed class TemporaryReservation(ImageCache owner, long byteCount) : IDisposable
    {
        private ImageCache? _owner = owner;

        public void Dispose() =>
            Interlocked.Exchange(ref _owner, null)?.ReleaseTemporary(byteCount);
    }
}

/// <summary>An owner-thread view of one shared image load.</summary>
public sealed class ImageLoadHandle : IDisposable
{
    private readonly ImageCache _cache;
    private readonly ReactiveScope _owner;
    private readonly ImageCache.OwnerRegistration _registration;
    private readonly ImageCache.CacheEntry _entry;
    private Action? _changed;
    private bool _disposed;
    private int _generation;
    private ImageLoadStatus _status;
    private ImageLoadException? _error;

    internal ImageLoadHandle(
        ImageCache cache,
        ReactiveScope owner,
        ImageCache.OwnerRegistration registration,
        ImageCache.CacheEntry entry
    )
    {
        _cache = cache;
        _owner = owner;
        _registration = registration;
        _entry = entry;
    }

    internal ImageCache.CacheEntry Entry => _entry;
    internal ImageLoadStatus CurrentStatus => _status;

    /// <summary>Occurs on the owner thread when the visible load state changes.</summary>
    public event Action Changed
    {
        add
        {
            CheckActive();
            _changed += value;
        }
        remove
        {
            _owner.Graph.CheckThread();
            _changed -= value;
        }
    }

    /// <summary>Gets the current load state on the owner thread.</summary>
    public ImageLoadStatus Status
    {
        get
        {
            CheckActive();
            return _status;
        }
    }

    /// <summary>Gets the expected failure for a failed or declined load.</summary>
    public ImageLoadException? Error
    {
        get
        {
            CheckActive();
            return _error;
        }
    }

    /// <summary>Returns an independent resource lease when ready.</summary>
    public ImageLease? AcquireLease()
    {
        CheckActive();
        return Status == ImageLoadStatus.Ready ? _cache.AcquireLease(this, _entry) : null;
    }

    /// <summary>Retries a failed or budget-declined load.</summary>
    public void Retry()
    {
        CheckActive();
        _cache.Retry(this, _entry);
    }

    /// <summary>Releases this owner's demand.</summary>
    public void Dispose()
    {
        _owner.Graph.CheckThread();
        if (_disposed)
            return;
        _disposed = true;
        _changed = null;
        _cache.ReleaseHandle(this, _registration, _entry);
    }

    internal void MarkOwnerDisposed()
    {
        _disposed = true;
        _changed = null;
    }

    internal void PublishDirect(
        ImageLoadStatus status,
        ImageLoadException? error,
        ImageCache.SharedPreparedImage? resource,
        int generation
    )
    {
        if (_disposed || generation < _generation)
            return;
        _generation = generation;
        _status = status;
        _error = error;
        _changed?.Invoke();
    }

    internal void SchedulePublish(
        ImageLoadStatus status,
        ImageLoadException? error,
        ImageCache.SharedPreparedImage? resource,
        int generation
    ) => _owner.Post(() => PublishDirect(status, error, resource, generation));

    internal void ScheduleFatal(Exception error) =>
        _owner.Post(() => ExceptionDispatchInfo.Capture(error).Throw());

    private void CheckActive()
    {
        _owner.Graph.CheckThread();
        ObjectDisposedException.ThrowIf(_disposed || _owner.IsDisposed, this);
    }
}

/// <summary>An independent retained reference to a prepared image.</summary>
public sealed class ImageLease : IDisposable
{
    private ImageCache.SharedPreparedImage? _shared;

    internal ImageLease(ImageCache.SharedPreparedImage shared) => _shared = shared;

    /// <summary>Gets the prepared resource retained by this lease.</summary>
    public PreparedImage Resource
    {
        get
        {
            var shared =
                Volatile.Read(ref _shared) ?? throw new ObjectDisposedException(nameof(ImageLease));
            return shared.Owner.GetResource(shared);
        }
    }

    /// <summary>Creates another independent lease for the same resource.</summary>
    public ImageLease Retain()
    {
        var shared =
            Volatile.Read(ref _shared) ?? throw new ObjectDisposedException(nameof(ImageLease));
        return shared.Owner.RetainLease(shared);
    }

    /// <summary>Releases this retained reference.</summary>
    public void Dispose()
    {
        var shared = Interlocked.Exchange(ref _shared, null);
        shared?.Owner.ReleaseLease(shared);
    }
}
