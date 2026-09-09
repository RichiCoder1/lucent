using System.Diagnostics;
using Lucent.Core;
using Lucent.Renderer.Skia;

namespace Lucent.IssueBrowser.Tests;

public sealed partial class IssueBrowserTests
{
    [TestMethod]
    public void StockRowHoverUsesRetainedPaintAndKeepsSelectionImmediate()
    {
        using var composition = LoadedComposition(out _, out var browser, out var theme);
        PreloadStockIcons(composition);
        using var renderer = new SkiaSceneRenderer();
        composition.SamplePresentation(TimeSpan.Zero);
        using var initial = Install(composition, renderer, new(1120, 760, 1));
        Assert(
            composition.TryAcknowledgePresentation(initial.Generation),
            "Initial frame acknowledgement failed."
        );
        Assert(
            browser.VisibleIssues.Count == 10_000,
            "The proving consumer must retain its large fixture source."
        );
        var row = Flatten(composition.SemanticSnapshot()!)
            .Where(node => node.Role == SemanticRole.ListItem && node.Name.StartsWith('#'))
            .Skip(1)
            .First();
        var bounds = initial
            .Boxes.Single(box => box.Identity.ElementId == row.Identity.ElementId)
            .Bounds;
        var move = composition.Input.DispatchPointer(
            new(PointerCommandKind.Move, 1, bounds.X + 8, bounds.Y + bounds.Height / 2)
        );
        Assert(move.Rejection == InputRejection.None, "The row hover used stale geometry.");
        using var hover = Install(composition, renderer, initial.Viewport);
        Assert(
            composition.TryAcknowledgePresentation(hover.Generation),
            "Hover frame acknowledgement failed."
        );
        Assert(
            composition.PresentationDiagnostics.ActiveTracks == 1,
            "Only the hovered realized row should allocate an active stock transition."
        );
        var beforeSemantics = composition.SemanticRevision;
        var phases = new List<double>();
        for (var frame = 1; frame <= 6; frame++)
        {
            var started = Stopwatch.GetTimestamp();
            composition.SamplePresentation(TimeSpan.FromMilliseconds(frame * 16));
            using var scene = SceneLayout.ProjectFrame(
                composition,
                hover.Viewport,
                renderer,
                hover
            );
            Assert(
                scene.IsPaintOnly,
                "Stock hover triggered full layout instead of retained paint."
            );
            Assert(
                ReferenceEquals(hover.Boxes, scene.Boxes)
                    && ReferenceEquals(hover.Input, scene.Input),
                "Hover replaced immutable geometry or hit data."
            );
            Assert(
                composition.Input.SetScene(scene),
                "Paint replay could not install input continuity."
            );
            Assert(
                composition.TryAcknowledgePresentation(scene.Generation),
                "Paint replay acknowledgement failed."
            );
            phases.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        Assert(beforeSemantics == composition.SemanticRevision, "Hover recomputed semantic state.");
        Assert(
            composition.ExecuteSemanticCommand(row.Identity, new(SemanticCommandKind.Select))
                == SemanticCommandResult.Applied,
            "Selection was delayed by visual motion."
        );
        using var selected = Install(composition, renderer, hover.Viewport);
        Assert(
            composition.PresentationDiagnostics.ActiveTracks == 0,
            "Applied selection must cancel cosmetic hover motion on its first frame."
        );
        theme.ReducedMotion = true;
        composition.SamplePresentation(TimeSpan.FromMilliseconds(120));
        using var suppressed = Install(composition, renderer, hover.Viewport);
        Assert(!composition.PresentationDemand.IsActive, "Reduced motion left stock demand armed.");

        var output = Environment.GetEnvironmentVariable("LUCENT_MOTION_CHARACTERIZATION");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(
                Path.Combine(output, "issue-browser-motion.txt"),
                FormattableString.Invariant(
                    $"sourceRows=10000 realizedBoxes={hover.Boxes.Count} sampleFrames={phases.Count} projectionP50Ms={phases.Order().ElementAt(phases.Count / 2):F3} projectionMaxMs={phases.Max():F3} additionalLayoutPasses=0 additionalSemanticRevisions=0\n"
                )
            );
        }
    }
}
