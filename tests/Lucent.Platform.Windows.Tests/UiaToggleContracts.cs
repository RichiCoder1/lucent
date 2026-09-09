using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

public sealed unsafe partial class UiaLifecycleContracts
{
    [TestMethod]
    public void ToggleProviderUsesNativeSlotsAndReportsAppliedMixedState()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(toggle) failed.");
        var window = CreateWindow("Lucent UIA toggle");
        try
        {
            using var composition = new Composition(new ReactiveGraph(), "uia-toggle");
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            var applied = SemanticToggleState.Indeterminate;
            var requests = 0;
            var element = composition.Mount(
                composition.Root,
                theme,
                ComponentRecipe.Create(
                    "toggle",
                    (_, root) =>
                    {
                        root.Present(theme, Style.Empty.Width(160).Height(40));
                        root.AttachBehaviors(new ToggleProbe(() => requests++));
                    }
                )
            );
            using var renderer = new SkiaSceneRenderer();
            using var initial = SceneLayout.Project(composition, new(160, 80, 1), renderer);
            Assert(composition.Input.SetScene(initial), "Toggle scene rejected.");
            using var dispatcher = new WindowsUiaDispatcher();
            using var provider = new WindowsUiaProvider(
                Hwnd(window),
                composition,
                dispatcher,
                "Toggle"
            );
            provider.Refresh(initial);
            var toggle = FindPattern(provider.InterfacePointer, 10015);
            Assert(toggle != 0, "Missing TogglePattern.");
            try
            {
                var state = -1;
                Assert(
                    Simple(toggle, 4, &state) == 0 && state == 2,
                    "Mixed state not exposed in native get_ToggleState slot."
                );
                Assert(
                    Fragment(toggle, 3) == WindowsUiaProvider.InvalidOperation && requests == 1,
                    "Native Toggle slot did not report an unapplied request exactly once."
                );
                Assert(
                    Simple(toggle, 4, &state) == 0 && state == 2,
                    "A deferred request overwrote applied state."
                );
                applied = SemanticToggleState.On;
                element.UpdateControlSemantics(
                    new(
                        SemanticRole.CheckBox,
                        "Choice",
                        actions: SemanticAction.Toggle,
                        toggleState: applied
                    )
                );
                using var next = SceneLayout.Project(composition, new(160, 80, 1), renderer);
                provider.Refresh(next);
                Assert(
                    Simple(toggle, 4, &state) == 0 && state == 1,
                    "Applied toggle state was not refreshed."
                );
                element.UpdateControlSemantics(
                    new(
                        SemanticRole.CheckBox,
                        "Choice",
                        enabled: false,
                        actions: SemanticAction.Toggle,
                        toggleState: applied
                    )
                );
                using var disabled = SceneLayout.Project(composition, new(160, 80, 1), renderer);
                provider.Refresh(disabled);
                Assert(
                    Fragment(toggle, 3) == WindowsUiaProvider.ElementNotEnabled && requests == 1,
                    "Disabled toggle invoked application action."
                );
                element.Dispose();
                using var removed = SceneLayout.Project(composition, new(160, 80, 1), renderer);
                provider.Refresh(removed);
                Assert(
                    Simple(toggle, 4, &state) == WindowsUiaProvider.NotAvailable,
                    "Removed toggle remained available."
                );
            }
            finally
            {
                Release(toggle);
            }
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    [TestMethod]
    public void RadioSelectionProviderReportsRequiredSelectionThroughNativeSlots()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(radio selection) failed.");
        var window = CreateWindow("Lucent UIA radio");
        try
        {
            using var composition = new Composition(new ReactiveGraph(), "uia-radio");
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            composition.Mount(
                composition.Root,
                theme,
                ComponentRecipe.Create(
                    "radio-group",
                    (_, root) =>
                    {
                        root.Present(theme, Style.Empty.Width(160).Height(40));
                        root.AttachBehaviors(new SelectionProbe());
                    }
                )
            );
            using var renderer = new SkiaSceneRenderer();
            using var scene = SceneLayout.Project(composition, new(160, 80, 1), renderer);
            Assert(composition.Input.SetScene(scene), "Radio scene rejected.");
            using var dispatcher = new WindowsUiaDispatcher();
            using var provider = new WindowsUiaProvider(
                Hwnd(window),
                composition,
                dispatcher,
                "Radio"
            );
            provider.Refresh(scene);
            var selection = FindPattern(provider.InterfacePointer, 10001);
            Assert(selection != 0, "RadioGroup missing SelectionPattern.");
            try
            {
                var value = -1;
                Assert(
                    Simple(selection, 4, &value) == 0 && value == 0,
                    "Radio group incorrectly supports multiple selection."
                );
                Assert(
                    Simple(selection, 5, &value) == 0 && value == 1,
                    "Required selection was hardcoded false."
                );
            }
            finally
            {
                Release(selection);
            }
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    private sealed class ToggleProbe(Action request) : Behavior
    {
        public override string Name => "toggle-probe";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            context.SetSemantics(
                new(
                    SemanticRole.CheckBox,
                    "Choice",
                    actions: SemanticAction.Toggle,
                    toggleState: SemanticToggleState.Indeterminate
                )
            );
            context.OnSemanticCommand(command =>
            {
                if (command.Kind != SemanticCommandKind.Toggle)
                    return false;
                request();
                return true;
            });
        }
    }

    private sealed class SelectionProbe : Behavior
    {
        public override string Name => "selection-probe";
        public override BehaviorOwnership Ownership => BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context) =>
            context.SetSemantics(
                new(SemanticRole.RadioGroup, "Choices", selection: new(false, true))
            );
    }
}
