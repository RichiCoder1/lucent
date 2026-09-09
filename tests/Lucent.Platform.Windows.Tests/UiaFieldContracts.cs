using System.Runtime.InteropServices;
using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

public sealed unsafe partial class UiaLifecycleContracts
{
    [TestMethod]
    public void FieldProviderRetainsLabelHelpAndValidationRelationships()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(field metadata) failed.");
        var window = CreateWindow("Lucent UIA field");
        try
        {
            using var composition = new Composition(new ReactiveGraph(), "uia-field");
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            using var form = new FormSession(composition.Root.Scope, "details");
            composition.Mount(
                composition.Root,
                theme,
                Components.Field(
                    "Email",
                    field => Components.TextField(field),
                    () => ValidationState.Invalid("Enter a complete address."),
                    () => "Used for recovery.",
                    form,
                    "email",
                    required: true,
                    style: Style.Empty.Width(300)
                )
            );
            composition.Flush();
            using var renderer = new SkiaSceneRenderer();
            using var initial = SceneLayout.Project(composition, new(320, 200, 1), renderer);
            Assert(composition.Input.SetScene(initial), "Field scene rejected.");
            using var dispatcher = new WindowsUiaDispatcher();
            using var provider = new WindowsUiaProvider(
                Hwnd(window),
                composition,
                dispatcher,
                "Field"
            );
            provider.Refresh(initial);
            var valuePattern = FindPattern(provider.InterfacePointer, 10002);
            Assert(valuePattern != 0, "Field has no ValuePattern.");
            var editor = Query(valuePattern, UiaWrappers.Simple);
            try
            {
                Assert(
                    ReadName(editor) == "Email",
                    "Required suffix contaminated the accessible name."
                );
                Assert(
                    ReadFieldString(editor, 30013) == "Used for recovery.",
                    "HelpText was not projected."
                );
                WindowsUiaProvider.RawVariant labelled = default;
                Assert(
                    Simple(editor, 5, 30018, &labelled) == 0
                        && labelled.Type == 13
                        && labelled.Value != 0,
                    "Missing LabeledBy provider."
                );
                try
                {
                    Assert(
                        ReadName(labelled.Value) == "Email *",
                        "LabeledBy points to the wrong element."
                    );
                }
                finally
                {
                    Release(labelled.Value);
                }
                var result = form.SubmitAsync().AsTask().GetAwaiter().GetResult();
                Assert(result.Status == FormSubmitStatus.Invalid, "Invalid form submitted.");
                composition.Flush();
                using var next = WindowsBootstrap.ProjectAndInstall(
                    composition,
                    new(320, 200, 1),
                    renderer,
                    initial
                );
                provider.Refresh(next);
                WindowsUiaProvider.RawVariant valid = default;
                Assert(
                    Simple(editor, 5, 30103, &valid) == 0 && valid.Type == 11 && valid.Value == 0,
                    "Invalid form metadata was lost."
                );
                Assert(
                    ReadFieldString(editor, 30159) == "Enter a complete address.",
                    "Validation description was not projected."
                );
                WindowsUiaProvider.RawVariant described = default;
                Assert(
                    Simple(editor, 5, 30105, &described) == 0 && described.Type == (0x2000 | 13),
                    "DescribedBy is not a provider array."
                );
                try
                {
                    var count = 0;
                    Assert(
                        FieldArrayUpperBound(described.Value, 1, out count) == 0 && count == 1,
                        "DescribedBy must contain help and error providers."
                    );
                }
                finally
                {
                    _ = ClearFieldVariant(&described);
                }
            }
            finally
            {
                Release(editor);
                Release(valuePattern);
            }
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    private static string? ReadFieldString(nint provider, int property)
    {
        WindowsUiaProvider.RawVariant value = default;
        Assert(
            Simple(provider, 5, property, &value) == 0 && value.Type == 8,
            "Missing field string metadata."
        );
        try
        {
            return Marshal.PtrToStringBSTR(value.Value);
        }
        finally
        {
            Marshal.FreeBSTR(value.Value);
        }
    }

    [LibraryImport("oleaut32.dll", EntryPoint = "VariantClear")]
    private static partial int ClearFieldVariant(WindowsUiaProvider.RawVariant* value);

    [LibraryImport("oleaut32.dll", EntryPoint = "SafeArrayGetUBound")]
    private static partial int FieldArrayUpperBound(nint array, uint dimension, out int bound);
}
