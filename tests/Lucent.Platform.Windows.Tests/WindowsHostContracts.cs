using Lucent.Core;
using Lucent.Platform.Windows;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

[TestClass]
public sealed class WindowsHostContracts
{
    [TestMethod]
    public void InputAdapterAndRoutingContract()
    {
        Assert(
            WindowsInputAdapter.MapKey(SDL.Keycode.Tab) == Key.Tab
                && WindowsInputAdapter.MapKey(SDL.Keycode.KpEnter) == Key.Enter
                && WindowsInputAdapter.MapKey(SDL.Keycode.A) is null
                && WindowsInputAdapter.MapShortcut(SDL.Keycode.A, KeyModifiers.Control) == Key.A
                && WindowsInputAdapter.MapShortcut(
                    SDL.Keycode.A,
                    KeyModifiers.Control | KeyModifiers.Alt
                )
                    is null
                && WindowsInputAdapter.MapShortcut(
                    SDL.Keycode.A,
                    KeyModifiers.Meta | KeyModifiers.Alt
                )
                    is null
                && WindowsInputAdapter.MapShortcut(SDL.Keycode.A, KeyModifiers.None) is null
                && WindowsInputAdapter.MapKey(SDL.Keycode.RAlt) is null,
            "Command-key translation invented printable, AltGr, or dead-key text input."
        );
        var modifiers = WindowsInputAdapter.MapModifiers(
            SDL.Keymod.LShift | SDL.Keymod.LCtrl | SDL.Keymod.RAlt | SDL.Keymod.RGUI
        );
        Assert(
            modifiers
                == (
                    KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta
                )
                && WindowsInputAdapter.MapButton(1) == PointerButton.Primary
                && WindowsInputAdapter.MapButton(2) == PointerButton.Middle
                && WindowsInputAdapter.MapButton(3) == PointerButton.Secondary
                && WindowsInputAdapter.MapButton(4) is null,
            "SDL modifier/button conversion was not bounded to portable values: " + modifiers
        );

        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "windows-input");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("windows-input"));
        composition.Root.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 40f)
                .Set(LayoutProperties.Height, 40f)
                .Set(LayoutProperties.Clip, true)
        );
        var child = composition.Child(composition.Root, "target");
        var activations = 0;
        child.Present(
            theme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 20f)
                .Set(LayoutProperties.Height, 20f)
                .Set(VisualProperties.Background, Color.Parse("#ffffff"))
        );
        child.AttachBehaviors(
            new RowActionBehavior(
                "target-action",
                new(SemanticRole.Button, "target", actions: SemanticAction.Invoke),
                () => activations++
            )
        );
        using var renderer = new SkiaSceneRenderer();
        var router = composition.Input;
        var scene = SceneLayout.Project(composition, new(40, 40, 1), renderer);
        Assert(router.SetScene(scene), "Input adapter test scene was rejected.");
        var box = scene.Boxes.Single(candidate => candidate.Identity.ElementId == child.Id);
        var x = box.Bounds.X + 1;
        var y = box.Bounds.Y + 1;
        using var adapter = new WindowsInputAdapter(composition);
        Assert(
            adapter.Dispatch(
                new SDL.Event
                {
                    Button = new()
                    {
                        Type = SDL.EventType.MouseButtonDown,
                        Which = 9,
                        Button = 1,
                        X = x,
                        Y = y,
                    },
                }
            )
                && router.FocusedElement?.ElementId == child.Id
                && router.Dump().Contains("capture pointer=9", StringComparison.Ordinal),
            "SDL pointer down did not enter Core focus/capture routing."
        );
        Assert(
            adapter.Dispatch(
                new SDL.Event
                {
                    Motion = new()
                    {
                        Type = SDL.EventType.MouseMotion,
                        Which = 9,
                        X = 39,
                        Y = 39,
                    },
                }
            ),
            "SDL captured pointer move was ignored."
        );
        adapter.Dispatch(new SDL.Event { Window = new() { Type = SDL.EventType.WindowFocusLost } });
        Assert(
            !router.Dump().Contains("capture pointer=9", StringComparison.Ordinal),
            "Focus loss did not cancel active Core capture."
        );
        Assert(
            adapter.Dispatch(
                new SDL.Event
                {
                    Key = new()
                    {
                        Type = SDL.EventType.KeyDown,
                        Key = SDL.Keycode.Return,
                        Down = true,
                        Repeat = true,
                    },
                }
            )
                && activations == 0,
            "Repeated key down escaped Core repeat semantics."
        );
        Assert(
            adapter.Dispatch(
                new SDL.Event
                {
                    Key = new()
                    {
                        Type = SDL.EventType.KeyDown,
                        Key = SDL.Keycode.Return,
                        Down = true,
                    },
                }
            )
                && activations == 1,
            "Mapped key down did not route to focused Core behavior."
        );
        adapter.Dispose();
        Assert(
            !adapter.Dispatch(
                new SDL.Event
                {
                    Key = new()
                    {
                        Type = SDL.EventType.KeyDown,
                        Key = SDL.Keycode.Tab,
                        Down = true,
                    },
                }
            ),
            "Disposed adapter accepted a stale callback."
        );

        var textGraph = new ReactiveGraph();
        using var textComposition = new Composition(textGraph, "windows-text-input");
        var textTheme = new ThemeContext(textComposition.Root.Scope, ControlThemes.Light);
        Controls.Panel(
            textComposition.Root,
            textTheme,
            "root",
            Style.Empty.Set(LayoutProperties.Width, 40f).Set(LayoutProperties.Height, 20f)
        );
        var textField = textComposition.Child(textComposition.Root, "field");
        Controls.TextField(
            textField,
            textTheme,
            "Field",
            "a",
            Style.Empty.Set(LayoutProperties.Width, 40f).Set(LayoutProperties.Height, 20f)
        );
        var textRouter = textComposition.Input;
        Assert(
            textRouter.SetScene(SceneLayout.Project(textComposition, new(40, 20, 1), renderer))
                && textRouter.MoveFocus(FocusTraversalDirection.Next),
            "Text adapter field did not focus."
        );
        textComposition.Flush();
        Assert(
            textRouter.SetScene(SceneLayout.Project(textComposition, new(40, 20, 1), renderer)),
            "Focused text field scene did not converge."
        );
        var starts = 0;
        var stops = 0;
        SDL.Rect? area = null;
        var active = false;
        using (
            var textAdapter = new WindowsInputAdapter(
                textComposition,
                1,
                textInput: new TextInputTransport(
                    _ => active,
                    _ =>
                    {
                        starts++;
                        active = true;
                        return true;
                    },
                    _ =>
                    {
                        stops++;
                        active = false;
                        return true;
                    },
                    (_, value, _) =>
                    {
                        area = value;
                        return true;
                    }
                )
            )
        )
        {
            textAdapter.RefreshTextInput();
            Assert(
                starts == 1 && area is { W: 1, H: 20 },
                "Focused text field did not start SDL input with its Core caret rectangle."
            );
            Assert(
                textAdapter.DispatchText(new(TextInputKind.Preedit, "中", 0, 1))
                    && stops == 0
                    && starts == 1,
                "Handled preedit synchronized stale text geometry and stopped SDL input."
            );
            textAdapter.Dispatch(
                new SDL.Event { Window = new() { Type = SDL.EventType.WindowFocusLost } }
            );
            Assert(
                !textAdapter.DispatchText(new(TextInputKind.Commit, "x"))
                    && stops == 1
                    && starts == 1,
                "Window focus loss accepted queued text or failed to stop SDL input."
            );
            textAdapter.RefreshTextInput();
            Assert(stops == 1 && starts == 1, "Refresh restarted text input while unfocused.");
            textAdapter.Dispatch(
                new SDL.Event { Window = new() { Type = SDL.EventType.WindowFocusGained } }
            );
            Assert(starts == 2, "Window focus gain did not permit text input restart.");
        }
        Assert(stops == 2, "Text adapter disposal did not stop SDL text input.");
    }

    [TestMethod]
    public void SettingsAndAppearanceContract()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "windows-settings");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("windows-settings"));
        var settingsSnapshot = new WindowsSettingsSnapshot(
            ThemeColorScheme.Dark,
            ThemeContrast.High,
            false
        );
        var settings = new WindowsSettings(() => settingsSnapshot);
        var appearanceRuns = 0;
        var motionRuns = 0;
        var appearance = composition.Root.Scope.Derived(
            () =>
            {
                appearanceRuns++;
                return theme.Appearance;
            },
            "platform-appearance-reader"
        );
        var motion = composition.Root.Scope.Derived(
            () =>
            {
                motionRuns++;
                return theme.ReducedMotion;
            },
            "platform-motion-reader"
        );
        _ = appearance.Value;
        _ = motion.Value;
        Assert(
            settings.Apply(theme)
                && appearance.Value
                    == new ThemeAppearance(ThemeColorScheme.Dark, ThemeContrast.High)
                && !motion.Value
                && appearanceRuns == 2
                && motionRuns == 1
                && !settings.Apply(theme),
            "Settings did not coalesce equal values or invalidate only changed portable facets."
        );
        settingsSnapshot = settingsSnapshot with { ReducedMotion = true };
        Assert(
            settings.Apply(theme) && appearanceRuns == 2 && motion.Value && motionRuns == 2,
            "Reduced motion invalidated an unrelated appearance reader."
        );
        settingsSnapshot = new(null, null, null);
        Assert(
            !settings.Apply(theme)
                && theme.Appearance
                    == new ThemeAppearance(ThemeColorScheme.Dark, ThemeContrast.High)
                && theme.ReducedMotion,
            "Unknown theme or failed SPI fields fabricated a settings update."
        );

        var startupGraph = new ReactiveGraph();
        using var startupComposition = new Composition(startupGraph, "settings-startup");
        var startupTheme = new ThemeContext(
            startupComposition.Root.Scope,
            new Theme("settings-startup")
        );
        ThemeAppearance? firstAppearance = null;
        _ = startupComposition.Root.Scope.Effect(
            () => firstAppearance = startupTheme.Appearance,
            "first-frame-appearance"
        );
        var startupSettings = new WindowsSettings(() =>
            new(ThemeColorScheme.Dark, ThemeContrast.Normal, true)
        );
        Assert(
            WindowsBootstrap.ApplySettings(startupComposition, startupSettings, startupTheme)
                && firstAppearance
                    == new ThemeAppearance(ThemeColorScheme.Dark, ThemeContrast.Normal)
                && startupTheme.ReducedMotion,
            "Initial settings did not drain appearance authoring before the first frame."
        );
        var diagnosticSettings = new WindowsSettings(() =>
            new(
                null,
                null,
                null,
                WindowsSettingsDiagnostic.UnknownTheme
                    | WindowsSettingsDiagnostic.HighContrastReadFailed
                    | WindowsSettingsDiagnostic.ReducedMotionReadFailed
            )
        );
        Assert(
            !diagnosticSettings.Apply(startupTheme)
                && diagnosticSettings.Diagnostics
                    == (
                        WindowsSettingsDiagnostic.UnknownTheme
                        | WindowsSettingsDiagnostic.HighContrastReadFailed
                        | WindowsSettingsDiagnostic.ReducedMotionReadFailed
                    ),
            "Unknown SDL theme or failed SPI reads lacked observable diagnostics."
        );
        var diagnosticLines = new List<string>();
        var observedDiagnostics = WindowsBootstrap.ReportDiagnostics(
            diagnosticSettings,
            WindowsSettingsDiagnostic.None,
            diagnosticLines.Add
        );
        Assert(
            diagnosticLines.SequenceEqual([
                "Lucent Windows settings diagnostics: UnknownTheme, HighContrastReadFailed, ReducedMotionReadFailed",
            ])
                && WindowsBootstrap.ReportDiagnostics(
                    diagnosticSettings,
                    observedDiagnostics,
                    diagnosticLines.Add
                ) == observedDiagnostics,
            "Runtime settings diagnostics were not stable or were re-emitted without a flag change."
        );
    }

    [TestMethod]
    public void InputInstallConvergenceContract()
    {
        using var renderer = new SkiaSceneRenderer();
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "windows-input-drain");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        Controls.Panel(
            composition.Root,
            theme,
            "root",
            Style
                .Empty.Set(LayoutProperties.Width, 100f)
                .Set(LayoutProperties.Height, 100f)
                .Set(LayoutProperties.Clip, true)
        );
        var viewport = composition.Child(composition.Root, "viewport");
        Controls.ScrollViewport(
            viewport,
            theme,
            "Viewport",
            style: Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 20f)
        );
        var content = composition.Child(viewport, "content");
        Controls.Panel(
            content,
            theme,
            "Content",
            Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 100f)
        );
        var selectable = composition.Child(composition.Root, "selectable");
        Controls.Selectable(
            selectable,
            theme,
            "Selectable",
            style: Style
                .Empty.Set(LayoutProperties.Width, 100f)
                .Set(LayoutProperties.Height, 20f)
                .When(
                    VariantState.Selected | VariantState.FocusVisible,
                    Style.Empty.Set(VisualProperties.Background, Color.Parse("#00ff00"))
                )
        );
        var router = composition.Input;
        var first = WindowsBootstrap.ProjectAndInstall(composition, new(100, 100, 1), renderer);
        Assert(
            router.DispatchKey(new(KeyCommandKind.Down, Key.Tab)).Handled,
            "Input-install baseline did not focus the scroll viewport."
        );
        var before = first.Boxes.Single(box => box.Identity.ElementId == content.Id).Bounds.Y;
        Assert(
            router.DispatchKey(new(KeyCommandKind.Down, Key.Down)).Handled,
            "Input-install scroll command was not handled."
        );
        var scrolled = WindowsBootstrap.ProjectAndInstall(composition, new(100, 100, 1), renderer);
        Assert(
            scrolled.Boxes.Single(box => box.Identity.ElementId == content.Id).Bounds.Y
                == before - 40
                && router.DispatchPointer(new(PointerCommandKind.Move, 99, 1, 1)).Rejection
                    != InputRejection.StaleScene,
            "Event-driven scroll did not flush, project, and install one current scene."
        );
        Assert(
            router.DispatchKey(new(KeyCommandKind.Down, Key.Tab)).Handled
                && router.DispatchKey(new(KeyCommandKind.Down, Key.Enter)).Handled,
            "Input-install baseline did not select the sibling."
        );
        var selected = WindowsBootstrap.ProjectAndInstall(composition, new(100, 100, 1), renderer);
        Assert(
            selected.Dump().Contains("brush=solid(#00FF00FF)", StringComparison.Ordinal)
                && router.DispatchPointer(new(PointerCommandKind.Move, 99, 1, 1)).Rejection
                    != InputRejection.StaleScene,
            "Event-driven selection did not converge without an application drain callback."
        );

        var clampedGraph = new ReactiveGraph();
        using var clamped = new Composition(clampedGraph, "windows-scroll-clamp");
        var clampedTheme = new ThemeContext(clamped.Root.Scope, ControlThemes.Light);
        Controls.Panel(
            clamped.Root,
            clampedTheme,
            "root",
            Style
                .Empty.Set(LayoutProperties.Width, 100f)
                .Set(LayoutProperties.Height, 20f)
                .Set(LayoutProperties.Clip, true)
        );
        var clampedViewport = clamped.Child(clamped.Root, "viewport");
        var state = Controls.ScrollViewport(
            clampedViewport,
            clampedTheme,
            "Viewport",
            new(0, 100),
            Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 20f)
        );
        var clampedContent = clamped.Child(clampedViewport, "content");
        Controls.Panel(
            clampedContent,
            clampedTheme,
            "Content",
            Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 100f)
        );
        var converged = WindowsBootstrap.ProjectAndInstall(clamped, new(100, 20, 1), renderer);
        Assert(
            state.Offset.Y == 80
                && converged
                    .Boxes.Single(box => box.Identity.ElementId == clampedContent.Id)
                    .Bounds.Y == -80
                && clamped.Input.DispatchPointer(new(PointerCommandKind.Move, 100, 0, 0)).Rejection
                    != InputRejection.StaleScene,
            "Out-of-range scroll did not reproject to an accepted scene within the bounded host install loop."
        );
    }

    [TestMethod]
    public void VirtualizedInputFreshnessContract()
    {
        using var renderer = new SkiaSceneRenderer();
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "windows-virtual-freshness");
        var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        Controls.Panel(
            composition.Root,
            theme,
            "root",
            Style
                .Empty.Set(LayoutProperties.Width, 100f)
                .Set(LayoutProperties.Height, 100f)
                .Set(LayoutProperties.Clip, true)
        );
        var field = composition.Child(composition.Root, "search");
        Controls.TextField(
            field,
            theme,
            "Search",
            style: Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 20f)
        );
        var viewport = composition.Child(composition.Root, "viewport");
        Controls.ScrollViewport(
            viewport,
            theme,
            "Rows",
            style: Style.Empty.Set(LayoutProperties.Width, 100f).Set(LayoutProperties.Height, 30f)
        );
        var values = graph.Signal(Array.Empty<int>(), "virtual-values");
        var loaded = graph.Signal(false, "virtual-loaded");
        _ = Controls.VirtualizedList(
            viewport,
            theme,
            "rows",
            "Rows",
            () => values.Value,
            value => value,
            (value, context) =>
            {
                var row = context.Element("row");
                Controls.Selectable(row, theme, "row " + value.Value);
                return row;
            },
            30f
        );
        _ = composition.When(
            composition.Root,
            "loading",
            () => !loaded.Value,
            context =>
            {
                var loading = context.Element("loading");
                Controls.Loading(
                    loading,
                    theme,
                    "Loading",
                    Style.Empty.Set(LayoutProperties.Height, 20f)
                );
                return loading;
            }
        );
        _ = WindowsBootstrap.ProjectAndInstall(composition, new(100, 100, 1), renderer);
        _ = composition.SemanticSnapshot();
        values.Value = Enumerable.Range(1, 10_000).ToArray();
        loaded.Value = true;
        using var adapter = new WindowsInputAdapter(composition);
        adapter.RefreshTextInput();
        Assert(
            adapter.Dispatch(
                new SDL.Event
                {
                    Key = new()
                    {
                        Type = SDL.EventType.KeyDown,
                        Key = SDL.Keycode.Tab,
                        Down = true,
                    },
                }
            )
                && composition.Input.FocusedElement?.ElementId == field.Id,
            "A pending async/virtual update left the installed scene stale for ordinary Tab focus."
        );
        _ = WindowsBootstrap.ProjectAndInstall(composition, new(100, 100, 1), renderer);
        Assert(
            composition.Input.FocusedElement?.ElementId == field.Id,
            "Virtual realization/reprojection dropped ordinary Tab focus."
        );
    }

    [TestMethod]
    public void InputReconciliationPaintContract()
    {
        using var renderer = new SkiaSceneRenderer();
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "windows-input-reconciliation");
        var enabled = new Token<bool>("reconciliation-enabled", true);
        var visible = new Token<bool>("reconciliation-visible", true);
        var theme = new ThemeContext(
            composition.Root.Scope,
            ControlThemes.Light.Set(enabled, true).Set(visible, true)
        );
        var normal = Color.Parse("#102030");
        var pressed = Color.Parse("#405060");
        var disabled = Color.Parse("#708090");
        Controls.Panel(
            composition.Root,
            theme,
            "root",
            Style
                .Empty.Set(LayoutProperties.Width, 100f)
                .Set(LayoutProperties.Height, 20f)
                .Set(LayoutProperties.Clip, true)
        );
        var button = composition.Child(composition.Root, "button");
        var activations = 0;
        Controls.Button(
            button,
            theme,
            "Button",
            () => activations++,
            Style
                .Empty.Set(LayoutProperties.Width, 100f)
                .Set(LayoutProperties.Height, 20f)
                .Set(InputProperties.Enabled, enabled)
                .Set(InputProperties.Visible, visible)
                .Set(VisualProperties.Background, normal)
                .When(VariantState.Pressed, Style.Empty.Set(VisualProperties.Background, pressed))
                .When(VariantState.Disabled, Style.Empty.Set(VisualProperties.Background, disabled))
        );
        var router = composition.Input;
        var viewport = new LayoutViewport(100, 20, 1);
        var initial = WindowsBootstrap.ProjectAndInstall(composition, viewport, renderer);
        var point = initial.Boxes.Single(box => box.Identity.ElementId == button.Id).Bounds;
        _ = router.DispatchPointer(
            new(PointerCommandKind.Down, 50, point.X + 1, point.Y + 1, PointerButton.Primary)
        );
        theme.Theme = theme.Theme.Set(enabled, false);
        var disabledCandidate = SceneLayout.Project(composition, viewport, renderer);
        Assert(
            !router.SetScene(disabledCandidate)
                && router.FocusedElement is null
                && !router.Dump().Contains("capture pointer=50", StringComparison.Ordinal),
            "Disabling a focused pressed control accepted stale input paint."
        );
        var disabledScene = WindowsBootstrap.ProjectAndInstall(composition, viewport, renderer);
        Assert(
            Fill(disabledScene, button) == disabled
                && button.Resolve(VisualProperties.Background).Value.Color == disabled
                && composition.Dump().Contains("style variants=Disabled", StringComparison.Ordinal),
            "The presented disabled scene did not resolve the disabled fill and variants."
        );

        theme.Theme = theme.Theme.Set(enabled, true);
        var enabledScene = WindowsBootstrap.ProjectAndInstall(composition, viewport, renderer);
        point = enabledScene.Boxes.Single(box => box.Identity.ElementId == button.Id).Bounds;
        _ = router.DispatchPointer(
            new(PointerCommandKind.Down, 51, point.X + 1, point.Y + 1, PointerButton.Primary)
        );
        theme.Theme = theme.Theme.Set(visible, false);
        var hiddenCandidate = SceneLayout.Project(composition, viewport, renderer);
        Assert(
            !router.SetScene(hiddenCandidate)
                && router.FocusedElement is null
                && !router.Dump().Contains("capture pointer=51", StringComparison.Ordinal),
            "Hiding a focused pressed control accepted stale input paint."
        );
        var hiddenScene = WindowsBootstrap.ProjectAndInstall(composition, viewport, renderer);
        Assert(
            Fill(hiddenScene, button) == disabled
                && button.Resolve(VisualProperties.Background).Value.Color == disabled
                && composition.Dump().Contains("style variants=Disabled", StringComparison.Ordinal),
            "The presented hidden scene retained pressed or focused paint."
        );

        theme.Theme = theme.Theme.Set(visible, true);
        var restored = WindowsBootstrap.ProjectAndInstall(composition, viewport, renderer);
        point = restored.Boxes.Single(box => box.Identity.ElementId == button.Id).Bounds;
        _ = router.DispatchPointer(
            new(PointerCommandKind.Down, 52, point.X + 1, point.Y + 1, PointerButton.Primary)
        );
        var replacement = SceneLayout.Project(composition, viewport, renderer);
        Assert(
            router.SetScene(replacement) && Fill(replacement, button) == pressed,
            "A stable pressed capture replacement did not accept immediately."
        );
        _ = router.DispatchPointer(new(PointerCommandKind.Up, 52, point.X + 1, point.Y + 1));
        Assert(activations == 1, "A stable replacement did not preserve capture continuity.");
    }

    [TestMethod]
    public void ClipboardAndCursorContract()
    {
        var clipboard = new WindowsClipboard(
            () => throw new InvalidOperationException("read failed"),
            _ => false,
            () => "clipboard failed"
        );
        var read = clipboard.Read();
        var write = clipboard.Write("unchanged");
        Assert(
            !read.Succeeded
                && read.Text is null
                && read.Error == "read failed"
                && !write.Succeeded
                && !string.IsNullOrEmpty(write.Error),
            "Clipboard failures were not explicit and non-destructive."
        );
        var nullClipboard = new WindowsClipboard(() => null, _ => true, () => "null read");
        Assert(
            !nullClipboard.Read().Succeeded && nullClipboard.Read().Error == "null read",
            "Null SDL clipboard text was accepted as content."
        );
        Assert(
            !Task.Run(() => nullClipboard.Read()).GetAwaiter().GetResult().Succeeded,
            "Clipboard accepted a stale thread callback."
        );
        nullClipboard.Dispose();
        Assert(
            !nullClipboard.Write("stale").Succeeded,
            "Clipboard accepted access after lifetime disposal."
        );
        var creates = 0;
        var destroys = 0;
        using (
            var cursor = new WindowsCursor(
                () =>
                {
                    creates++;
                    return 1;
                },
                _ => true,
                _ => destroys++,
                () => "cursor failed"
            )
        )
        {
            Assert(
                cursor.Activate() && cursor.Activate() && creates == 1,
                "Cursor ownership recreated its SDL resource."
            );
        }
        Assert(destroys == 1, "Cursor ownership did not release its SDL resource exactly once.");
        var failedCursorDestroyed = 0;
        using (
            var failedCursor = new WindowsCursor(
                () => 2,
                _ => false,
                _ => failedCursorDestroyed++,
                () => "set failed"
            )
        )
        {
            try
            {
                failedCursor.Activate();
                throw new InvalidOperationException("Cursor activation failure was accepted.");
            }
            catch (InvalidOperationException error)
            {
                Assert(
                    error.Message == "SDL_SetCursor: set failed",
                    "Cursor failure omitted SDL error."
                );
            }
        }
        Assert(failedCursorDestroyed == 1, "Failed cursor activation leaked its SDL cursor.");
    }

    [TestMethod]
    public void ThrowingCleanupContract()
    {
        using var renderer = new SkiaSceneRenderer();
        var cleanupGraph = new ReactiveGraph();
        using var cleanupComposition = new Composition(cleanupGraph, "adapter-cleanup");
        var cleanupTheme = new ThemeContext(
            cleanupComposition.Root.Scope,
            new Theme("adapter-cleanup")
        );
        cleanupComposition.Root.Present(
            cleanupTheme,
            author: Style
                .Empty.Set(LayoutProperties.Width, 20f)
                .Set(LayoutProperties.Height, 20f)
                .Set(LayoutProperties.Clip, true)
        );
        var cleanupChild = cleanupComposition.Child(cleanupComposition.Root, "target");
        cleanupChild.Present(
            cleanupTheme,
            author: Style.Empty.Set(LayoutProperties.Width, 20f).Set(LayoutProperties.Height, 20f)
        );
        cleanupChild.AttachBehaviors(new ThrowingCaptureBehavior());
        var cleanupRouter = cleanupComposition.Input;
        Assert(
            cleanupRouter.SetScene(
                SceneLayout.Project(cleanupComposition, new(20, 20, 1), renderer)
            ),
            "Cleanup scene rejected."
        );
        var stopCalls = 0;
        var cleanupAdapter = new WindowsInputAdapter(
            cleanupComposition,
            1,
            textInput: new TextInputTransport(
                _ => true,
                _ => true,
                _ =>
                {
                    stopCalls++;
                    throw new InvalidOperationException("stop cleanup");
                },
                (_, _, _) => true
            )
        );
        foreach (var pointer in new uint[] { 31, 32 })
            _ = cleanupAdapter.Dispatch(
                new SDL.Event
                {
                    Button = new()
                    {
                        Type = SDL.EventType.MouseButtonDown,
                        Which = pointer,
                        Button = 1,
                        X = 1,
                        Y = 1,
                    },
                }
            );
        try
        {
            cleanupAdapter.Dispatch(
                new SDL.Event { Window = new() { Type = SDL.EventType.WindowFocusLost } }
            );
            throw new InvalidOperationException(
                "Focus-loss cancellation did not aggregate failures."
            );
        }
        catch (AggregateException error)
        {
            Assert(
                error.Flatten().InnerExceptions.Count == 3 && stopCalls == 1,
                "Focus-loss cleanup did not independently continue through pointer, text, and SDL stop failures."
            );
        }
        Assert(
            !cleanupRouter.Dump().Contains("capture pointer=31", StringComparison.Ordinal)
                && !cleanupRouter.Dump().Contains("capture pointer=32", StringComparison.Ordinal)
                && cleanupAdapter.ConsumeRepaintRequest()
                && !cleanupAdapter.ConsumeRepaintRequest(),
            "Focus-loss cancellation retained capture or requested more than one repaint."
        );
        foreach (var pointer in new uint[] { 33, 34 })
            _ = cleanupAdapter.Dispatch(
                new SDL.Event
                {
                    Button = new()
                    {
                        Type = SDL.EventType.MouseButtonDown,
                        Which = pointer,
                        Button = 1,
                        X = 1,
                        Y = 1,
                    },
                }
            );
        try
        {
            cleanupAdapter.Dispose();
            throw new InvalidOperationException("Dispose cancellation did not aggregate failures.");
        }
        catch (AggregateException error)
        {
            Assert(
                error.Flatten().InnerExceptions.Count == 3 && stopCalls == 2,
                "Dispose cleanup did not independently continue through pointer, text, and SDL stop failures."
            );
        }
        Assert(
            !cleanupRouter.Dump().Contains("capture pointer=33", StringComparison.Ordinal)
                && !cleanupRouter.Dump().Contains("capture pointer=34", StringComparison.Ordinal)
                && !cleanupAdapter.Dispatch(
                    new SDL.Event
                    {
                        Key = new()
                        {
                            Type = SDL.EventType.KeyDown,
                            Key = SDL.Keycode.Tab,
                            Down = true,
                        },
                    }
                ),
            "Disposed adapter left Core capture state or accepted stale dispatch."
        );
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static Color Fill(RetainedScene scene, Element element) =>
        Paints(scene.Nodes)
            .Single(node => node.Identity.Element.ElementId == element.Id)
            .Brush.Color
        ?? throw new InvalidOperationException("Expected solid paint.");

    private static IEnumerable<PaintSceneNode> Paints(IEnumerable<SceneNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is PaintSceneNode paint)
                yield return paint;
            if (node is ClipSceneNode clip)
                foreach (var child in Paints(clip.Children))
                    yield return child;
            if (node is OpacitySceneNode opacity)
                foreach (var child in Paints(opacity.Children))
                    yield return child;
        }
    }
}

sealed class ThrowingCaptureBehavior : Behavior
{
    public override string Name => "throwing-capture";
    public override BehaviorOwnership Ownership =>
        BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics;

    public override void Attach(BehaviorContext context)
    {
        context.SetSemantics(new(SemanticRole.Button, "throwing", actions: SemanticAction.Invoke));
        context.OnPointer(route =>
        {
            if (route.Command.Kind == PointerCommandKind.Down)
                route.Capture();
        });
        context.OnCaptureLost(_ => throw new InvalidOperationException("capture cleanup"));
    }
}
