using System.Buffers.Binary;
using Lucent.Core;
using Lucent.Testing.Skia;

namespace Lucent.Testing.Tests;

[TestClass]
public sealed class HeadlessHarnessTests
{
    [TestMethod]
    public void BoundedReactiveDrainRejectsSelfSchedulingEffect()
    {
        var graph = new ReactiveGraph();
        var value = graph.Signal(0, "self-scheduling-value");
        using var effect = graph.Effect(() => value.Value++, "self-scheduling-effect");

        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => graph.Drain(5));

        StringAssert.Contains(failure.Message, "within 5 work items");
    }

    [TestMethod]
    public void BoundedReactiveDrainSharesBudgetWithNestedBatch()
    {
        var graph = new ReactiveGraph();
        var value = graph.Signal(0, "nested-batch-value");
        var entered = false;
        using var effect = graph.Effect(
            () =>
            {
                _ = value.Value;
                if (!entered)
                {
                    entered = true;
                    graph.Batch(() => value.Value++);
                }
                else
                    value.Value++;
            },
            "nested-batch-effect"
        );

        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => graph.Drain(5));

        StringAssert.Contains(failure.ToString(), "within 5 work items");
    }

    [TestMethod]
    public async Task ThemeFactoryReceivesRuntimeAppearanceChanges()
    {
        var appearances = new List<ThemeAppearance>();
        await using var application = await HeadlessApplication.StartAsync(
            EmptyRecipe(),
            new HeadlessApplicationOptions
            {
                ThemeFactory = appearance =>
                {
                    appearances.Add(appearance);
                    return new Theme(
                        appearance.ColorScheme == ThemeColorScheme.Dark
                            ? "runtime-dark"
                            : "runtime-light"
                    );
                },
            }
        );

        Assert.AreEqual(
            "runtime-light",
            await application.InvokeAfterSettleAsync(context => context.Session.Theme.Theme.Name)
        );

        var dark = new ThemeAppearance(ThemeColorScheme.Dark, ThemeContrast.Normal);
        await application.InvokeAsync(context =>
        {
            context.Session.Theme.Appearance = dark;
            return 0;
        });

        Assert.AreEqual(
            "runtime-dark",
            await application.InvokeAfterSettleAsync(context => context.Session.Theme.Theme.Name)
        );
        CollectionAssert.Contains(appearances, ThemeAppearance.Light);
        CollectionAssert.Contains(appearances, dark);
    }

    [TestMethod]
    public async Task ShutdownBoundsAlternatingOwnerAndReactiveWork()
    {
        var lifecycle = new AlternatingCloseLifecycle();
        var application = await HeadlessApplication.StartAsync(
            lifecycle,
            new HeadlessApplicationOptions { MaximumWorkItems = 3 }
        );

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            application.DisposeAsync().AsTask()
        );

        StringAssert.Contains(failure.Message, "shutting down the application");
    }

    [TestMethod]
    public async Task ContextRejectsSemanticSnapshotFromAnotherApplication()
    {
        await using var first = await HeadlessApplication.StartAsync(
            HeadlessFixtures.Components.Editor()
        );
        await using var second = await HeadlessApplication.StartAsync(
            HeadlessFixtures.Components.Editor()
        );
        var foreign = (await first.SnapshotAsync()).Require(SemanticRole.TextField, "Draft");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            second.InvokeAsync(context => context.RequireElement(foreign))
        );

        StringAssert.Contains(failure.Message, "another application");
    }

    [TestMethod]
    public async Task ConcurrentDisposeRejectsAdmittedWorkThatDidNotStart()
    {
        var application = await HeadlessApplication.StartAsync(EmptyRecipe());
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var active = application.InvokeAsync(_ =>
        {
            entered.Set();
            Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
            return 1;
        });
        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
        var pending = application.SnapshotAsync();
        var disposal = application.DisposeAsync().AsTask();
        release.Set();

        Assert.AreEqual(1, await active);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
        await disposal;
    }

    [TestMethod]
    public async Task SnapshotSettlesQueuedOwnerContextWork()
    {
        await using var application = await HeadlessApplication.StartAsync(EmptyRecipe());
        var calls = 0;
        await application.InvokeAsync(_ =>
        {
            SynchronizationContext.Current!.Post(_ => calls++, null);
            return 0;
        });

        _ = await application.SnapshotAsync();

        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task SnapshotRetainsSceneAfterTheApplicationReprojects()
    {
        await using var application = await HeadlessApplication.StartAsync(EmptyRecipe());
        var initial = await application.SnapshotAsync();

        using var resized = await application.ResizeAsync(new LayoutViewport(800, 600, 1));

        Assert.IsFalse(initial.Scene.IsDisposed);
        Assert.IsFalse(resized.Scene.IsDisposed);
        Assert.AreNotEqual(initial.Scene, resized.Scene);

        initial.Dispose();
        Assert.IsTrue(initial.Scene.IsDisposed);
    }

    [TestMethod]
    public async Task SnapshotRetainsPreparedImageAfterApplicationDisposal()
    {
        var source = TestImageSource();
        var application = await HeadlessApplication.StartAsync(
            Components.Image(source, "Preview"),
            new HeadlessApplicationOptions { ImagePreparer = new ImmediateImagePreparer() }
        );
        HeadlessSnapshot? snapshot = null;
        try
        {
            var preload = await application.InvokeAsync(context =>
                context.Composition.Images!.PreloadAsync(
                    context.Composition.Root.Scope,
                    source,
                    new ImageRendition(64, 64)
                )
            );
            Assert.AreEqual(ImagePreloadStatus.Ready, (await preload).Status);
            using (await application.DrainAsync()) { }
            var cache = await application.InvokeAsync(context => context.Composition.Images!);
            snapshot = await application.SnapshotAsync();
            Assert.IsGreaterThan(0L, cache.Metrics.LeasedBytes);

            await application.DisposeAsync();

            Assert.IsFalse(snapshot.Scene.IsDisposed);
            Assert.IsGreaterThan(0L, cache.Metrics.LeasedBytes);
            snapshot.Dispose();
            Assert.IsTrue(snapshot.Scene.IsDisposed);
            Assert.AreEqual(0L, cache.Metrics.LeasedBytes);
        }
        finally
        {
            snapshot?.Dispose();
            await application.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task ImagePreparerIsConfiguredBeforeRecipeMounts()
    {
        var preparer = new UnusedImagePreparer();
        var sawCache = false;
        await using var application = await HeadlessApplication.StartAsync(
            context =>
            {
                sawCache = context.Composition.Images is not null;
                return EmptyRecipe();
            },
            new HeadlessApplicationOptions { ImagePreparer = preparer }
        );

        Assert.IsTrue(sawCache);
    }

    [TestMethod]
    public async Task ContextMenuPopupSharesOwnerImageCacheWithoutOwningItsDisposal()
    {
        ContextMenuRequest? request = null;
        var source = TestImageSource();
        await using var application = await HeadlessApplication.StartAsync(
            Components.ContextMenu(
                [Components.Button("Target", static () => { })],
                () => Components.Menu([Components.MenuItem("Run", static () => { })])
            ),
            new HeadlessApplicationOptions { ImagePreparer = new ImmediateImagePreparer() }
        );

        await application.InvokeAsync(context =>
        {
            context.Input.ContextMenuRequested += value => request = value;
            return context.Input.MoveFocus(FocusTraversalDirection.Next);
        });
        await application.KeyAsync(new(KeyCommandKind.Down, Key.ContextMenu));

        var shared = await application.InvokeAsync(_ =>
        {
            var popup = request!.CreateComposition();
            return (
                SameCache: ReferenceEquals(request.Owner.Images, popup.Images),
                OwnerMetrics: request.Owner.Images!.Metrics
            );
        });

        Assert.IsTrue(shared.SameCache);
        Assert.AreEqual(0, shared.OwnerMetrics.ReadyEntries);
        await application.InvokeAsync(_ =>
        {
            request!.Dispose();
            return 0;
        });
        var preload = await application.InvokeAsync(context =>
            context.Composition.Images!.PreloadAsync(
                context.Composition.Root.Scope,
                source,
                new ImageRendition(64, 64)
            )
        );
        Assert.AreEqual(ImagePreloadStatus.Ready, (await preload).Status);
    }

    [TestMethod]
    public async Task SettleCrossesFromReactiveEffectToApplicationContext()
    {
        await using var application = await HeadlessApplication.StartAsync(EmptyRecipe());
        var calls = 0;

        await application.InvokeAsync(context =>
        {
            var value = context.Composition.Root.Scope.Signal(false, "cross-queue-value");
            var ownerContext = SynchronizationContext.Current!;
            _ = context.Composition.Root.Scope.Effect(
                () =>
                {
                    if (value.Value)
                        ownerContext.Post(_ => calls++, null);
                },
                "cross-queue-effect"
            );
            value.Value = true;
            return 0;
        });

        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task SkiaCaptureReturnsPhysicalViewportPng()
    {
        await using var application = await SkiaHeadlessApplication.StartAsync(
            HeadlessFixtures.Components.Editor(),
            new HeadlessApplicationOptions { Viewport = new(160, 80, 1.5f) }
        );

        var png = await application.CapturePngAsync();

        CollectionAssert.AreEqual(
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
            png.Take(8).ToArray()
        );
        Assert.IsGreaterThan(100, png.Length);
        Assert.AreEqual(240, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
        Assert.AreEqual(120, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }

    [TestMethod]
    public async Task TextShaperCleanupFailureCompletesDisposalWithError()
    {
        var application = await HeadlessApplication.StartAsync(
            EmptyRecipe(),
            new HeadlessApplicationOptions
            {
                TextShaperFactory = static () => new ThrowingDisposeShaper(),
            }
        );

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            application.DisposeAsync().AsTask()
        );

        Assert.AreEqual("shaper-cleanup", failure.Message);
    }

    private static ComponentRecipe EmptyRecipe() =>
        ComponentRecipe.Create("headless-empty", static (_, _) => { });

    private static ImageSource TestImageSource() =>
        ImageSource.FromAsset(
            new AssetReference(
                new AssetId("headless", "preview.png"),
                new string('0', 64),
                1,
                AssetFormat.Png,
                static () => new MemoryStream(new byte[] { 0 }),
                new AssetImageMetadata(1, 1)
            )
        );

    private sealed class ThrowingDisposeShaper : ITextShaper, IDisposable
    {
        private readonly HeadlessTextShaper _inner = new();

        public ShapedText Shape(TextMeasureRequest request) => _inner.Shape(request);

        public void Dispose() => throw new InvalidOperationException("shaper-cleanup");
    }

    private sealed class UnusedImagePreparer : IImagePreparer
    {
        public ValueTask<PreparedImage> PrepareAsync(
            ImagePreparationRequest request,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException("The lifecycle test does not mount an image.");
    }

    private sealed class ImmediateImagePreparer : IImagePreparer
    {
        public ValueTask<PreparedImage> PrepareAsync(
            ImagePreparationRequest request,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult<PreparedImage>(new RasterImage(1, 1, new byte[] { 0, 0, 0, 0 }));
    }

    private sealed class AlternatingCloseLifecycle : IApplicationLifecycle
    {
        private readonly TaskCompletionSource<bool> _close = new();
        private Signal<int>? _pulse;
        private SynchronizationContext? _ownerContext;
        private bool _preparing;
        private int _callbacks;

        public ValueTask<ComponentRecipe> StartAsync(ApplicationSession session)
        {
            _ownerContext = SynchronizationContext.Current;
            _pulse = session.Scope.Signal(0, "alternating-close-pulse");
            _ = session.Scope.Effect(
                () =>
                {
                    _ = _pulse.Value;
                    if (!_preparing)
                        return;
                    _ownerContext!.Post(
                        _ =>
                        {
                            if (Interlocked.Increment(ref _callbacks) >= 20)
                                _close.TrySetResult(true);
                            else
                                _pulse.Value++;
                        },
                        null
                    );
                },
                "alternating-close-effect"
            );
            return ValueTask.FromResult(EmptyRecipe());
        }

        public ValueTask<bool> PrepareCloseAsync(CancellationToken cancellationToken)
        {
            _preparing = true;
            _pulse!.Value++;
            return new ValueTask<bool>(_close.Task);
        }

        public ValueTask StopAsync() => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
