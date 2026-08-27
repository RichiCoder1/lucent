using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HbBuffer = HarfBuzzSharp.Buffer;
using HarfBuzzSharp;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

internal enum FlexDirection { Row, Column }
internal enum FlexAlign { Start, Center, End, Stretch, SpaceBetween, SpaceAround }
internal readonly record struct FlexEdges(float Left, float Top, float Right, float Bottom)
{ public static readonly FlexEdges Zero = new(0, 0, 0, 0); public float Main(FlexDirection d) => d == FlexDirection.Row ? Left + Right : Top + Bottom; public float Cross(FlexDirection d) => d == FlexDirection.Row ? Top + Bottom : Left + Right; }
internal sealed record FlexSize(float? Fixed = null, float Min = 0, float Max = float.PositiveInfinity);
internal sealed record FlexItem(string Id, FlexSize Main, FlexSize Cross, float Grow = 0, float Shrink = 1, ShapedRun? Text = null);
internal sealed record FlexContainer(FlexDirection Direction, float AvailableMain, float AvailableCross, float Gap, FlexEdges Padding, FlexAlign MainAlign, FlexAlign CrossAlign, float Scale);
internal sealed record FlexBox(string Id, float X, float Y, float Width, float Height, ShapedRun? Text);

/// <summary>Finite flex: clamped bases freeze at min/max; remaining free space redistributes to eligible siblings for at most child-count + 1 passes.</summary>
internal static class BoundedFlex
{
    public static FlexBox[] Layout(FlexContainer c, IReadOnlyList<FlexItem> items)
    {
        Check(c, items); var mainAvailable = Finite(c.AvailableMain - c.Padding.Main(c.Direction)); var crossAvailable = Finite(c.AvailableCross - c.Padding.Cross(c.Direction));
        if (mainAvailable < 0 || crossAvailable < 0) throw new ArgumentException("Padding exceeds available size.");
        var bases = items.Select(i => Clamp(i.Main.Fixed ?? i.Text?.Width ?? i.Main.Min, i.Main)).ToArray(); var sizes = (float[])bases.Clone();
        var free = Finite(mainAvailable - sizes.Sum() - c.Gap * (items.Count - 1)); Distribute(items, bases, sizes, free);
        free = Finite(mainAvailable - sizes.Sum() - c.Gap * (items.Count - 1)); var (offset, gap) = Space(c.MainAlign, free, c.Gap, items.Count);
        var main = Finite((c.Direction == FlexDirection.Row ? c.Padding.Left : c.Padding.Top) + offset); var boxes = new FlexBox[items.Count];
        for (var n = 0; n < items.Count; n++)
        {
            var item = items[n]; var itemCross = Clamp(item.Cross.Fixed ?? (c.CrossAlign == FlexAlign.Stretch ? crossAvailable : item.Cross.Min), item.Cross);
            var crossOffset = c.CrossAlign switch { FlexAlign.Center => (crossAvailable - itemCross) / 2, FlexAlign.End => crossAvailable - itemCross, _ => 0 };
            var x = c.Direction == FlexDirection.Row ? main : c.Padding.Left + crossOffset; var y = c.Direction == FlexDirection.Row ? c.Padding.Top + crossOffset : main;
            var width = c.Direction == FlexDirection.Row ? sizes[n] : itemCross; var height = c.Direction == FlexDirection.Row ? itemCross : sizes[n];
            var left = Round(Finite(x), c.Scale); var top = Round(Finite(y), c.Scale); var right = Round(Finite(x + width), c.Scale); var bottom = Round(Finite(y + height), c.Scale);
            boxes[n] = new(item.Id, left, top, Finite(right - left), Finite(bottom - top), item.Text); main = Finite(main + sizes[n] + gap);
        }
        return boxes;
    }
    private static void Distribute(IReadOnlyList<FlexItem> items, float[] bases, float[] sizes, float free)
    {
        var grow = free > 0; var target = Finite(sizes.Sum() + free); var frozen = new bool[items.Count];
        for (var pass = 0; pass <= items.Count; pass++)
        {
            if (MathF.Abs(free) < 0.0001f) return;
            var weights = items.Select((i, n) => frozen[n] ? 0 : (grow ? i.Grow : i.Shrink * bases[n])).ToArray(); var total = Finite(weights.Sum());
            if (total <= 0) return;
            var froze = false;
            for (var n = 0; n < sizes.Length; n++) if (!frozen[n])
            {
                var proposed = Finite(sizes[n] + free * weights[n] / total); var clamped = Clamp(proposed, items[n].Main);
                if (clamped != proposed) { frozen[n] = true; froze = true; } sizes[n] = clamped;
            }
            free = Finite(target - sizes.Sum());
            if (!froze) return;
        }
        throw new ArgumentException("Flex distribution did not converge.");
    }
    private static (float, float) Space(FlexAlign a, float free, float gap, int count) => a switch { FlexAlign.Center => (free / 2, gap), FlexAlign.End => (free, gap), FlexAlign.SpaceBetween when count > 1 && free > 0 => (0, gap + free / (count - 1)), FlexAlign.SpaceAround when free > 0 => (free / count / 2, gap + free / count), _ => (0, gap) };
    private static float Clamp(float v, FlexSize s) => Math.Clamp(Finite(v), s.Min, s.Max);
    private static float Finite(float v) => float.IsFinite(v) ? v : throw new ArgumentException("Nonfinite flex arithmetic.");
    private static float Round(float v, float scale) { var scaled = Finite(Finite(v) * Finite(scale)); return Finite(MathF.Round(scaled, MidpointRounding.AwayFromZero) / scale); }
    private static void Check(FlexContainer c, IReadOnlyList<FlexItem> items)
    {
        static bool Size(FlexSize s) => (s.Fixed is null || float.IsFinite(s.Fixed.Value) && s.Fixed >= 0) && float.IsFinite(s.Min) && s.Min >= 0 && (float.IsPositiveInfinity(s.Max) || float.IsFinite(s.Max) && s.Max >= s.Min);
        if (!float.IsFinite(c.AvailableMain) || c.AvailableMain < 0 || !float.IsFinite(c.AvailableCross) || c.AvailableCross < 0 || !float.IsFinite(c.Gap) || c.Gap < 0 || !float.IsFinite(c.Scale) || c.Scale <= 0 || new[] { c.Padding.Left, c.Padding.Top, c.Padding.Right, c.Padding.Bottom }.Any(v => !float.IsFinite(v) || v < 0) || items.Count is 0 or > 32 || items.Any(i => string.IsNullOrWhiteSpace(i.Id) || !Size(i.Main) || !Size(i.Cross) || !float.IsFinite(i.Grow) || i.Grow < 0 || !float.IsFinite(i.Shrink) || i.Shrink < 0) || items.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != items.Count) throw new ArgumentException("Invalid flex input.");
    }
}

internal sealed class FaceCache : IDisposable
{
    private readonly Dictionary<string, SKTypeface> _faces = new(StringComparer.Ordinal);
    public SKTypeface Get(string family, int character) => _faces.TryGetValue(family, out var face) ? face : _faces[family] = (family.StartsWith("Missing ", StringComparison.Ordinal) ? SKFontManager.Default.MatchCharacter(character) : SKTypeface.FromFamilyName(family)) ?? SKFontManager.Default.MatchCharacter(character) ?? throw new InvalidOperationException("No covering typeface.");
    public void Dispose() { foreach (var face in _faces.Values) face.Dispose(); }
}
internal sealed class ShapedRun : IDisposable
{
    private readonly SKFont _font; private readonly SKShaper _shaper;
    public ShapedRun(string name, string text, string requested, SKTypeface face, Direction direction, Script script, string language)
    {
        Name = name; Text = text; RequestedFamily = requested; Face = face; Direction = direction; Script = script; Language = language; _font = new(face, 20); _shaper = new(face);
        using var buffer = new HbBuffer(); buffer.AddUtf16(text); buffer.Direction = direction; buffer.Script = script; buffer.Language = new Language(language); Result = _shaper.Shape(buffer, _font);
        GlyphIds = Result.Codepoints; Clusters = Result.Clusters; Points = Result.Points; if (GlyphIds.Length == 0 || GlyphIds.Any(id => id == 0) || !float.IsFinite(Result.Width) || Result.Width <= 0) throw new InvalidOperationException($"Unsupported shaped run: {name}.");
        using var builder = new SKTextBlobBuilder(); builder.AddPositionedRun(GlyphIds.Select(id => checked((ushort)id)).ToArray(), _font, Points); Blob = builder.Build() ?? throw new InvalidOperationException("Text blob build failed.");
        Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',', GlyphIds) + "/" + string.Join(',', Clusters) + "/" + string.Join(',', Points.Select(p => $"{p.X:R}:{p.Y:R}")) ))).ToLowerInvariant();
    }
    public string Name { get; } public string Text { get; } public string RequestedFamily { get; } public SKTypeface Face { get; } public Direction Direction { get; } public Script Script { get; } public string Language { get; } public SKShaper.Result Result { get; } public uint[] GlyphIds { get; } public uint[] Clusters { get; } public SKPoint[] Points { get; } public SKTextBlob Blob { get; } public string Hash { get; }
    public float Width => Result.Width; public SKRect Bounds => Blob.Bounds; public string FaceIdentity => $"{Face.FamilyName}:{Face.FontStyle.Weight}:{Face.FontStyle.Width}:{Face.FontStyle.Slant}";
    public void Draw(SKCanvas canvas, float x, float baseline, SKPaint paint) => canvas.DrawText(Blob, x, baseline, paint);
    public void Dispose() { Blob.Dispose(); _shaper.Dispose(); _font.Dispose(); }
}

internal sealed record LayoutCheckResult(bool Ok, string ArtifactDirectory, ArtifactHash[] Artifacts, bool FlexChecks, bool ShapeChecks, bool CultureChecks, bool GlyphsRendered, string SkiaPackage, string ShaperPackage, string HarfBuzzPackage);
internal static class LayoutProbe
{
    public static LayoutCheckResult Run(string directory)
    {
        Directory.CreateDirectory(directory); using var faces = new FaceCache(); using var runs = new RunOwner();
        var shaped = new[] {
            runs.Add(Shape("latin-ligature", "ffi", "Calibri", 'f', Direction.LeftToRight, Script.Latin, "en", faces)),
            runs.Add(Shape("combining", "q\u0307", "Segoe UI", 'q', Direction.LeftToRight, Script.Latin, "en", faces)),
            runs.Add(Shape("rtl-arabic", "العَرَبِيَّة", "Segoe UI", 'ا', Direction.RightToLeft, Script.Arabic, "ar", faces)),
            runs.Add(Shape("fallback-cjk", "漢字", "Missing Lucent Font", '漢', Direction.LeftToRight, Script.Han, "ja", faces)),
            runs.Add(Shape("emoji-surrogate", "😀", "Segoe UI Emoji", 0x1F600, Direction.LeftToRight, Script.Common, "en", faces)),
            runs.Add(Shape("missing-font", "fallback", "Missing Lucent Font", 'f', Direction.LeftToRight, Script.Latin, "en", faces)) };
        var mixed = new[] { runs.Add(Shape("mixed-latin", "Latin", "Segoe UI", 'L', Direction.LeftToRight, Script.Latin, "en", faces)), runs.Add(Shape("mixed-cjk", "漢", "Missing Lucent Font", '漢', Direction.LeftToRight, Script.Han, "ja", faces)), runs.Add(Shape("mixed-arabic", "العربية", "Segoe UI", 'ا', Direction.RightToLeft, Script.Arabic, "ar", faces)) };
        var flex = VerifyFlex(); var shape = VerifyShapes(shaped, mixed, faces); var cultures = VerifyCultures(faces);
        var boxes = BoundedFlex.Layout(new(FlexDirection.Row, 240, 80, 6, new(8, 8, 8, 8), FlexAlign.SpaceBetween, FlexAlign.Center, 1.25f), shaped.Concat(mixed).Select((run, n) => new FlexItem($"text.{n}", new(Max: 120), new(Min: 20), Text: run)).ToArray());
        var pixels = Render(boxes); var glyphsRendered = pixels.Painted;
        File.WriteAllText(Path.Combine(directory, "layout-text.json"), Dump(shaped.Concat(mixed), boxes, cultures), new UTF8Encoding(false)); File.WriteAllBytes(Path.Combine(directory, "layout-text.png"), pixels.Png);
        var artifacts = Directory.GetFiles(directory).OrderBy(Path.GetFileName, StringComparer.Ordinal).Select(path => new ArtifactHash(Path.GetFileName(path), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant())).ToArray();
        var ok = flex && shape && cultures.Ok && glyphsRendered && artifacts.Length == 2;
        if (!ok) throw new InvalidOperationException("Layout proof failed.");
        return new(ok, Path.GetFullPath(directory), artifacts, flex, shape, cultures.Ok, glyphsRendered, "4.151.1", "4.151.1", "14.2.1.1");
    }
    private static ShapedRun Shape(string name, string text, string family, int character, Direction direction, Script script, string language, FaceCache faces) => new(name, text, family, faces.Get(family, character), direction, script, language);
    private static bool VerifyShapes(ShapedRun[] runs, ShapedRun[] mixed, FaceCache faces)
    {
        var ligature = runs[0]; var combining = runs[1]; var rtl = runs[2]; var cjk = runs[3]; var emoji = runs[4]; var missing = runs[5];
        using var negativeFace = SKTypeface.FromFamilyName("Calibri") ?? throw new InvalidOperationException("Negative-control face unavailable."); using var negative = new SKFont(negativeFace, 20); using var shaper = new SKShaper(negativeFace); using var buffer = new HbBuffer(); buffer.AddUtf16("漢"); buffer.Direction = Direction.LeftToRight; buffer.Script = Script.Han; buffer.Language = new Language("ja"); var tofu = shaper.Shape(buffer, negative).Codepoints.Any(id => id == 0);
        var ligatureOk = ligature.GlyphIds.Length < ligature.Text.EnumerateRunes().Count() && ligature.Clusters is [0];
        var combiningOk = combining.Clusters.Distinct().Count() < combining.Clusters.Length || combining.GlyphIds.Length < combining.Text.EnumerateRunes().Count();
        var rtlOk = rtl.Direction == Direction.RightToLeft && rtl.Clusters.First() > rtl.Clusters.Last();
        var emojiOk = emoji.Text.Length == 2 && emoji.GlyphIds.Length == 1 && emoji.Clusters is [0];
        var facesOk = cjk.GlyphIds.All(id => id != 0) && mixed.All(run => run.GlyphIds.All(id => id != 0)) && missing.Face.FamilyName != missing.RequestedFamily;
        var blobsOk = runs.Concat(mixed).All(run => !run.Blob.Bounds.IsEmpty && run.Width > 0 && run.Hash.Length == 64);
        if (!tofu || !ligatureOk || !combiningOk || !rtlOk || !emojiOk || !facesOk || !blobsOk) throw new InvalidOperationException("Shaping fixture assertion failed.");
        return true;
    }
    private static bool VerifyFlex()
    {
        var grow = BoundedFlex.Layout(new(FlexDirection.Row, 100, 20, 0, FlexEdges.Zero, FlexAlign.Start, FlexAlign.Stretch, 1), [new("a", new(Fixed: 20, Max: 30), new(), Grow: 1), new("b", new(Fixed: 20), new(), Grow: 1)]);
        var shrink = BoundedFlex.Layout(new(FlexDirection.Column, 50, 20, 0, FlexEdges.Zero, FlexAlign.End, FlexAlign.Stretch, 2), [new("a", new(Fixed: 40, Min: 30), new()), new("b", new(Fixed: 40), new())]);
        var align = BoundedFlex.Layout(new(FlexDirection.Row, 50, 20, 2, new(3, 4, 3, 4), FlexAlign.SpaceBetween, FlexAlign.Center, 1.25f), [new("a", new(Fixed: 10), new(Fixed: 4)), new("b", new(Fixed: 10), new(Fixed: 4))]);
        var around = BoundedFlex.Layout(new(FlexDirection.Row, 50, 10, 0, FlexEdges.Zero, FlexAlign.SpaceAround, FlexAlign.End, 1), [new("a", new(Fixed: 10), new(Fixed: 2)), new("b", new(Fixed: 10), new(Fixed: 2))]);
        var half = BoundedFlex.Layout(new(FlexDirection.Row, 1, 1, 0, FlexEdges.Zero, FlexAlign.Start, FlexAlign.Stretch, 1), [new("a", new(Fixed: .5f), new()), new("b", new(Fixed: .5f), new())]);
        var halfAt2x = BoundedFlex.Layout(new(FlexDirection.Row, 1, 1, 0, FlexEdges.Zero, FlexAlign.Start, FlexAlign.Stretch, 2), [new("a", new(Fixed: .25f), new()), new("b", new(Fixed: .75f), new())]);
        var start = BoundedFlex.Layout(new(FlexDirection.Row, 10, 8, 0, FlexEdges.Zero, FlexAlign.Start, FlexAlign.Stretch, 1), [new("a", new(Fixed: 2), new())]);
        var center = BoundedFlex.Layout(new(FlexDirection.Row, 10, 8, 0, FlexEdges.Zero, FlexAlign.Center, FlexAlign.Start, 1), [new("a", new(Fixed: 2), new(Fixed: 2))]);
        var end = BoundedFlex.Layout(new(FlexDirection.Row, 10, 8, 0, FlexEdges.Zero, FlexAlign.End, FlexAlign.End, 1), [new("a", new(Fixed: 2), new(Fixed: 2))]);
        var column = BoundedFlex.Layout(new(FlexDirection.Column, 10, 8, 0, FlexEdges.Zero, FlexAlign.Start, FlexAlign.End, 1), [new("a", new(Fixed: 2), new(Fixed: 3))]);
        var paddedGap = BoundedFlex.Layout(new(FlexDirection.Row, 20, 8, 3, new(2, 1, 2, 1), FlexAlign.Start, FlexAlign.Start, 1), [new("a", new(Fixed: 4), new(Fixed: 2)), new("b", new(Fixed: 4), new(Fixed: 2))]);
        var totals = grow.Sum(box => box.Width) == 100 && shrink.Sum(box => box.Height) == 50;
        var values = grow[0].Width == 30 && grow[1].Width == 70 && shrink[0].Height == 30 && shrink[1].Height == 20 &&
            align[0].X == 3.2f && align[1].X == 36.8f && align[0].Y == 8 && around[0].X == 8 && around[1].X == 33 && around[0].Y == 8 &&
            half.Sum(box => box.Width) == 1 && half[0].X == 0 && half[1].X == 1 && half.All(box => box.Width >= 0) &&
            halfAt2x.Sum(box => box.Width) == 1 && halfAt2x[0].Width == .5f && halfAt2x[1].Width == .5f &&
            start[0] is { X: 0, Y: 0, Width: 2, Height: 8 } && center[0] is { X: 4, Y: 0 } && end[0] is { X: 8, Y: 6 } &&
            column[0] is { X: 5, Y: 0, Width: 3, Height: 2 } && paddedGap[0] is { X: 2, Y: 1 } && paddedGap[1].X == 9;
        static void Reject(Action action)
        {
            try { action(); }
            catch (ArgumentException) { return; }
            throw new InvalidOperationException("Invalid flex input was accepted.");
        }
        Reject(() => BoundedFlex.Layout(new(FlexDirection.Row, float.NaN, 1, 0, FlexEdges.Zero, FlexAlign.Start, FlexAlign.Start, 1), [new("a", new(), new())]));
        Reject(() => BoundedFlex.Layout(new(FlexDirection.Row, 1, 1, float.PositiveInfinity, FlexEdges.Zero, FlexAlign.Start, FlexAlign.Start, 1), [new("a", new(), new())]));
        Reject(() => BoundedFlex.Layout(new(FlexDirection.Row, 1, 1, 0, new(-1, 0, 0, 0), FlexAlign.Start, FlexAlign.Start, 1), [new("a", new(), new())]));
        Reject(() => BoundedFlex.Layout(new(FlexDirection.Row, 1, 1, 0, FlexEdges.Zero, FlexAlign.Start, FlexAlign.Start, 1), [new("", new(), new())]));
        Reject(() => BoundedFlex.Layout(new(FlexDirection.Row, 1, 1, 0, FlexEdges.Zero, FlexAlign.Start, FlexAlign.Start, 1), [new("a", new(), new()), new("a", new(), new())]));
        Reject(() => BoundedFlex.Layout(new(FlexDirection.Row, 1, 1, 0, FlexEdges.Zero, FlexAlign.Start, FlexAlign.Start, 1), Enumerable.Range(0, 33).Select(i => new FlexItem(i.ToString(CultureInfo.InvariantCulture), new(), new())).ToArray()));
        Reject(() => BoundedFlex.Layout(new(FlexDirection.Row, 1, 1, 0, FlexEdges.Zero, FlexAlign.Start, FlexAlign.Start, 1), [new("a", new(Fixed: float.NaN), new(), Grow: float.PositiveInfinity)]));
        if (!totals || !values) throw new InvalidOperationException("Flex assertion failed."); return true;
    }
    private static CultureEvidence VerifyCultures(FaceCache faces)
    {
        var old = CultureInfo.CurrentCulture; var oldUi = CultureInfo.CurrentUICulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US"); using var en = Shape("culture-en", "ffi", "Calibri", 'f', Direction.LeftToRight, Script.Latin, "en", faces); CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR"); CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR"); using var tr = Shape("culture-tr", "ffi", "Calibri", 'f', Direction.LeftToRight, Script.Latin, "en", faces); var same = en.Hash == tr.Hash && en.Width == tr.Width; CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US"); CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US"); using var languageTr = Shape("language-tr", "ffi", "Calibri", 'f', Direction.LeftToRight, Script.Latin, "tr", faces); return new(same, same, "en-US", "tr-TR", en.Hash, tr.Hash, en.Width, tr.Width, "en", "tr", languageTr.Hash, languageTr.Width); } finally { CultureInfo.CurrentCulture = old; CultureInfo.CurrentUICulture = oldUi; }
    }
    private static (byte[] Png, bool Painted) Render(IEnumerable<FlexBox> boxes)
    {
        var scene = new RetainedScene(); var expected = new List<Bounds>(); foreach (var box in boxes) if (box.Text is not null) { var b = new Bounds((int)box.X, (int)box.Y, (int)box.Width, (int)box.Height); scene.UpsertText(new(box.Id), b, "#ffffff", box.Text); expected.Add(b); }
        using var bitmap = new SKBitmap(240, 80, SKColorType.Rgba8888, SKAlphaType.Premul); using (var canvas = new SKCanvas(bitmap)) { canvas.Clear(new SKColor(9, 12, 20)); new SkiaSceneRenderer().Render(scene, canvas); }
        var painted = expected.All(b => Enumerable.Range(Math.Max(0, b.Y), Math.Min(bitmap.Height, b.Y + b.Height) - Math.Max(0, b.Y)).Any(y => Enumerable.Range(Math.Max(0, b.X), Math.Min(bitmap.Width, b.X + b.Width) - Math.Max(0, b.X)).Any(x => bitmap.GetPixel(x, y) != new SKColor(9, 12, 20))));
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); return (data.ToArray(), painted);
    }
    private static string Dump(IEnumerable<ShapedRun> runs, IEnumerable<FlexBox> boxes, CultureEvidence culture) => Json("{\"packages\":{\"skiaSharp\":\"4.151.1\",\"skiaSharpHarfBuzz\":\"4.151.1\",\"harfBuzzSharp\":\"14.2.1.1\"},\"assemblies\":{\"skia\":\"" + AssemblyIdentity(typeof(SKBitmap)) + "\",\"shaper\":\"" + AssemblyIdentity(typeof(SKShaper)) + "\",\"harfBuzz\":\"" + AssemblyIdentity(typeof(HbBuffer)) + "\"},\"scales\":[1,1.25,2],\"culture\":{\"same\":" + culture.Same.ToString().ToLowerInvariant() + ",\"names\":[\"" + culture.EnCulture + "\",\"" + culture.TrCulture + "\"],\"hashes\":[\"" + culture.EnHash + "\",\"" + culture.TrHash + "\"],\"widths\":[" + culture.EnWidth.ToString("R", CultureInfo.InvariantCulture) + "," + culture.TrWidth.ToString("R", CultureInfo.InvariantCulture) + "],\"languageVariation\":{\"fixedCulture\":\"en-US\",\"languages\":[\"" + culture.EnLanguage + "\",\"" + culture.TrLanguage + "\"],\"hash\":\"" + culture.LanguageTrHash + "\",\"width\":" + culture.LanguageTrWidth.ToString("R", CultureInfo.InvariantCulture) + "}},\"runs\":[" + string.Join(',', runs.OrderBy(r => r.Name, StringComparer.Ordinal).Select(r => "{\"name\":\"" + r.Name + "\",\"face\":\"" + r.FaceIdentity + "\",\"direction\":\"" + r.Direction + "\",\"script\":\"" + r.Script + "\",\"language\":\"" + r.Language + "\",\"glyphIds\":[" + string.Join(',', r.GlyphIds) + "],\"clusters\":[" + string.Join(',', r.Clusters) + "],\"points\":[" + string.Join(',', r.Points.Select(p => "[" + p.X.ToString("R", CultureInfo.InvariantCulture) + "," + p.Y.ToString("R", CultureInfo.InvariantCulture) + "]")) + "],\"width\":" + r.Width.ToString("R", CultureInfo.InvariantCulture) + ",\"blobBounds\":[" + r.Bounds.Left.ToString("R", CultureInfo.InvariantCulture) + "," + r.Bounds.Top.ToString("R", CultureInfo.InvariantCulture) + "," + r.Bounds.Right.ToString("R", CultureInfo.InvariantCulture) + "," + r.Bounds.Bottom.ToString("R", CultureInfo.InvariantCulture) + "],\"hash\":\"" + r.Hash + "\"}")) + "],\"boxes\":[" + string.Join(',', boxes.OrderBy(b => b.Id, StringComparer.Ordinal).Select(b => "{\"id\":\"" + b.Id + "\",\"bounds\":[" + b.X.ToString("R", CultureInfo.InvariantCulture) + "," + b.Y.ToString("R", CultureInfo.InvariantCulture) + "," + b.Width.ToString("R", CultureInfo.InvariantCulture) + "," + b.Height.ToString("R", CultureInfo.InvariantCulture) + "],\"shapeHash\":\"" + b.Text?.Hash + "\"}")) + "]}");
    private static string AssemblyIdentity(Type type) { var name = type.Assembly.GetName(); return $"{name.Name}:{name.Version}"; }
    private static string Json(string value) => value + "\n";
    private sealed class RunOwner : IDisposable { private readonly List<ShapedRun> _runs = []; public ShapedRun Add(ShapedRun run) { _runs.Add(run); return run; } public void Dispose() { foreach (var run in _runs) run.Dispose(); } }
    private sealed record CultureEvidence(bool Ok, bool Same, string EnCulture, string TrCulture, string EnHash, string TrHash, float EnWidth, float TrWidth, string EnLanguage, string TrLanguage, string LanguageTrHash, float LanguageTrWidth);
}
