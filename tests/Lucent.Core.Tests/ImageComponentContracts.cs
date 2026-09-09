using System.Security.Cryptography;
using System.Xml.Linq;
using Lucent.Core;
using Lucent.Icons.Lucide;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ImageComponentContracts
{
    private static readonly string[] ExpectedButtonNames = ["Refresh", "More actions"];

    [TestMethod]
    public void StockIconButtonsOwnOneAccessibleActionAndDecorativeGlyphs()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "stock-icon-buttons");
        composition.ConfigureImages(new ImageCache(new ImmediatePreparer()));
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var invocations = 0;
        var leading = composition.Mount(
            composition.Root,
            theme,
            Components.Button("Refresh", Source(1), () => invocations++)
        );
        var iconOnly = composition.Mount(
            composition.Root,
            theme,
            Components.IconButton(Source(2), "More actions", () => invocations++)
        );
        graph.Drain();

        Assert.AreEqual(2, leading.Children.Count);
        Assert.AreEqual(0, iconOnly.Children.Count);
        Assert.AreEqual(
            ImageColorMode.Monochrome,
            leading.Children[0].Resolve(ImageProperties.ColorMode).Value
        );
        Assert.AreEqual("Refresh", leading.Children[1].Resolve(ProjectionProperties.Text).Value);
        Assert.AreEqual(
            ImageColorMode.Monochrome,
            iconOnly.Resolve(ImageProperties.ColorMode).Value
        );
        var buttons = Flatten(composition.SemanticSnapshot()!)
            .Where(node => node.Role == SemanticRole.Button)
            .ToArray();
        CollectionAssert.AreEquivalent(
            ExpectedButtonNames,
            buttons.Select(node => node.Name).ToArray()
        );
        Assert.AreEqual(2, buttons.Length);
        Assert.AreEqual(
            0,
            Flatten(composition.SemanticSnapshot()!).Count(node => node.Role == SemanticRole.Image)
        );
        foreach (var button in buttons)
            Assert.AreEqual(
                SemanticCommandResult.Applied,
                composition.ExecuteSemanticCommand(button.Identity, new(SemanticCommandKind.Invoke))
            );
        Assert.AreEqual(2, invocations);
        Assert.ThrowsExactly<ArgumentException>(() => Components.IconButton(Source(3), " "));
    }

    [TestMethod]
    public void PinnedLucideAccessorsExposeExactCanonicalArtwork()
    {
        var sources = new Dictionary<string, ImageSource>(StringComparer.Ordinal)
        {
            ["archive"] = LucideIcons.Archive,
            ["arrow-left"] = LucideIcons.ArrowLeft,
            ["arrow-right"] = LucideIcons.ArrowRight,
            ["circle-check"] = LucideIcons.CircleCheck,
            ["circle-dot"] = LucideIcons.CircleDot,
            ["ellipsis"] = LucideIcons.Ellipsis,
            ["file-text"] = LucideIcons.FileText,
            ["inbox"] = LucideIcons.Inbox,
            ["link"] = LucideIcons.Link,
            ["list-filter"] = LucideIcons.ListFilter,
            ["notebook-pen"] = LucideIcons.NotebookPen,
            ["plus"] = LucideIcons.Plus,
            ["refresh-cw"] = LucideIcons.RefreshCw,
            ["search"] = LucideIcons.Search,
            ["trash"] = LucideIcons.Trash,
        };
        Assert.AreEqual(15, sources.Count);
        foreach (var (name, source) in sources)
        {
            var asset = source.PackagedAsset!;
            Assert.AreEqual(new AssetId("Lucent.Icons.Lucide", "icons/" + name + ".svg"), asset.Id);
            Assert.AreEqual(AssetFormat.Svg, asset.Format);
            Assert.AreEqual(24f, source.Metadata.Width);
            Assert.AreEqual(24f, source.Metadata.Height);
            using var stream = asset.OpenRead();
            Assert.AreEqual(asset.ByteLength, stream.Length);
            Assert.AreEqual(
                asset.ContentHash,
                Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()
            );
            stream.Position = 0;
            var svg = XDocument.Load(stream).Root!;
            Assert.AreEqual("0 0 24 24", (string?)svg.Attribute("viewBox"));
            Assert.AreEqual("currentColor", (string?)svg.Attribute("stroke"));
            Assert.AreEqual("2", (string?)svg.Attribute("stroke-width"));
            Assert.AreEqual("round", (string?)svg.Attribute("stroke-linecap"));
            Assert.AreEqual("round", (string?)svg.Attribute("stroke-linejoin"));
        }
    }

    [TestMethod]
    public void SceneCleanupReleasesEveryResourceWhenOneAdapterThrows()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "image-cleanup-failure");
        var preparer = new CleanupPreparer();
        var cache = new ImageCache(preparer);
        composition.ConfigureImages(cache);
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Mount(composition.Root, theme, Components.Icon(Source(1)));
        composition.Mount(composition.Root, theme, Components.Icon(Source(2)));
        using var scene = ReadyScene(composition, expectedCount: 2);
        var resources = Nodes(scene.Nodes)
            .OfType<ImageSceneNode>()
            .Select(node => node.Image)
            .ToArray();
        composition.Dispose();

        var error = Assert.ThrowsExactly<AggregateException>(scene.Dispose);
        StringAssert.Contains(error.ToString(), "adapter cleanup failure");
        Assert.IsTrue(resources.All(resource => resource.IsDisposed));
        Assert.AreEqual(0L, cache.Metrics.LeasedBytes);
        scene.Dispose();
    }

    [TestMethod]
    public void IntrinsicAspectUsesTheConstrainedAuthoredDimension()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "image-constrained-aspect");
        composition.ConfigureImages(new ImageCache(new ImmediatePreparer()));
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var image = composition.Mount(
            composition.Root,
            theme,
            Components.Image(
                Source(1),
                "Photo",
                style: Style
                    .Empty.Set(LayoutProperties.Width, 40f)
                    .Set(LayoutProperties.MaxWidth, 30f)
            )
        );
        using var scene = SceneLayout.Project(composition, new(100, 100, 1), new EmptyShaper());
        var bounds = scene.Boxes.Single(box => box.Identity.ElementId == image.Id).Bounds;
        Assert.AreEqual(30f, bounds.Width);
        Assert.AreEqual(15f, bounds.Height);
    }

    [TestMethod]
    public void ImagesRequireIntentAndIconsHaveOneRootWithoutInteractiveSemantics()
    {
        var source = Source(1);
        Assert.ThrowsExactly<ArgumentException>(() => Components.Image(source));
        Assert.ThrowsExactly<ArgumentException>(() => Components.Image(source, " "));
        Assert.ThrowsExactly<ArgumentException>(() =>
            Components.Image(source, "label", decorative: true)
        );
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "image-intent");
        composition.ConfigureImages(new ImageCache(new ImmediatePreparer()));
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var icon = composition.Mount(composition.Root, theme, Components.Icon(source));
        var image = composition.Mount(composition.Root, theme, Components.Image(source, "Diagram"));
        Assert.AreEqual(0, icon.Children.Count);
        Assert.AreEqual(0, image.Children.Count);
        Assert.AreEqual(16f, icon.Resolve(LayoutProperties.Width).Value);
        Assert.AreEqual(ImageColorMode.Monochrome, icon.Resolve(ImageProperties.ColorMode).Value);
        var semantics = composition.SemanticSnapshot()!;
        var images = Flatten(semantics).Where(node => node.Role == SemanticRole.Image).ToArray();
        Assert.AreEqual(1, images.Length);
        Assert.AreEqual("Diagram", images[0].Name);
        Assert.AreEqual(SemanticAction.None, images[0].Actions);
    }

    [TestMethod]
    public void PreparedScenesRetainPixelsAcrossUnmountAndCacheDisposal()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "image-retention");
        var preparer = new ImmediatePreparer();
        var cache = new ImageCache(preparer);
        composition.ConfigureImages(cache);
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var root = composition.Mount(composition.Root, theme, Components.Icon(Source(1)));
        using var scene = ReadyScene(composition);
        using var retained = scene.Retain();
        var frame = Nodes(scene.Nodes).OfType<ImageSceneNode>().Single();
        root.Dispose();
        scene.Dispose();
        composition.Dispose();
        Assert.AreEqual((byte)255, ((RasterImage)frame.Image).Pixels.Span[3]);
        Assert.AreEqual(1, Nodes(retained.Nodes).OfType<ImageSceneNode>().Count());
        Assert.IsTrue(
            cache.Metrics.LeasedBytes > 0,
            "The retained scene must pin resources even after its cache closes."
        );
        retained.Dispose();
        Assert.AreEqual(0L, cache.Metrics.LeasedBytes);
    }

    [TestMethod]
    public void SourceReplacementClearsOldPixelsWhileSameContentUpgradeRetainsThem()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "image-replacement");
        var preparer = new ControlledPreparer();
        composition.ConfigureImages(new ImageCache(preparer));
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var source = composition.Root.Scope.Signal(Source(1), "source");
        var extent = composition.Root.Scope.Signal(16f, "extent");
        var root = composition.Mount(
            composition.Root,
            theme,
            Components.Icon(
                () => source.Value,
                style: Style
                    .Empty.Bind(LayoutProperties.Width, () => (float?)extent.Value)
                    .Bind(LayoutProperties.Height, () => (float?)extent.Value)
            )
        );
        using (var initial = SceneLayout.Project(composition, new(100, 100, 1), new EmptyShaper()))
            Assert.AreEqual(0, Nodes(initial.Nodes).OfType<ImageSceneNode>().Count());
        preparer.Complete(1);
        using var ready = ReadyScene(composition);
        extent.Value = 200f;
        using (var upgrade = SceneLayout.Project(composition, new(300, 300, 2), new EmptyShaper()))
            Assert.AreEqual(
                1,
                Nodes(upgrade.Nodes).OfType<ImageSceneNode>().Count(),
                "An upgrade may retain this same content."
            );
        source.Value = Source(2);
        using var replaced = SceneLayout.Project(composition, new(300, 300, 2), new EmptyShaper());
        Assert.AreEqual(
            0,
            Nodes(replaced.Nodes).OfType<ImageSceneNode>().Count(),
            "Unrelated content must not remain beside the new source identity."
        );
        preparer.Complete(1);
        composition.Flush();
        using var late = SceneLayout.Project(composition, new(300, 300, 2), new EmptyShaper());
        Assert.AreEqual(
            0,
            Nodes(late.Nodes).OfType<ImageSceneNode>().Count(),
            "Late old-source work cannot commit into the replacement."
        );
        preparer.Complete(2);
        using var final = ReadyScene(composition, new(300, 300, 2));
        Assert.AreEqual(
            (byte)2,
            ((RasterImage)Nodes(final.Nodes).OfType<ImageSceneNode>().Single().Image).Pixels.Span[0]
        );
    }

    [TestMethod]
    public void CoverCroppingAndIntrinsicLayoutDoNotDependOnDecodeResolution()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "image-layout");
        composition.ConfigureImages(new ImageCache(new ImmediatePreparer()));
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var image = composition.Mount(
            composition.Root,
            theme,
            Components.Image(
                Source(1),
                "Photo",
                style: Style
                    .Empty.Set(LayoutProperties.Width, 40f)
                    .Set(LayoutProperties.Height, 40f)
                    .Set(ImageProperties.Fit, ImageFit.Cover)
            )
        );
        using var ready = ReadyScene(composition);
        var node = Nodes(ready.Nodes).OfType<ImageSceneNode>().Single();
        Assert.AreEqual(40f, node.Bounds.Width);
        Assert.AreEqual(40f, node.Bounds.Height);
        Assert.AreEqual(.5f, node.SourceBounds.Width / node.Image.Width, .001f);
        Assert.AreEqual(.25f, node.SourceBounds.X / node.Image.Width, .001f);
        Assert.AreEqual(
            40f,
            ready.Boxes.Single(box => box.Identity.ElementId == image.Id).Bounds.Width
        );
    }

    private static RetainedScene ReadyScene(
        Composition composition,
        LayoutViewport? viewport = null,
        int expectedCount = 1
    )
    {
        var deadline = Environment.TickCount64 + 5_000;
        do
        {
            var scene = SceneLayout.Project(
                composition,
                viewport ?? new(100, 100, 1),
                new EmptyShaper()
            );
            if (Nodes(scene.Nodes).OfType<ImageSceneNode>().Count() == expectedCount)
                return scene;
            scene.Dispose();
            Thread.Sleep(1);
        } while (Environment.TickCount64 < deadline);
        Assert.Fail("Image preparation did not publish a ready frame.");
        throw new InvalidOperationException();
    }

    private static ImageSource Source(byte value)
    {
        byte[] data = [value];
        return ImageSource.FromAsset(
            new AssetReference(
                new("Tests", value + ".png"),
                Convert.ToHexString(SHA256.HashData(data)),
                1,
                AssetFormat.Png,
                () => new MemoryStream(data),
                new(20, 10)
            )
        );
    }

    private static RasterImage Pixels(byte value) =>
        new(2, 1, new byte[] { value, 0, 0, 255, value, 0, 0, 255 });

    private sealed class ImmediatePreparer : IImagePreparer
    {
        public ValueTask<PreparedImage> PrepareAsync(
            ImagePreparationRequest request,
            CancellationToken token
        ) => ValueTask.FromResult<PreparedImage>(Pixels(1));
    }

    private sealed class CleanupPreparer : IImagePreparer
    {
        public ValueTask<PreparedImage> PrepareAsync(
            ImagePreparationRequest request,
            CancellationToken token
        )
        {
            using var source = request.Source.PackagedAsset!.OpenRead();
            return ValueTask.FromResult<PreparedImage>(new CleanupImage(source.ReadByte() == 1));
        }
    }

    private sealed class CleanupImage(bool throws) : PreparedImage(1, 1, 4)
    {
        protected override void DisposeCore()
        {
            if (throws)
                throw new InvalidOperationException("adapter cleanup failure");
        }
    }

    private sealed class ControlledPreparer : IImagePreparer
    {
        private readonly object _gate = new();
        private readonly Dictionary<byte, Queue<TaskCompletionSource<PreparedImage>>> _pending = [];

        public ValueTask<PreparedImage> PrepareAsync(
            ImagePreparationRequest request,
            CancellationToken token
        )
        {
            using var stream = request.Source.PackagedAsset!.OpenRead();
            var value = (byte)stream.ReadByte();
            lock (_gate)
            {
                if (!_pending.TryGetValue(value, out var pending))
                    _pending[value] = pending = new();
                var completion = new TaskCompletionSource<PreparedImage>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                pending.Enqueue(completion);
                return new(completion.Task);
            }
        }

        public void Complete(byte value)
        {
            var deadline = Environment.TickCount64 + 5_000;
            do
            {
                lock (_gate)
                    if (
                        _pending.TryGetValue(value, out var pending)
                        && pending.TryDequeue(out var completion)
                    )
                    {
                        completion.SetResult(Pixels(value));
                        return;
                    }
                Thread.Sleep(1);
            } while (Environment.TickCount64 < deadline);
            Assert.Fail("The requested source did not begin preparing.");
        }
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) => new("empty", 0, 0, []);
    }

    private static IEnumerable<SemanticSnapshot> Flatten(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var item in Flatten(child))
            yield return item;
    }

    private static IEnumerable<SceneNode> Nodes(IEnumerable<SceneNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            var children =
                node is ClipSceneNode clip ? clip.Children
                : node is OpacitySceneNode opacity ? opacity.Children
                : null;
            if (children is not null)
                foreach (var item in Nodes(children))
                    yield return item;
        }
    }
}
