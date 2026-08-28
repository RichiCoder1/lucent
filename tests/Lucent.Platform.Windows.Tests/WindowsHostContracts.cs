using Lucent.Core;
using Lucent.Platform.Windows;
using Lucent.Renderer.Skia;
using SDL3;

internal static class WindowsHostContracts
{
    public static void InputAdapterAndRoutingContract()
    {
    Assert(WindowsInputAdapter.MapKey(SDL.Keycode.Tab) == Key.Tab && WindowsInputAdapter.MapKey(SDL.Keycode.KpEnter) == Key.Enter &&
        WindowsInputAdapter.MapKey(SDL.Keycode.A) is null && WindowsInputAdapter.MapKey(SDL.Keycode.RAlt) is null,
        "Command-key translation invented printable, AltGr, or dead-key text input.");
    var modifiers = WindowsInputAdapter.MapModifiers(SDL.Keymod.LShift | SDL.Keymod.LCtrl | SDL.Keymod.RAlt | SDL.Keymod.RGUI);
    Assert(modifiers ==
        (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta) && WindowsInputAdapter.MapButton(1) == PointerButton.Primary &&
        WindowsInputAdapter.MapButton(2) == PointerButton.Middle && WindowsInputAdapter.MapButton(3) == PointerButton.Secondary && WindowsInputAdapter.MapButton(4) is null,
        "SDL modifier/button conversion was not bounded to portable values: " + modifiers);

    var graph = new ReactiveGraph();
    using var composition = new Composition(graph, "windows-input");
    var theme = new ThemeContext(composition.Root.Scope, new Theme("windows-input"));
    composition.Root.Present(theme, author: Style.Empty.Set(Arrangement.Width, 40f).Set(Arrangement.Height, 40f).Set(Arrangement.Clip, true));
    var child = composition.Child(composition.Root, "target");
    var activations = 0;
    child.Present(theme, author: Style.Empty.Set(Arrangement.Width, 20f).Set(Arrangement.Height, 20f).Set(SceneProperties.Fill, 0xffffffffU));
    child.AttachBehaviors(new RowActionBehavior("target-action", new(SemanticRole.Button, "target", actions: SemanticAction.Invoke), () => activations++));
    using var renderer = new SkiaSceneRenderer();
    var router = composition.Input;
    var scene = SceneLayout.Project(composition, new(40, 40, 1), renderer);
    Assert(router.SetScene(scene), "Input adapter test scene was rejected.");
    var box = scene.Boxes.Single(candidate => candidate.Identity.ElementId == child.Id);
    var x = box.Bounds.X + 1; var y = box.Bounds.Y + 1;
    using var adapter = new WindowsInputAdapter(composition);
    Assert(adapter.Dispatch(new SDL.Event { Button = new() { Type = SDL.EventType.MouseButtonDown, Which = 9, Button = 1, X = x, Y = y } }) &&
        router.FocusedElement?.ElementId == child.Id && router.Dump().Contains("capture pointer=9", StringComparison.Ordinal),
        "SDL pointer down did not enter Core focus/capture routing.");
    Assert(adapter.Dispatch(new SDL.Event { Motion = new() { Type = SDL.EventType.MouseMotion, Which = 9, X = 39, Y = 39 } }), "SDL captured pointer move was ignored.");
    adapter.Dispatch(new SDL.Event { Window = new() { Type = SDL.EventType.WindowFocusLost } });
    Assert(!router.Dump().Contains("capture pointer=9", StringComparison.Ordinal), "Focus loss did not cancel active Core capture.");
    Assert(adapter.Dispatch(new SDL.Event { Key = new() { Type = SDL.EventType.KeyDown, Key = SDL.Keycode.Return, Down = true, Repeat = true } }) && activations == 0,
        "Repeated key down escaped Core repeat semantics.");
    Assert(adapter.Dispatch(new SDL.Event { Key = new() { Type = SDL.EventType.KeyDown, Key = SDL.Keycode.Return, Down = true } }) && activations == 1,
        "Mapped key down did not route to focused Core behavior.");
    adapter.Dispose();
    Assert(!adapter.Dispatch(new SDL.Event { Key = new() { Type = SDL.EventType.KeyDown, Key = SDL.Keycode.Tab, Down = true } }), "Disposed adapter accepted a stale callback.");

    }

    public static void SettingsAndAppearanceContract()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "windows-settings");
        var theme = new ThemeContext(composition.Root.Scope, new Theme("windows-settings"));
    var settingsSnapshot = new WindowsSettingsSnapshot(ThemeColorScheme.Dark, ThemeContrast.High, false);
    var settings = new WindowsSettings(() => settingsSnapshot);
    var appearanceRuns = 0; var motionRuns = 0;
    var appearance = composition.Root.Scope.Derived(() => { appearanceRuns++; return theme.Appearance; }, "platform-appearance-reader");
    var motion = composition.Root.Scope.Derived(() => { motionRuns++; return theme.ReducedMotion; }, "platform-motion-reader");
    _ = appearance.Value; _ = motion.Value;
    Assert(settings.Apply(theme) && appearance.Value == new ThemeAppearance(ThemeColorScheme.Dark, ThemeContrast.High) && !motion.Value && appearanceRuns == 2 && motionRuns == 1 && !settings.Apply(theme),
        "Settings did not coalesce equal values or invalidate only changed portable facets.");
    settingsSnapshot = settingsSnapshot with { ReducedMotion = true };
    Assert(settings.Apply(theme) && appearanceRuns == 2 && motion.Value && motionRuns == 2, "Reduced motion invalidated an unrelated appearance reader.");
    settingsSnapshot = new(null, null, null);
    Assert(!settings.Apply(theme) && theme.Appearance == new ThemeAppearance(ThemeColorScheme.Dark, ThemeContrast.High) && theme.ReducedMotion,
        "Unknown theme or failed SPI fields fabricated a settings update.");

    var startupGraph = new ReactiveGraph();
    using var startupComposition = new Composition(startupGraph, "settings-startup");
    var startupTheme = new ThemeContext(startupComposition.Root.Scope, new Theme("settings-startup"));
    ThemeAppearance? firstAppearance = null;
    _ = startupComposition.Root.Scope.Effect(() => firstAppearance = startupTheme.Appearance, "first-frame-appearance");
    var startupSettings = new WindowsSettings(() => new(ThemeColorScheme.Dark, ThemeContrast.Normal, true));
    Assert(WindowsBootstrap.ApplySettings(startupSettings, startupTheme, startupGraph.Drain) && firstAppearance == new ThemeAppearance(ThemeColorScheme.Dark, ThemeContrast.Normal) && startupTheme.ReducedMotion,
        "Initial settings did not drain appearance authoring before the first frame.");
    var diagnosticSettings = new WindowsSettings(() => new(null, null, null, WindowsSettingsDiagnostic.UnknownTheme | WindowsSettingsDiagnostic.HighContrastReadFailed | WindowsSettingsDiagnostic.ReducedMotionReadFailed));
    Assert(!diagnosticSettings.Apply(startupTheme) && diagnosticSettings.Diagnostics == (WindowsSettingsDiagnostic.UnknownTheme | WindowsSettingsDiagnostic.HighContrastReadFailed | WindowsSettingsDiagnostic.ReducedMotionReadFailed),
        "Unknown SDL theme or failed SPI reads lacked observable diagnostics.");
    var diagnosticLines = new List<string>();
    var observedDiagnostics = WindowsBootstrap.ReportDiagnostics(diagnosticSettings, WindowsSettingsDiagnostic.None, diagnosticLines.Add);
    Assert(diagnosticLines.SequenceEqual(["Lucent Windows settings diagnostics: UnknownTheme, HighContrastReadFailed, ReducedMotionReadFailed"]) && WindowsBootstrap.ReportDiagnostics(diagnosticSettings, observedDiagnostics, diagnosticLines.Add) == observedDiagnostics,
        "Runtime settings diagnostics were not stable or were re-emitted without a flag change.");

    }

    public static void ClipboardAndCursorContract()
    {
    var clipboard = new WindowsClipboard(() => throw new InvalidOperationException("read failed"), _ => false, () => "clipboard failed");
    var read = clipboard.Read(); var write = clipboard.Write("unchanged");
    Assert(!read.Succeeded && read.Text is null && read.Error == "read failed" && !write.Succeeded && !string.IsNullOrEmpty(write.Error),
        "Clipboard failures were not explicit and non-destructive.");
    var nullClipboard = new WindowsClipboard(() => null, _ => true, () => "null read");
    Assert(!nullClipboard.Read().Succeeded && nullClipboard.Read().Error == "null read", "Null SDL clipboard text was accepted as content.");
    Assert(!Task.Run(() => nullClipboard.Read()).GetAwaiter().GetResult().Succeeded, "Clipboard accepted a stale thread callback.");
    nullClipboard.Dispose();
    Assert(!nullClipboard.Write("stale").Succeeded, "Clipboard accepted access after lifetime disposal.");
    var creates = 0; var destroys = 0;
    using (var cursor = new WindowsCursor(() => { creates++; return 1; }, _ => true, _ => destroys++, () => "cursor failed"))
    {
        Assert(cursor.Activate() && cursor.Activate() && creates == 1, "Cursor ownership recreated its SDL resource.");
    }
    Assert(destroys == 1, "Cursor ownership did not release its SDL resource exactly once.");
    var failedCursorDestroyed = 0;
    using (var failedCursor = new WindowsCursor(() => 2, _ => false, _ => failedCursorDestroyed++, () => "set failed"))
    {
        try { failedCursor.Activate(); throw new InvalidOperationException("Cursor activation failure was accepted."); }
        catch (InvalidOperationException error) { Assert(error.Message == "SDL_SetCursor: set failed", "Cursor failure omitted SDL error."); }
    }
    Assert(failedCursorDestroyed == 1, "Failed cursor activation leaked its SDL cursor.");

    }

    public static void ThrowingCleanupContract()
    {
        using var renderer = new SkiaSceneRenderer();
    var cleanupGraph = new ReactiveGraph();
    using var cleanupComposition = new Composition(cleanupGraph, "adapter-cleanup");
    var cleanupTheme = new ThemeContext(cleanupComposition.Root.Scope, new Theme("adapter-cleanup"));
    cleanupComposition.Root.Present(cleanupTheme, author: Style.Empty.Set(Arrangement.Width, 20f).Set(Arrangement.Height, 20f).Set(Arrangement.Clip, true));
    var cleanupChild = cleanupComposition.Child(cleanupComposition.Root, "target");
    cleanupChild.Present(cleanupTheme, author: Style.Empty.Set(Arrangement.Width, 20f).Set(Arrangement.Height, 20f));
    cleanupChild.AttachBehaviors(new ThrowingCaptureBehavior());
    var cleanupRouter = cleanupComposition.Input;
    Assert(cleanupRouter.SetScene(SceneLayout.Project(cleanupComposition, new(20, 20, 1), renderer)), "Cleanup scene rejected.");
    var cleanupAdapter = new WindowsInputAdapter(cleanupComposition);
    foreach (var pointer in new uint[] { 31, 32 }) _ = cleanupAdapter.Dispatch(new SDL.Event { Button = new() { Type = SDL.EventType.MouseButtonDown, Which = pointer, Button = 1, X = 1, Y = 1 } });
    try { cleanupAdapter.Dispatch(new SDL.Event { Window = new() { Type = SDL.EventType.WindowFocusLost } }); throw new InvalidOperationException("Focus-loss cancellation did not aggregate failures."); }
    catch (AggregateException error) { Assert(error.Flatten().InnerExceptions.Count == 2, "Focus-loss cancellation did not continue after each callback failure."); }
    Assert(!cleanupRouter.Dump().Contains("capture pointer=31", StringComparison.Ordinal) && !cleanupRouter.Dump().Contains("capture pointer=32", StringComparison.Ordinal) && cleanupAdapter.ConsumeRepaintRequest() && !cleanupAdapter.ConsumeRepaintRequest(),
        "Focus-loss cancellation retained capture or requested more than one repaint.");
    foreach (var pointer in new uint[] { 33, 34 }) _ = cleanupAdapter.Dispatch(new SDL.Event { Button = new() { Type = SDL.EventType.MouseButtonDown, Which = pointer, Button = 1, X = 1, Y = 1 } });
    try { cleanupAdapter.Dispose(); throw new InvalidOperationException("Dispose cancellation did not aggregate failures."); }
    catch (AggregateException error) { Assert(error.Flatten().InnerExceptions.Count == 2, "Dispose cancellation did not continue after each callback failure."); }
        Assert(!cleanupRouter.Dump().Contains("capture pointer=33", StringComparison.Ordinal) && !cleanupRouter.Dump().Contains("capture pointer=34", StringComparison.Ordinal) && !cleanupAdapter.Dispatch(new SDL.Event { Key = new() { Type = SDL.EventType.KeyDown, Key = SDL.Keycode.Tab, Down = true } }),
        "Disposed adapter left Core capture state or accepted stale dispatch.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

sealed class ThrowingCaptureBehavior : Behavior
{
    public override string Name => "throwing-capture";
    public override BehaviorOwnership Ownership => BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics;
    public override void Attach(BehaviorContext context)
    {
        context.SetSemantics(new(SemanticRole.Button, "throwing", actions: SemanticAction.Invoke));
        context.OnPointer(route => { if (route.Command.Kind == PointerCommandKind.Down) route.Capture(); });
        context.OnCaptureLost(_ => throw new InvalidOperationException("capture cleanup"));
    }
}
