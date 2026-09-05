using System.Globalization;
using Lucent.Core;

var row = RowWidths(1060);
var overflow = OverflowWidths(300);
var wideText = TextAt(210);
var narrowText = TextAt(105);
Console.WriteLine(FormattableString.Invariant($"managed-row=[{string.Join(',', row)}]"));
Console.WriteLine(FormattableString.Invariant($"constrained-row=[{string.Join(',', overflow)}]"));
Console.WriteLine(
    FormattableString.Invariant(
        $"text-210-height={wideText.Height}; text-105-height={narrowText.Height}"
    )
);
Console.WriteLine(
    $"requests-equal={wideText.Request == narrowText.Request}; request={Describe(wideText.Request)}"
);
Console.WriteLine("baseline-commit=8fea38ad14d8e14f732eac9aa34421e4009cbc4c");
Console.WriteLine("core-sha256=5BC42866737BD21C0045A31C2071135207FFEAD116925219270BCFB63F7DDA8F");
static float[] RowWidths(float width)
{
    var graph = new ReactiveGraph();
    using var composition = new Composition(graph, "managed-row");
    var theme = new ThemeContext(composition.Root.Scope, new Theme("probe"));
    composition.Root.Present(theme, author: Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Row));
    var nav = Child(184, 0, "nav");
    var list = Child(320, 0, "list");
    var editor = Child(null, 1, "editor");
    var scene = SceneLayout.Project(composition, new(width, 520, 1), new ProbeShaper());
    return new[] { Width(nav), Width(list), Width(editor) };

    Element Child(float? basis, float grow, string name)
    {
        var element = composition.Child(composition.Root, name);
        var style = Style.Empty.Set(LayoutProperties.MainGrow, grow);
        if (basis is not null)
            style = style.Set(LayoutProperties.Width, basis);
        element.Present(theme, author: style);
        return element;
    }

    float Width(Element element) =>
        scene.Boxes.Single(box => box.Identity.ElementId == element.Id).Bounds.Width;
}

static float[] OverflowWidths(float width)
{
    var graph = new ReactiveGraph();
    using var composition = new Composition(graph, "managed-overflow");
    var theme = new ThemeContext(composition.Root.Scope, new Theme("probe"));
    composition.Root.Present(
        theme,
        author: Style
            .Empty.Set(LayoutProperties.Axis, LayoutAxis.Row)
            .Set(LayoutProperties.Spacing, 8)
    );
    var field = composition.Child(composition.Root, "field");
    field.Present(
        theme,
        author: Style.Empty.Set(LayoutProperties.Width, 240f).Set(LayoutProperties.MainGrow, 1f)
    );
    var button = composition.Child(composition.Root, "button");
    button.Present(theme, author: Style.Empty.Set(LayoutProperties.Width, 80f));
    var scene = SceneLayout.Project(composition, new(width, 40, 1), new ProbeShaper());
    return new[]
    {
        scene.Boxes.Single(box => box.Identity.ElementId == field.Id).Bounds.Width,
        scene.Boxes.Single(box => box.Identity.ElementId == button.Id).Bounds.Width,
    };
}

static (float Height, TextMeasureRequest Request) TextAt(float width)
{
    var graph = new ReactiveGraph();
    using var composition = new Composition(
        graph,
        "managed-text-" + width.ToString(CultureInfo.InvariantCulture)
    );
    var theme = new ThemeContext(composition.Root.Scope, new Theme("probe"));
    composition.Root.Present(
        theme,
        author: Style.Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
    );
    var text = composition.Child(composition.Root, "text");
    text.Present(
        theme,
        author: Style
            .Empty.Set(LayoutProperties.Width, width)
            .Set(ProjectionProperties.Text, new string('x', 113))
            .Set(TypographyProperties.FontSize, 16f)
    );
    var shaper = new ProbeShaper();
    var scene = SceneLayout.Project(composition, new(width, 200, 1), shaper);
    var height = scene.Boxes.Single(box => box.Identity.ElementId == text.Id).Bounds.Height;
    return (height, shaper.Last);
}

static string Describe(TextMeasureRequest request) =>
    FormattableString.Invariant(
        $"length={request.Text.Length},fontSize={request.FontSize},language={request.Language},direction={request.Direction},scale={request.Scale}"
    );

sealed class ProbeShaper : ITextShaper
{
    public TextMeasureRequest Last { get; private set; }

    public ShapedText Shape(TextMeasureRequest request)
    {
        Last = request;
        var width = request.Text.Length * request.FontSize / 2;
        var glyphs = new[] { new ShapedGlyph(1, 0, 0, 0, width, 0, 0) };
        var run = new ShapedRun(
            "run",
            "probe",
            400,
            5,
            0,
            "probe-fingerprint",
            0,
            "probe#0",
            request.Direction,
            request.Language,
            request.FontSize,
            0,
            request.FontSize,
            -request.FontSize,
            0,
            width,
            glyphs
        );
        return new("shape", width, request.FontSize, [run]);
    }
}
