using Lucent.Core;
using Lucent.Renderer.Skia;
using SkiaSharp;

namespace Lucent.Renderer.Skia.Tests;

[TestClass]
public sealed class GaugePaintTests
{
    [TestMethod]
    public void GaugeRendersTrackAndControlledArcAcrossThemesSizesAndScales()
    {
        foreach (
            var palette in new[]
            {
                ControlThemes.Light,
                ControlThemes.Dark,
                ControlThemes.HighContrast,
            }
        )
        foreach (var scale in new[] { 1f, 1.5f, 2f })
        foreach (var size in new[] { 48f, 96f, 144f })
        {
            using var composition = new Composition(new ReactiveGraph(), "gauge-pixels");
            using var theme = new ThemeContext(
                composition.Root.Scope,
                palette,
                reducedMotion: true
            );
            var value = composition.Root.Scope.Signal<double?>(50, "value");
            var gauge = composition.Mount(
                composition.Root,
                theme,
                Components
                    .Gauge("Usage", () => value.Value)
                    .Style(Style.Empty.Width(size).Height(size))
            );
            using var renderer = new SkiaSceneRenderer();
            var physicalSize = (int)(size * scale);
            using var bitmap = new SKBitmap(
                physicalSize,
                physicalSize,
                SKColorType.Rgba8888,
                SKAlphaType.Premul
            );
            using var canvas = new SKCanvas(bitmap);
            SKColor[] Samples(bool vertical = false)
            {
                composition.Flush();
                using var scene = SceneLayout.Project(
                    composition,
                    new(size, size, scale),
                    renderer
                );
                var box = scene.Boxes.Single(box => box.Identity.ElementId == gauge.Id);
                Assert.AreEqual(size, box.Bounds.Width);
                Assert.AreEqual(size, box.Bounds.Height);
                canvas.Clear(SKColors.Transparent);
                renderer.Render(scene, canvas);
                var near = Math.Clamp((int)(size * scale * 3 / 96), 0, physicalSize - 1);
                var far = Math.Clamp((int)(size * scale * 92 / 96), 0, physicalSize - 1);
                SKColor Sample(int edge) =>
                    vertical
                        ? bitmap.GetPixel(physicalSize / 2, edge)
                        : bitmap.GetPixel(edge, physicalSize / 2);
                return [Sample(near), Sample(far), Sample(0), Sample(physicalSize - 1)];
            }
            var half = Samples();
            Assert.IsTrue(
                half.Take(2).All(pixel => pixel.Alpha > 200),
                "The six-DIP ring disappeared under scale/layout."
            );
            Assert.IsTrue(
                half[0] != half[1] || half[2] != half[3],
                "The accent arc was indistinguishable from the remaining track."
            );
            value.Value = 0;
            var empty = Samples();
            Assert.AreEqual(empty[0], empty[1], "Minimum value painted an unexpected arc.");
            // Empty/invalid labels change; sample away from the ordinary text centerline.
            var emptyVertical = Samples(vertical: true);
            value.Value = null;
            CollectionAssert.AreEqual(
                emptyVertical,
                Samples(vertical: true),
                "Empty input must retain only the track."
            );
            value.Value = double.NaN;
            CollectionAssert.AreEqual(
                emptyVertical,
                Samples(vertical: true),
                "Invalid input must retain only the track."
            );
            value.Value = 100;
            var full = Samples();
            Assert.AreEqual(full[0], full[1], "Maximum value did not fill the ring.");
            Assert.IsTrue(
                empty[0] != full[0] || empty[2] != full[2],
                "Value changes did not reach frozen drawing replay."
            );
        }
    }

    [TestMethod]
    public void EmptyInvalidAndDisposedGaugeScenesRemainStableDuringReplay()
    {
        using var composition = new Composition(new ReactiveGraph(), "gauge-replay");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var reads = 0;
        var root = composition.Mount(
            composition.Root,
            theme,
            Components.Gauge(
                "Reading",
                () =>
                {
                    reads++;
                    return double.NaN;
                }
            )
        );
        using var renderer = new SkiaSceneRenderer();
        composition.Flush();
        using var scene = SceneLayout.Project(composition, new(96, 96, 1), renderer);
        var preparedReads = reads;
        root.Dispose();
        using var bitmap = new SKBitmap(96, 96);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        renderer.Render(scene, canvas);
        var pixels = bitmap.Bytes;
        canvas.Clear(SKColors.Transparent);
        renderer.Render(scene, canvas);
        CollectionAssert.AreEqual(pixels, bitmap.Bytes);
        Assert.AreEqual(preparedReads, reads, "Retained replay read the disposed live model.");
    }

    [TestMethod]
    public void NarrowGaugeContainsNormalEmptyInvalidAndLongUnitInk()
    {
        foreach (
            var sample in new (double? Value, GaugeOptions Options)[]
            {
                (50, new()),
                (null, new()),
                (double.NaN, new()),
                (50, new(unit: "extraordinarily-long-unit")),
            }
        )
        {
            using var composition = new Composition(new ReactiveGraph(), "gauge-containment");
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            var gauge = Components
                .Gauge("Reading", () => sample.Value, sample.Options)
                .Style(Style.Empty.Width(48).Height(48));
            var root = composition.Mount(
                composition.Root,
                theme,
                Components.Column(
                    ComponentContent.Create([gauge]),
                    Style.Empty.Width(96).Height(96).Padding(Insets.Uniform(24))
                )
            );
            composition.Flush();
            using var renderer = new SkiaSceneRenderer();
            using var scene = SceneLayout.Project(composition, new(96, 96, 1), renderer);
            var gaugeRoot = root.Children.Single();
            var gaugeBox = scene.Boxes.Single(box => box.Identity.ElementId == gaugeRoot.Id).Bounds;
            using var bitmap = new SKBitmap(96, 96, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            renderer.Render(scene, canvas);

            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha == 0)
                    continue;
                Assert.IsTrue(
                    x >= gaugeBox.X
                        && x < gaugeBox.X + gaugeBox.Width
                        && y >= gaugeBox.Y
                        && y < gaugeBox.Y + gaugeBox.Height,
                    $"Gauge ink escaped its 48-DIP box at ({x},{y}) for '{sample.Value}' and unit '{sample.Options.Unit}'."
                );
            }
        }
    }
}
