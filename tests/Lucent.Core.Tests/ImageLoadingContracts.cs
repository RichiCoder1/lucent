using System.Collections.Concurrent;
using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ImageLoadingContracts
{
    [TestMethod]
    public void RasterImagesOwnImmutablePremultipliedPixelsAndReleaseThemInConstantTime()
    {
        byte[] pixels = [10, 20, 30, 40];
        using var image = new RasterImage(1, 1, pixels);
        pixels[0] = 40;
        Assert.AreEqual((byte)10, image.Pixels.Span[0]);
        Assert.ThrowsExactly<ArgumentException>(() => new RasterImage(1, 1, [41, 0, 0, 40]));

        image.Dispose();
        Assert.IsTrue(image.IsDisposed);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = image.Pixels);
    }

    [TestMethod]
    public void ConcurrentDemandDeduplicatesAndIndependentLeasesOutliveCache()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("image-owner");
        var preparer = new ControlledPreparer();
        var cache = new ImageCache(preparer);
        var source = Source('a');
        using var first = cache.Acquire(scope, source, new ImageRendition(2, 2));
        using var second = cache.Acquire(scope, source, new ImageRendition(2, 2));
        var changedThreads = new List<int>();
        first.Changed += () => changedThreads.Add(Environment.CurrentManagedThreadId);
        var call = preparer.Next();
        var resource = new TrackingImage(2, 2, 16);
        call.Complete(resource);
        DrainCompletion(graph, cache, () => first.Status != ImageLoadStatus.Loading);

        Assert.AreEqual(1, preparer.CallCount);
        Assert.AreEqual(ImageLoadStatus.Ready, first.Status);
        Assert.AreEqual(ImageLoadStatus.Ready, second.Status);
        CollectionAssert.AreEqual(new[] { Environment.CurrentManagedThreadId }, changedThreads);
        var lease = first.AcquireLease();
        Assert.IsNotNull(lease);
        var retained = lease.Retain();
        first.Dispose();
        second.Dispose();
        cache.Dispose();
        Assert.IsFalse(resource.IsDisposed);
        lease.Dispose();
        Assert.IsFalse(resource.IsDisposed);
        retained.Dispose();
        Assert.IsTrue(resource.IsDisposed);
    }

    [TestMethod]
    public void ReadyCacheHitIsVisibleSynchronouslyWithoutAnotherOwnerDrain()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("image-owner");
        var preparer = new ControlledPreparer();
        using var cache = new ImageCache(preparer);
        var source = Source('b');
        using var first = cache.Acquire(scope, source, new ImageRendition(1, 1));
        preparer.Next().Complete(new TrackingImage(1, 1, 4));
        DrainCompletion(graph, cache, () => first.Status != ImageLoadStatus.Loading);
        first.Dispose();

        using var hit = cache.Acquire(scope, source, new ImageRendition(1, 1));
        Assert.AreEqual(ImageLoadStatus.Ready, hit.Status);
        using var hitLease = hit.AcquireLease();
        Assert.IsNotNull(hitLease);
        Assert.AreEqual(1, cache.Metrics.Hits);
    }

    [TestMethod]
    public void QueueAdmissionIsBoundedAndZeroDemandWorkCannotPublishLateResources()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("image-owner");
        var preparer = new ControlledPreparer();
        using var cache = new ImageCache(
            preparer,
            new ImageLoadLimits(maximumQueuedRequests: 1, maximumConcurrentPreparations: 1)
        );
        var active = cache.Acquire(scope, Source('c'), new ImageRendition(1, 1));
        var queued = cache.Acquire(scope, Source('d'), new ImageRendition(1, 1));
        using var declined = cache.Acquire(scope, Source('e'), new ImageRendition(1, 1));
        Assert.AreEqual(ImageLoadStatus.BudgetDeclined, declined.Status);
        queued.Dispose();
        active.Dispose();

        var call = preparer.Next();
        Assert.IsTrue(
            SpinWait.SpinUntil(() => call.CancellationToken.IsCancellationRequested, 5000)
        );
        var late = new TrackingImage(1, 1, 4);
        call.Complete(late);
        DrainCompletion(graph, cache, () => late.IsDisposed);
        Assert.IsTrue(late.IsDisposed);
        Assert.AreEqual(1, preparer.CallCount);
        Assert.AreEqual(0, cache.Metrics.Queued);
    }

    [TestMethod]
    public async Task ExpectedFailuresAndPreloadCancellationRemainExplicit()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("image-owner");
        var preparer = new ControlledPreparer();
        using var cache = new ImageCache(preparer);
        using var handle = cache.Acquire(scope, Source('f'), new ImageRendition(1, 1));
        preparer.Next().Fail(new ImageLoadException(ImageLoadFailureKind.InvalidData, "bad image"));
        DrainCompletion(graph, cache, () => handle.Status != ImageLoadStatus.Loading);
        Assert.AreEqual(ImageLoadStatus.Failed, handle.Status);
        Assert.AreEqual(ImageLoadFailureKind.InvalidData, handle.Error?.Kind);

        using var cancellation = new CancellationTokenSource();
        var preload = cache.PreloadAsync(
            scope,
            Source('0'),
            new ImageRendition(1, 1),
            cancellation.Token
        );
        var call = preparer.Next();
        cancellation.Cancel();
        Assert.AreEqual(ImagePreloadStatus.Canceled, (await preload).Status);
        var canceledResource = new TrackingImage(1, 1, 4);
        call.Complete(canceledResource);
        DrainCompletion(graph, cache, () => canceledResource.IsDisposed);
    }

    [TestMethod]
    public void LiveLeaseExhaustionDeclinesNewDemandWithoutReleasingTheLiveResource()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("image-owner");
        var preparer = new ControlledPreparer();
        using var cache = new ImageCache(
            preparer,
            new ImageLoadLimits(maximumCachedBytes: 16, maximumLeasedBytes: 16)
        );
        using var first = cache.Acquire(scope, Source('1'), new ImageRendition(2, 2));
        var firstResource = new TrackingImage(2, 2, 16);
        preparer.Next().Complete(firstResource);
        DrainCompletion(graph, cache, () => first.Status != ImageLoadStatus.Loading);
        using var lease = first.AcquireLease();
        Assert.IsNotNull(lease);

        using var second = cache.Acquire(scope, Source('2'), new ImageRendition(2, 2));
        var refusedResource = new TrackingImage(2, 2, 16);
        preparer.Next().Complete(refusedResource);
        DrainCompletion(graph, cache, () => second.Status != ImageLoadStatus.Loading);
        Assert.AreEqual(ImageLoadStatus.BudgetDeclined, second.Status);
        Assert.AreEqual(ImageLoadFailureKind.BudgetDeclined, second.Error?.Kind);
        Assert.IsTrue(refusedResource.IsDisposed);
        Assert.IsFalse(firstResource.IsDisposed);
        using var retained = lease.Retain();
    }

    [TestMethod]
    public void ReleasingLastLeaseDeclinesSurvivingDemandWhenCacheCannotReAdmitIt()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("image-owner");
        var preparer = new ControlledPreparer();
        using var cache = new ImageCache(
            preparer,
            new ImageLoadLimits(maximumCachedBytes: 16, maximumLeasedBytes: 16)
        );
        using var first = cache.Acquire(scope, Source('a'), new ImageRendition(2, 2));
        preparer.Next().Complete(new TrackingImage(2, 2, 16));
        DrainCompletion(graph, cache, () => first.Status == ImageLoadStatus.Ready);
        var lease = first.AcquireLease();
        Assert.IsNotNull(lease);

        var preload = cache.PreloadAsync(scope, Source('b'), new ImageRendition(2, 2));
        preparer.Next().Complete(new TrackingImage(2, 2, 16));
        Assert.AreEqual(ImagePreloadStatus.Ready, preload.GetAwaiter().GetResult().Status);
        using var pinned = cache.Acquire(scope, Source('b'), new ImageRendition(2, 2));
        Assert.AreEqual(ImageLoadStatus.BudgetDeclined, pinned.Status);

        lease.Dispose();
        DrainCompletion(graph, cache, () => first.Status != ImageLoadStatus.Ready);
        Assert.AreEqual(ImageLoadStatus.BudgetDeclined, first.Status);
        Assert.AreEqual(ImageLoadFailureKind.BudgetDeclined, first.Error?.Kind);
        Assert.AreEqual(0L, cache.Metrics.LeasedBytes);

        pinned.Dispose();
        first.Retry();
        preparer.Next().Complete(new TrackingImage(2, 2, 16));
        DrainCompletion(graph, cache, () => first.Status == ImageLoadStatus.Ready);
        using var retryLease = first.AcquireLease();
        Assert.IsNotNull(retryLease);
    }

    [TestMethod]
    public void RetryPublishesBudgetDeclineWhenAdmissionIsAlreadyFull()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("image-owner");
        var preparer = new ControlledPreparer();
        using var cache = new ImageCache(
            preparer,
            new ImageLoadLimits(maximumQueuedRequests: 1, maximumConcurrentPreparations: 1)
        );
        using var retried = cache.Acquire(scope, Source('c'), new ImageRendition(1, 1));
        preparer.Next().Fail(new ImageLoadException(ImageLoadFailureKind.InvalidData, "bad image"));
        DrainCompletion(graph, cache, () => retried.Status == ImageLoadStatus.Failed);

        using var active = cache.Acquire(scope, Source('d'), new ImageRendition(1, 1));
        var activeCall = preparer.Next();
        using var queued = cache.Acquire(scope, Source('e'), new ImageRendition(1, 1));

        retried.Retry();
        Assert.AreEqual(ImageLoadStatus.BudgetDeclined, retried.Status);
        Assert.AreEqual(ImageLoadFailureKind.BudgetDeclined, retried.Error?.Kind);

        queued.Dispose();
        active.Dispose();
        Assert.IsTrue(
            SpinWait.SpinUntil(() => activeCall.CancellationToken.IsCancellationRequested, 5000)
        );
        var late = new TrackingImage(1, 1, 4);
        activeCall.Complete(late);
        DrainCompletion(graph, cache, () => late.IsDisposed);
        Assert.IsTrue(late.IsDisposed);
    }

    [TestMethod]
    public void CancellationCallbackBugsRemainObservableAfterDemandIsReleased()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("image-owner");
        var preparer = new ControlledPreparer();
        using var cache = new ImageCache(preparer);
        var handle = cache.Acquire(scope, Source('3'), new ImageRendition(1, 1));
        var call = preparer.Next();
        using var callback = call.CancellationToken.Register(() =>
            throw new InvalidOperationException("bad cancellation callback")
        );
        handle.Dispose();
        call.Complete(new TrackingImage(1, 1, 4));
        var failure = DrainFailure(graph);
        StringAssert.Contains(failure.ToString(), "bad cancellation callback");
    }

    [TestMethod]
    public void WarmDemandCanRetryLeaseAdmissionWithoutDecodingAgain()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("image-owner");
        var preparer = new ControlledPreparer();
        using var cache = new ImageCache(
            preparer,
            new ImageLoadLimits(maximumCachedBytes: 16, maximumLeasedBytes: 16)
        );
        var first = cache.Acquire(scope, Source('5'), new ImageRendition(2, 2));
        preparer.Next().Complete(new TrackingImage(2, 2, 16));
        DrainCompletion(graph, cache, () => first.Status == ImageLoadStatus.Ready);
        var oldSceneLease = first.AcquireLease();
        Assert.IsNotNull(oldSceneLease);
        first.Dispose();

        var preload = cache.PreloadAsync(scope, Source('6'), new ImageRendition(2, 2));
        preparer.Next().Complete(new TrackingImage(2, 2, 16));
        Assert.AreEqual(ImagePreloadStatus.Ready, preload.GetAwaiter().GetResult().Status);
        using var replacement = cache.Acquire(scope, Source('6'), new ImageRendition(2, 2));
        Assert.AreEqual(ImageLoadStatus.BudgetDeclined, replacement.Status);
        oldSceneLease.Dispose();

        replacement.Retry();
        Assert.AreEqual(ImageLoadStatus.Ready, replacement.Status);
        using var replacementLease = replacement.AcquireLease();
        Assert.IsNotNull(replacementLease);
        Assert.AreEqual(2, preparer.CallCount, "Retrying lease admission must not decode again.");
    }

    [TestMethod]
    public void UnrequestedPreparerCancellationWithLiveDemandIsFatal()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("image-owner");
        var preparer = new ControlledPreparer();
        using var cache = new ImageCache(preparer);
        using var handle = cache.Acquire(scope, Source('7'), new ImageRendition(1, 1));
        preparer.Next().Fail(new OperationCanceledException("unexpected cancellation"));
        var failure = DrainFailure(graph);
        StringAssert.Contains(failure.ToString(), "unexpected cancellation");
    }

    [TestMethod]
    public void CacheDisposalPreservesAndReleasesAnInflightTemporaryReservation()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("image-owner");
        var preparer = new ControlledPreparer();
        var cache = new ImageCache(preparer, new ImageLoadLimits(maximumTemporaryBytes: 16));
        using var handle = cache.Acquire(scope, Source('8'), new ImageRendition(1, 1));
        var call = preparer.Next();
        var reservation = call.Request.ReserveTemporaryBytes(8);
        Assert.AreEqual(8L, cache.Metrics.TemporaryBytes);

        cache.Dispose();
        Assert.AreEqual(
            8L,
            cache.Metrics.TemporaryBytes,
            "Disposal must not erase an in-flight reservation before its owner releases it."
        );
        reservation.Dispose();
        Assert.AreEqual(0L, cache.Metrics.TemporaryBytes);

        var late = new TrackingImage(1, 1, 4);
        call.Complete(late);
        Assert.IsTrue(SpinWait.SpinUntil(() => late.IsDisposed, 5000));
    }

    [TestMethod]
    public void CacheDisposalRejectsANewTemporaryReservationWithoutRelabelingTheFailure()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("image-owner");
        var preparer = new ControlledPreparer();
        var cache = new ImageCache(preparer);
        using var handle = cache.Acquire(scope, Source('9'), new ImageRendition(1, 1));
        var call = preparer.Next();

        cache.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => call.Request.ReserveTemporaryBytes(1));

        var late = new TrackingImage(1, 1, 4);
        call.Complete(late);
        Assert.IsTrue(SpinWait.SpinUntil(() => late.IsDisposed, 5000));
    }

    [TestMethod]
    public void RetryPublishesOutsideCacheLockAndSubscriberFailureLeavesWorkReleasable()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("image-owner");
        var preparer = new ControlledPreparer();
        using var cache = new ImageCache(preparer);
        var handle = cache.Acquire(scope, Source('4'), new ImageRendition(1, 1));
        preparer
            .Next()
            .Fail(new ImageLoadException(ImageLoadFailureKind.SourceUnavailable, "offline"));
        DrainCompletion(graph, cache, () => handle.Status != ImageLoadStatus.Loading);
        handle.Changed += () =>
        {
            handle.Dispose();
            throw new InvalidOperationException("subscriber failed");
        };

        var failure = Assert.ThrowsExactly<InvalidOperationException>(handle.Retry);
        Assert.AreEqual("subscriber failed", failure.Message);
        var retry = preparer.Next();
        Assert.IsTrue(
            SpinWait.SpinUntil(() => retry.CancellationToken.IsCancellationRequested, 5000)
        );
        var late = new TrackingImage(1, 1, 4);
        retry.Complete(late);
        DrainCompletion(graph, cache, () => late.IsDisposed);
        Assert.IsTrue(late.IsDisposed);
    }

    private static ImageSource Source(char hashCharacter)
    {
        var asset = new AssetReference(
            new AssetId("Tests", $"{hashCharacter}.png"),
            new string(hashCharacter, 64),
            4,
            AssetFormat.Png,
            () => new MemoryStream([0, 0, 0, 0], writable: false),
            new AssetImageMetadata(1, 1)
        );
        return ImageSource.FromAsset(asset);
    }

    private static void DrainCompletion(ReactiveGraph graph, ImageCache cache, Func<bool> completed)
    {
        Assert.IsTrue(
            SpinWait.SpinUntil(
                () =>
                {
                    graph.Drain();
                    return cache.Metrics.Active == 0 && completed();
                },
                5000
            )
        );
    }

    private static AggregateException DrainFailure(ReactiveGraph graph)
    {
        // Active counts preparation, not delivery. Completion releases the cache lock
        // before posting faults; observe the actual owner outcome across that boundary.
        AggregateException? failure = null;
        Assert.IsTrue(
            SpinWait.SpinUntil(
                () =>
                {
                    try
                    {
                        graph.Drain();
                    }
                    catch (AggregateException error)
                    {
                        failure = error;
                    }
                    return failure is not null;
                },
                5000
            ),
            "The image preparation failure did not reach the owner queue."
        );
        return failure!;
    }

    private sealed class ControlledPreparer : IImagePreparer
    {
        private readonly ConcurrentQueue<PreparationCall> _calls = new();
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public ValueTask<PreparedImage> PrepareAsync(
            ImagePreparationRequest request,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref _callCount);
            var call = new PreparationCall(request, cancellationToken);
            _calls.Enqueue(call);
            return new ValueTask<PreparedImage>(call.Task);
        }

        public PreparationCall Next()
        {
            Assert.IsTrue(SpinWait.SpinUntil(() => !_calls.IsEmpty, 5000));
            Assert.IsTrue(_calls.TryDequeue(out var call));
            return call;
        }
    }

    private sealed class PreparationCall(
        ImagePreparationRequest request,
        CancellationToken cancellationToken
    )
    {
        private readonly TaskCompletionSource<PreparedImage> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public ImagePreparationRequest Request { get; } = request;
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public Task<PreparedImage> Task => _completion.Task;

        public void Complete(PreparedImage image) => _completion.SetResult(image);

        public void Fail(Exception error) => _completion.SetException(error);
    }

    private sealed class TrackingImage(int width, int height, long byteCount)
        : PreparedImage(width, height, byteCount)
    {
        protected override void DisposeCore() { }
    }
}
