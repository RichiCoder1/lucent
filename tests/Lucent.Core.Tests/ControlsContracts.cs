using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ControlsContracts
{
    [TestMethod]
    public void SemanticCommandsFailClosed()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "semantic-commands");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        Controls.Panel(
            composition.Root,
            theme,
            "root",
            Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 60f)
        );
        var calls = 0;
        var button = composition.Child(composition.Root, "button");
        Controls.Button(button, theme, "Button", () => calls++);
        var field = composition.Child(composition.Root, "field");
        Controls.TextField(field, theme, "Field");
        var list = composition.Child(composition.Root, "list");
        Controls.List(list, theme, "Choices");
        var keep = composition.Child(list, "keep");
        var keepState = Controls.Selectable(keep, theme, "Keep");
        var retire = composition.Child(list, "retire");
        var retireState = Controls.Selectable(retire, theme, "Retire");
        graph.Drain();
        var nodes = Flatten(composition.SemanticSnapshot()!).ToArray();
        var buttonNode = nodes.Single(node => node.Name == "Button");
        var fieldNode = nodes.Single(node => node.Name == "Field");
        Assert(
            composition.SemanticDump().Contains("suppressions=[]", StringComparison.Ordinal),
            "Declared semantic matrix did not report zero suppressions."
        );
        Assert(
            composition.ExecuteSemanticCommand(buttonNode.Identity, new(SemanticCommandKind.Invoke))
                == SemanticCommandResult.Applied
                && calls == 1,
            "Declared Invoke did not dispatch through its behavior."
        );
        Assert(
            composition.ExecuteSemanticCommand(
                fieldNode.Identity,
                new(SemanticCommandKind.SetValue, "日本")
            ) == SemanticCommandResult.Applied,
            "Declared SetValue did not dispatch through text state."
        );
        graph.Drain();
        fieldNode = Flatten(composition.SemanticSnapshot()!).Single(node => node.Name == "Field");
        Assert(fieldNode.Value == "日本", "Semantic SetValue did not refresh the retained value.");
        Assert(
            composition.ExecuteSemanticCommand(buttonNode.Identity, new(SemanticCommandKind.Select))
                == SemanticCommandResult.Rejected
                && composition.ExecuteSemanticCommand(
                    fieldNode.Identity,
                    new(SemanticCommandKind.SetValue, "bad\nvalue")
                ) == SemanticCommandResult.Rejected
                && composition.ExecuteSemanticCommand(
                    buttonNode.Identity with
                    {
                        Generation = buttonNode.Identity.Generation + 1,
                    },
                    new(SemanticCommandKind.Invoke)
                ) == SemanticCommandResult.Stale,
            "Unsupported, malformed, or stale semantic commands did not fail closed."
        );
        var keepNode = Flatten(composition.SemanticSnapshot()!).Single(node => node.Name == "Keep");
        var retireNode = Flatten(composition.SemanticSnapshot()!)
            .Single(node => node.Name == "Retire");
        var firstSelection = composition.ExecuteSemanticCommand(
            keepNode.Identity,
            new(SemanticCommandKind.Select)
        );
        retireNode = Flatten(composition.SemanticSnapshot()!).Single(node => node.Name == "Retire");
        var secondSelection = composition.ExecuteSemanticCommand(
            retireNode.Identity,
            new(SemanticCommandKind.Select)
        );
        Assert(
            firstSelection == SemanticCommandResult.Applied
                && secondSelection == SemanticCommandResult.Applied,
            $"Selectable semantic commands were rejected: {firstSelection}/{secondSelection}."
        );
        graph.Drain();
        var selected = Flatten(composition.SemanticSnapshot()!)
            .Where(node => node.Role == SemanticRole.ListItem && node.Selected)
            .ToArray();
        Assert(
            selected.Length == 1
                && selected[0].Name == "Retire"
                && !keepState.Selected
                && retireState.Selected,
            "List selection did not clear the sibling behavior and ControlState."
        );
    }

    private static string Build(out Fixture fixture)
    {
        var graph = new ReactiveGraph();
        var composition = new Composition(graph, "controls");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        Controls.Column(
            composition.Root,
            theme,
            "Controls",
            Style.Empty.Set(LayoutProperties.Clip, true)
        );
        var text = composition.Child(composition.Root, "text");
        Controls.Text(text, theme, "Text", Style.Empty.Set(LayoutProperties.Height, 10f));
        var panel = composition.Child(composition.Root, "panel");
        Controls.Panel(panel, theme, "Panel", Style.Empty.Set(LayoutProperties.Height, 10f));
        var row = composition.Child(composition.Root, "row");
        Controls.Row(row, theme, "Row", Style.Empty.Set(LayoutProperties.Height, 10f));
        var calls = new Calls();
        var button = composition.Child(composition.Root, "button");
        Controls.Button(
            button,
            theme,
            "Button",
            () => calls.Button++,
            Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 20f)
        );
        var selectable = composition.Child(composition.Root, "selectable");
        Controls.Selectable(
            selectable,
            theme,
            "Selectable",
            () => calls.Selection++,
            Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 20f)
        );
        var viewport = composition.Child(composition.Root, "viewport");
        Controls.ScrollViewport(
            viewport,
            theme,
            "Viewport",
            style: Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 20f)
        );
        var loading = composition.Child(composition.Root, "loading");
        Controls.Loading(loading, theme, "Loading", Style.Empty.Set(LayoutProperties.Height, 10f));
        var progress = composition.Child(composition.Root, "progress");
        Controls.Progress(
            progress,
            theme,
            "Progress",
            .5f,
            Style.Empty.Set(LayoutProperties.Height, 10f)
        );
        var error = composition.Child(composition.Root, "error");
        Controls.Error(
            error,
            theme,
            "Failed",
            Style
                .Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
                .Set(LayoutProperties.Height, 30f)
        );
        var retry = composition.Child(error, "retry");
        Controls.Button(
            retry,
            theme,
            "Retry",
            () => calls.Retry++,
            Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 20f)
        );
        var conditional = composition.When(
            composition.Root,
            "recipe",
            () => true,
            context =>
            {
                var recipe = context.Element("recipe-child");
                Controls.Text(recipe, theme, "Recipe");
                return recipe;
            }
        );
        graph.Drain();
        Assert(conditional.Active is not null, "Explicit recipe did not create its supplied root.");
        fixture = new(graph, composition, theme, button, selectable, retry, calls);
        return composition.Dump();
    }

    private static void ActivateAndSelect(Fixture fixture)
    {
        var router = fixture.Composition.Input;
        var scene = SceneLayout.Project(fixture.Composition, new(100, 220, 1), new EmptyShaper());
        Assert(router.SetScene(scene), "Control scene was rejected.");
        var buttonBox = scene
            .Boxes.Single(candidate => candidate.Identity.ElementId == fixture.Button.Id)
            .Bounds;
        router.DispatchPointer(
            new(
                PointerCommandKind.Down,
                0,
                buttonBox.X + 1,
                buttonBox.Y + 1,
                PointerButton.Secondary
            )
        );
        router.DispatchPointer(new(PointerCommandKind.Up, 0, buttonBox.X + 1, buttonBox.Y + 1));
        Assert(fixture.Calls.Button == 0, "Button accepted a non-primary pointer activation.");
        Click(router, scene, fixture.Button, 1);
        Click(router, scene, fixture.Selectable, 2);
        Click(router, scene, fixture.Retry, 3);
        router.DispatchKey(new(KeyCommandKind.Down, Key.Enter));
        Assert(
            fixture.Calls.Button == 1 && fixture.Calls.Selection == 1 && fixture.Calls.Retry == 2,
            "Button/selectable/retry intents did not route through pointer and keyboard exactly once each."
        );
        var semantic = fixture.Composition.SemanticSnapshot()!;
        var selected = Flatten(semantic).Single(node => node.Name == "Selectable");
        Assert(
            selected.Selected
                && selected.Actions == SemanticAction.Select
                && Flatten(semantic).Single(node => node.Name == "Button").Actions
                    == SemanticAction.Invoke,
            "Control semantics or selectable state diverged."
        );
        router.MoveFocus(FocusTraversalDirection.Next);
        Assert(
            fixture.Composition.Dump().Contains("FocusVisible", StringComparison.Ordinal),
            "Keyboard focus did not produce a reusable focus-visible variant."
        );
    }

    private static void StatusRetryThemesAndDisposal(Fixture fixture)
    {
        var status = Flatten(fixture.Composition.SemanticSnapshot()!)
            .Where(node => node.Role == SemanticRole.Status)
            .ToArray();
        Assert(
            status.Length == 3 && status.Single(node => node.Name == "Progress").Value == "50%",
            "Loading/progress/error status semantics are incomplete."
        );
        var light = fixture.Button.Resolve(VisualProperties.Background).Value;
        fixture.Theme.Theme = ControlThemes.Dark;
        fixture.Graph.Drain();
        Assert(
            light != fixture.Button.Resolve(VisualProperties.Background).Value,
            "Source-owned dark control theme did not invalidate button style."
        );
        var identity = Flatten(fixture.Composition.SemanticSnapshot()!)
            .Single(node => node.Name == "Retry")
            .Identity;
        fixture.Retry.Dispose();
        Assert(
            !fixture.Composition.IsCurrent(identity),
            "Disposed control semantic identity remained current."
        );
        var stale = fixture.Composition.Input.DispatchPointer(
            new(PointerCommandKind.Move, 4, 1, 1)
        );
        Assert(
            stale.Rejection == InputRejection.StaleScene,
            "Disposed control did not fail closed against its prior retained scene."
        );
        Expect<ArgumentOutOfRangeException>(() =>
            Controls.Progress(fixture.Button, fixture.Theme, "bad", 2)
        );
    }

    [TestMethod]
    public void ArmedPointerAndPaletteRegressions()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "armed");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.HighContrast);
        Controls.Panel(
            composition.Root,
            theme,
            "root",
            Style
                .Empty.Set(LayoutProperties.Width, 100f)
                .Set(LayoutProperties.Height, 60f)
                .Set(LayoutProperties.Clip, true)
        );
        var calls = 0;
        var button = composition.Child(composition.Root, "button");
        Controls.Button(
            button,
            theme,
            "Button",
            () => calls++,
            Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 20f)
        );
        var row = composition.Child(composition.Root, "row");
        var selected = Controls.Selectable(
            row,
            theme,
            "Row",
            () => calls++,
            Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 20f)
        );
        graph.Drain();
        var router = composition.Input;
        var scene = SceneLayout.Project(composition, new(100, 60, 1), new EmptyShaper());
        Assert(router.SetScene(scene), "Armed-pointer scene rejected.");
        var expectedCalls = 0;
        foreach (var element in new[] { button, row })
        {
            var box = scene
                .Boxes.Single(candidate => candidate.Identity.ElementId == element.Id)
                .Bounds;
            router.DispatchPointer(
                new(
                    PointerCommandKind.Down,
                    (int)element.Id,
                    box.X + 1,
                    box.Y + 1,
                    PointerButton.Primary
                )
            );
            router.DispatchPointer(
                new(PointerCommandKind.Up, (int)element.Id, box.X + box.Width + 1, box.Y + 1)
            );
            router.DispatchPointer(
                new(
                    PointerCommandKind.Down,
                    (int)element.Id + 10,
                    box.X + 1,
                    box.Y + 1,
                    PointerButton.Primary
                )
            );
            router.DispatchPointer(
                new(PointerCommandKind.Cancel, (int)element.Id + 10, box.X + 1, box.Y + 1)
            );
            Assert(
                calls == expectedCalls,
                "Captured outside/cancel gesture activated " + element.Name + "."
            );
            router.DispatchPointer(
                new(
                    PointerCommandKind.Down,
                    (int)element.Id + 15,
                    box.X + 1,
                    box.Y + 1,
                    PointerButton.Primary
                )
            );
            scene = SceneLayout.Project(composition, new(100, 60, 1), new EmptyShaper());
            Assert(router.SetScene(scene), "Capture-loss replacement scene rejected.");
            router.DispatchPointer(
                new(PointerCommandKind.Up, (int)element.Id + 15, box.X + 1, box.Y + 1)
            );
            expectedCalls++;
            Assert(
                calls == expectedCalls,
                "Reactive replacement cancelled a stable " + element.Name + " gesture."
            );
            router.DispatchPointer(
                new(
                    PointerCommandKind.Down,
                    (int)element.Id + 20,
                    box.X + 1,
                    box.Y + 1,
                    PointerButton.Primary
                )
            );
            router.DispatchPointer(
                new(PointerCommandKind.Up, (int)element.Id + 20, box.X + 1, box.Y + 1)
            );
            expectedCalls++;
        }
        Assert(
            calls == 4 && selected.Selected,
            "Armed primary gestures did not activate both controls exactly once."
        );
        row.SetVariants(VariantState.FocusVisible);
        Assert(
            row.Resolve(VisualProperties.Background).Value.Color == Color.Parse("#ffff00")
                && row.Resolve(TypographyProperties.TextColor).Value == Color.Parse("#000000"),
            "High-contrast focus channels collide."
        );
        selected.Selected = true;
        graph.Drain();
        Assert(
            row.Resolve(VisualProperties.Background).Value.Color == Color.Parse("#ffff00"),
            "Focus should remain distinct and visible over selection."
        );
        row.SetVariants(VariantState.None);
        Assert(
            row.Resolve(VisualProperties.Background).Value.Color == Color.Parse("#0000ff"),
            "High-contrast selection is indistinguishable from its surface."
        );
        theme.Theme = ControlThemes.Dark;
        Assert(
            row.Resolve(VisualProperties.Background).Value.Color == Color.Parse("#1e3a5f"),
            "Dark selection token did not resolve deterministically."
        );
        var overrideButton = composition.Child(composition.Root, "override");
        Controls.Button(
            overrideButton,
            theme,
            "Override",
            style: Style.Empty.When(
                VariantState.FocusVisible,
                Style.Empty.Set(VisualProperties.Background, Color.Parse("#00ff00"))
            )
        );
        overrideButton.SetVariants(VariantState.FocusVisible);
        Assert(
            overrideButton.Resolve(VisualProperties.Background).Value.Color
                == Color.Parse("#00ff00"),
            "Typed author focus style did not override the source palette."
        );
    }

    [TestMethod]
    public void CaptureContinuityAcrossReprojection()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "capture-continuity");
        var enabled = new Token<bool>("capture-enabled", true);
        var visible = new Token<bool>("capture-visible", true);
        var paint = new Token<Brush>("capture-paint", Color.Parse("#000000"));
        var refresh = composition.Root.Scope.Signal(false, "capture-refresh");
        var baseTheme = ControlThemes.Light.Set(enabled, true).Set(visible, true);
        var theme = new ThemeContext(
            composition.Root.Scope,
            baseTheme.Set(paint, Color.Parse("#000000"))
        );
        _ = composition.Root.Scope.Effect(
            () =>
                theme.Theme = baseTheme.Set(
                    paint,
                    refresh.Value ? Color.Parse("#ffffff") : Color.Parse("#000000")
                ),
            "capture-refresh-effect"
        );
        Controls.Panel(
            composition.Root,
            theme,
            "root",
            Style
                .Empty.Set(LayoutProperties.Width, 100f)
                .Set(LayoutProperties.Height, 60f)
                .Set(LayoutProperties.Clip, true)
                .Set(VisualProperties.Background, paint)
        );
        var rows = graph.Signal(new[] { 0, 1 }, "capture-rows");
        var calls = 0;
        var region = composition.ForEach(
            composition.Root,
            "rows",
            () => rows.Value,
            value => value,
            (value, context) =>
            {
                var item = value.Value;
                var row = context.Element("row");
                if (item == 0)
                    Controls.Button(
                        row,
                        theme,
                        "Button",
                        () => calls++,
                        Style
                            .Empty.Set(LayoutProperties.Width, 100f)
                            .Set(LayoutProperties.Height, 20f)
                            .Set(InputProperties.Enabled, enabled)
                    );
                else
                    Controls.Selectable(
                        row,
                        theme,
                        "Selectable",
                        () => calls++,
                        Style
                            .Empty.Set(LayoutProperties.Width, 100f)
                            .Set(LayoutProperties.Height, 20f)
                            .Set(InputProperties.Visible, visible)
                    );
                return row;
            }
        );
        graph.Drain();
        var router = composition.Input;
        RetainedScene Install()
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                graph.Drain();
                var scene = SceneLayout.Project(composition, new(100, 60, 1), new EmptyShaper());
                if (router.SetScene(scene))
                    return scene;
            }
            throw new InvalidOperationException("Capture continuity scene did not converge.");
        }
        (float X, float Y) Point(RetainedScene scene, Element element)
        {
            var box = scene.Boxes.Single(box => box.Identity.ElementId == element.Id).Bounds;
            return (box.X + 1, box.Y + 1);
        }
        void Down(RetainedScene scene, Element element, int pointer)
        {
            var point = Point(scene, element);
            router.DispatchPointer(
                new(PointerCommandKind.Down, pointer, point.X, point.Y, PointerButton.Primary)
            );
        }
        void Up(RetainedScene scene, Element element, int pointer)
        {
            var point = Point(scene, element);
            router.DispatchPointer(new(PointerCommandKind.Up, pointer, point.X, point.Y));
        }

        var button = region.Items[0];
        var selectable = region.Items[1];
        var scene = Install();
        Down(scene, button, 40);
        refresh.Value = true;
        graph.Drain();
        scene = Install();
        Up(scene, button, 40);
        Assert(
            calls == 1 && !router.Dump().Contains("capture pointer=40", StringComparison.Ordinal),
            "Reactive replacement cancelled a structurally stable button gesture."
        );
        scene = Install();
        Down(scene, selectable, 41);
        refresh.Value = false;
        graph.Drain();
        scene = Install();
        Up(scene, selectable, 41);
        Assert(
            calls == 2 && !router.Dump().Contains("capture pointer=41", StringComparison.Ordinal),
            "Reactive replacement cancelled a structurally stable selectable gesture."
        );

        scene = Install();
        Down(scene, button, 42);
        rows.Value = [1, 0];
        graph.Drain();
        scene = Install();
        Up(scene, button, 42);
        Assert(
            calls == 2
                && router.Dump().Contains("captureLoss=SceneChanged", StringComparison.Ordinal),
            "Reordered retained path kept a captured button armed."
        );
        button = region.Items[1];
        selectable = region.Items[0];
        scene = Install();
        Down(scene, selectable, 43);
        rows.Value = [0];
        graph.Drain();
        scene = Install();
        Up(scene, composition.Root, 43);
        Assert(
            calls == 2 && router.Dump().Contains("captureLoss=Disposed", StringComparison.Ordinal),
            "Removed selectable capture did not fail closed."
        );

        rows.Value = [0, 1];
        graph.Drain();
        button = region.Items[0];
        selectable = region.Items[1];
        scene = Install();
        Down(scene, button, 44);
        theme.Theme = theme.Theme.Set(enabled, false);
        scene = Install();
        Up(scene, button, 44);
        Assert(
            calls == 2 && router.Dump().Contains("captureLoss=Disabled", StringComparison.Ordinal),
            "Disabled button capture did not fail closed."
        );
        theme.Theme = theme.Theme.Set(enabled, true);
        scene = Install();
        Down(scene, selectable, 45);
        theme.Theme = theme.Theme.Set(visible, false);
        scene = Install();
        Up(scene, selectable, 45);
        Assert(
            calls == 2 && router.Dump().Contains("captureLoss=Hidden", StringComparison.Ordinal),
            "Hidden selectable capture did not fail closed."
        );
        theme.Theme = theme.Theme.Set(visible, true);
        scene = Install();
        Down(scene, button, 46);
        _ = SceneLayout.Project(composition, new(100, 60, 1), new EmptyShaper());
        Assert(!router.SetScene(scene), "Superseded capture scene was accepted.");
        Assert(
            calls == 2
                && router.Dump().Contains("captureLoss=SceneChanged", StringComparison.Ordinal),
            "Superseded replacement kept capture armed."
        );
    }

    [TestMethod]
    public void MutableStateScrollAndRollback()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "mutable");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        Controls.Panel(
            composition.Root,
            theme,
            "root",
            Style
                .Empty.Set(LayoutProperties.Width, 100f)
                .Set(LayoutProperties.Height, 120f)
                .Set(LayoutProperties.Clip, true)
        );
        var loading = composition.Child(composition.Root, "loading");
        var loadingState = Controls.Loading(loading, theme, "Loading");
        var progress = composition.Child(composition.Root, "progress");
        var progressState = Controls.Progress(progress, theme, "Progress", .25f);
        var error = composition.Child(composition.Root, "error");
        var errorState = Controls.Error(error, theme, "Error");
        var viewport = composition.Child(composition.Root, "viewport");
        var scroll = Controls.ScrollViewport(
            viewport,
            theme,
            "Viewport",
            style: Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 20f)
        );
        var content = composition.Child(viewport, "content");
        Controls.Button(
            content,
            theme,
            "Content",
            style: Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 100f)
        );
        graph.Drain();
        var before = Flatten(composition.SemanticSnapshot()!)
            .Single(node => node.Name == "Progress")
            .Identity;
        loadingState.Label = "Ready";
        progressState.Label = "Uploading";
        progressState.Progress = .75f;
        errorState.Label = "Retry";
        graph.Drain();
        var status = Flatten(composition.SemanticSnapshot()!)
            .Where(node => node.Role == SemanticRole.Status)
            .ToArray();
        Assert(
            status.Single(node => node.Name == "Uploading").Value == "75%"
                && loading.Resolve(ProjectionProperties.Text).Value == "Ready"
                && !composition.IsCurrent(before),
            "Mutable control state did not synchronize visual/semantic freshness."
        );
        var router = composition.Input;
        var first = SceneLayout.Project(composition, new(100, 120, 1), new EmptyShaper());
        Assert(
            router.SetScene(first) && router.MoveFocus(FocusTraversalDirection.Next),
            "Scroll test scene/focus rejected."
        );
        var contentBefore = first
            .Boxes.Single(box => box.Identity.ElementId == content.Id)
            .Bounds.Y;
        router.DispatchKey(new(KeyCommandKind.Down, Key.Down));
        graph.Drain();
        Assert(
            scroll.Offset.Y == 40 && viewport.Resolve(LayoutProperties.Scroll).Value.Y == 40,
            "Scroll key did not update bounded arrangement state."
        );
        var second = SceneLayout.Project(composition, new(100, 120, 1), new EmptyShaper());
        Assert(
            second.Boxes.Single(box => box.Identity.ElementId == content.Id).Bounds.Y
                == contentBefore - 40
                && composition.Dump() == composition.Dump(),
            "Scroll projection or diagnostic dump is not deterministic."
        );
        Assert(router.SetScene(second), "Scrolled scene rejected.");
        router.DispatchKey(new(KeyCommandKind.Down, Key.End));
        graph.Drain();
        Assert(scroll.Offset.Y == 80, "Scroll end did not clamp to content bounds.");
        var stale = Flatten(composition.SemanticSnapshot()!)
            .Single(node => node.Name == "Viewport")
            .Identity;
        viewport.Dispose();
        Assert(
            !composition.IsCurrent(stale),
            "Disposed viewport semantic identity remained current."
        );
        Expect<ObjectDisposedException>(() => scroll.Offset = default);

        var conflict = composition.Child(composition.Root, "conflict");
        conflict.AttachBehaviors(new ConflictBehavior());
        var dump = composition.Dump();
        var topology = graph.Dump();
        Expect<InvalidOperationException>(() => Controls.Loading(conflict, theme));
        Expect<InvalidOperationException>(() => Controls.Selectable(conflict, theme, "Conflict"));
        Assert(
            composition.Dump() == dump && graph.Dump() == topology,
            "Control ownership conflict partially installed presentation/state."
        );
    }

    [TestMethod]
    public void PresentationPreflightAndControlAuthority()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "control-preflight");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        Controls.Panel(
            composition.Root,
            theme,
            "root",
            Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 100f)
        );
        var duplicate = composition.Child(composition.Root, "duplicate");
        var type = composition.Child(composition.Root, "type");
        var progress = composition.Child(composition.Root, "progress-conflict");
        progress.AttachBehaviors(new ConflictBehavior());
        var error = composition.Child(composition.Root, "error-conflict");
        error.AttachBehaviors(new ConflictBehavior());
        var viewport = composition.Child(composition.Root, "viewport-conflict");
        viewport.AttachBehaviors(new ConflictBehavior());
        var disposedTheme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        disposedTheme.Dispose();
        var disposedSelectable = composition.Child(composition.Root, "disposed-selectable");
        var disposedLoading = composition.Child(composition.Root, "disposed-loading");
        var disposedProgress = composition.Child(composition.Root, "disposed-progress");
        var disposedError = composition.Child(composition.Root, "disposed-error");
        var disposedViewport = composition.Child(composition.Root, "disposed-viewport");
        graph.Drain();
        var router = composition.Input;
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(100, 100, 1), new EmptyShaper())),
            "Preflight baseline scene rejected."
        );
        var graphDump = graph.Dump();
        var compositionDump = composition.Dump();
        var semantic = SemanticSummary(composition.SemanticSnapshot());
        var input = router.Dump();
        var duplicateText = Style.Empty.Set(
            new Property<string?>(ProjectionProperties.Text.Name, null),
            "author"
        );
        var typeText = Style.Empty.Set(new Property<int>(ProjectionProperties.Text.Name, 0), 1);
        foreach (
            var attempt in new Action[]
            {
                () => Controls.Selectable(duplicate, theme, "Duplicate", style: duplicateText),
                () => Controls.Loading(type, theme, style: typeText),
                () => Controls.Progress(progress, theme, "Progress", .5f),
                () => Controls.Error(error, theme, "Error"),
                () => Controls.ScrollViewport(viewport, theme, "Viewport"),
            }
        )
        {
            ExpectPreflightFailure(attempt);
            graph.Drain();
            Assert(
                graph.Dump() == graphDump
                    && composition.Dump() == compositionDump
                    && SemanticSummary(composition.SemanticSnapshot()) == semantic
                    && router.Dump() == input,
                "Failed control setup leaked graph, presentation, semantic, or input state."
            );
        }
        foreach (
            var attempt in new Action[]
            {
                () => Controls.Selectable(disposedSelectable, disposedTheme, "Selectable"),
                () => Controls.Loading(disposedLoading, disposedTheme),
                () => Controls.Progress(disposedProgress, disposedTheme, "Progress", .5f),
                () => Controls.Error(disposedError, disposedTheme, "Error"),
                () => Controls.ScrollViewport(disposedViewport, disposedTheme, "Viewport"),
            }
        )
        {
            ExpectPreflightFailure(attempt);
            graph.Drain();
            Assert(
                graph.Dump() == graphDump
                    && composition.Dump() == compositionDump
                    && SemanticSummary(composition.SemanticSnapshot()) == semantic
                    && router.Dump() == input,
                "Disposed theme setup leaked graph, presentation, semantic, or input state."
            );
        }

        var loading = composition.Child(composition.Root, "author-loading");
        var loadingState = Controls.Loading(
            loading,
            theme,
            "Loading",
            Style
                .Empty.Set(ProjectionProperties.Text, "author-text")
                .Set(LayoutProperties.Height, 10f)
        );
        var scroll = composition.Child(composition.Root, "author-scroll");
        var scrollState = Controls.ScrollViewport(
            scroll,
            theme,
            "Scroll",
            style: Style
                .Empty.Set(LayoutProperties.Scroll, new ScrollOffset(99, 99))
                .Set(LayoutProperties.Width, 100f)
                .Set(LayoutProperties.Height, 20f)
        );
        var content = composition.Child(scroll, "author-content");
        Controls.Panel(
            content,
            theme,
            "content",
            Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 100f)
        );
        graph.Drain();
        loadingState.Label = "Ready";
        scrollState.Offset = new(0, 40);
        graph.Drain();
        var scene = SceneLayout.Project(composition, new(100, 180, 1), new EmptyShaper());
        var status = Flatten(composition.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.Status && node.Name == "Ready");
        Assert(
            loading.Resolve(ProjectionProperties.Text).Value == status.Name
                && loading.Resolve(ProjectionProperties.Text).Winner.Source == "control"
                && loading.Resolve(LayoutProperties.Height)
                    is { Value: 10f, Winner.Source: "author" }
                && scroll.Resolve(LayoutProperties.Scroll).Value == scrollState.Offset
                && scroll.Resolve(LayoutProperties.Scroll).Winner.Source == "control"
                && scene.Boxes.Single(box => box.Identity.ElementId == content.Id).Bounds.Y
                    < scene.Boxes.Single(box => box.Identity.ElementId == scroll.Id).Bounds.Y
                && composition.Dump().Contains("winner=\"control\"", StringComparison.Ordinal),
            "Author Text/Scroll assignments split control visuals, dump provenance, or semantics from control state."
        );
    }

    [TestMethod]
    public void ViewportFocusAndSceneInstallation()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "viewport-focus");
        var height = new Token<float?>("content-height", 100f);
        var viewportHeight = new Token<float?>("viewport-height", 20f);
        var theme = new ThemeContext(
            composition.Root.Scope,
            new Theme("viewport-focus").Set(height, 100f).Set(viewportHeight, 20f)
        );
        Controls.Panel(
            composition.Root,
            theme,
            "root",
            Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 20f)
        );
        var viewport = composition.Child(composition.Root, "viewport");
        var state = Controls.ScrollViewport(
            viewport,
            theme,
            "Viewport",
            new(0, 100),
            Style
                .Empty.Set(LayoutProperties.Width, 100f)
                .Set(LayoutProperties.Height, viewportHeight)
        );
        var content = composition.Child(viewport, "content");
        Controls.Panel(
            content,
            theme,
            "Content",
            Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, height)
        );
        graph.Drain();
        var router = composition.Input;
        var initial = SceneLayout.Project(composition, new(100, 20, 1), new EmptyShaper());
        Assert(
            !router.SetScene(initial)
                && state.Offset.Y == 80
                && router.DispatchPointer(new(PointerCommandKind.Move, 30, 0, 0)).Rejection
                    == InputRejection.NoScene,
            "Out-of-range initial scroll installed an immediately stale scene."
        );
        graph.Drain();
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(100, 20, 1), new EmptyShaper())),
            "Clamped initial scroll scene rejected."
        );
        router.DispatchKey(new(KeyCommandKind.Down, Key.Tab));
        Assert(
            router.FocusedElement?.ElementId == viewport.Id
                && Flatten(composition.SemanticSnapshot()!).Single(node => node.Name == "Viewport")
                    is { Focused: true, Actions: SemanticAction.Scroll },
            "Viewport without focusable descendants was not reachable by Tab."
        );
        router.DispatchKey(new(KeyCommandKind.Down, Key.Home));
        graph.Drain();
        Assert(state.Offset == default, "Viewport Home did not scroll to the start.");
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(100, 20, 1), new EmptyShaper())),
            "Home scene rejected."
        );
        router.DispatchKey(new(KeyCommandKind.Down, Key.Down));
        graph.Drain();
        Assert(state.Offset.Y == 40, "Viewport arrow key did not scroll deterministically.");
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(100, 20, 1), new EmptyShaper())),
            "Arrow scene rejected."
        );
        router.DispatchKey(new(KeyCommandKind.Down, Key.End));
        graph.Drain();
        Assert(state.Offset.Y == 80, "Viewport End did not scroll to the bounded end.");
        theme.Theme = theme.Theme.Set(height, 20f);
        graph.Drain();
        Assert(
            !router.SetScene(SceneLayout.Project(composition, new(100, 20, 1), new EmptyShaper()))
                && state.Offset == default
                && router.DispatchKey(new(KeyCommandKind.Down, Key.Down)).Rejection
                    == InputRejection.NoScene,
            "Content shrink accepted an immediately stale scroll scene."
        );
        graph.Drain();
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(100, 20, 1), new EmptyShaper())),
            "Shrunk-content reprojection was rejected."
        );
        theme.Theme = theme.Theme.Set(height, 100f).Set(viewportHeight, 10f);
        graph.Drain();
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(100, 20, 1), new EmptyShaper()))
                && router.DispatchPointer(new(PointerCommandKind.Move, 31, 0, 0)).Rejection
                    != InputRejection.StaleScene,
            "Viewport shrink installed a stale retained scene."
        );
        var focusable = composition.Child(viewport, "focusable");
        Controls.Button(
            focusable,
            theme,
            "Focusable",
            style: Style.Empty.Set(LayoutProperties.Height, 10f)
        );
        graph.Drain();
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(100, 20, 1), new EmptyShaper()))
                && router.MoveFocus(FocusTraversalDirection.Next)
                && router.FocusedElement?.ElementId == focusable.Id,
            "Viewport displaced its focusable descendant from deterministic traversal order."
        );
    }

    [TestMethod]
    public void ScrollUsesInstalledGeometry()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "installed-scroll");
        var contentWidth = new Token<float?>("content-width", 100f);
        var contentHeight = new Token<float?>("content-height", 100f);
        var viewportWidth = new Token<float?>("viewport-width", 20f);
        var viewportHeight = new Token<float?>("viewport-height", 20f);
        var theme = new ThemeContext(
            composition.Root.Scope,
            new Theme("installed-scroll")
                .Set(contentWidth, 100f)
                .Set(contentHeight, 100f)
                .Set(viewportWidth, 20f)
                .Set(viewportHeight, 20f)
        );
        Controls.Panel(
            composition.Root,
            theme,
            "root",
            Style
                .Empty.Set(LayoutProperties.Width, 20f)
                .Set(LayoutProperties.Height, 20f)
                .Set(LayoutProperties.Clip, true)
        );
        var viewport = composition.Child(composition.Root, "viewport");
        var state = Controls.ScrollViewport(
            viewport,
            theme,
            "Viewport",
            style: Style
                .Empty.Set(LayoutProperties.Width, viewportWidth)
                .Set(LayoutProperties.Height, viewportHeight)
        );
        var content = composition.Child(viewport, "content");
        Controls.Panel(
            content,
            theme,
            "Content",
            Style
                .Empty.Set(LayoutProperties.Width, contentWidth)
                .Set(LayoutProperties.Height, contentHeight)
        );
        graph.Drain();
        var router = composition.Input;
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(20, 20, 1), new EmptyShaper()))
                && router.MoveFocus(FocusTraversalDirection.Next),
            "Installed-scroll baseline was rejected."
        );
        Assert(
            !router.DispatchKey(new(KeyCommandKind.Down, Key.Left)).Handled
                && !router.DispatchKey(new(KeyCommandKind.Down, Key.Up)).Handled
                && !router.DispatchKey(new(KeyCommandKind.Down, Key.Home)).Handled
                && state.Offset == default,
            "Start-boundary Left, Up, or Home threw or changed the offset."
        );
        for (var key = 0; key < 3; key++)
        {
            _ = router.DispatchKey(new(KeyCommandKind.Down, Key.Right));
            _ = router.DispatchKey(new(KeyCommandKind.Down, Key.Down));
        }
        Assert(
            state.Offset == new ScrollOffset(80, 80),
            "Burst Right/Down exceeded the installed content extent before reprojection."
        );
        _ = router.DispatchKey(new(KeyCommandKind.Down, Key.Home));
        Assert(state.Offset == default, "Home did not return both axes to start.");
        _ = router.DispatchKey(new(KeyCommandKind.Down, Key.End));
        Assert(
            state.Offset == new ScrollOffset(80, 80),
            "End did not use the installed content extent."
        );
        theme.Theme = theme.Theme.Set(contentWidth, 20f).Set(contentHeight, 20f);
        graph.Drain();
        Assert(
            !router.SetScene(SceneLayout.Project(composition, new(20, 20, 1), new EmptyShaper()))
                && state.Offset == default,
            "Content shrink accepted an out-of-range installed offset."
        );
        graph.Drain();
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(20, 20, 1), new EmptyShaper())),
            "Shrunk-content reprojection was rejected."
        );
        theme.Theme = theme
            .Theme.Set(contentWidth, 100f)
            .Set(contentHeight, 100f)
            .Set(viewportWidth, 10f)
            .Set(viewportHeight, 10f);
        graph.Drain();
        Assert(
            router.SetScene(SceneLayout.Project(composition, new(20, 20, 1), new EmptyShaper()))
                && router.MoveFocus(FocusTraversalDirection.Next),
            "Viewport change rejected a valid zero offset."
        );
        _ = router.DispatchKey(new(KeyCommandKind.Down, Key.End));
        Assert(
            state.Offset == new ScrollOffset(90, 90),
            "Viewport change did not update the scroll extent."
        );
    }

    [TestMethod]
    public void VisualRejectionPreservesOnlyStableCapture()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "visual-rejection");
        var otherEnabled = new Token<bool>("other-enabled", true);
        var theme = new ThemeContext(
            composition.Root.Scope,
            new Theme("visual-rejection").Set(otherEnabled, true)
        );
        Controls.Panel(
            composition.Root,
            theme,
            "root",
            Style
                .Empty.Set(LayoutProperties.Width, 100f)
                .Set(LayoutProperties.Height, 40f)
                .Set(LayoutProperties.Clip, true)
        );
        var rows = graph.Signal(new[] { 0, 1 }, "visual-rows");
        var activations = 0;
        var region = composition.ForEach(
            composition.Root,
            "rows",
            () => rows.Value,
            value => value,
            (value, context) =>
            {
                var item = value.Value;
                var row = context.Element("row");
                if (item == 0)
                    Controls.Button(
                        row,
                        theme,
                        "Button",
                        () => activations++,
                        Style
                            .Empty.Set(LayoutProperties.Width, 100f)
                            .Set(LayoutProperties.Height, 20f)
                    );
                else
                    Controls.Panel(
                        row,
                        theme,
                        "Other",
                        Style
                            .Empty.Set(InputProperties.Enabled, otherEnabled)
                            .Set(LayoutProperties.Width, 100f)
                            .Set(LayoutProperties.Height, 20f)
                    );
                return row;
            }
        );
        RetainedScene Install()
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                graph.Drain();
                var scene = SceneLayout.Project(composition, new(100, 40, 1), new EmptyShaper());
                if (composition.Input.SetScene(scene))
                    return scene;
            }
            throw new InvalidOperationException("Visual reconciliation did not converge.");
        }
        (float X, float Y) Point(RetainedScene scene)
        {
            var bounds = scene
                .Boxes.Single(box => box.Identity.ElementId == region.Items[0].Id)
                .Bounds;
            return (bounds.X + 1, bounds.Y + 1);
        }
        var router = composition.Input;
        var scene = Install();
        var point = Point(scene);
        _ = router.DispatchPointer(
            new(PointerCommandKind.Down, 70, point.X, point.Y, PointerButton.Primary)
        );
        theme.Theme = theme.Theme.Set(otherEnabled, false);
        graph.Drain();
        Assert(
            !router.SetScene(SceneLayout.Project(composition, new(100, 40, 1), new EmptyShaper()))
                && router
                    .DispatchPointer(new(PointerCommandKind.Move, 70, point.X, point.Y))
                    .Rejection == InputRejection.NoScene,
            "Visual reconciliation left a rejected scene dispatchable."
        );
        scene = Install();
        point = Point(scene);
        _ = router.DispatchPointer(new(PointerCommandKind.Up, 70, point.X, point.Y));
        Assert(activations == 1, "A visual-only rejection released a structurally stable capture.");

        theme.Theme = theme.Theme.Set(otherEnabled, true);
        scene = Install();
        point = Point(scene);
        _ = router.DispatchPointer(
            new(PointerCommandKind.Down, 71, point.X, point.Y, PointerButton.Primary)
        );
        theme.Theme = theme.Theme.Set(otherEnabled, false);
        graph.Drain();
        Assert(
            !router.SetScene(SceneLayout.Project(composition, new(100, 40, 1), new EmptyShaper())),
            "Expected visual reconciliation rejection."
        );
        rows.Value = [1, 0];
        graph.Drain();
        _ = Install();
        _ = router.DispatchPointer(new(PointerCommandKind.Up, 71, point.X, point.Y));
        Assert(
            activations == 1
                && router.Dump().Contains("captureLoss=SceneChanged", StringComparison.Ordinal),
            "A structural replacement retained capture through visual reconciliation."
        );
    }

    private static void Click(InputRouter router, RetainedScene scene, Element element, int pointer)
    {
        var box = scene
            .Boxes.Single(candidate => candidate.Identity.ElementId == element.Id)
            .Bounds;
        router.DispatchPointer(
            new(PointerCommandKind.Down, pointer, box.X + 1, box.Y + 1, PointerButton.Primary)
        );
        router.DispatchPointer(new(PointerCommandKind.Up, pointer, box.X + 1, box.Y + 1));
    }

    private static IEnumerable<SemanticSnapshot> Flatten(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Flatten(child))
            yield return descendant;
    }

    private static string SemanticSummary(SemanticSnapshot? snapshot) =>
        snapshot is null
            ? "-"
            : string.Join(
                '|',
                Flatten(snapshot)
                    .Select(node =>
                        node.Identity.ElementId
                        + ":"
                        + node.Identity.Generation
                        + ":"
                        + node.Name
                        + ":"
                        + node.Actions
                    )
            );

    private static void Expect<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static void ExpectPreflightFailure(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException("Expected control setup preflight failure.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class Fixture(
        ReactiveGraph graph,
        Composition composition,
        ThemeContext theme,
        Element button,
        Element selectable,
        Element retry,
        Calls calls
    )
    {
        public ReactiveGraph Graph { get; } = graph;
        public Composition Composition { get; } = composition;
        public ThemeContext Theme { get; } = theme;
        public Element Button { get; } = button;
        public Element Selectable { get; } = selectable;
        public Element Retry { get; } = retry;
        public Calls Calls { get; } = calls;
    }

    private sealed class Calls
    {
        public int Button;
        public int Selection;
        public int Retry;
    }

    private sealed class ConflictBehavior : Behavior
    {
        public override string Name => "conflict";
        public override BehaviorOwnership Ownership => BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context) =>
            context.SetSemantics(new(SemanticRole.Group, "conflict"));
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            if (request.Text.Length == 0)
                return new("empty", 0, 0, []);
            var glyph = new ShapedGlyph(1, 0, 0, 0, request.Text.Length, 0, 0);
            var run = new ShapedRun(
                "controls",
                "controls",
                400,
                5,
                0,
                "controls",
                0,
                "controls#0",
                request.Direction,
                request.Language,
                request.FontSize,
                0,
                request.FontSize,
                -request.FontSize,
                0,
                request.Text.Length,
                [glyph]
            );
            return new("controls", request.Text.Length, request.FontSize, [run]);
        }
    }

    [TestMethod]
    public void EquivalentCompositionsAreDeterministic()
    {
        var first = Build(out var controls);
        using var composition = controls.Composition;
        Assert(
            first == Build(out var second),
            "Control composition dumps differ for equivalent recipes."
        );
        using (second.Composition) { }
    }

    [TestMethod]
    public void ActivationAndSelection()
    {
        _ = Build(out var controls);
        using var composition = controls.Composition;
        ActivateAndSelect(controls);
    }

    [TestMethod]
    public void StatusRetryThemesAndDisposal()
    {
        _ = Build(out var controls);
        using var composition = controls.Composition;
        ActivateAndSelect(controls);
        StatusRetryThemesAndDisposal(controls);
    }
}
