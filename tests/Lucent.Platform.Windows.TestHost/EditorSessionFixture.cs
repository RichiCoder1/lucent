using Lucent.Platform.Windows;
using Lucent.Renderer.Skia;
using SDL3;
using SkiaSharp;

namespace Lucent.Platform.Windows.TestHost;

/// <summary>Published resize and mount-continuity proof using the Windows input transport and real shaping.</summary>
internal static class EditorSessionFixture
{
    internal static int Run()
    {
        nint window = 0;
        try
        {
            Require(SDL.Init(SDL.InitFlags.Video), "SDL video startup");
            window = SDL.CreateWindow(
                "Lucent editor session proof",
                900,
                300,
                SDL.WindowFlags.Hidden | SDL.WindowFlags.Resizable
            );
            Require(window != 0, "native window creation");
            using var composition = new Composition(new ReactiveGraph(), "editor-proof");
            var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            var model = new EditorSessionFixtureModel(composition.Root.Scope);
            composition.Mount(
                composition.Root,
                theme,
                LuiFixtures.Components.EditorSessionView(model)
            );
            using var renderer = new SkiaSceneRenderer();
            using var input = new WindowsInputAdapter(composition, window);
            var scene = Install(composition, renderer, 900, 1);
            Focus(composition, renderer, input, 900, 1);
            Require(
                input.DispatchText(new(TextInputKind.Commit, " draft")),
                "committed text input"
            );
            model.Session.MoveHome();
            model.Session.MoveRight();
            model.Session.MoveRight(extend: true);
            var text = model.Session.Text;
            var anchor = model.Session.Anchor;
            var caret = model.Session.Caret;
            var oldId = Field(composition).Id;
            _ = Install(composition, renderer, 900, 1);
            Require(
                input.DispatchText(new(TextInputKind.Preedit, "中", 0, 1)),
                "IME preedit input"
            );
            Require(model.Session.Text == text, "preedit remained uncommitted");

            foreach (var width in new[] { 420, 900 })
            {
                Require(
                    SDL.SetWindowSize(window, width, 300) && SDL.SyncWindow(window),
                    "native resize"
                );
                Require(
                    SDL.GetWindowSize(window, out var actualWidth, out var height)
                        && actualWidth == width
                        && height == 300,
                    "actual native size"
                );
                model.Compact.Value = actualWidth < 600;
                composition.Flush();
                scene = Install(composition, renderer, actualWidth, 1.5f);
                input.RefreshTextInput();
                Require(composition.Input.FocusedElement is null, "removed mount released focus");
                Require(!SDL.TextInputActive(window), "removed mount stopped native text input");
                var field = Field(composition);
                Require(field.Id != oldId, "arrangement replaced the mount");
                oldId = field.Id;
                Require(
                    model.Session.Text == text
                        && model.Session.Anchor == anchor
                        && model.Session.Caret == caret
                        && model.Session.CanUndo,
                    "draft/selection/undo continuity"
                );
                Require(
                    field.Resolve(ProjectionProperties.Text).Value == text,
                    "old preedit did not transfer"
                );
                Focus(composition, renderer, input, actualWidth, 1.5f);
                scene = Install(composition, renderer, actualWidth, 1.5f);
                Require(
                    SDL.TextInputActive(window),
                    "deliberate focus restarted native text input"
                );
                using var bitmap = new SKBitmap((int)(actualWidth * 1.5f), 450);
                using var canvas = new SKCanvas(bitmap);
                canvas.Clear(SKColors.White);
                renderer.Render(scene, canvas);
                Require(renderer.LiveTextBlobCount == 0, "paint released temporary native blobs");
                _ = Install(composition, renderer, actualWidth, 1.5f);
                Require(
                    input.DispatchText(new(TextInputKind.Preedit, "中", 0, 1)),
                    "next arrangement preedit"
                );
            }
            Require(
                input.DispatchText(new(TextInputKind.Cancel, "")),
                "final preedit cancellation"
            );
            model.Session.Undo();
            Require(model.Session.Text == "Initial", "undo survived both remounts");
            model.Session.Redo();
            Require(model.Session.Text == text, "redo survived both remounts");
            composition.Dispose();
            Require(model.Session.IsDisposed, "application scope disposed editor session");
            Console.WriteLine(
                "editor-session-proof: PASS; native resize 900->420->900; .lui remount; draft/selection/undo; focus/SDL text input; canceled preedit; 150% shaping/paint"
            );
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("editor-session-proof: " + error);
            return 1;
        }
        finally
        {
            if (window != 0)
                SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    private static void Focus(
        Composition composition,
        SkiaSceneRenderer renderer,
        WindowsInputAdapter input,
        int width,
        float scale
    )
    {
        Require(
            composition.Input.MoveFocus(FocusTraversalDirection.Next),
            "deliberate focus handoff"
        );
        _ = Install(composition, renderer, width, scale);
        input.RefreshTextInput();
    }

    private static RetainedScene Install(
        Composition composition,
        SkiaSceneRenderer renderer,
        int width,
        float scale
    )
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            composition.Flush();
            var scene = SceneLayout.Project(composition, new(width, 300, scale), renderer);
            if (composition.Input.SetScene(scene))
                return scene;
        }
        throw new InvalidOperationException("Editor scene failed to converge.");
    }

    private static Element Field(Composition composition) =>
        composition
            .Elements()
            .Single(element => element.DeclaredSemanticRole == SemanticRole.TextField);

    private static void Require(bool value, string action)
    {
        if (!value)
            throw new InvalidOperationException(action + ": " + SDL.GetError());
    }
}

internal sealed class EditorSessionFixtureModel
{
    internal EditorSessionFixtureModel(ReactiveScope owner)
    {
        Session = new EditorSession(owner, "note-1", "Initial");
        Compact = owner.Signal(false, "compact");
    }

    public EditorSession Session { get; }
    public Signal<bool> Compact { get; }
}
