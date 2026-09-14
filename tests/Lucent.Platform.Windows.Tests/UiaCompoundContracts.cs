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
                    SemanticDeclaration
                        .Create(SemanticRole.ProgressBar, "Preparing")
                        .Value("50%")
                        .Range(new(0.5, 0, 1, 0.1, 0.2, isReadOnly: true), false)
                        .Build()
                );
                using var changed = SceneLayout.Project(composition, new(220, 100, 1), renderer);
                provider.Refresh(changed);
                AssertCompoundPattern(simple, 10001, expected: false);
                AssertCompoundPattern(simple, 10004, expected: false);
                AssertCompoundPattern(simple, 10003, expected: true);
                AssertCompoundPattern(simple, 10002, expected: false);
                AssertCompoundProperty(
                    simple,
                    30043,
                    expected: false,
                    "Formatted display values must not advertise ValuePattern."
                );
                target.UpdateControlSemantics(
                    SemanticDeclaration.Create(SemanticRole.Group, "Selectionless group").Build()
                );
                using var selectionless = SceneLayout.Project(
                    composition,
                    new(220, 100, 1),
                    renderer
                );
                provider.Refresh(selectionless);
                AssertCompoundPattern(simple, 10001, expected: false);
                AssertCompoundProperty(
                    simple,
                    30037,
                    expected: false,
                    "A role without a selection capability must not advertise SelectionPattern."
                );
                target.UpdateControlSemantics(
                    SemanticDeclaration
                        .Create(SemanticRole.Group, "Explicit selection group")
                        .SelectionContainer(new(false, false))
                        .Build()
                );
                using var explicitSelection = SceneLayout.Project(
                    composition,
                    new(220, 100, 1),
                    renderer
                );
                provider.Refresh(explicitSelection);
                AssertCompoundPattern(simple, 10001, expected: true);
                AssertCompoundProperty(
                    simple,
                    30037,
                    expected: true,
                    "An explicit selection capability must advertise SelectionPattern."
                );
                target.UpdateControlSemantics(
                    SemanticDeclaration
                        .Create(SemanticRole.ComboBox, "Choice")
                        .Value("Alpha")
                        .Expansion(false, true)
                        .ValuePattern(false)
                        .Build()
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
                target.UpdateControlSemantics(
                    SemanticDeclaration
                        .Create(SemanticRole.TextField, "Password")
                        .ConfidentialEditing(false)
                        .Build()
                );
                using var confidential = SceneLayout.Project(
                    composition,
                    new(220, 100, 1),
                    renderer
                );
                provider.Refresh(confidential);
                AssertCompoundPattern(simple, 10002, expected: false);
                AssertCompoundProperty(
                    simple,
                    30043,
                    expected: false,
                    "Read-only confidential editing must not expose ValuePattern."
                );
                target.UpdateControlSemantics(
                    SemanticDeclaration
                        .Create(SemanticRole.TextField, "Read-only draft")
                        .Editing(
                            new SemanticTextSnapshot("draft", 0, 0, isReadOnly: true),
                            false,
                            false,
                            false
                        )
                        .Build()
                );
                using var readOnlyDraft = SceneLayout.Project(
                    composition,
                    new(220, 100, 1),
                    renderer
                );
                provider.Refresh(readOnlyDraft);
                AssertCompoundPattern(simple, 10002, expected: true);
                AssertCompoundPattern(simple, 10014, expected: false);
                AssertCompoundPattern(simple, 10024, expected: false);
                target.UpdateControlSemantics(
                    SemanticDeclaration
                        .Create(SemanticRole.RadioButton, "Disabled choice")
                        .Enabled(false)
                        .SelectionItem(true, false)
                        .Build()
                );
                using var disabledItem = SceneLayout.Project(
                    composition,
                    new(220, 100, 1),
                    renderer
                );
                provider.Refresh(disabledItem);
                AssertCompoundPattern(simple, 10010, expected: false);
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

    private static void AssertCompoundProperty(
        nint provider,
        int property,
        bool expected,
        string message
    )
    {
        WindowsUiaProvider.RawVariant value = default;
        Assert(
            Simple(provider, 5, property, &value) == 0
                && value.Type == 11
                && (value.Value != 0) == expected,
            message
        );
    }

    private sealed class CompoundProbe : Behavior
    {
        public override string Name => "compound-pattern";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            context.SetSemantics(
                SemanticDeclaration
                    .Create(SemanticRole.TabList, "Tabs")
                    .Scroll(true)
                    .SelectionContainer(new(false, true))
                    .Build()
            );
            context.OnSemanticCommand(_ => false);
        }
    }
}
