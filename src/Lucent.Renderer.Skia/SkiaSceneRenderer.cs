using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using HarfBuzzSharp;
using Lucent.Core;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using HbBuffer = HarfBuzzSharp.Buffer;

namespace Lucent.Renderer.Skia;

/// <summary>Shapes Core text with HarfBuzz and paints retained scenes through Skia.</summary>
/// <remarks>
/// The renderer owns bounded LRU shape and native text-blob caches (256 entries and 16 MiB
/// estimated retained payload per cache; 32 MiB combined) and the typeface fingerprints used to
/// verify that cached glyph data still identifies the face that produced it. Shape results are immutable Core values and do
/// not retain Skia resources. Itemization supports common left-to-right
/// text and Arabic/Hebrew right-to-left text; full Unicode bidirectional reordering is not provided.
///
/// An instance is confined to the managed thread that created it. Call <see cref="Shape"/>,
/// <see cref="Render"/>, and <see cref="Dispose"/> from that thread; instances are not
/// synchronized for concurrent use. Dispose the renderer after the final frame to release its
/// cache and fingerprint data.
/// </remarks>
public sealed class SkiaSceneRenderer : ITextShaper, IDisposable
{
    // Pinned SKShaper encodes HarfBuzz positions against this source constant.
    private const float FontSizeScale = 512f;
    private const int ShapeCacheCapacity = 256;
    private const long ShapeCacheByteBudget = 16L * 1024 * 1024;
    private const int TextBlobCacheCapacity = 256;
    private const long TextBlobCacheByteBudget = 16L * 1024 * 1024;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<FaceKey, string> _faceFingerprints = [];
    private readonly Dictionary<TextMeasureRequest, ShapedText> _shapes = [];
    private readonly LinkedList<ShapeCacheEntry> _shapeLru = [];
    private readonly Dictionary<
        TextMeasureRequest,
        LinkedListNode<ShapeCacheEntry>
    > _shapeLruNodes = [];
    private readonly Dictionary<ShapedRun, TextBlobCacheEntry> _textBlobs = new(
        ShapedRunReferenceComparer.Instance
    );
    private readonly LinkedList<TextBlobCacheEntry> _textBlobLru = [];
    private readonly Dictionary<ShapedRun, LinkedListNode<TextBlobCacheEntry>> _textBlobLruNodes =
        new(ShapedRunReferenceComparer.Instance);
    private long _shapeBytes;
    private long _textBlobBytes;
    private bool _disposed;

    /// <summary>Gets the number of retained native text blobs owned by this renderer.</summary>
    /// <remarks>The bounded cache is released by <see cref="Dispose"/>; this count is zero afterward.</remarks>
    public int LiveTextBlobCount => _textBlobs.Count;

    /// <summary>Gets the estimated bytes retained by the native text-blob cache.</summary>
    public long RetainedTextBlobBytes => _textBlobBytes;

    /// <summary>Shapes a Core text request into immutable logical-pixel glyph runs.</summary>
    /// <param name="request">Font, language, direction, text, and scale values to shape.</param>
    /// <returns>A cached or newly shaped result whose glyph IDs, clusters, advances, and metrics can be retained by Core.</returns>
    /// <exception cref="ArgumentException"><paramref name="request"/> contains a missing, nonpositive, or nonfinite required value.</exception>
    /// <exception cref="InvalidOperationException">The owner thread is not calling the renderer, or no installed font can shape the requested text.</exception>
    /// <exception cref="ObjectDisposedException">The renderer has been disposed.</exception>
    public ShapedText Shape(TextMeasureRequest request)
    {
        CheckThread();
        ThrowIfDisposed();
        request.Validate();
        if (_shapes.TryGetValue(request, out var cached))
        {
            TouchCache(request);
            return cached;
        }

        var drafts = new List<LineDraft>();
        foreach (var span in ParagraphSpans(request.Text))
        {
            if (
                request.Wrap is TextWrap.NoWrap or TextWrap.ExplicitBreaks
                || !request.InlineConstraint.IsBounded
            )
                drafts.Add(ShapeLine(request, span.Start, span.Length, span.HardBreak));
            else
                drafts.AddRange(Wrap(request, span.Start, span.Length, span.HardBreak));
        }

        if (drafts.Count == 0)
            drafts.Add(ShapeLine(request, 0, 0, false));

        var didOverflow = drafts.Any(draft =>
            request.InlineConstraint.IsBounded
            && draft.Width > request.InlineConstraint.Limit!.Value + .001f
        );
        var visible = drafts;
        if (request.MaxLines is { } maxLines && visible.Count > maxLines)
        {
            visible = visible.Take(maxLines).ToList();
            didOverflow = true;
        }

        var lineHeight = 0f;
        var kept = new List<LineDraft>(visible.Count);
        if (request.BlockConstraint.IsBounded)
        {
            var limit = request.BlockConstraint.Limit!.Value;
            foreach (var draft in visible)
            {
                if (kept.Count > 0 && lineHeight + draft.Height > limit + .001f)
                {
                    didOverflow = true;
                    break;
                }
                if (kept.Count == 0 && draft.Height > limit + .001f && limit == 0)
                {
                    didOverflow = true;
                    break;
                }
                kept.Add(draft);
                lineHeight = Checked(lineHeight + draft.Height);
            }
            if (kept.Count < visible.Count)
                didOverflow = true;
        }
        else
        {
            kept.AddRange(visible);
            foreach (var draft in kept)
                lineHeight = Checked(lineHeight + draft.Height);
        }

        if (request.Overflow == TextOverflow.Ellipsis && didOverflow && kept.Count > 0)
            kept[^1] = Ellipsize(request, kept[^1]);

        var runs = new List<ShapedRun>();
        var lines = new List<ParagraphLine>(kept.Count);
        var top = 0f;
        var width = 0f;
        foreach (var draft in kept)
        {
            var baseline = Checked(top - draft.Ascent);
            var origin = 0f;
            foreach (var value in draft.Pending)
            {
                var run = new ShapedRun(
                    RunIdentity(
                        request,
                        value.Family,
                        value.Style,
                        value.Fingerprint,
                        value.CollectionIndex,
                        value.Direction,
                        value.Glyphs,
                        origin,
                        baseline,
                        draft.Ascent,
                        draft.Descent
                    ),
                    value.Family,
                    value.Style.Weight,
                    value.Style.Width,
                    (int)value.Style.Slant,
                    value.Fingerprint,
                    value.CollectionIndex,
                    value.SourceIdentity,
                    value.Direction,
                    request.Language,
                    request.FontSize,
                    origin,
                    baseline,
                    draft.Ascent,
                    draft.Descent,
                    value.Width,
                    value.Glyphs
                );
                runs.Add(run);
                origin = Checked(origin + value.Width);
            }
            width = MathF.Max(width, draft.Width);
            lines.Add(
                new ParagraphLine(
                    draft.Start,
                    draft.Length,
                    top,
                    baseline,
                    draft.Ascent,
                    draft.Descent,
                    draft.Leading,
                    draft.Width,
                    draft.TrailingWhitespaceAdvance,
                    draft.HardBreak
                )
            );
            top = Checked(top + draft.Height);
        }
        var height = request.BlockConstraint.IsBounded
            ? MathF.Min(top, request.BlockConstraint.Limit!.Value)
            : top;
        var identity = Identity(request, runs, lines, didOverflow);
        return Cache(
            request,
            new ShapedText(
                identity,
                width,
                Checked(height),
                runs,
                lines,
                didOverflow,
                request.InlineConstraint,
                request.BlockConstraint
            )
        );
    }

    private LineDraft ShapeLine(TextMeasureRequest request, int start, int length, bool hardBreak)
    {
        var text = request.Text.Substring(start, length);
        if (text.Length == 0)
        {
            var metrics = EmptyMetrics(request);
            return new(
                start,
                length,
                hardBreak,
                [],
                0,
                metrics.Ascent,
                metrics.Descent,
                metrics.Leading,
                metrics.Descent - metrics.Ascent + metrics.Leading,
                0
            );
        }

        var lineRequest = request with { Text = text };
        var pending = ShapePending(lineRequest, start);
        var ascent = pending.Min(run => run.Ascent);
        var descent = pending.Max(run => run.Descent);
        var leading = pending.Max(run => run.Leading);
        var width = Checked(pending.Sum(run => run.Width));
        var trailing = TrailingWhitespaceAdvance(start, text, pending);
        return new(
            start,
            length,
            hardBreak,
            pending,
            width,
            ascent,
            descent,
            leading,
            Checked(descent - ascent + leading),
            trailing
        );
    }

    private LineDraft Ellipsize(TextMeasureRequest request, LineDraft draft)
    {
        var source = request.Text.Substring(draft.Start, draft.Length);
        var end = checked(draft.Start + draft.Length);
        var boundaries = GraphemeBoundaries(request.Text, draft.Start, end);
        var sourcePending = ShapePending(request with { Text = source }, draft.Start);
        var cumulative = GraphemeAdvances(boundaries, sourcePending);
        var ellipsisPending = ShapePending(request with { Text = "\u2026" }, end);
        var ellipsisWidth = PendingWidth(ellipsisPending);
        var maxWidth = request.InlineConstraint.Limit;
        var bestIndex = boundaries.Count - 1;
        if (maxWidth is { } limit)
        {
            bestIndex = 0;
            for (var index = 1; index < boundaries.Count; index++)
                if (cumulative[index] + ellipsisWidth <= limit + .001f)
                    bestIndex = index;
        }

        var pending = ShapeEllipsisCandidate(request, source, draft.Start, boundaries[bestIndex]);
        if (maxWidth is { } bounded && PendingWidth(pending) > bounded + .001f && bestIndex > 0)
        {
            var low = 0;
            var high = bestIndex - 1;
            var fitting = 0;
            while (low <= high)
            {
                var index = low + (high - low) / 2;
                var candidate = ShapeEllipsisCandidate(
                    request,
                    source,
                    draft.Start,
                    boundaries[index]
                );
                if (PendingWidth(candidate) <= bounded + .001f)
                {
                    fitting = index;
                    low = index + 1;
                    pending = candidate;
                }
                else
                    high = index - 1;
            }
            if (PendingWidth(pending) > bounded + .001f)
                pending = ShapeEllipsisCandidate(request, source, draft.Start, boundaries[0]);
            bestIndex = fitting;
        }

        var width = PendingWidth(pending);
        var ascent = pending.Min(run => run.Ascent);
        var descent = pending.Max(run => run.Descent);
        var leading = pending.Max(run => run.Leading);
        return new LineDraft(
            draft.Start,
            boundaries[bestIndex] - draft.Start,
            false,
            pending,
            width,
            ascent,
            descent,
            leading,
            Checked(descent - ascent + leading),
            0
        );
    }

    private List<PendingRun> ShapeEllipsisCandidate(
        TextMeasureRequest request,
        string source,
        int sourceStart,
        int cut
    )
    {
        var prefixLength = cut - sourceStart;
        var candidate = string.Concat(source.AsSpan(0, prefixLength), "\u2026".AsSpan());
        var pending = ShapePending(request with { Text = candidate }, sourceStart);
        var syntheticStart = cut;
        var replacementCluster = Math.Clamp(
            Math.Max(0, syntheticStart - 1),
            0,
            Math.Max(0, request.Text.Length - 1)
        );
        return pending
            .Select(run =>
                run with
                {
                    Glyphs = run
                        .Glyphs.Select(glyph =>
                            glyph.Cluster >= syntheticStart
                                ? glyph with
                                {
                                    Cluster = (uint)replacementCluster,
                                }
                                : glyph
                        )
                        .ToArray(),
                }
            )
            .ToList();
    }

    private static float PendingWidth(IEnumerable<PendingRun> pending) =>
        Checked(pending.Sum(run => run.Width));

    private List<LineDraft> Wrap(TextMeasureRequest request, int start, int length, bool hardBreak)
    {
        var end = checked(start + length);
        if (start == end)
            return [ShapeLine(request, start, length, hardBreak)];

        var boundaries = GraphemeBoundaries(request.Text, start, end);
        var boundaryIndexes = boundaries
            .Select((boundary, index) => (boundary, index))
            .ToDictionary(value => value.boundary, value => value.index);
        var source = request.Text.Substring(start, length);
        var sourceRequest = request with { Text = source };
        // Shape the paragraph span once. Wrapping only scans the resulting grapheme advances;
        // each emitted line is shaped again so its glyph positions remain line-local.
        var sourcePending = ShapePending(sourceRequest, start);
        var cumulative = GraphemeAdvances(boundaries, sourcePending);
        var limit = request.InlineConstraint.Limit!.Value;
        var lines = new List<LineDraft>();
        var cursor = start;
        while (cursor < end)
        {
            var previous = cursor;
            var lastBreak = -1;
            var emitted = false;
            var cursorIndex = boundaryIndexes[cursor];
            for (var index = cursorIndex + 1; index < boundaries.Count; index++)
            {
                var candidateEnd = boundaries[index];
                var candidateWidth = Checked(cumulative[index] - cumulative[cursorIndex]);
                if (candidateWidth <= limit + .001f)
                {
                    previous = candidateEnd;
                    if (IsWhitespace(request.Text, boundaries[index - 1], candidateEnd))
                        lastBreak = candidateEnd;
                    continue;
                }

                var cut = lastBreak > cursor ? lastBreak : previous;
                if (cut <= cursor)
                    cut = candidateEnd;
                lines.Add(ShapeLine(request, cursor, cut - cursor, false));
                cursor = cut;
                emitted = true;
                break;
            }
            if (!emitted)
            {
                lines.Add(ShapeLine(request, cursor, end - cursor, hardBreak));
                cursor = end;
            }
        }
        return lines;
    }

    private static float[] GraphemeAdvances(
        IReadOnlyList<int> boundaries,
        IReadOnlyList<PendingRun> pending
    )
    {
        var boundaryValues = boundaries.ToArray();
        var advances = new float[boundaryValues.Length - 1];
        foreach (var run in pending)
        foreach (var glyph in run.Glyphs)
        {
            var index = Array.BinarySearch(boundaryValues, (int)glyph.Cluster);
            if (index < 0)
                index = ~index - 1;
            index = Math.Clamp(index, 0, advances.Length - 1);
            advances[index] = Checked(advances[index] + glyph.XAdvance);
        }

        var cumulative = new float[boundaryValues.Length];
        for (var index = 0; index < advances.Length; index++)
            cumulative[index + 1] = Checked(cumulative[index] + advances[index]);
        return cumulative;
    }

    private List<PendingRun> ShapePending(TextMeasureRequest request, int sourceOffset)
    {
        var pending = new List<PendingRun>();
        foreach (var piece in Itemize(request, request.Text, sourceOffset))
        {
            using var face = ResolveFace(request, piece.Text, out var collectionIndex);
            using var font = new SKFont(face, request.FontSize);
            using var shaper = new SKShaper(face);
            using var buffer = new HbBuffer();
            buffer.AddUtf16(piece.Text);
            buffer.GuessSegmentProperties();
            buffer.Direction =
                piece.Direction == TextDirection.RightToLeft
                    ? Direction.RightToLeft
                    : Direction.LeftToRight;
            buffer.Language = new Language(request.Language);
            var result = shaper.Shape(buffer, font);
            var positions = buffer.GlyphPositions;
            if (
                result.Codepoints.Length == 0
                || result.Codepoints.Any(glyph => glyph == 0)
                || positions.Length != result.Codepoints.Length
                || result.Points.Length != result.Codepoints.Length
            )
                throw new InvalidOperationException("Font fallback produced a missing glyph.");
            var textSizeY = font.Size / FontSizeScale;
            var textSizeX = textSizeY * font.ScaleX;
            if (
                !float.IsFinite(textSizeX)
                || !float.IsFinite(textSizeY)
                || !float.IsFinite(result.Width)
                || result.Width < 0
            )
                throw new InvalidOperationException("SKShaper returned non-finite text metrics.");
            var glyphs = new ShapedGlyph[result.Codepoints.Length];
            for (var index = 0; index < glyphs.Length; index++)
            {
                var position = positions[index];
                var advance = position.XAdvance * textSizeX;
                var xOffset = position.XOffset * textSizeX;
                var yOffset = position.YOffset * textSizeY;
                var point = result.Points[index];
                if (
                    !float.IsFinite(point.X)
                    || !float.IsFinite(point.Y)
                    || !float.IsFinite(advance)
                    || !float.IsFinite(xOffset)
                    || !float.IsFinite(yOffset)
                )
                    throw new InvalidOperationException(
                        "HarfBuzz returned non-finite glyph metrics."
                    );
                glyphs[index] = new(
                    result.Codepoints[index],
                    result.Clusters[index] + (uint)piece.Utf16Offset,
                    point.X,
                    point.Y,
                    advance,
                    xOffset,
                    yOffset
                );
            }
            var initialCursor = glyphs[0].X - glyphs[0].XOffset;
            if (MathF.Abs(initialCursor) > .001f)
                throw new InvalidOperationException(
                    "SKShaper returned a run whose first positioned glyph does not start at its origin."
                );
            for (var index = 0; index < glyphs.Length; index++)
            {
                var cursor = glyphs[index].X - glyphs[index].XOffset;
                var nextCursor =
                    index + 1 < glyphs.Length
                        ? glyphs[index + 1].X - glyphs[index + 1].XOffset
                        : result.Width;
                var positionedAdvance = nextCursor - cursor;
                if (!float.IsFinite(positionedAdvance))
                    throw new InvalidOperationException(
                        "SKShaper returned a non-finite positioned glyph advance."
                    );
                glyphs[index] = glyphs[index] with { XAdvance = positionedAdvance };
            }
            var width = Checked((float)glyphs.Sum(glyph => (double)glyph.XAdvance));
            if (MathF.Abs(width - result.Width) > .001f)
                throw new InvalidOperationException(
                    "SKShaper positioned glyph edges do not match its reported width."
                );
            var metrics = font.Metrics;
            pending.Add(
                new(
                    face.FamilyName,
                    face.FontStyle,
                    FaceFingerprint(face, collectionIndex),
                    collectionIndex,
                    face.FamilyName + "#" + collectionIndex.ToString(CultureInfo.InvariantCulture),
                    piece.Direction,
                    width,
                    metrics.Ascent,
                    metrics.Descent,
                    MathF.Max(0, metrics.Leading),
                    glyphs
                )
            );
        }
        return pending;
    }

    private static (float Ascent, float Descent, float Leading) EmptyMetrics(
        TextMeasureRequest request
    )
    {
        using var face = ResolveFace(request, " ", out _);
        using var font = new SKFont(face, request.FontSize);
        var metrics = font.Metrics;
        return (metrics.Ascent, metrics.Descent, MathF.Max(0, metrics.Leading));
    }

    private static float TrailingWhitespaceAdvance(
        int sourceOffset,
        string text,
        IReadOnlyList<PendingRun> pending
    )
    {
        var starts = StringInfo.ParseCombiningCharacters(text);
        var end = text.Length;
        while (end > 0)
        {
            var start = starts.Where(offset => offset < end).DefaultIfEmpty(0).Last();
            if (!char.IsWhiteSpace(text[start]))
                break;
            end = start;
        }
        if (end == text.Length)
            return 0;
        return Checked(
            pending
                .SelectMany(run => run.Glyphs)
                .Where(glyph => glyph.Cluster >= (uint)(sourceOffset + end))
                .Sum(glyph => glyph.XAdvance)
        );
    }

    private static List<int> GraphemeBoundaries(string text, int start, int end)
    {
        var boundaries = new List<int> { start };
        var enumerator = StringInfo.GetTextElementEnumerator(text, start);
        while (enumerator.MoveNext() && enumerator.ElementIndex < end)
        {
            var next = checked(enumerator.ElementIndex + ((string)enumerator.Current!).Length);
            if (next > start && next <= end)
                boundaries.Add(next);
        }
        if (boundaries[^1] != end)
            boundaries.Add(end);
        return boundaries;
    }

    private static bool IsWhitespace(string text, int start, int end) =>
        text.Substring(start, end - start).All(char.IsWhiteSpace);

    private static IEnumerable<(int Start, int Length, bool HardBreak)> ParagraphSpans(string text)
    {
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var length = text[index] switch
            {
                '\r' when index + 1 < text.Length && text[index + 1] == '\n' => 2,
                '\r' or '\n' or '\u2028' or '\u2029' => 1,
                _ => 0,
            };
            if (length == 0)
                continue;
            yield return (start, index - start, true);
            index += length - 1;
            start = index + 1;
        }
        if (start < text.Length || text.Length == 0)
            yield return (start, text.Length - start, false);
    }

    /// <summary>Paints a retained Core scene into a caller-owned Skia canvas.</summary>
    /// <param name="scene">Immutable scene whose logical viewport scale and renderer nodes define the frame.</param>
    /// <param name="canvas">Destination canvas; its existing state is preserved, and the canvas remains owned by the caller.</param>
    /// <remarks>
    /// The renderer applies <see cref="RetainedScene.Viewport"/>.Scale exactly once, then paints
    /// solid and gradient boxes, clipped subtrees, composited opacity groups, and shaped text.
    /// Temporary Skia paints, fonts, and typefaces are released before this method returns. Native
    /// text blobs are retained in the renderer's bounded cache and released on eviction or disposal.
    /// The scene and canvas must be used on the renderer's owner thread.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> or <paramref name="canvas"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The owner thread is not calling the renderer, or a scene face cannot be recreated for painting.</exception>
    /// <exception cref="ObjectDisposedException">The renderer has been disposed.</exception>
    public void Render(RetainedScene scene, SKCanvas canvas)
    {
        CheckThread();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(canvas);
        canvas.Save();
        try
        {
            canvas.Scale(scene.Viewport.Scale, scene.Viewport.Scale);
            Paint(scene.Nodes, canvas);
        }
        finally
        {
            canvas.Restore();
        }
    }

    private void Paint(IEnumerable<SceneNode> nodes, SKCanvas canvas)
    {
        foreach (var node in nodes)
        {
            if (node is OpacitySceneNode opacity)
            {
                if (opacity.Opacity == 0)
                    continue;
                var bounds = Rect(opacity.Bounds);
                if (bounds.Width <= 0 || bounds.Height <= 0 || canvas.QuickReject(bounds))
                    continue;
                using var layer = new SKPaint
                {
                    Color = SKColors.White.WithAlpha(
                        (byte)MathF.Round(opacity.Opacity * byte.MaxValue)
                    ),
                };
                canvas.SaveLayer(bounds, layer);
                try
                {
                    canvas.ClipRect(bounds);
                    Paint(opacity.Children, canvas);
                }
                finally
                {
                    canvas.Restore();
                }
            }
            else if (node is ClipSceneNode clip)
            {
                canvas.Save();
                try
                {
                    canvas.ClipRect(Rect(clip.Bounds));
                    Paint(clip.Children, canvas);
                }
                finally
                {
                    canvas.Restore();
                }
            }
            else if (node is PaintSceneNode paint)
            {
                using var brush = Paint(paint.Brush, paint.Bounds);
                canvas.DrawRect(Rect(paint.Bounds), brush);
            }
            else if (node is TextSceneNode text)
            {
                using var brush = new SKPaint { Color = Color(text.Color), IsAntialias = true };
                foreach (var run in text.Text.Runs)
                    PaintRun(canvas, text.Bounds, run, brush);
            }
        }
    }

    private void PaintRun(SKCanvas canvas, LayoutRect bounds, ShapedRun run, SKPaint brush)
    {
        using var lease = GetTextBlob(run);
        canvas.Save();
        try
        {
            canvas.ClipRect(Rect(bounds));
            canvas.DrawText(lease.Blob, bounds.X + run.OriginX, bounds.Y + run.Baseline, brush);
        }
        finally
        {
            canvas.Restore();
        }
    }

    private TextBlobLease GetTextBlob(ShapedRun run)
    {
        if (_textBlobs.TryGetValue(run, out var cached))
        {
            TouchTextBlob(run);
            return new(cached.Blob, false);
        }

        using var face =
            SKTypeface.FromFamilyName(
                run.Family,
                run.Weight,
                run.Width,
                (SKFontStyleSlant)run.Slant
            ) ?? throw new InvalidOperationException("Scene face is unavailable.");
        if (FaceFingerprint(face, run.CollectionIndex) != run.Fingerprint)
            throw new InvalidOperationException(
                "Scene face fingerprint did not match its shaped run."
            );
        using var font = new SKFont(face, run.FontSize);
        using var builder = new SKTextBlobBuilder();
        builder.AddPositionedRun(
            run.Glyphs.Select(glyph => checked((ushort)glyph.GlyphId)).ToArray(),
            font,
            run.Glyphs.Select(glyph => new SKPoint(glyph.X, glyph.Y)).ToArray()
        );
        var blob =
            builder.Build()
            ?? throw new InvalidOperationException(
                "Immutable run produced no paintable text blob."
            );
        var bytes = EstimateTextBlobBytes(run);
        if (bytes > TextBlobCacheByteBudget)
            return new(blob, true);

        while (
            _textBlobLru.First is not null
            && (
                _textBlobs.Count >= TextBlobCacheCapacity
                || _textBlobBytes + bytes > TextBlobCacheByteBudget
            )
        )
        {
            var evicted = _textBlobLru.First!;
            _textBlobLru.RemoveFirst();
            _textBlobLruNodes.Remove(evicted.Value.Run);
            _textBlobs.Remove(evicted.Value.Run);
            _textBlobBytes -= evicted.Value.Bytes;
            evicted.Value.Blob.Dispose();
        }
        var entry = new TextBlobCacheEntry(run, blob, bytes);
        _textBlobs.Add(run, entry);
        _textBlobLruNodes.Add(run, _textBlobLru.AddLast(entry));
        _textBlobBytes = checked(_textBlobBytes + bytes);
        return new(blob, false);
    }

    private void TouchTextBlob(ShapedRun run)
    {
        if (!_textBlobLruNodes.TryGetValue(run, out var node))
            return;
        _textBlobLru.Remove(node);
        _textBlobLru.AddLast(node);
    }

    private void ReleaseTextBlobs(ShapedText shaped)
    {
        foreach (var run in shaped.Runs)
            if (_textBlobs.Remove(run, out var entry))
            {
                if (_textBlobLruNodes.Remove(run, out var node))
                    _textBlobLru.Remove(node);
                _textBlobBytes -= entry.Bytes;
                entry.Blob.Dispose();
            }
    }

    private void DisposeTextBlobs()
    {
        foreach (var entry in _textBlobs.Values)
            entry.Blob.Dispose();
        _textBlobs.Clear();
        _textBlobLru.Clear();
        _textBlobLruNodes.Clear();
        _textBlobBytes = 0;
    }

    private static long EstimateTextBlobBytes(ShapedRun run) =>
        checked(128L + 2L * run.Identity.Length + 64L * run.Glyphs.Count);

    private static SKTypeface ResolveFace(
        TextMeasureRequest request,
        string text,
        out int collectionIndex
    )
    {
        var requested = SKTypeface.FromFamilyName(request.FontFamily);
        if (requested is not null && Covers(requested, request, text))
        {
            collectionIndex = CollectionIndex(requested);
            return requested;
        }
        requested?.Dispose();
        foreach (var rune in text.EnumerateRunes())
        {
            var fallback = SKFontManager.Default.MatchCharacter(rune.Value);
            if (fallback is null)
                continue;
            if (Covers(fallback, request, text))
            {
                collectionIndex = CollectionIndex(fallback);
                return fallback;
            }
            fallback.Dispose();
        }
        throw new InvalidOperationException("No font fallback covers the text element.");
    }

    private static bool Covers(SKTypeface face, TextMeasureRequest request, string text)
    {
        using var font = new SKFont(face, request.FontSize);
        using var shaper = new SKShaper(face);
        using var buffer = new HbBuffer();
        buffer.AddUtf16(text);
        buffer.GuessSegmentProperties();
        buffer.Direction =
            request.Direction == TextDirection.RightToLeft
                ? Direction.RightToLeft
                : Direction.LeftToRight;
        buffer.Language = new Language(request.Language);
        return shaper.Shape(buffer, font).Codepoints.All(glyph => glyph != 0);
    }

    private List<Piece> Itemize(TextMeasureRequest request) => Itemize(request, request.Text, 0);

    private List<Piece> Itemize(TextMeasureRequest request, string source, int sourceOffset)
    {
        var blocks = new List<DirectionalBlock>();
        var enumerator = StringInfo.GetTextElementEnumerator(source);
        TextDirection prior = request.Direction;
        while (enumerator.MoveNext())
        {
            var text = (string)enumerator.Current!;
            var offset = sourceOffset + enumerator.ElementIndex;
            var direction = DirectionOf(text, prior);
            prior = direction;
            var element = new TextElement(text, offset);
            if (blocks.LastOrDefault() is { } priorBlock && priorBlock.Direction == direction)
                priorBlock.Elements.Add(element);
            else
                blocks.Add(new DirectionalBlock(direction, [element]));
        }
        var result = new List<Piece>();
        if (request.Direction == TextDirection.RightToLeft)
            blocks.Reverse();
        foreach (var block in blocks)
        {
            var blockText = string.Concat(block.Elements.Select(item => item.Text));
            if (TryDescribeRequestedFace(request, blockText, out var blockFace))
            {
                result.Add(
                    new Piece(blockText, block.Elements[0].Offset, block.Direction, blockFace)
                );
                continue;
            }

            var faceRuns = new List<Piece>();
            foreach (var item in block.Elements)
            {
                using var face = ResolveFace(request, item.Text, out var collectionIndex);
                var descriptor =
                    FaceFingerprint(face, collectionIndex)
                    + ":"
                    + collectionIndex.ToString(CultureInfo.InvariantCulture);
                if (faceRuns.LastOrDefault() is { } priorRun && priorRun.Face == descriptor)
                    faceRuns[^1] = priorRun with { Text = priorRun.Text + item.Text };
                else
                    faceRuns.Add(new Piece(item.Text, item.Offset, block.Direction, descriptor));
            }
            result.AddRange(faceRuns);
        }
        return result;
    }

    private bool TryDescribeRequestedFace(
        TextMeasureRequest request,
        string text,
        out string descriptor
    )
    {
        using var face = SKTypeface.FromFamilyName(request.FontFamily);
        if (face is null || !Covers(face, request, text))
        {
            descriptor = string.Empty;
            return false;
        }

        var collectionIndex = CollectionIndex(face);
        descriptor =
            FaceFingerprint(face, collectionIndex)
            + ":"
            + collectionIndex.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static TextDirection DirectionOf(string element, TextDirection fallback)
    {
        foreach (var rune in element.EnumerateRunes())
            if (
                (
                    rune.Value
                    is >= 0x0590
                        and <= 0x08ff
                        or >= 0xfb1d
                        and <= 0xfdff
                        or >= 0xfe70
                        and <= 0xfeff
                ) && Rune.IsLetterOrDigit(rune)
            )
                return TextDirection.RightToLeft;
            else if (Rune.IsLetterOrDigit(rune))
                return TextDirection.LeftToRight;
        return fallback;
    }

    /// <summary>Releases the renderer's shape cache and face-fingerprint data.</summary>
    /// <remarks>Disposal is idempotent, must occur on the owner thread, and does not dispose Core scenes or caller-owned canvases.</remarks>
    /// <exception cref="InvalidOperationException">The call is made from a thread other than the one that created the renderer.</exception>
    public void Dispose()
    {
        CheckThread();
        if (_disposed)
            return;
        _disposed = true;
        _shapes.Clear();
        _shapeLru.Clear();
        _shapeLruNodes.Clear();
        _shapeBytes = 0;
        DisposeTextBlobs();
        _faceFingerprints.Clear();
    }

    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Renderer access must remain on its owner thread.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            ObjectDisposedException.ThrowIf(true, typeof(SkiaSceneRenderer));
    }

    private ShapedText Cache(TextMeasureRequest request, ShapedText shaped)
    {
        var bytes = EstimateShapeBytes(request, shaped);
        if (bytes > ShapeCacheByteBudget)
            return shaped;

        while (
            _shapeLru.First is not null
            && (_shapes.Count >= ShapeCacheCapacity || _shapeBytes + bytes > ShapeCacheByteBudget)
        )
        {
            var evicted = _shapeLru.First!;
            _shapeLru.RemoveFirst();
            _shapeLruNodes.Remove(evicted.Value.Request);
            if (_shapes.Remove(evicted.Value.Request, out var removed))
            {
                _shapeBytes -= evicted.Value.Bytes;
                ReleaseTextBlobs(removed);
            }
        }

        _shapes.Add(request, shaped);
        var node = _shapeLru.AddLast(new ShapeCacheEntry(request, bytes));
        _shapeLruNodes.Add(request, node);
        _shapeBytes = checked(_shapeBytes + bytes);
        return shaped;
    }

    private void TouchCache(TextMeasureRequest request)
    {
        if (!_shapeLruNodes.TryGetValue(request, out var node))
            return;
        _shapeLru.Remove(node);
        _shapeLru.AddLast(node);
    }

    private static long EstimateShapeBytes(TextMeasureRequest request, ShapedText shaped)
    {
        var bytes =
            128L
            + checked(2L * request.Text.Length)
            + checked(2L * request.FontFamily.Length)
            + checked(2L * request.Language.Length)
            + checked(2L * shaped.Identity.Length)
            + checked(64L * shaped.Lines.Count);
        // Core may materialize its grapheme geometry index lazily; reserve its documented
        // per-grapheme footprint so the retained-byte budget remains conservative.
        var graphemeCount = GraphemeBoundaries(request.Text, 0, request.Text.Length).Count;
        bytes = checked(bytes + 12L * graphemeCount);
        foreach (var run in shaped.Runs)
            bytes = checked(
                bytes
                + 256L
                + 2L * run.Identity.Length
                + 2L * run.Family.Length
                + 2L * run.Fingerprint.Length
                + 2L * run.SourceIdentity.Length
                + 2L * run.Language.Length
                + 48L * run.Glyphs.Count
            );
        return bytes;
    }

    private static float Checked(float value)
    {
        if (!float.IsFinite(value))
            throw new InvalidOperationException("Text metric overflow.");
        return value;
    }

    private static string Identity(TextMeasureRequest request, IEnumerable<ShapedRun> runs) =>
        Identity(request, runs, [], false);

    private static string Identity(
        TextMeasureRequest request,
        IEnumerable<ShapedRun> runs,
        IEnumerable<ParagraphLine> lines,
        bool didOverflow
    ) =>
        Hash(
            Hash(request.Text)
                + "\n"
                + request.FontFamily
                + "\n"
                + request.FontSize.ToString("R", CultureInfo.InvariantCulture)
                + "\n"
                + request.Language
                + "\n"
                + request.Direction
                + "\n"
                + request.Scale.ToString("R", CultureInfo.InvariantCulture)
                + "\n"
                + (
                    request.InlineConstraint.Limit?.ToString("R", CultureInfo.InvariantCulture)
                    ?? "unbounded"
                )
                + "\n"
                + (
                    request.BlockConstraint.Limit?.ToString("R", CultureInfo.InvariantCulture)
                    ?? "unbounded"
                )
                + "\n"
                + request.Wrap
                + "\n"
                + (request.MaxLines?.ToString(CultureInfo.InvariantCulture) ?? "unlimited")
                + "\n"
                + request.Overflow
                + "\n"
                + didOverflow
                + "\n"
                + string.Join('|', runs.Select(run => run.Identity))
                + "\n"
                + string.Join(
                    '|',
                    lines.Select(line =>
                        line.Utf16Start
                        + ":"
                        + line.Utf16Length
                        + ":"
                        + line.Top.ToString("R", CultureInfo.InvariantCulture)
                        + ":"
                        + line.Baseline.ToString("R", CultureInfo.InvariantCulture)
                        + ":"
                        + line.Advance.ToString("R", CultureInfo.InvariantCulture)
                        + ":"
                        + line.HardBreak
                    )
                )
        );

    private static string RunIdentity(
        TextMeasureRequest request,
        string family,
        SKFontStyle style,
        string fingerprint,
        int collectionIndex,
        TextDirection direction,
        IEnumerable<ShapedGlyph> glyphs,
        float origin,
        float baseline,
        float ascent,
        float descent
    ) =>
        Hash(
            family
                + "\n"
                + style.Weight
                + "\n"
                + style.Width
                + "\n"
                + style.Slant
                + "\n"
                + fingerprint
                + "\n"
                + collectionIndex.ToString(CultureInfo.InvariantCulture)
                + "\n"
                + request.FontSize.ToString("R", CultureInfo.InvariantCulture)
                + "\n"
                + request.Language
                + "\n"
                + direction
                + "\n"
                + origin.ToString("R", CultureInfo.InvariantCulture)
                + ":"
                + baseline.ToString("R", CultureInfo.InvariantCulture)
                + ":"
                + ascent.ToString("R", CultureInfo.InvariantCulture)
                + ":"
                + descent.ToString("R", CultureInfo.InvariantCulture)
                + "\n"
                + string.Join(
                    ';',
                    glyphs.Select(glyph =>
                        glyph.GlyphId
                        + ":"
                        + glyph.Cluster
                        + ":"
                        + glyph.X.ToString("R", CultureInfo.InvariantCulture)
                        + ":"
                        + glyph.Y.ToString("R", CultureInfo.InvariantCulture)
                        + ":"
                        + glyph.XAdvance.ToString("R", CultureInfo.InvariantCulture)
                        + ":"
                        + glyph.XOffset.ToString("R", CultureInfo.InvariantCulture)
                        + ":"
                        + glyph.YOffset.ToString("R", CultureInfo.InvariantCulture)
                    )
                )
        );

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static SKRect Rect(LayoutRect value) =>
        new(value.X, value.Y, value.X + value.Width, value.Y + value.Height);

    private static SKPaint Paint(Brush brush, LayoutRect bounds)
    {
        if (brush.Color is { } color)
            return new SKPaint { Color = Color(color), IsAntialias = false };
        var gradient = brush.Gradient!;
        var start = new SKPoint(
            bounds.X + gradient.Start.X * bounds.Width,
            bounds.Y + gradient.Start.Y * bounds.Height
        );
        var end = new SKPoint(
            bounds.X + gradient.End.X * bounds.Width,
            bounds.Y + gradient.End.Y * bounds.Height
        );
        using var shader = SKShader.CreateLinearGradient(
            start,
            end,
            gradient.Stops.Select(stop => Color(stop.Color)).ToArray(),
            gradient.Stops.Select(stop => stop.Position).ToArray(),
            SKShaderTileMode.Clamp
        );
        return new SKPaint { Shader = shader, IsAntialias = false };
    }

    private static SKColor Color(global::Lucent.Core.Color value) =>
        new(value.R, value.G, value.B, value.A);

    private static int CollectionIndex(SKTypeface face)
    {
        using var stream =
            face.OpenStream(out var index)
            ?? throw new InvalidOperationException(
                "Typeface OpenStream did not expose a collection identity."
            );
        if (index < 0)
            throw new InvalidOperationException(
                "Typeface OpenStream returned an invalid collection index."
            );
        return index;
    }

    private string FaceFingerprint(SKTypeface face, int collectionIndex)
    {
        var style = face.FontStyle;
        var key = new FaceKey(
            face.FamilyName,
            style.Weight,
            style.Width,
            (int)style.Slant,
            collectionIndex
        );
        if (_faceFingerprints.TryGetValue(key, out var fingerprint))
            return fingerprint;
        using var stream =
            face.OpenStream(out var actualIndex)
            ?? throw new InvalidOperationException(
                "Typeface OpenStream did not expose bytes for fingerprint verification."
            );
        if (actualIndex != collectionIndex)
            throw new InvalidOperationException("Typeface collection index changed.");
        using var data = SKData.Create(stream);
        if (data is null || data.Size == 0)
            throw new InvalidOperationException(
                "Typeface OpenStream produced no fingerprintable bytes."
            );
        fingerprint = Hash(Convert.ToHexString(data.ToArray()));
        _faceFingerprints.Add(key, fingerprint);
        return fingerprint;
    }

    private sealed record TextElement(string Text, int Offset);

    private sealed class DirectionalBlock(TextDirection direction, List<TextElement> elements)
    {
        public TextDirection Direction { get; } = direction;
        public List<TextElement> Elements { get; } = elements;
    }

    private sealed record Piece(string Text, int Utf16Offset, TextDirection Direction, string Face);

    private sealed record PendingRun(
        string Family,
        SKFontStyle Style,
        string Fingerprint,
        int CollectionIndex,
        string SourceIdentity,
        TextDirection Direction,
        float Width,
        float Ascent,
        float Descent,
        float Leading,
        ShapedGlyph[] Glyphs
    );

    private sealed record LineDraft(
        int Start,
        int Length,
        bool HardBreak,
        IReadOnlyList<PendingRun> Pending,
        float Width,
        float Ascent,
        float Descent,
        float Leading,
        float Height,
        float TrailingWhitespaceAdvance
    );

    private readonly record struct ShapeCacheEntry(TextMeasureRequest Request, long Bytes);

    private sealed record TextBlobCacheEntry(ShapedRun Run, SKTextBlob Blob, long Bytes);

    private readonly struct TextBlobLease(SKTextBlob blob, bool dispose) : IDisposable
    {
        public SKTextBlob Blob { get; } = blob;

        public void Dispose()
        {
            if (dispose)
                Blob.Dispose();
        }
    }

    private sealed class ShapedRunReferenceComparer : IEqualityComparer<ShapedRun>
    {
        public static ShapedRunReferenceComparer Instance { get; } = new();

        public bool Equals(ShapedRun? x, ShapedRun? y) => ReferenceEquals(x, y);

        public int GetHashCode(ShapedRun obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private readonly record struct FaceKey(
        string Family,
        int Weight,
        int Width,
        int Slant,
        int CollectionIndex
    );
}
