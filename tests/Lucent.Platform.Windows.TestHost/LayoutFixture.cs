using Lucent.Platform.Windows;
using Lucent.Renderer.Skia;
using SDL3;
using SkiaSharp;

namespace Lucent.Platform.Windows.TestHost;

/// <summary>Published responsive Grid, constrained paragraph, resize, DPI, and paint proof.</summary>
internal static class LayoutFixture
{
    private const int Height = 520;
    private const int WideWidth = 1060;
    private const int CompactWidth = 480;

    internal static int RunLive()
    {
        try
        {
            var graph = new ReactiveGraph();
            using var composition = new Composition(graph, "live-layout-proof");
            var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            var model = new LayoutFixtureModel(composition.Root.Scope);
            composition.Mount(
                composition.Root,
                theme,
                LuiFixtures.Components.LayoutFixtureView(model)
            );
            return WindowsBootstrap.Run("Lucent Live Layout Fixture", composition, theme);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("live-layout-fixture: " + error.Message);
            return 1;
        }
    }

    internal static int Run()
    {
        nint window = 0;
        try
        {
            Require(SDL.Init(SDL.InitFlags.Video), "SDL video startup");
            window = SDL.CreateWindow(
                "Lucent layout proof",
                WideWidth,
                Height,
                SDL.WindowFlags.Hidden
                    | SDL.WindowFlags.Resizable
                    | SDL.WindowFlags.HighPixelDensity
            );
            Require(window != 0, "native window creation");

            using var composition = new Composition(new ReactiveGraph(), "layout-proof");
            var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            var model = new LayoutFixtureModel(composition.Root.Scope);
            composition.Mount(
                composition.Root,
                theme,
                LuiFixtures.Components.LayoutFixtureView(model)
            );
            using var renderer = new SkiaSceneRenderer();

            var wide = RunScenario(window, composition, renderer, model, WideWidth, 1f, true);
            var compact = RunScenario(
                window,
                composition,
                renderer,
                model,
                CompactWidth,
                1.5f,
                false
            );
            var wideDpi = RunScenario(window, composition, renderer, model, WideWidth, 2f, true);

            Require(
                compact.LineCount > wide.LineCount,
                "compact resize did not increase constrained paragraph wrapping"
            );
            Require(
                MathF.Abs(wide.ParagraphBounds.Width - wideDpi.ParagraphBounds.Width) <= 2,
                "DPI changed logical paragraph geometry"
            );
            Require(
                wide.ViewportScale == 1f
                    && compact.ViewportScale == 1.5f
                    && wideDpi.ViewportScale == 2f,
                "published scenes did not retain requested DPI scales"
            );

            composition.Dispose();
            Console.WriteLine(
                "layout-fixture: PASS; responsive Grid wide/compact resize; constrained paragraph lines "
                    + wide.LineCount
                    + "->"
                    + compact.LineCount
                    + "; 100%/150%/200% pixel paint"
            );
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("layout-fixture: " + error);
            return 1;
        }
        finally
        {
            if (window != 0)
                SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    private static ScenarioResult RunScenario(
        nint window,
        Composition composition,
        SkiaSceneRenderer renderer,
        LayoutFixtureModel model,
        int width,
        float scale,
        bool wide
    )
    {
        Require(
            SDL.SetWindowSize(window, width, Height) && SDL.SyncWindow(window),
            "native resize"
        );
        Require(
            SDL.GetWindowSize(window, out var actualWidth, out var actualHeight)
                && actualWidth == width
                && actualHeight == Height,
            "actual native size"
        );

        var scene = Install(composition, renderer, actualWidth, scale);
        var assigned = model.Constraints.Current;
        Require(
            MathF.Abs(assigned.Width - actualWidth) <= 0.01f
                && MathF.Abs(assigned.Height - Height) <= 0.01f,
            "responsive container did not publish native logical content size"
        );

        var paragraphElement = composition
            .Elements()
            .Single(element => element.Name == "layout.paragraph");
        var paragraph = scene.Boxes.Single(box => box.Identity.ElementId == paragraphElement.Id);
        Require(paragraph.Text is not null, "paragraph did not produce retained text geometry");
        var text = paragraph.Text!;
        Require(text.Lines.Count > 1, "paragraph did not wrap into multiple lines");
        Require(
            text.Lines.All(line =>
                line.Utf16Start >= 0
                && line.Utf16Length >= 0
                && line.Utf16Start + line.Utf16Length <= 1024
                && float.IsFinite(line.Baseline)
            ),
            "paragraph line geometry escaped its UTF-16 source range"
        );

        var captureElement = composition
            .Elements()
            .SingleOrDefault(element => element.Name == "layout.capture");
        if (wide)
        {
            Require(captureElement is not null, "wide responsive branch was not retained");
            var capture = scene.Boxes.Single(box => box.Identity.ElementId == captureElement!.Id);
            Require(
                paragraph.Bounds.X > capture.Bounds.X + capture.Bounds.Width,
                "wide Grid did not place paragraph after fixed tracks"
            );
        }
        else
        {
            Require(captureElement is null, "compact responsive branch retained wide children");
            Require(
                paragraph.Bounds.X >= 18,
                "compact Grid did not restart the paragraph at its single track"
            );
        }

        using var bitmap = new SKBitmap(
            checked((int)MathF.Ceiling(actualWidth * scale)),
            checked((int)MathF.Ceiling(Height * scale)),
            SKColorType.Rgba8888,
            SKAlphaType.Premul
        );
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            renderer.Render(scene, canvas);
        }
        Require(
            renderer.LiveTextBlobCount <= 256
                && renderer.RetainedTextBlobBytes <= 16L * 1024 * 1024,
            "paint retained an unbounded native text-blob cache"
        );
        RequirePainted(bitmap, paragraph.Bounds, scale, "paragraph paint");
        return new ScenarioResult(scene.Viewport.Scale, paragraph.Bounds, text.Lines.Count);
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
            var scene = SceneLayout.Project(composition, new(width, Height, scale), renderer);
            if (composition.Input.SetScene(scene))
                return scene;
        }
        throw new InvalidOperationException("Layout scene failed to converge.");
    }

    private static void RequirePainted(
        SKBitmap bitmap,
        LayoutRect bounds,
        float scale,
        string action
    )
    {
        var left = Math.Clamp((int)MathF.Floor(bounds.X * scale), 0, bitmap.Width);
        var top = Math.Clamp((int)MathF.Floor(bounds.Y * scale), 0, bitmap.Height);
        var right = Math.Clamp(
            (int)MathF.Ceiling((bounds.X + bounds.Width) * scale),
            left,
            bitmap.Width
        );
        var bottom = Math.Clamp(
            (int)MathF.Ceiling((bounds.Y + bounds.Height) * scale),
            top,
            bitmap.Height
        );
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
            if (bitmap.GetPixel(x, y).Alpha != 0)
                return;
        throw new InvalidOperationException(action + " produced no nontransparent pixels.");
    }

    private static void Require(bool value, string action)
    {
        if (!value)
            throw new InvalidOperationException(action + ": " + SDL.GetError());
    }

    private readonly record struct ScenarioResult(
        float ViewportScale,
        LayoutRect ParagraphBounds,
        int LineCount
    );

    private const string Text =
        "This constrained paragraph demonstrates word wrapping, grapheme-safe fallback, and retained line geometry while a published responsive Grid changes from a wide three-track arrangement to a compact single-track arrangement during resize.";
}

internal sealed class LayoutFixtureModel
{
    internal LayoutFixtureModel(ReactiveScope owner)
    {
        Constraints = new ResponsiveConstraints(owner, "layout-fixture");
    }

    public ResponsiveConstraints Constraints { get; }
}
