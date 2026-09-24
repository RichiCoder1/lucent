using Lucent.Core;
using Lucent.Renderer.Skia;

namespace Lucent.IssueBrowser.Tests;

public sealed partial class IssueBrowserTests
{
    [TestMethod]
    public void TabTraversalSkipsClippedRowsAndSemanticFocusRevealsOverscan()
    {
        using var composition = LoadedComposition(out _, out _);
        using var renderer = new SkiaSceneRenderer();
        var viewport = new LayoutViewport(1120, 760, 1);
        RetainedScene? current = null;

        void Install()
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var candidate = SceneLayout.ProjectFrame(
                    composition,
                    viewport,
                    renderer,
                    current,
                    10_000
                );
                if (!composition.Input.SetScene(candidate))
                {
                    candidate.Dispose();
                    continue;
                }
                var replaced = current;
                current = candidate;
                replaced?.Dispose();
                return;
            }
            throw new InvalidOperationException("Issue Browser focus scene did not settle.");
        }

        try
        {
            Install();
            for (var tab = 1; tab <= 18; tab++)
            {
                var down = composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Tab));
                _ = composition.Input.DispatchKey(new(KeyCommandKind.Up, Key.Tab));
                Install();

                var focused = Flatten(composition.SemanticSnapshot()!)
                    .Where(node => node.Focused)
                    .ToArray();
                Assert(
                    down.Handled && focused.Length == 1,
                    $"Tab {tab} lost its one semantic focus owner after immediate release. "
                        + $"Down={down.Rejection}; Focused={focused.Length}."
                );
                if (tab == 16)
                    Assert(
                        focused[0].Name.StartsWith("#9991 ", StringComparison.Ordinal),
                        "Tab 16 did not reach the partially visible final row."
                    );
                if (tab == 17)
                    Assert(
                        focused[0].Role == SemanticRole.Splitter
                            && focused[0].Name == "Resize issue list",
                        "Tab visited a fully clipped overscan row or lost splitter focus."
                    );
                if (tab == 18)
                    Assert(
                        focused[0].Name == "All issues",
                        "Tab did not wrap after the visible splitter."
                    );
            }

            var overscan = Flatten(composition.SemanticSnapshot()!)
                .Single(node =>
                    node.Role == SemanticRole.ListItem
                    && node.Name.StartsWith("#9990 ", StringComparison.Ordinal)
                );
            var overscanIdentity = new ElementIdentity(
                overscan.Identity.CompositionEpoch,
                overscan.Identity.ElementId
            );
            Assert(
                composition.Input.FocusSemantic(overscanIdentity),
                "Explicit semantic focus rejected a realized overscan row."
            );
            Install();
            var list = Flatten(composition.SemanticSnapshot()!)
                .Single(node =>
                    node.Name == "Issues" && node.Actions.HasFlag(SemanticAction.Scroll)
                );
            var focusedRow = Flatten(composition.SemanticSnapshot()!).Single(node => node.Focused);
            var clip = current!
                .Input.Single(item => item.Identity.ElementId == list.Identity.ElementId)
                .ChildClipBounds;
            var bounds = current
                .Input.Single(item => item.Identity.ElementId == focusedRow.Identity.ElementId)
                .Bounds;
            Assert(
                focusedRow.Identity.CompositionEpoch == overscan.Identity.CompositionEpoch
                    && focusedRow.Identity.ElementId == overscan.Identity.ElementId
                    && clip is { } viewportClip
                    && bounds.Y < viewportClip.Y + viewportClip.Height
                    && bounds.Y + bounds.Height > viewportClip.Y,
                $"Explicit semantic focus did not reveal its clipped virtual row: "
                    + $"expected={overscan.Identity}, actual={focusedRow.Identity}, "
                    + $"clip={clip}, row={bounds}."
            );
        }
        finally
        {
            current?.Dispose();
        }
    }
}
