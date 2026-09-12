using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class SemanticAnnouncementContracts
{
    [TestMethod]
    public void StatusAndInlineNoticeRequireExplicitPoliteAnnouncementOptIn()
    {
        using var composition = new Composition(new ReactiveGraph(), "announcement-contract");
        composition.ConfigureImages(new ImageCache(new ImmediatePreparer()));
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var message = composition.Root.Scope.Signal("Waiting", "announcement-message");
        composition.Mount(
            composition.Root,
            theme,
            Components.Column(
                ComponentContent.Create([
                    Components.Status("Quiet"),
                    Components.InlineNotice(
                        () => message.Value,
                        announcement: SemanticAnnouncement.Polite
                    ),
                ])
            )
        );
        composition.Flush();

        var statuses = Flatten(composition.SemanticSnapshot()!)
            .Where(node => node.Role == SemanticRole.Status)
            .ToArray();
        Assert.AreEqual(
            SemanticAnnouncement.None,
            statuses.Single(node => node.Name == "Quiet").Announcement
        );
        Assert.AreEqual(
            SemanticAnnouncement.Polite,
            statuses.Single(node => node.Name == "Waiting").Announcement
        );

        message.Value = "Working";
        message.Value = "Almost done";
        message.Value = "Done";
        composition.Flush();
        var updated = Flatten(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.Status && node.Name == "Done");
        Assert.AreEqual(SemanticAnnouncement.Polite, updated.Announcement);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            Components.InlineNotice(() => "Invalid", announcement: (SemanticAnnouncement)42)
        );
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
        ) => ValueTask.FromResult<PreparedImage>(new RasterImage(1, 1, [0, 0, 0, 255]));
    }
}
