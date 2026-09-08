using Lucent.Core;

namespace Lucent.Testing.Tests;

[TestClass]
public sealed class SplitPaneTests
{
    [TestMethod]
    public async Task CompiledResponsiveBranchMountsAndRemovesASplitPane()
    {
        await using var app = await HeadlessApplication.StartAsync(
            context =>
                HeadlessFixtures.Components.ResponsiveSplit(
                    new(context.Composition.Root.Scope),
                    new(context.Composition.Root.Scope)
                ),
            new() { Viewport = new(700, 400, 1) }
        );
        _ = (await app.SnapshotAsync()).Require(SemanticRole.Text, "Narrow content");
        await app.ResizeAsync(new(1000, 400, 1));
        Assert.AreEqual(
            320d,
            (await app.SnapshotAsync()).Require(SemanticRole.Splitter).Range!.Value
        );
        await app.ResizeAsync(new(700, 400, 1));
        _ = (await app.SnapshotAsync()).Require(SemanticRole.Text, "Narrow content");
        await app.ResizeAsync(new(1000, 400, 1.5f));
        Assert.AreEqual(
            320d,
            (await app.SnapshotAsync()).Require(SemanticRole.Splitter).Range!.Value
        );
    }

    [TestMethod]
    public async Task ClosingDuringCapturedResizeReleasesThePointerCleanly()
    {
        var app = await HeadlessApplication.StartAsync(
            context =>
                HeadlessFixtures.Components.SplitSurface(new(context.Composition.Root.Scope)),
            new() { Viewport = new(800, 400, 1) }
        );
        try
        {
            var snapshot = await app.SnapshotAsync();
            var bounds = snapshot.RequireBox(snapshot.Require(SemanticRole.Splitter)).Bounds;
            await app.PointerAsync(
                new(PointerCommandKind.Down, 1, bounds.X + 2, bounds.Y + 2, PointerButton.Primary)
            );
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task PreferredExtentSurvivesConstrainedLayoutWithoutRemountingPanes()
    {
        SplitPaneState? state = null;
        await using var app = await HeadlessApplication.StartAsync(
            context =>
            {
                state = new(context.Composition.Root.Scope, 320);
                return HeadlessFixtures.Components.SplitSurface(state);
            },
            new() { Viewport = new(800, 400, 1) }
        );
        var before = await app.SnapshotAsync();
        var first = before.Require(SemanticRole.Text, "First pane");
        var splitter = before.Require(SemanticRole.Splitter);
        Assert.AreEqual(320d, splitter.Range!.Value);
        Assert.AreEqual(632d, splitter.Range.Maximum);

        await app.ResizeAsync(new(300, 400, 1.5f));
        var constrained = await app.SnapshotAsync();
        Assert.AreEqual(132d, constrained.Require(SemanticRole.Splitter).Range!.Value);
        Assert.AreEqual(320f, await app.InvokeAsync(_ => state!.PreferredExtent));
        Assert.AreEqual(
            first.Identity.ElementId,
            constrained.Require(SemanticRole.Text, "First pane").Identity.ElementId
        );

        await app.ResizeAsync(new(800, 400, 1));
        Assert.AreEqual(
            320d,
            (await app.SnapshotAsync()).Require(SemanticRole.Splitter).Range!.Value
        );
        await app.ResizeAsync(new(4, 400, 1));
        var tiny = await app.SnapshotAsync();
        Assert.AreEqual(0d, tiny.Require(SemanticRole.Splitter).Range!.Value);
        Assert.IsTrue(
            tiny.Scene.Boxes.All(box => float.IsFinite(box.Bounds.Width) && box.Bounds.Width >= 0)
        );
    }

    [TestMethod]
    public async Task PointerCaptureMovesPaneAndCancelStopsFurtherResize()
    {
        await using var app = await HeadlessApplication.StartAsync(
            context =>
                HeadlessFixtures.Components.SplitSurface(new(context.Composition.Root.Scope, 320)),
            new() { Viewport = new(800, 400, 1) }
        );
        var snapshot = await app.SnapshotAsync();
        var splitter = snapshot.Require(SemanticRole.Splitter);
        var bounds = snapshot.RequireBox(splitter).Bounds;
        var x = bounds.X + bounds.Width / 2;
        var y = bounds.Y + bounds.Height / 2;
        Assert.AreEqual(
            CursorIntent.ResizeHorizontal,
            await app.InvokeAsync(c => c.Input.CursorAt(x, y))
        );
        await app.PointerAsync(new(PointerCommandKind.Down, 1, x, y, PointerButton.Primary));
        await app.PointerAsync(new(PointerCommandKind.Move, 1, x + 70, y));
        Assert.AreEqual(
            390d,
            (await app.SnapshotAsync()).Require(SemanticRole.Splitter).Range!.Value
        );
        await app.PointerAsync(new(PointerCommandKind.Cancel, 1, x + 70, y));
        await app.PointerAsync(new(PointerCommandKind.Move, 1, x + 120, y));
        Assert.AreEqual(
            390d,
            (await app.SnapshotAsync()).Require(SemanticRole.Splitter).Range!.Value
        );
    }

    [TestMethod]
    public async Task KeyboardAndRangeCommandsRespectCurrentBoundsAndStaleIdentity()
    {
        await using var app = await HeadlessApplication.StartAsync(
            context =>
                HeadlessFixtures.Components.SplitSurface(new(context.Composition.Root.Scope, 320)),
            new() { Viewport = new(800, 400, 1) }
        );
        await app.KeyAsync(new(KeyCommandKind.Down, Key.Tab));
        await app.KeyAsync(new(KeyCommandKind.Down, Key.Right));
        Assert.AreEqual(
            328d,
            (await app.SnapshotAsync()).Require(SemanticRole.Splitter).Range!.Value
        );
        await app.KeyAsync(new(KeyCommandKind.Down, Key.Right, KeyModifiers.Shift));
        Assert.AreEqual(
            368d,
            (await app.SnapshotAsync()).Require(SemanticRole.Splitter).Range!.Value
        );
        await app.KeyAsync(new(KeyCommandKind.Down, Key.Home));
        var old = (await app.SnapshotAsync()).Require(SemanticRole.Splitter);
        Assert.AreEqual(160d, old.Range!.Value);
        await app.KeyAsync(new(KeyCommandKind.Down, Key.End));
        var current = (await app.SnapshotAsync()).Require(SemanticRole.Splitter);
        Assert.AreEqual(632d, current.Range!.Value);
        Assert.AreEqual(
            SemanticCommandResult.Stale,
            await app.InvokeAsync(c =>
                c.Composition.ExecuteSemanticCommand(
                    old.Identity,
                    new(SemanticCommandKind.SetRangeValue, NumericValue: 400)
                )
            )
        );
        Assert.AreEqual(
            SemanticCommandResult.Rejected,
            await app.InvokeAsync(c =>
                c.Composition.ExecuteSemanticCommand(
                    current.Identity,
                    new(SemanticCommandKind.SetRangeValue, NumericValue: 700)
                )
            )
        );
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            await app.InvokeAsync(c =>
                c.Composition.ExecuteSemanticCommand(
                    current.Identity,
                    new(SemanticCommandKind.SetRangeValue, NumericValue: 400)
                )
            )
        );
        Assert.AreEqual(
            400d,
            (await app.SnapshotAsync()).Require(SemanticRole.Splitter).Range!.Value
        );
    }

    [TestMethod]
    public async Task VerticalPaneUsesVerticalKeysAndCursor()
    {
        await using var app = await HeadlessApplication.StartAsync(
            context =>
                HeadlessFixtures.Components.SplitSurface(
                    new(context.Composition.Root.Scope, 220, 80, 100, LayoutAxis.Column)
                ),
            new() { Viewport = new(500, 600, 1) }
        );
        var snapshot = await app.SnapshotAsync();
        var splitter = snapshot.Require(SemanticRole.Splitter);
        var bounds = snapshot.RequireBox(splitter).Bounds;
        Assert.AreEqual(
            CursorIntent.ResizeVertical,
            await app.InvokeAsync(c => c.Input.CursorAt(bounds.X + 1, bounds.Y + 1))
        );
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            await app.InvokeAsync(c =>
                c.Composition.ExecuteSemanticCommand(
                    splitter.Identity,
                    new(SemanticCommandKind.Focus)
                )
            )
        );
        await app.KeyAsync(new(KeyCommandKind.Down, Key.Down));
        Assert.AreEqual(
            228d,
            (await app.SnapshotAsync()).Require(SemanticRole.Splitter).Range!.Value
        );
        await app.KeyAsync(new(KeyCommandKind.Down, Key.Right));
        Assert.AreEqual(
            228d,
            (await app.SnapshotAsync()).Require(SemanticRole.Splitter).Range!.Value
        );
    }
}
