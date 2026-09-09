using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

public sealed unsafe partial class UiaLifecycleContracts
{
    [TestMethod]
    public void CompoundProviderAdvertisesEveryCurrentPatternAndRetainsIdentityAcrossCapabilityChanges()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(compound UIA) failed.");
        var window = CreateWindow("Lucent compound UIA");
        try
        {
            using var composition = new Composition(new ReactiveGraph(), "compound-uia");
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            var target = composition.Mount(
                composition.Root,
                theme,
                ComponentRecipe.Create(
                    "compound",
                    (_, root) =>
                    {
                        root.Present(theme, Style.Empty.Width(200).Height(36));
                        root.AttachBehaviors(new CompoundProbe());
                    }
                )
            );
            using var renderer = new SkiaSceneRenderer();
            using var first = SceneLayout.Project(composition, new(220, 100, 1), renderer);
            using var dispatcher = new WindowsUiaDispatcher();
            using var provider = new WindowsUiaProvider(
                Hwnd(window),
                composition,
                dispatcher,
                "Compound"
            );
            provider.Refresh(first);
            var selection = FindPattern(provider.InterfacePointer, 10001);
            Assert(selection != 0, "Scrollable selection container lost SelectionPattern.");
            var simple = Query(selection, UiaWrappers.Simple);
            try
            {
                AssertCompoundPattern(simple, 10004, expected: true);
                AssertCompoundPattern(simple, 10015, expected: false);
                AssertCompoundPattern(simple, 10003, expected: false);
                target.UpdateControlSemantics(
                    new(
                        SemanticRole.ProgressBar,
                        "Preparing",
                        range: new(0.5, 0, 1, 0.1, 0.2, isReadOnly: true)
                    )
                );
                using var changed = SceneLayout.Project(composition, new(220, 100, 1), renderer);
                provider.Refresh(changed);
                AssertCompoundPattern(simple, 10001, expected: false);
                AssertCompoundPattern(simple, 10004, expected: false);
                AssertCompoundPattern(simple, 10003, expected: true);
                target.UpdateControlSemantics(
                    new(
                        SemanticRole.ComboBox,
                        "Choice",
                        actions: SemanticAction.ExpandCollapse,
                        value: "Alpha",
                        expanded: false
                    )
                );
                using var choice = SceneLayout.Project(composition, new(220, 100, 1), renderer);
                provider.Refresh(choice);
                AssertCompoundPattern(simple, 10002, expected: true);
                AssertCompoundPattern(simple, 10005, expected: true);
                Assert(
                    ReadFieldString(simple, 30045) == "Alpha",
                    "Select lost its applied choice label."
                );
                WindowsUiaProvider.RawVariant readOnly = default;
                Assert(
                    Simple(simple, 5, 30046, &readOnly) == 0
                        && readOnly.Type == 11
                        && readOnly.Value != 0,
                    "Noneditable Select value must be read-only."
                );
                nint valuePattern = 0;
                Assert(
                    Simple(simple, 4, 10002, &valuePattern) == 0 && valuePattern != 0,
                    "Select value pattern missing."
                );
                try
                {
                    var isReadOnly = 0;
                    Assert(
                        Simple(valuePattern, 5, &isReadOnly) == 0 && isReadOnly == 1,
                        "Noneditable Select ValuePattern getter disagrees with its property."
                    );
                }
                finally
                {
                    Release(valuePattern);
                }
            }
            finally
            {
                Release(simple);
                Release(selection);
            }
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    private static void AssertCompoundPattern(nint provider, int pattern, bool expected)
    {
        nint value = 0;
        try
        {
            Assert(
                Simple(provider, 4, pattern, &value) == 0 && (value != 0) == expected,
                $"Pattern {pattern} availability does not match its live semantic declaration."
            );
        }
        finally
        {
            if (value != 0)
                Release(value);
        }
    }

    private sealed class CompoundProbe : Behavior
    {
        public override string Name => "compound-pattern";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            context.SetSemantics(
                new(
                    SemanticRole.TabList,
                    "Tabs",
                    actions: SemanticAction.Scroll,
                    selection: new(false, true)
                )
            );
            context.OnSemanticCommand(_ => false);
        }
    }
}
