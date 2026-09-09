using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class StatusComponentContracts
{
    [TestMethod]
    public void ProgressBarPublishesReadOnlyRangeAndOmitsUnknownPercentage()
    {
        using var composition = new Composition(new ReactiveGraph(), "progress-contract");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var progress = composition.Root.Scope.Signal<double?>(0.25, "progress");
        composition.Mount(
            composition.Root,
            theme,
            Components.ProgressBar("Preparing", () => progress.Value)
        );
        composition.Flush();
        var node = Flatten(composition.SemanticSnapshot()!)
            .Single(n => n.Role == SemanticRole.ProgressBar);
        Assert.AreEqual(0.25, node.Range!.Value);
        Assert.IsTrue(node.Range.IsReadOnly);
        Assert.AreEqual(SemanticAction.None, node.Actions);
        Assert.AreEqual(
            SemanticCommandResult.Rejected,
            composition.ExecuteSemanticCommand(
                node.Identity,
                new(SemanticCommandKind.SetRangeValue, NumericValue: 0.5)
            )
        );
        progress.Value = null;
        composition.Flush();
        node = Flatten(composition.SemanticSnapshot()!)
            .Single(n => n.Role == SemanticRole.ProgressBar);
        Assert.IsNull(node.Range);
        Assert.IsNull(node.Value);
        Assert.IsFalse(composition.PresentationDemand.IsActive);
        progress.Value = 1;
        composition.Flush();
        node = Flatten(composition.SemanticSnapshot()!)
            .Single(n => n.Role == SemanticRole.ProgressBar);
        Assert.AreEqual(1d, node.Range!.Value);
    }

    [TestMethod]
    public void ProgressRejectsInvalidFractions()
    {
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, -0.01, 1.01 })
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                Components.ValidateProgressFraction(value)
            );
    }

    [TestMethod]
    public void InlineNoticeKeepsRecoveryExplicitAndSourceArtworkImmutable()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            Components.InlineNotice(() => "Failed", onAction: () => { })
        );
        Assert.ThrowsExactly<ArgumentException>(() =>
            Components.InlineNotice(() => "Failed", actionLabel: "Retry")
        );
        using var composition = new Composition(new ReactiveGraph(), "notice-contract");
        composition.ConfigureImages(new ImageCache(new ImmediatePreparer()));
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.HighContrast);
        var calls = 0;
        composition.Mount(
            composition.Root,
            theme,
            Components.InlineNotice(
                () => "Could not save",
                NoticeSeverity.Error,
                () => calls++,
                "Try again"
            )
        );
        composition.Flush();
        var nodes = Flatten(composition.SemanticSnapshot()!).ToArray();
        var action = nodes.Single(node =>
            node.Role == SemanticRole.Button && node.Name == "Try again"
        );
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(action.Identity, new(SemanticCommandKind.Invoke))
        );
        Assert.AreEqual(1, calls);
        Assert.IsTrue(
            Flatten(composition.SemanticSnapshot()!).Any(node => node.Name == "Could not save")
        );
        Assert.IsFalse(composition.PresentationDemand.IsActive);
        foreach (var severity in Enum.GetValues<NoticeSeverity>())
        {
            var asset = Components.NoticeIcon(severity).PackagedAsset!;
            using var stream = asset.OpenRead();
            Assert.AreEqual(asset.ByteLength, stream.Length);
        }
    }

    private static IEnumerable<SemanticSnapshot> Flatten(SemanticSnapshot root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var descendant in Flatten(child))
            yield return descendant;
    }

    private sealed class ImmediatePreparer : IImagePreparer
    {
        public ValueTask<PreparedImage> PrepareAsync(
            ImagePreparationRequest request,
            CancellationToken cancellationToken
        ) =>
            ValueTask.FromResult<PreparedImage>(new RasterImage(1, 1, new byte[] { 0, 0, 0, 255 }));
    }
}
