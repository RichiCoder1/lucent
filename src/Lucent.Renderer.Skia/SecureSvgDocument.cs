using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Lucent.Core;
using SkiaSharp;

namespace Lucent.Renderer.Skia;

// This is a finite adapter language, not a browser sanitizer. Validate the exact hashed
// bytes before handing a normalized document to the third-party parser. Unknown rendering
// features fail closed; a library's partially rendered picture is not proof of support.
internal sealed record SecureSvgDocument(
    byte[] Bytes,
    float Width,
    float Height,
    long Cost,
    string Text
)
{
    internal const int MaximumBytes = 2 * 1024 * 1024;
    private const int MaximumElements = 4096;
    private const int MaximumExpandedElements = 16384;
    private static readonly HashSet<string> Elements = Words(
        "svg g defs title desc rect circle ellipse line polyline polygon path use symbol linearGradient radialGradient stop clipPath mask filter feGaussianBlur feOffset feColorMatrix feBlend feComposite feFlood feMerge feMergeNode text tspan textPath style image"
    );
    private static readonly HashSet<string> Attributes = Words(
        "id class version width height viewBox preserveAspectRatio x y x1 y1 x2 y2 dx dy cx cy r rx ry fx fy points d transform fill fill-rule fill-opacity stroke stroke-width stroke-linecap stroke-linejoin stroke-miterlimit stroke-dasharray stroke-dashoffset stroke-opacity opacity color display visibility clip-path clip-rule mask filter href gradientUnits gradientTransform spreadMethod offset stop-color stop-opacity maskUnits maskContentUnits clipPathUnits filterUnits primitiveUnits stdDeviation in in2 result type values mode operator k1 k2 k3 k4 flood-color flood-opacity font-family font-size font-weight font-style text-anchor dominant-baseline letter-spacing word-spacing textLength lengthAdjust startOffset method spacing style"
    );
    private static readonly HashSet<string> CssProperties = Words(
        "fill fill-rule fill-opacity stroke stroke-width stroke-linecap stroke-linejoin stroke-miterlimit stroke-dasharray stroke-dashoffset stroke-opacity opacity color display visibility clip-path clip-rule mask filter stop-color stop-opacity flood-color flood-opacity font-family font-size font-weight font-style text-anchor dominant-baseline letter-spacing word-spacing"
    );
    private static readonly Regex Url = new(
        @"url\(\s*['"" ]?(#[A-Za-z_][\w.:-]*)['"" ]?\s*\)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1)
    );
    private static readonly Regex Number = new(
        @"[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1)
    );
    private static readonly XNamespace SvgNamespace = "http://www.w3.org/2000/svg";

    internal static SecureSvgDocument Read(
        byte[] bytes,
        AssetImageMetadata metadata,
        bool hasFont,
        Func<long, IDisposable> reserve,
        CancellationToken token
    )
    {
        if (bytes.Length > MaximumBytes)
            throw Budget("SVG encoded input exceeds 2 MiB.");
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumBytes,
                MaxCharactersFromEntities = 0,
                IgnoreComments = true,
            };
            // Bound depth before XDocument can allocate a deep tree.
            using (var reader = XmlReader.Create(new MemoryStream(bytes, false), settings))
            {
                var count = 0;
                while (reader.Read())
                {
                    token.ThrowIfCancellationRequested();
                    if (
                        reader.Depth > 32
                        || reader.AttributeCount > 64
                        || (reader.NodeType == XmlNodeType.Element && ++count > MaximumElements)
                    )
                        throw Budget("SVG exceeds the XML depth, attribute or element limit.");
                    if (reader.NodeType == XmlNodeType.ProcessingInstruction)
                        throw Unsupported(
                            "SVG processing instructions and imported stylesheets are unsupported."
                        );
                }
            }
            using var input = XmlReader.Create(new MemoryStream(bytes, false), settings);
            var document = XDocument.Load(input);
            var root = document.Root;
            if (root?.Name == "svg")
                foreach (var element in root.DescendantsAndSelf())
                    if (element.Name.Namespace == XNamespace.None)
                        element.Name = SvgNamespace + element.Name.LocalName;
            if (root is null || root.Name != SvgNamespace + "svg")
                throw Invalid("Expected an SVG namespace root.");
            var nodes = root.DescendantsAndSelf().ToArray();
            var ids = new Dictionary<string, XElement>(StringComparer.Ordinal);
            var references = new Dictionary<XElement, List<string>>();
            var rasterPixels = new Dictionary<XElement, long>();
            var transforms = new Dictionary<XElement, Matrix3x2>();
            long pathCharacters = 0;
            long embeddedPixels = 0;
            var filters = 0;
            foreach (var node in nodes)
            {
                token.ThrowIfCancellationRequested();
                if (node.Name.Namespace != SvgNamespace || !Elements.Contains(node.Name.LocalName))
                    throw Unsupported(
                        $"SVG element '{node.Name}' is not in the Secure Static support matrix."
                    );
                if (node != root && node.Name.LocalName == "svg")
                    throw Unsupported("Nested SVG viewports are unsupported.");
                if (node.Attribute("id") is { } id && !ids.TryAdd(id.Value, node))
                    throw Invalid($"Duplicate SVG id '{id.Value}'.");
                if (
                    node.Name.LocalName.StartsWith("fe", StringComparison.Ordinal)
                    && ++filters > 32
                )
                    throw Budget("SVG filter primitive limit is 32.");
                if (node.Name.LocalName is "text" or "tspan" or "textPath" && !hasFont)
                    throw Unsupported(
                        "SVG text requires an explicitly supplied font in SkiaImagePreparer; use outlined artwork or configure the 'Lucent SVG' font."
                    );
                var refs = references[node] = [];
                var localTransform = ParseTransform(node.Attribute("transform")?.Value);
                var cumulativeTransform =
                    node.Parent is { } parent && transforms.TryGetValue(parent, out var inherited)
                        ? localTransform * inherited
                        : localTransform;
                CheckTransform(cumulativeTransform);
                transforms[node] = cumulativeTransform;
                if (node.Attribute("gradientTransform") is { } gradientTransform)
                    CheckTransform(ParseTransform(gradientTransform.Value));
                foreach (var attribute in node.Attributes())
                {
                    if (attribute.IsNamespaceDeclaration)
                        continue;
                    var name = attribute.Name.LocalName;
                    var value = attribute.Value;
                    if (attribute.Name == XNamespace.Xml + "space")
                        continue;
                    if (
                        attribute.Name.Namespace != XNamespace.None
                        && !(
                            name == "href"
                            && attribute.Name.NamespaceName == "http://www.w3.org/1999/xlink"
                        )
                    )
                        throw Unsupported(
                            $"SVG attribute namespace '{attribute.Name}' is unsupported."
                        );
                    if (!Attributes.Contains(name))
                        throw Unsupported($"SVG attribute '{name}' is unsupported.");
                    if (value.Length > 65536 && name != "href")
                        throw Budget("SVG attribute exceeds 64 KiB.");
                    if (name == "href")
                    {
                        if (value.StartsWith('#') && value.Length > 1)
                            refs.Add(value[1..]);
                        else if (node.Name.LocalName == "image")
                        {
                            var pixels = ValidateDataImage(value, reserve);
                            embeddedPixels += pixels;
                            rasterPixels[node] = pixels;
                        }
                        else
                            throw Unsupported(
                                "SVG references must identify an existing same-document id; file and network resources are prohibited."
                            );
                    }
                    else if (name == "style")
                        ValidateDeclarations(value, refs, hasFont);
                    else
                    {
                        ValidateValue(name, value, refs, hasFont);
                        if (name is "d" or "points")
                        {
                            pathCharacters += value.Length;
                            if (pathCharacters > 262144)
                                throw Budget("SVG path data exceeds 256 KiB.");
                            if (name == "d")
                            {
                                using var path = SKPath.ParseSvgPathData(value);
                                if (path is null)
                                    throw Invalid("SVG path data is malformed.");
                            }
                        }
                    }
                }
                if (
                    node.Name.LocalName == "image"
                    && !node.Attributes().Any(a => a.Name.LocalName == "href")
                )
                    throw Invalid("SVG image requires an embedded raster href.");
                if (node.Name.LocalName is "use" or "textPath" && refs.Count == 0)
                    throw Invalid("SVG use/textPath requires an existing reference.");
                if (node.Name.LocalName == "style")
                    ValidateStylesheet(node.Value, refs, hasFont);
                ValidateFeatureValues(node);
                if (embeddedPixels > 4 * 1024 * 1024)
                    throw Budget("SVG embedded raster area exceeds 4 megapixels.");
            }
            foreach (var (node, refs) in references)
            {
                foreach (var id in refs)
                    if (!ids.ContainsKey(id))
                        throw Invalid(
                            $"Required SVG reference '#{id}' on '{node.Name.LocalName}' is missing."
                        );
                ValidateReferenceTypes(node, ids);
            }
            // Follow both containment and references. This rejects recursive use/mask/gradient
            // expansion and caps repeated instantiation before the renderer allocates it.
            var active = new HashSet<XElement>();
            var expanded = 0;
            var expandedFilters = 0;
            long expandedPixels = 0;
            long expandedNodeCost = 0;
            void Visit(XElement node)
            {
                token.ThrowIfCancellationRequested();
                if (active.Count >= 64)
                    throw Budget("SVG reference nesting exceeds 64 levels.");
                if (++expanded > MaximumExpandedElements)
                    throw Budget("SVG reference expansion exceeds 16384 elements.");
                if (
                    node.Name.LocalName.StartsWith("fe", StringComparison.Ordinal)
                    && ++expandedFilters > 64
                )
                    throw Budget("SVG expanded filter primitive limit is 64.");
                expandedPixels += rasterPixels.GetValueOrDefault(node);
                expandedNodeCost +=
                    2048
                    + node.Attributes().Sum(a => a.Value.Length * 16L)
                    + node.Nodes().OfType<XText>().Sum(t => t.Value.Length * 16L);
                if (expandedPixels > 4 * 1024 * 1024)
                    throw Budget("SVG expanded raster area exceeds 4 megapixels.");
                if (!active.Add(node))
                    throw Invalid("SVG contains a cyclic rendering reference.");
                foreach (var child in node.Elements())
                    Visit(child);
                foreach (var id in references[node])
                    Visit(ids[id]);
                active.Remove(node);
            }
            Visit(root);

            var width = metadata.Width;
            var height = metadata.Height;
            if (width > 4096 || height > 4096)
                throw Budget("SVG intrinsic viewport exceeds 4096 units per axis.");
            ValidateRootSize(root, metadata);
            var filterExtent = ValidateFilterBounds(nodes, width, height, filters);
            // Resolve percentages from already-validated metadata, never from raster buckets.
            // Image layout resolves viewport-relative axes; vector drawing scales this logical viewport.
            root.SetAttributeValue("width", width.ToString(CultureInfo.InvariantCulture));
            root.SetAttributeValue("height", height.ToString(CultureInfo.InvariantCulture));
            if (root.Attribute("color") is null)
                root.SetAttributeValue("color", "black");
            var cost = checked(
                bytes.LongLength * 16
                + expandedNodeCost
                + expandedPixels * 16
                + (long)Math.Ceiling(filterExtent * filterExtent) * 16 * expandedFilters
                + 65536
            );
            var text = string.Concat(
                root.DescendantNodes()
                    .OfType<XText>()
                    .Where(t =>
                        t.Ancestors().Any(e => e.Name.LocalName is "text" or "tspan" or "textPath")
                    )
                    .Select(t => t.Value)
            );
            return new(
                Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting)),
                width,
                height,
                cost,
                text
            );
        }
        catch (ImageLoadException)
        {
            throw;
        }
        catch (Exception e)
            when (e
                    is XmlException
                        or FormatException
                        or OverflowException
                        or RegexMatchTimeoutException
            )
        {
            throw new ImageLoadException(
                ImageLoadFailureKind.InvalidData,
                "SVG structure or numeric data is invalid.",
                e
            );
        }
    }

    private static void ValidateFeatureValues(XElement node)
    {
        void OneOf(string attribute, params string[] allowed)
        {
            if (
                node.Attribute(attribute) is { } value
                && !allowed.Contains(value.Value, StringComparer.Ordinal)
            )
                throw Unsupported(
                    $"SVG {node.Name.LocalName}.{attribute} value '{value.Value}' is unsupported."
                );
        }
        foreach (
            var name in new[]
            {
                "gradientUnits",
                "clipPathUnits",
                "maskUnits",
                "maskContentUnits",
                "filterUnits",
                "primitiveUnits",
            }
        )
            OneOf(name, "userSpaceOnUse", "objectBoundingBox");
        OneOf("spreadMethod", "pad", "reflect", "repeat");
        OneOf("stroke-linecap", "butt", "round", "square");
        OneOf("stroke-linejoin", "miter", "round", "bevel");
        OneOf("fill-rule", "nonzero", "evenodd");
        OneOf("clip-rule", "nonzero", "evenodd");
        if (node.Name.LocalName == "feBlend")
            OneOf("mode", "normal", "multiply", "screen", "darken", "lighten");
        if (node.Name.LocalName == "feComposite")
            OneOf("operator", "over", "in", "out", "atop", "xor", "arithmetic");
        if (node.Name.LocalName == "feColorMatrix")
        {
            OneOf("type", "matrix", "saturate", "hueRotate", "luminanceToAlpha");
            if (node.Attribute("values") is { } values)
            {
                var expected = node.Attribute("type")?.Value switch
                {
                    "saturate" or "hueRotate" => 1,
                    "luminanceToAlpha" => 0,
                    _ => 20,
                };
                if (
                    Number.Count(values.Value) != expected
                    || Number.Replace(values.Value, "").Any(c => !char.IsWhiteSpace(c) && c != ',')
                )
                    throw Invalid("SVG color matrix has an invalid value count or syntax.");
            }
        }
    }

    private static Matrix3x2 ParseTransform(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Matrix3x2.Identity;
        var matrix = Matrix3x2.Identity;
        var rest = value.AsSpan().Trim();
        while (!rest.IsEmpty)
        {
            var open = rest.IndexOf('(');
            var close = rest.IndexOf(')');
            if (open <= 0 || close <= open)
                throw Invalid("Malformed SVG transform.");
            var name = rest[..open].Trim().ToString();
            var argumentText = rest[(open + 1)..close].ToString();
            if (Number.Replace(argumentText, "").Any(c => !char.IsWhiteSpace(c) && c != ','))
                throw Invalid("Malformed SVG transform arguments.");
            var args = Number
                .Matches(argumentText)
                .Select(m => float.Parse(m.Value, CultureInfo.InvariantCulture))
                .ToArray();
            if (args.Any(n => !float.IsFinite(n)))
                throw Invalid("SVG transform arguments must be finite.");
            var next = name switch
            {
                "matrix" when args.Length == 6 => new Matrix3x2(
                    args[0],
                    args[1],
                    args[2],
                    args[3],
                    args[4],
                    args[5]
                ),
                "translate" when args.Length is 1 or 2 => Matrix3x2.CreateTranslation(
                    args[0],
                    args.Length == 2 ? args[1] : 0
                ),
                "scale" when args.Length is 1 or 2 => Matrix3x2.CreateScale(
                    args[0],
                    args.Length == 2 ? args[1] : args[0]
                ),
                "rotate" when args.Length == 1 => Matrix3x2.CreateRotation(
                    args[0] * MathF.PI / 180
                ),
                "rotate" when args.Length == 3 => Matrix3x2.CreateRotation(
                    args[0] * MathF.PI / 180,
                    new(args[1], args[2])
                ),
                "skewX" when args.Length == 1 => Matrix3x2.CreateSkew(args[0] * MathF.PI / 180, 0),
                "skewY" when args.Length == 1 => Matrix3x2.CreateSkew(0, args[0] * MathF.PI / 180),
                _ => throw Unsupported(
                    $"SVG transform '{name}' or its argument count is unsupported."
                ),
            };
            matrix = next * matrix;
            CheckTransform(matrix);
            rest = rest[(close + 1)..].Trim();
            if (rest.StartsWith(","))
                rest = rest[1..].Trim();
        }
        return matrix;
    }

    private static void CheckTransform(Matrix3x2 value)
    {
        foreach (
            var entry in new[] { value.M11, value.M12, value.M21, value.M22, value.M31, value.M32 }
        )
            if (!float.IsFinite(entry) || Math.Abs(entry) > 1_000_000)
                throw Budget("SVG cumulative transform exceeds the finite matrix budget.");
    }

    private static double ValidateFilterBounds(
        XElement[] nodes,
        float width,
        float height,
        int filters
    )
    {
        if (filters == 0)
            return 0;
        // Filter intermediates are evaluated in document coordinates. Until bounds can be
        // established for transformed geometry, fail explicitly rather than under-account it.
        var extent = (double)Math.Max(width, height);
        foreach (var node in nodes)
        {
            if (node.Attribute("transform") is not null)
                throw Unsupported(
                    "Transforms combined with SVG filters require a future bounded-filter adapter."
                );
            if (node.Name.LocalName == "use")
                throw Unsupported(
                    "SVG filters combined with use expansion are not supported by the initial bounded-filter adapter."
                );
            foreach (var attribute in node.Attributes())
            {
                if (
                    attribute.Name.LocalName
                    is not (
                        "x"
                        or "y"
                        or "x1"
                        or "x2"
                        or "y1"
                        or "y2"
                        or "width"
                        or "height"
                        or "cx"
                        or "cy"
                        or "r"
                        or "rx"
                        or "ry"
                        or "d"
                        or "points"
                        or "dx"
                        or "dy"
                    )
                )
                    continue;
                if (attribute.Value.EndsWith('%'))
                    continue;
                foreach (Match match in Number.Matches(attribute.Value))
                    extent = Math.Max(
                        extent,
                        Math.Abs(double.Parse(match.Value, CultureInfo.InvariantCulture))
                    );
            }
            if (node.Name.LocalName != "filter")
                continue;
            var objectUnits = node.Attribute("filterUnits")?.Value is null or "objectBoundingBox";
            if (objectUnits)
                foreach (var name in new[] { "x", "y", "width", "height" })
                {
                    var value = node.Attribute(name)?.Value;
                    if (value is null || value.EndsWith('%'))
                        continue;
                    if (Math.Abs(double.Parse(value, CultureInfo.InvariantCulture)) > 2)
                        throw Budget(
                            "SVG object-bounding-box filter coordinates exceed two bounds."
                        );
                }
            var inputs = new HashSet<string>(
                ["SourceGraphic", "SourceAlpha"],
                StringComparer.Ordinal
            );
            foreach (var primitive in node.Elements())
            {
                foreach (
                    var input in primitive
                        .DescendantsAndSelf()
                        .Attributes()
                        .Where(a => a.Name.LocalName is "in" or "in2")
                )
                    if (!inputs.Contains(input.Value))
                        throw Invalid($"Required SVG filter input '{input.Value}' is missing.");
                if (primitive.Attribute("result") is { } result)
                    inputs.Add(result.Value);
            }
        }
        // x+width, a two-bound filter region and offset/blur padding are conservatively
        // represented by this envelope; transformed/use geometry is excluded above.
        extent = extent * 4 + 384;
        if (nodes[0].Attribute("viewBox") is { } viewBox)
        {
            var values = viewBox
                .Value.Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(v => double.Parse(v, CultureInfo.InvariantCulture))
                .ToArray();
            var scale = Math.Max(1, Math.Max(width / values[2], height / values[3]));
            extent = (extent + Math.Abs(values[0]) + Math.Abs(values[1])) * scale;
        }
        if (extent > 4096)
            throw Budget("SVG filter working extent exceeds 4096 units.");
        return extent;
    }

    private static void ValidateReferenceTypes(XElement node, Dictionary<string, XElement> ids)
    {
        void Check(string property, string value)
        {
            foreach (Match match in Url.Matches(value))
            {
                var kind = ids[match.Groups[1].Value[1..]].Name.LocalName;
                var valid = property switch
                {
                    "fill" or "stroke" => kind is "linearGradient" or "radialGradient",
                    "clip-path" => kind == "clipPath",
                    "mask" => kind == "mask",
                    "filter" => kind == "filter",
                    _ => false,
                };
                if (!valid)
                    throw Unsupported(
                        $"SVG '{property}' references an unsupported '{kind}' resource."
                    );
            }
        }
        foreach (var attribute in node.Attributes())
        {
            if (attribute.Name.LocalName is "style")
            {
                foreach (
                    var declaration in attribute.Value.Split(
                        ';',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    )
                )
                {
                    var colon = declaration.IndexOf(':');
                    Check(declaration[..colon].Trim(), declaration[(colon + 1)..]);
                }
            }
            else if (attribute.Name.LocalName == "href" && attribute.Value.StartsWith('#'))
            {
                var kind = ids[attribute.Value[1..]].Name.LocalName;
                if (
                    node.Name.LocalName is "linearGradient" or "radialGradient"
                    && kind is not ("linearGradient" or "radialGradient")
                )
                    throw Invalid("SVG gradient href must reference a gradient.");
                if (node.Name.LocalName == "textPath" && kind != "path")
                    throw Invalid("SVG textPath must reference a path.");
                if (node.Name.LocalName == "image")
                    throw Unsupported(
                        "Same-document image href is not supported; use embedded raster data or use."
                    );
                if (
                    node.Name.LocalName == "use"
                    && kind
                        is not (
                            "g"
                            or "symbol"
                            or "rect"
                            or "circle"
                            or "ellipse"
                            or "path"
                            or "line"
                            or "polyline"
                            or "polygon"
                            or "use"
                            or "text"
                            or "image"
                        )
                )
                    throw Unsupported($"SVG use cannot instantiate '{kind}'.");
            }
            else
                Check(attribute.Name.LocalName, attribute.Value);
        }
        if (node.Name.LocalName == "style")
        {
            foreach (
                Match match in Regex.Matches(
                    node.Value,
                    @"([\w-]+)\s*:\s*([^;}]+)",
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)
                )
            )
                Check(match.Groups[1].Value, match.Groups[2].Value);
        }
    }

    private static void ValidateRootSize(XElement root, AssetImageMetadata metadata)
    {
        var viewBox = root.Attribute("viewBox")
            ?.Value.Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (viewBox is not null)
        {
            if (viewBox.Length != 4)
                throw Invalid("SVG viewBox requires four finite numbers.");
            var values = viewBox
                .Select(v => float.Parse(v, CultureInfo.InvariantCulture))
                .ToArray();
            if (values.Any(v => !float.IsFinite(v)) || values[2] <= 0 || values[3] <= 0)
                throw Invalid("SVG viewBox has invalid dimensions.");
        }
        (double? Definite, double? Relative) Length(string name)
        {
            var value = root.Attribute(name)?.Value.Trim();
            if (value is null or "auto")
                return (null, null);
            if (value.EndsWith('%'))
                return (null, double.Parse(value[..^1], CultureInfo.InvariantCulture) / 100);
            var scale = 1d;
            foreach (
                var (unit, factor) in new[]
                {
                    ("px", 1d),
                    ("pt", 96d / 72),
                    ("pc", 16d),
                    ("in", 96d),
                    ("cm", 96d / 2.54),
                    ("mm", 96d / 25.4),
                    ("Q", 96d / 101.6),
                }
            )
            {
                if (!value.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
                    continue;
                value = value[..^unit.Length];
                scale = factor;
                break;
            }
            var result = double.Parse(value, CultureInfo.InvariantCulture) * scale;
            if (!double.IsFinite(result) || result <= 0)
                throw Invalid($"SVG {name} must be finite and positive.");
            return (result, null);
        }
        var (width, relativeWidth) = Length("width");
        var (height, relativeHeight) = Length("height");
        double? aspect = viewBox is null
            ? null
            : double.Parse(viewBox[2], CultureInfo.InvariantCulture)
                / double.Parse(viewBox[3], CultureInfo.InvariantCulture);
        if (
            width is null
            && height is null
            && aspect is null
            && relativeWidth is null
            && relativeHeight is null
        )
            throw Invalid("SVG needs dimensions or a viewBox.");
        if (width is null && height is { } h && aspect is { } a)
            width = h * a;
        if (height is null && width is { } w && aspect is { } b)
            height = w / b;
        width ??= 300;
        height ??= aspect is { } ratio ? width / ratio : 150;
        if (
            viewBox is not null
            && (
                width / double.Parse(viewBox[2], CultureInfo.InvariantCulture) > 1_000_000
                || height / double.Parse(viewBox[3], CultureInfo.InvariantCulture) > 1_000_000
            )
        )
            throw Budget("SVG viewBox scale exceeds the finite matrix budget.");
        if (
            Math.Abs(width.Value - metadata.Width) > .01
            || Math.Abs(height.Value - metadata.Height) > .01
            || (float?)relativeWidth != metadata.RelativeWidth
            || (float?)relativeHeight != metadata.RelativeHeight
            || metadata.Density != 1
        )
            throw Invalid("SVG dimensions do not match their declared asset metadata.");
    }

    private static void ValidateValue(
        string name,
        string value,
        List<string> references,
        bool hasFont
    )
    {
        if (
            value.Contains('\\')
            || value.Contains('@')
            || value.Contains("/*", StringComparison.Ordinal)
        )
            throw Unsupported("SVG CSS escapes, imports and comments are unsupported.");
        if (value.Contains("url", StringComparison.OrdinalIgnoreCase))
        {
            var matches = Url.Matches(value);
            if (
                matches.Count == 0
                || Url.Replace(value, "").Contains("url", StringComparison.OrdinalIgnoreCase)
            )
                throw Unsupported("SVG paint/resource URLs must be same-document fragments.");
            foreach (Match match in matches)
                references.Add(match.Groups[1].Value[1..]);
        }
        if (
            value.Contains("NaN", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Infinity", StringComparison.OrdinalIgnoreCase)
        )
            throw Invalid("SVG numbers must be finite.");
        if (name is not ("id" or "class" or "font-family"))
            foreach (Match match in Number.Matches(value))
                if (
                    !double.TryParse(match.Value, CultureInfo.InvariantCulture, out var number)
                    || !double.IsFinite(number)
                    || Math.Abs(number) > 1_000_000
                )
                    throw Budget("SVG numeric magnitude exceeds 1000000.");
        if (name == "stdDeviation")
            foreach (Match match in Number.Matches(value))
                if (double.Parse(match.Value, CultureInfo.InvariantCulture) is < 0 or > 64)
                    throw Budget("SVG blur deviation must be between 0 and 64.");
        if (
            name is "in" or "in2"
            && value is "BackgroundImage" or "BackgroundAlpha" or "FillPaint" or "StrokePaint"
        )
            throw Unsupported("SVG filter background and context-paint inputs are unsupported.");
        if (name == "font-family" && (!hasFont || value.Trim(' ', '\'', '"') != "Lucent SVG"))
            throw Unsupported("SVG text only supports the explicitly pinned 'Lucent SVG' family.");
        if (name == "font-weight" && value is not ("normal" or "400"))
            throw Unsupported("SVG pinned text currently supports normal weight only.");
        if (name == "font-style" && value != "normal")
            throw Unsupported("SVG pinned text currently supports normal style only.");
        if (
            name is "width" or "height"
            && value.EndsWith('%')
            && (
                !float.TryParse(value[..^1], CultureInfo.InvariantCulture, out var percent)
                || percent < 0
                || percent > 200
            )
        )
            throw Budget("SVG percentage dimensions must be between 0 and 200%.");
    }

    private static void ValidateDeclarations(string css, List<string> references, bool hasFont)
    {
        foreach (
            var declaration in css.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            var colon = declaration.IndexOf(':');
            if (colon <= 0)
                throw Invalid("Malformed SVG style declaration.");
            var property = declaration[..colon].Trim();
            if (!CssProperties.Contains(property))
                throw Unsupported($"SVG CSS property '{property}' is unsupported.");
            ValidateValue(property, declaration[(colon + 1)..].Trim(), references, hasFont);
        }
    }

    private static void ValidateStylesheet(string css, List<string> references, bool hasFont)
    {
        while (!string.IsNullOrWhiteSpace(css))
        {
            var open = css.IndexOf('{');
            var close = css.IndexOf('}');
            if (open <= 0 || close <= open)
                throw Invalid("Malformed SVG stylesheet.");
            var selector = css[..open].Trim();
            if (
                !Regex.IsMatch(
                    selector,
                    @"^[A-Za-z0-9_#. ,:-]+$",
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)
                ) || selector.Contains(':')
            )
                throw Unsupported(
                    "Only simple static SVG type, class and id selectors are supported."
                );
            ValidateDeclarations(css[(open + 1)..close], references, hasFont);
            css = css[(close + 1)..];
        }
    }

    private static long ValidateDataImage(string href, Func<long, IDisposable> reserve)
    {
        const string png = "data:image/png;base64,";
        const string jpeg = "data:image/jpeg;base64,";
        var prefix =
            href.StartsWith(png, StringComparison.Ordinal) ? png
            : href.StartsWith(jpeg, StringComparison.Ordinal) ? jpeg
            : null;
        if (prefix is null)
            throw Unsupported(
                "SVG images only permit bounded base64 PNG/JPEG data; external and nested SVG resources are prohibited."
            );
        if (href.Length > 1400000)
            throw Budget("SVG embedded encoded raster exceeds 1 MiB.");
        var data = Convert.FromBase64String(href[prefix.Length..]);
        using var codec = SKCodec.Create(new MemoryStream(data, false));
        if (
            codec is null
            || codec.EncodedFormat
                != (prefix == png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg)
        )
            throw Invalid("SVG embedded raster data is invalid.");
        var pixels = (long)codec.Info.Width * codec.Info.Height;
        if (codec.FrameCount > 1)
            throw Unsupported("Animated embedded raster images are not supported in static SVG.");
        if (pixels <= 0 || pixels > 4 * 1024 * 1024)
            throw Budget("SVG embedded raster exceeds 4 megapixels.");
        using var reservation = reserve(pixels * 16 + data.LongLength + 262144);
        // Check actual decode, not just a header that the SVG parser might silently omit.
        using var bitmap = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw Invalid("SVG embedded raster cannot be decoded.");
        return pixels;
    }

    private static HashSet<string> Words(string value) =>
        new(value.Split(' '), StringComparer.Ordinal);

    internal static ImageLoadException Invalid(string message) =>
        new(ImageLoadFailureKind.InvalidData, message);

    internal static ImageLoadException Unsupported(string message) =>
        new(ImageLoadFailureKind.UnsupportedFormat, message);

    internal static ImageLoadException Budget(string message) =>
        new(ImageLoadFailureKind.BudgetDeclined, message);
}
