using Lucent.Core;
using SkiaSharp;
using Svg;
using Svg.Model;
using Svg.Skia;
using Svg.Skia.TypefaceProviders;

namespace Lucent.Renderer.Skia;

internal sealed class SkiaSvgImage : PreparedImage, ISkiaPreparedImage
{
    private readonly object _gate = new();
    private SKSvg? _svg;
    private SKTypeface? _font;
    private readonly float _width;
    private readonly float _height;

    private SkiaSvgImage(SKSvg svg, SKTypeface? font, SecureSvgDocument document)
        : base((int)Math.Ceiling(document.Width), (int)Math.Ceiling(document.Height), document.Cost)
    {
        _svg = svg;
        _font = font;
        _width = document.Width;
        _height = document.Height;
    }

    internal static PreparedImage Prepare(
        ImagePreparationRequest request,
        byte[] bytes,
        byte[] fontBytes,
        CancellationToken token
    )
    {
        // Bound XML/DOM/path validation and one embedded-raster validation before allocating.
        using var preflight = request.ReserveTemporaryBytes(
            4L * 1024 * 1024 + bytes.LongLength * 16 + fontBytes.LongLength * 2
        );
        var document = SecureSvgDocument.Read(
            bytes,
            request.Source.Metadata,
            fontBytes.Length > 0,
            request.ReserveTemporaryBytes,
            token
        );
        document = document with { Cost = checked(document.Cost + fontBytes.LongLength * 2) };
        if (document.Cost > request.Limits.MaximumOutputBytes)
            throw SecureSvgDocument.Budget(
                "SVG prepared representation exceeds the output-memory budget."
            );
        using var preparation = request.ReserveTemporaryBytes(document.Cost);
        token.ThrowIfCancellationRequested();
        var svg = new SKSvg();
        SKTypeface? font = null;
        try
        {
            svg.Settings.EnableJavaScript = false;
            svg.Settings.EnableExternalJavaScript = false;
            svg.Settings.EnableTextSelectionRendering = false;
            svg.Settings.EnableBrokenImagePlaceholders = false;
            svg.Settings.EnableSvgFonts = false;
            svg.Settings.EnableTextReferences = false;
            svg.Settings.EnableFilterBackgroundInputs = false;
            svg.Settings.TypefaceProviders = [];
            if (fontBytes.Length > 0)
            {
                using var data = SKData.CreateCopy(fontBytes);
                font =
                    SKTypeface.FromData(data)
                    ?? throw SecureSvgDocument.Invalid("The configured SVG font is invalid.");
                using var fontProbe = new SKFont(font);
                var visibleText = string.Concat(document.Text.Where(c => !char.IsControl(c)));
                if (fontProbe.GetGlyphs(visibleText).Any(glyph => glyph == 0))
                    throw SecureSvgDocument.Unsupported(
                        "The pinned SVG font does not cover all required text glyphs."
                    );
                svg.Settings.TypefaceProviders.Add(new PinnedFont(font));
            }
            var parameters = new SvgParameters
            {
                LoadOptions = new SvgDocumentLoadOptions
                {
                    ProcessingMode = SvgProcessingMode.SecureStatic,
                    ExternalResources = SvgExternalResourcePolicy.SameDocumentAndDataOnly,
                    PreferSvg2Href = true,
                },
            };
            using var input = new MemoryStream(document.Bytes, false);
            if (svg.Load(input, parameters) is null)
                throw SecureSvgDocument.Invalid(
                    "The static SVG adapter could not prepare the document."
                );
            if (
                svg.HasAnimations
                || svg.HasPendingAnimationFrame
                || svg.AnimationController is not null
            )
                throw SecureSvgDocument.Unsupported("Animation-bearing SVG is not supported.");
            token.ThrowIfCancellationRequested();
            return new SkiaSvgImage(svg, font, document);
        }
        catch
        {
            Release(svg, font);
            throw;
        }
    }

    public void Draw(
        SKCanvas canvas,
        LayoutRect sourceBounds,
        LayoutRect destinationBounds,
        ImageColorMode colorMode,
        Color tint
    )
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (
                sourceBounds.Width <= 0
                || sourceBounds.Height <= 0
                || destinationBounds.Width <= 0
                || destinationBounds.Height <= 0
            )
                return;
            var save = canvas.Save();
            try
            {
                canvas.ClipRect(
                    new SKRect(
                        destinationBounds.X,
                        destinationBounds.Y,
                        destinationBounds.X + destinationBounds.Width,
                        destinationBounds.Y + destinationBounds.Height
                    )
                );
                canvas.Translate(destinationBounds.X, destinationBounds.Y);
                canvas.Scale(
                    destinationBounds.Width / sourceBounds.Width,
                    destinationBounds.Height / sourceBounds.Height
                );
                canvas.Translate(-sourceBounds.X, -sourceBounds.Y);
                canvas.Scale(Width / _width, Height / _height);
                if (colorMode == ImageColorMode.Monochrome)
                {
                    using var filter = SKColorFilter.CreateBlendMode(
                        new SKColor(tint.R, tint.G, tint.B, tint.A),
                        SKBlendMode.SrcIn
                    );
                    using var paint = new SKPaint { ColorFilter = filter, IsAntialias = true };
                    canvas.DrawPicture(_svg!.Picture!, paint);
                }
                else
                    canvas.DrawPicture(_svg!.Picture!);
            }
            finally
            {
                canvas.RestoreToCount(save);
            }
        }
    }

    protected override void DisposeCore()
    {
        lock (_gate)
        {
            var svg = _svg;
            _svg = null;
            if (svg is not null)
                Release(svg, _font);
            _font = null;
        }
    }

    private static void Release(SKSvg svg, SKTypeface? font)
    {
        svg.Dispose();
        // The pinned SKSvg.Dispose resets the document/picture but does not dispose settings.
        svg.Settings.Srgb.Dispose();
        svg.Settings.SrgbLinear.Dispose();
        font?.Dispose();
    }

    private sealed class PinnedFont(SKTypeface face) : ITypefaceProvider
    {
        public SKTypeface? FromFamilyName(
            string fontFamily,
            SKFontStyleWeight fontWeight,
            SKFontStyleWidth fontWidth,
            SKFontStyleSlant fontStyle
        ) => face;
    }
}
