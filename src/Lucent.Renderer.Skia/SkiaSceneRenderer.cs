using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HarfBuzzSharp;
using Lucent.Core;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using HbBuffer = HarfBuzzSharp.Buffer;

namespace Lucent.Renderer.Skia;

/// <summary>CPU Skia/HarfBuzz shaper. Itemization is deliberately bounded: common LTR and Arabic/Hebrew RTL only; full Unicode bidi is deferred.</summary>
public sealed class SkiaSceneRenderer : ITextShaper, IDisposable
{
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private bool _disposed;

    public ShapedText Shape(TextMeasureRequest request)
    {
        CheckThread(); ThrowIfDisposed(); request.Validate();
        if (request.Text.Length == 0) return new ShapedText(Identity(request, []), 0, 0, []);
        var pieces = Itemize(request);
        var pending = new List<PendingRun>();
        foreach (var piece in pieces)
        {
            using var face = ResolveFace(request, piece.Text, out var collectionIndex);
            using var font = new SKFont(face, request.FontSize);
            using var shaper = new SKShaper(face);
            using var buffer = new HbBuffer();
            buffer.AddUtf16(piece.Text); buffer.GuessSegmentProperties();
            buffer.Direction = piece.Direction == TextDirection.RightToLeft ? Direction.RightToLeft : Direction.LeftToRight;
            buffer.Language = new Language(request.Language);
            var result = shaper.Shape(buffer, font);
            var positions = buffer.GlyphPositions;
            if (result.Codepoints.Length == 0 || result.Codepoints.Any(glyph => glyph == 0) || positions.Length != result.Codepoints.Length) throw new InvalidOperationException("Font fallback produced a missing glyph.");
            var cursor = 0f;
            var glyphs = new ShapedGlyph[result.Codepoints.Length];
            for (var index = 0; index < glyphs.Length; index++)
            {
                var position = positions[index];
                var advance = position.XAdvance / 64f; var xOffset = position.XOffset / 64f; var yOffset = position.YOffset / 64f;
                if (!float.IsFinite(cursor) || !float.IsFinite(advance) || !float.IsFinite(xOffset) || !float.IsFinite(yOffset)) throw new InvalidOperationException("HarfBuzz returned non-finite glyph metrics.");
                glyphs[index] = new(result.Codepoints[index], result.Clusters[index] + (uint)piece.Utf16Offset, cursor + xOffset, -yOffset, advance, xOffset, yOffset);
                cursor = Checked(cursor + advance);
            }
            var metrics = font.Metrics; var ascent = metrics.Ascent; var descent = metrics.Descent;
            var family = face.FamilyName; var style = face.FontStyle;
            pending.Add(new(family, style, FaceFingerprint(face, collectionIndex), collectionIndex, family + "#" + collectionIndex.ToString(CultureInfo.InvariantCulture), piece.Direction, cursor, ascent, descent, glyphs));
        }
        var lineAscent = pending.Min(run => run.Ascent); var lineDescent = pending.Max(run => run.Descent); var baseline = -lineAscent; var height = Checked(lineDescent - lineAscent); var origin = 0f;
        var runs = new List<ShapedRun>();
        foreach (var value in pending)
        {
            var run = new ShapedRun(RunIdentity(request, value.Family, value.Style, value.Fingerprint, value.CollectionIndex, value.Direction, value.Glyphs, origin, baseline, lineAscent, lineDescent), value.Family, value.Style.Weight, value.Style.Width, (int)value.Style.Slant, value.Fingerprint, value.CollectionIndex, value.SourceIdentity, value.Direction, request.Language, request.FontSize, origin, baseline, lineAscent, lineDescent, value.Width, value.Glyphs);
            runs.Add(run); origin = Checked(origin + value.Width);
        }
        return new ShapedText(Identity(request, runs), origin, height, runs);
    }

    public void Render(RetainedScene scene, SKCanvas canvas)
    {
        CheckThread(); ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(scene); ArgumentNullException.ThrowIfNull(canvas);
        canvas.Save();
        try { canvas.Scale(scene.Viewport.Scale, scene.Viewport.Scale); Paint(scene.Nodes, canvas); }
        finally { canvas.Restore(); }
    }

    private void Paint(IEnumerable<SceneNode> nodes, SKCanvas canvas)
    {
        foreach (var node in nodes)
        {
            if (node is ClipSceneNode clip)
            {
                canvas.Save();
                try { canvas.ClipRect(Rect(clip.Bounds)); Paint(clip.Children, canvas); }
                finally { canvas.Restore(); }
            }
            else if (node is PaintSceneNode paint)
            {
                using var brush = new SKPaint { Color = Color(paint.Color), IsAntialias = false };
                canvas.DrawRect(Rect(paint.Bounds), brush);
            }
            else if (node is TextSceneNode text)
            {
                using var brush = new SKPaint { Color = Color(text.Color), IsAntialias = true };
                foreach (var run in text.Text.Runs) PaintRun(canvas, text.Bounds, run, brush);
            }
        }
    }

    private static void PaintRun(SKCanvas canvas, LayoutRect bounds, ShapedRun run, SKPaint brush)
    {
        using var face = SKTypeface.FromFamilyName(run.Family, run.Weight, run.Width, (SKFontStyleSlant)run.Slant) ?? throw new InvalidOperationException("Scene face is unavailable.");
        if (FaceFingerprint(face, run.CollectionIndex) != run.Fingerprint) throw new InvalidOperationException("Scene face fingerprint did not match its shaped run.");
        using var font = new SKFont(face, run.FontSize);
        using var builder = new SKTextBlobBuilder();
        builder.AddPositionedRun(run.Glyphs.Select(glyph => checked((ushort)glyph.GlyphId)).ToArray(), font, run.Glyphs.Select(glyph => new SKPoint(glyph.X, glyph.Y)).ToArray());
        using var blob = builder.Build() ?? throw new InvalidOperationException("Immutable run produced no paintable text blob.");
        canvas.Save();
        try { canvas.ClipRect(Rect(bounds)); canvas.DrawText(blob, bounds.X + run.OriginX, bounds.Y + run.Baseline, brush); }
        finally { canvas.Restore(); }
    }

    private static SKTypeface ResolveFace(TextMeasureRequest request, string text, out int collectionIndex)
    {
        var requested = SKTypeface.FromFamilyName(request.FontFamily);
        if (requested is not null && Covers(requested, request, text)) { collectionIndex = CollectionIndex(requested); return requested; }
        requested?.Dispose();
        foreach (var rune in text.EnumerateRunes())
        {
            var fallback = SKFontManager.Default.MatchCharacter(rune.Value);
            if (fallback is null) continue;
            if (Covers(fallback, request, text)) { collectionIndex = CollectionIndex(fallback); return fallback; }
            fallback.Dispose();
        }
        throw new InvalidOperationException("No font fallback covers the text element.");
    }

    private static bool Covers(SKTypeface face, TextMeasureRequest request, string text)
    {
        using var font = new SKFont(face, request.FontSize); using var shaper = new SKShaper(face); using var buffer = new HbBuffer();
        buffer.AddUtf16(text); buffer.GuessSegmentProperties(); buffer.Direction = request.Direction == TextDirection.RightToLeft ? Direction.RightToLeft : Direction.LeftToRight; buffer.Language = new Language(request.Language);
        return shaper.Shape(buffer, font).Codepoints.All(glyph => glyph != 0);
    }

    private static List<Piece> Itemize(TextMeasureRequest request)
    {
        var blocks = new List<DirectionalBlock>(); var enumerator = StringInfo.GetTextElementEnumerator(request.Text);
        TextDirection prior = request.Direction;
        while (enumerator.MoveNext())
        {
            var text = (string)enumerator.Current!; var offset = enumerator.ElementIndex; var direction = DirectionOf(text, prior); prior = direction;
            var element = new TextElement(text, offset);
            if (blocks.LastOrDefault() is { } priorBlock && priorBlock.Direction == direction) priorBlock.Elements.Add(element);
            else blocks.Add(new DirectionalBlock(direction, [element]));
        }
        var result = new List<Piece>();
        if (request.Direction == TextDirection.RightToLeft) blocks.Reverse();
        foreach (var block in blocks)
        {
            var faceRuns = new List<Piece>();
            foreach (var item in block.Elements)
            {
                using var face = ResolveFace(request, item.Text, out var collectionIndex); var style = face.FontStyle;
                var descriptor = FaceFingerprint(face, collectionIndex) + ":" + collectionIndex.ToString(CultureInfo.InvariantCulture);
                if (faceRuns.LastOrDefault() is { } priorRun && priorRun.Face == descriptor) faceRuns[^1] = priorRun with { Text = priorRun.Text + item.Text };
                else faceRuns.Add(new Piece(item.Text, item.Offset, block.Direction, descriptor));
            }
            result.AddRange(faceRuns);
        }
        return result;
    }

    private static TextDirection DirectionOf(string element, TextDirection fallback)
    {
        foreach (var rune in element.EnumerateRunes())
            if ((rune.Value is >= 0x0590 and <= 0x08ff or >= 0xfb1d and <= 0xfdff or >= 0xfe70 and <= 0xfeff) && Rune.IsLetterOrDigit(rune)) return TextDirection.RightToLeft;
            else if (Rune.IsLetterOrDigit(rune)) return TextDirection.LeftToRight;
        return fallback;
    }

    public void Dispose() { CheckThread(); if (_disposed) return; _disposed = true; }
    private void CheckThread() { if (Environment.CurrentManagedThreadId != _ownerThread) throw new InvalidOperationException("Renderer access must remain on its owner thread."); }
    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(SkiaSceneRenderer)); }
    private static float Checked(float value) { if (!float.IsFinite(value)) throw new InvalidOperationException("Text metric overflow."); return value; }
    private static string Identity(TextMeasureRequest request, IEnumerable<ShapedRun> runs) => Hash(request.FontFamily + "\n" + request.FontSize.ToString("R", CultureInfo.InvariantCulture) + "\n" + request.Language + "\n" + request.Direction + "\n" + request.Scale.ToString("R", CultureInfo.InvariantCulture) + "\n" + string.Join('|', runs.Select(run => run.Identity)));
    private static string RunIdentity(TextMeasureRequest request, string family, SKFontStyle style, string fingerprint, int collectionIndex, TextDirection direction, IEnumerable<ShapedGlyph> glyphs, float origin, float baseline, float ascent, float descent) => Hash(family + "\n" + style.Weight + "\n" + style.Width + "\n" + style.Slant + "\n" + fingerprint + "\n" + collectionIndex.ToString(CultureInfo.InvariantCulture) + "\n" + request.FontSize.ToString("R", CultureInfo.InvariantCulture) + "\n" + request.Language + "\n" + direction + "\n" + origin.ToString("R", CultureInfo.InvariantCulture) + ":" + baseline.ToString("R", CultureInfo.InvariantCulture) + ":" + ascent.ToString("R", CultureInfo.InvariantCulture) + ":" + descent.ToString("R", CultureInfo.InvariantCulture) + "\n" + string.Join(';', glyphs.Select(glyph => glyph.GlyphId + ":" + glyph.Cluster + ":" + glyph.X.ToString("R", CultureInfo.InvariantCulture) + ":" + glyph.Y.ToString("R", CultureInfo.InvariantCulture) + ":" + glyph.XAdvance.ToString("R", CultureInfo.InvariantCulture) + ":" + glyph.XOffset.ToString("R", CultureInfo.InvariantCulture) + ":" + glyph.YOffset.ToString("R", CultureInfo.InvariantCulture))));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static SKRect Rect(LayoutRect value) => new(value.X, value.Y, value.X + value.Width, value.Y + value.Height);
    private static SKColor Color(uint value) => new((byte)(value >> 16 & 0xff), (byte)(value >> 8 & 0xff), (byte)(value & 0xff), (byte)(value >> 24 & 0xff));
    private static int CollectionIndex(SKTypeface face)
    {
        using var stream = face.OpenStream(out var index) ?? throw new InvalidOperationException("Typeface OpenStream did not expose a collection identity.");
        if (index < 0) throw new InvalidOperationException("Typeface OpenStream returned an invalid collection index.");
        return index;
    }
    private static string FaceFingerprint(SKTypeface face, int collectionIndex)
    {
        using var stream = face.OpenStream(out var actualIndex) ?? throw new InvalidOperationException("Typeface OpenStream did not expose bytes for fingerprint verification.");
        if (actualIndex != collectionIndex) throw new InvalidOperationException("Typeface collection index changed.");
        using var data = SKData.Create(stream);
        if (data is null || data.Size == 0) throw new InvalidOperationException("Typeface OpenStream produced no fingerprintable bytes.");
        return Hash(Convert.ToHexString(data.ToArray()));
    }
    private sealed record TextElement(string Text, int Offset);
    private sealed class DirectionalBlock(TextDirection direction, List<TextElement> elements) { public TextDirection Direction { get; } = direction; public List<TextElement> Elements { get; } = elements; }
    private sealed record Piece(string Text, int Utf16Offset, TextDirection Direction, string Face);
    private sealed record PendingRun(string Family, SKFontStyle Style, string Fingerprint, int CollectionIndex, string SourceIdentity, TextDirection Direction, float Width, float Ascent, float Descent, ShapedGlyph[] Glyphs);
}
