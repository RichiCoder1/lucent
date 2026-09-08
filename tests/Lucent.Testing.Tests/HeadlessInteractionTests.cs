using Lucent.Core;
using Lucent.Reactive.R3;
using Lucent.Testing;
using Lucent.Testing.Skia;
using Microsoft.Extensions.Time.Testing;

namespace Lucent.Testing.Tests;

[TestClass]
public sealed class HeadlessInteractionTests
{
    [TestMethod]
    public async Task CompiledLuiEditorRoutesCommittedTextAndSelection()
    {
        await using var app = await HeadlessApplication.StartAsync(
            HeadlessFixtures.Components.Editor()
        );

        var initial = await app.SnapshotAsync();
        var initialText = EditorText(initial);
        Assert.AreEqual("seed", initialText.Text);
        Assert.AreEqual(initialText.Text.Length, initialText.Caret);

        await app.KeyAsync(new(KeyCommandKind.Down, Key.Tab));
        await app.KeyAsync(new(KeyCommandKind.Down, Key.End));
        await app.TextAsync(new(TextInputKind.Commit, "-draft"));

        var edited = EditorText(await app.SnapshotAsync());
        Assert.AreEqual("seed-draft", edited.Text);
        Assert.AreEqual(edited.Text.Length, edited.Caret);
        Assert.AreEqual(edited.Caret, edited.Anchor);

        await app.KeyAsync(new(KeyCommandKind.Down, Key.Home, KeyModifiers.Shift));
        var selected = EditorText(await app.SnapshotAsync());
        Assert.AreEqual(edited.Text.Length, selected.Anchor);
        Assert.AreEqual(0, selected.Caret);
    }

    [TestMethod]
    public async Task CompiledLuiEditorRetainsSemanticIdentityAndReprojectsOnResize()
    {
        await using var app = await HeadlessApplication.StartAsync(
            HeadlessFixtures.Components.Editor(),
            new HeadlessApplicationOptions
            {
                Viewport = new(360, 160, 1.5f),
                Appearance = new(ThemeColorScheme.Dark, ThemeContrast.Normal),
                ThemeFactory = _ => new Theme("headless-dark"),
            }
        );

        var before = await app.SnapshotAsync();
        var field = before.Require(SemanticRole.TextField, "Draft");
        var beforeBox = before.RequireBox(field);
        Assert.AreEqual(new LayoutViewport(360, 160, 1.5f), before.Scene.Viewport);

        var themeName = await app.InvokeAsync(context => context.Session.Theme.Theme.Name);
        Assert.AreEqual("headless-dark", themeName);

        await app.ResizeAsync(new LayoutViewport(640, 240, 2));
        var after = await app.SnapshotAsync();
        var afterField = after.Require(SemanticRole.TextField, "Draft");
        Assert.AreEqual(field.Identity.ElementId, afterField.Identity.ElementId);
        Assert.AreEqual(new LayoutViewport(640, 240, 2), after.Scene.Viewport);
        Assert.AreEqual(beforeBox.Bounds.Width, after.RequireBox(afterField).Bounds.Width);
    }

    [TestMethod]
    public async Task SkiaCompiledLuiEditorUsesShapedGeometryForPointerSelection()
    {
        await using var app = await SkiaHeadlessApplication.StartAsync(
            HeadlessFixtures.Components.Editor(),
            new HeadlessApplicationOptions { Viewport = new(400, 160, 1) }
        );

        var initial = await app.SnapshotAsync();
        var field = initial.Require(SemanticRole.TextField, "Draft");
        var textNode = initial.SceneNodes(field).OfType<TextSceneNode>().Single();
        var bounds = textNode.Bounds;
        var y = bounds.Y + MathF.Max(1, bounds.Height / 2);
        var left = bounds.X + 1;
        var right = MathF.Max(left + 1, bounds.X + bounds.Width - 1);

        await app.PointerAsync(new(PointerCommandKind.Down, 1, left, y, PointerButton.Primary));
        await app.PointerAsync(new(PointerCommandKind.Up, 1, left, y));
        var clicked = EditorText(await app.SnapshotAsync());
        Assert.IsTrue(
            clicked.Caret <= 1,
            $"Expected the left glyph hit, got caret {clicked.Caret}."
        );

        await app.PointerAsync(new(PointerCommandKind.Down, 1, left, y, PointerButton.Primary));
        await app.PointerAsync(new(PointerCommandKind.Move, 1, right, y));
        await app.PointerAsync(new(PointerCommandKind.Up, 1, right, y));
        var dragged = EditorText(await app.SnapshotAsync());
        Assert.AreNotEqual(dragged.Anchor, dragged.Caret);
        Assert.IsTrue(Math.Max(dragged.Anchor, dragged.Caret) >= 3);

        var png = await app.CapturePngAsync();
        CollectionAssert.AreEqual(
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
            png.Take(8).ToArray()
        );
    }

    [TestMethod]
    public async Task CompiledLuiMenuPreservesSelectionAndOwnsOnePopup()
    {
        var calls = 0;
        var selected = 0;
        var opening = new List<bool>();
        var menu = new MenuProbe();
        Action enable = null!;
        await using var app = await HeadlessApplication.StartAsync(context =>
        {
            var enabled = context.Composition.Root.Scope.Signal(false, "menu-enabled");
            enable = () => enabled.Value = true;
            return HeadlessFixtures.Components.MenuSurface(
                () => Interlocked.Increment(ref calls),
                () => Interlocked.Add(ref calls, 2),
                () => enabled.Value,
                static () => false,
                () => Volatile.Read(ref selected) != 0,
                () => Interlocked.Exchange(ref selected, 1),
                value => opening.Add(value)
            );
        });

        await app.InvokeAsync(context =>
        {
            context.Input.ContextMenuRequested += request => menu.Request = request;
            return context.Input.MoveFocus(FocusTraversalDirection.Next);
        });
        var ownerBefore = await app.SnapshotAsync();
        var target = ownerBefore.Require(SemanticRole.ListItem, "Target");
        Assert.IsTrue(target.Focused);
        Assert.AreEqual(0, Volatile.Read(ref selected));

        await app.PointerAsync(new(PointerCommandKind.Down, 1, 10, 10, PointerButton.Secondary));
        await app.PointerAsync(new(PointerCommandKind.Up, 1, 10, 10));

        var disabled = await app.InvokeAsync(context =>
        {
            var request =
                menu.Request
                ?? throw new AssertFailedException("The headless menu request was not raised.");
            Assert.AreSame(context.Composition, request.Owner);
            Assert.AreEqual(target.Identity.CompositionEpoch, request.Target.CompositionEpoch);
            Assert.IsTrue(request.IsValid);
            menu.Popup = request.CreateComposition();
            Assert.AreSame(menu.Popup, request.CreateComposition());
            var measure = request.Measure(new HeadlessTextShaper(), new(800, 600, 1));
            Assert.AreEqual(260f, measure.Width);
            Assert.IsTrue(measure.Height >= 96 && measure.Height < 600);
            InstallPopup(menu.Popup, context.Viewport);
            var run = Find(menu.Popup.SemanticSnapshot(), SemanticRole.MenuItem, "Run");
            Assert.IsFalse(run.Enabled);
            return menu.Popup.ExecuteSemanticCommand(run.Identity, new(SemanticCommandKind.Invoke));
        });
        Assert.AreEqual(SemanticCommandResult.Disabled, disabled);
        Assert.AreEqual(0, Volatile.Read(ref calls));

        await app.InvokeAsync(_ =>
        {
            enable();
            return 0;
        });
        await app.DrainAsync();
        var invoked = await app.InvokeAsync(context =>
        {
            InstallPopup(menu.Popup!, context.Viewport);
            var item = Find(menu.Popup!.SemanticSnapshot(), SemanticRole.MenuItem, "Run");
            Assert.IsTrue(item.Enabled);
            Assert.IsTrue(menu.Popup.Input.MoveFocus(FocusTraversalDirection.Next));
            var focused = Find(menu.Popup.SemanticSnapshot(), SemanticRole.MenuItem, "Run");
            Assert.IsTrue(focused.Focused);
            Assert.AreEqual(
                focused.Identity.ElementId,
                menu.Popup.Input.FocusedElement!.Value.ElementId
            );
            menu.Popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Down));
            InstallPopup(menu.Popup, context.Viewport);
            var other = Find(menu.Popup.SemanticSnapshot(), SemanticRole.MenuItem, "Other");
            Assert.IsTrue(other.Focused);
            Assert.AreEqual(
                other.Identity.ElementId,
                menu.Popup.Input.FocusedElement!.Value.ElementId
            );
            menu.Popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Home));
            InstallPopup(menu.Popup, context.Viewport);
            var run = Find(menu.Popup.SemanticSnapshot(), SemanticRole.MenuItem, "Run");
            Assert.IsTrue(run.Focused);
            var enter = menu.Popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Enter));
            Assert.IsTrue(enter.Handled);
            return menu.Request!.IsDismissed
                ? SemanticCommandResult.Applied
                : SemanticCommandResult.Rejected;
        });
        Assert.AreEqual(SemanticCommandResult.Applied, invoked);
        Assert.AreEqual(1, Volatile.Read(ref calls));
        Assert.AreEqual(0, Volatile.Read(ref selected));

        await app.InvokeAsync(context =>
        {
            Assert.IsTrue(menu.Request!.IsDismissed);
            Assert.IsTrue(menu.Request.RestoreFocus());
            menu.Request.Dispose();
            return 0;
        });
        var ownerAfter = await app.SnapshotAsync();
        Assert.AreEqual(2, opening.Count);
        Assert.IsTrue(opening[0]);
        Assert.IsFalse(opening[1]);
        Assert.IsTrue(ownerAfter.Require(SemanticRole.ListItem, "Target").Focused);
    }

    [TestMethod]
    public async Task CompiledLuiMenuKeyboardDismissalRestoresFocus()
    {
        var menu = new MenuProbe();
        await using var app = await HeadlessApplication.StartAsync(
            HeadlessFixtures.Components.MenuSurface(
                static () => { },
                static () => { },
                static () => true,
                static () => false,
                static () => false,
                static () => { },
                static _ => { }
            )
        );

        await app.InvokeAsync(context =>
        {
            context.Input.ContextMenuRequested += request => menu.Request = request;
            return context.Input.MoveFocus(FocusTraversalDirection.Next);
        });
        await app.KeyAsync(new(KeyCommandKind.Down, Key.ContextMenu));
        await app.InvokeAsync(context =>
        {
            var request =
                menu.Request
                ?? throw new AssertFailedException("The headless menu request was not raised.");
            menu.Popup = request.CreateComposition();
            InstallPopup(menu.Popup, context.Viewport);
            var open = menu.Popup.SemanticSnapshot();
            _ = Find(open, SemanticRole.Menu, "Context menu");
            Assert.IsTrue(FindAll(open, SemanticRole.MenuItem).Length >= 2);
            Assert.IsTrue(menu.Popup.Input.MoveFocus(FocusTraversalDirection.Next));
            menu.Popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Escape));
            Assert.IsTrue(request.IsDismissed);
            Assert.IsTrue(request.RestoreFocus());
            request.Dispose();
            return 0;
        });
        var closed = await app.SnapshotAsync();
        Assert.IsTrue(closed.Require(SemanticRole.ListItem, "Target").Focused);
    }

    [TestMethod]
    public async Task DebounceAdvanceDeliversOnlyLatestCallbackOnOwner()
    {
        await using var app = await HeadlessApplication.StartAsync(
            ComponentRecipe.Create("debounce-root", (_, _) => { })
        );

        var probe = await app.InvokeAsync(context =>
        {
            var state = new DebounceProbe(Environment.CurrentManagedThreadId);
            var scheduler = new OwnedDebouncedAction(
                context.Composition.Root.Scope,
                context.TimeProvider
            );
            scheduler.Restart(TimeSpan.FromSeconds(1), () => state.Record(1));
            context.TimeProvider.Advance(TimeSpan.FromMilliseconds(500));
            scheduler.Restart(TimeSpan.FromSeconds(1), () => state.Record(2));
            return state;
        });

        await app.AdvanceAsync(TimeSpan.FromMilliseconds(999));
        Assert.AreEqual(0, probe.Calls);
        await app.AdvanceAsync(TimeSpan.FromMilliseconds(1));
        Assert.AreEqual(1, probe.Calls);
        Assert.AreEqual(2, probe.LastValue);
        Assert.AreEqual(probe.OwnerThread, probe.CallbackThread);
    }

    [TestMethod]
    public async Task DebounceCancelSuppressesAlreadyQueuedCallback()
    {
        await using var app = await HeadlessApplication.StartAsync(
            ComponentRecipe.Create("debounce-cancel-root", (_, _) => { })
        );

        var probe = await app.InvokeAsync(context =>
        {
            var state = new DebounceProbe(Environment.CurrentManagedThreadId);
            var scheduler = new OwnedDebouncedAction(
                context.Composition.Root.Scope,
                context.TimeProvider
            );
            scheduler.Restart(TimeSpan.FromSeconds(1), () => state.Record(1));
            context.TimeProvider.Advance(TimeSpan.FromSeconds(1));
            scheduler.Cancel();
            return state;
        });

        await app.DrainAsync();
        Assert.AreEqual(0, probe.Calls);
    }

    [TestMethod]
    public async Task DisposingHeadlessApplicationSuppressesPendingOwnedWork()
    {
        await using var app = await HeadlessApplication.StartAsync(
            ComponentRecipe.Create("dispose-root", (_, _) => { })
        );
        var state = await app.InvokeAsync(context =>
        {
            var result = new DebounceProbe(Environment.CurrentManagedThreadId)
            {
                Clock = context.TimeProvider,
            };
            var scheduler = new OwnedDebouncedAction(
                context.Composition.Root.Scope,
                context.TimeProvider
            );
            scheduler.Restart(TimeSpan.FromSeconds(1), () => result.Record(1));
            return result;
        });

        await app.DisposeAsync();
        state.Clock!.Advance(TimeSpan.FromSeconds(1));
        Assert.AreEqual(0, state.Calls);
    }

    private static SemanticTextSnapshot EditorText(HeadlessSnapshot snapshot)
    {
        var field = snapshot.Require(SemanticRole.TextField, "Draft");
        Assert.IsNotNull(field.Text);
        return field.Text!;
    }

    private static SemanticSnapshot Find(SemanticSnapshot? root, SemanticRole role, string name)
    {
        if (root is null)
            throw new InvalidOperationException("No semantic snapshot was available.");
        if (root.Role == role && root.Name == name)
            return root;
        foreach (var child in root.Children)
        {
            if (TryFind(child, role, name, out var found))
                return found;
        }
        throw new InvalidOperationException($"No semantic node matched {role}/{name}.");
    }

    private static SemanticSnapshot[] FindAll(SemanticSnapshot? root, SemanticRole role) =>
        root is null ? [] : Descendants(root).Where(node => node.Role == role).ToArray();

    private static bool TryFind(
        SemanticSnapshot root,
        SemanticRole role,
        string name,
        out SemanticSnapshot found
    )
    {
        if (root.Role == role && root.Name == name)
        {
            found = root;
            return true;
        }
        foreach (var child in root.Children)
            if (TryFind(child, role, name, out found))
                return true;
        found = null!;
        return false;
    }

    private static IEnumerable<SemanticSnapshot> Descendants(SemanticSnapshot root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var descendant in Descendants(child))
            yield return descendant;
    }

    private static void InstallPopup(Composition popup, LayoutViewport viewport)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var scene = SceneLayout.Project(popup, viewport, new HeadlessTextShaper());
            if (popup.Input.SetScene(scene))
                return;
        }
        Assert.Fail("Popup scene installation failed.");
    }

    private sealed class MenuProbe
    {
        public ContextMenuRequest? Request { get; set; }
        public Composition? Popup { get; set; }
    }

    private sealed class DebounceProbe(int ownerThread)
    {
        public int OwnerThread { get; } = ownerThread;
        public int Calls { get; private set; }
        public int LastValue { get; private set; }
        public int CallbackThread { get; private set; }
        public FakeTimeProvider? Clock { get; init; }

        public void Record(int value)
        {
            Calls++;
            LastValue = value;
            CallbackThread = Environment.CurrentManagedThreadId;
        }
    }
}
