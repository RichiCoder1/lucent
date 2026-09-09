using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

public sealed unsafe partial class UiaLifecycleContracts
{
    [TestMethod]
    public void PasswordProviderRejectsContentReadsAndDeclaresConfidentialEditing()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(password UIA) failed.");
        var window = CreateWindow("Lucent confidential UIA");
        try
        {
            using var composition = new Composition(new ReactiveGraph(), "password-uia");
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            composition.Mount(
                composition.Root,
                theme,
                ComponentRecipe.Create(
                    "password",
                    (_, root) =>
                    {
                        root.Present(theme, Style.Empty.Width(200).Height(36));
                        root.AttachBehaviors(new PasswordSemanticProbe());
                    }
                )
            );
            using var renderer = new SkiaSceneRenderer();
            using var scene = SceneLayout.Project(composition, new(220, 100, 1), renderer);
            using var dispatcher = new WindowsUiaDispatcher();
            using var provider = new WindowsUiaProvider(
                Hwnd(window),
                composition,
                dispatcher,
                "Password"
            );
            provider.Refresh(scene);
            var valuePattern = FindPattern(provider.InterfacePointer, 10002);
            Assert(valuePattern != 0, "Password editor must support setting its value.");
            var simple = Query(valuePattern, UiaWrappers.Simple);
            try
            {
                WindowsUiaProvider.RawVariant flag = default;
                Assert(
                    Simple(simple, 5, 30019, &flag) == 0 && flag.Type == 11 && flag.Value != 0,
                    "Password metadata was not exported."
                );
                WindowsUiaProvider.RawVariant property = default;
                Assert(
                    Simple(simple, 5, 30045, &property) == WindowsUiaProvider.InvalidOperation
                        && property.Type == 0,
                    "Password value property must reject reads."
                );
                nint value = 0;
                var result = (
                    (delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)valuePattern)[4]
                )(valuePattern, &value);
                Assert(
                    result == WindowsUiaProvider.InvalidOperation && value == 0,
                    "ValuePattern leaked password contents."
                );
                AssertCompoundPattern(simple, 10014, expected: false);
                AssertCompoundPattern(simple, 10024, expected: false);
            }
            finally
            {
                Release(simple);
                Release(valuePattern);
            }
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    private sealed class PasswordSemanticProbe : Behavior
    {
        public override string Name => "confidential-editor";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            context.SetSemantics(
                new(
                    SemanticRole.TextField,
                    "Password",
                    actions: SemanticAction.SetValue,
                    isPassword: true
                )
            );
            context.OnSemanticCommand(_ => false);
        }
    }
}
