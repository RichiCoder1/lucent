using Lucent.Core;
using Lucent.Platform.Windows;

namespace Lucent.Platform.Windows.TestHost;

/// <summary>Test-owned bounded retained fixture for the external UIA contract; it uses only public authoring APIs.</summary>
internal static class UiaFixture
{
    private static readonly string[] InitialItems = ["Keep", "Retire", "Disabled"];

    internal static int Run()
    {
        try
        {
            var graph = new ReactiveGraph();
            using var composition = Create(graph, out var theme);
            return WindowsBootstrap.Run("Lucent UIA Fixture", composition, theme);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Lucent UIA fixture: " + error.Message);
            return 1;
        }
    }

    internal static Composition Create(ReactiveGraph graph, out ThemeContext theme)
    {
        var composition = new Composition(
            graph ?? throw new ArgumentNullException(nameof(graph)),
            "uia-fixture"
        );
        var themeContext = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        theme = themeContext;
        Controls.Column(
            composition.Root,
            themeContext,
            "Fixture",
            Style
                .Empty.Set(LayoutProperties.Width, 400f)
                .Set(LayoutProperties.Height, 500f)
                .Set(LayoutProperties.Clip, true)
        );
        var group = composition.Child(composition.Root, "group");
        Controls.Panel(
            group,
            themeContext,
            "Controls",
            Style.Empty.Set(LayoutProperties.Width, 400f)
        );
        var text = composition.Child(group, "text");
        Controls.Text(
            text,
            themeContext,
            "Read only",
            Style.Empty.Set(LayoutProperties.Height, 24f)
        );
        var field = composition.Child(group, "field");
        Controls.TextField(
            field,
            themeContext,
            "Value",
            style: Style.Empty.Set(LayoutProperties.Width, 200f).Set(LayoutProperties.Height, 24f)
        );
        var statusElement = composition.Child(group, "status");
        var status = Controls.Loading(
            statusElement,
            themeContext,
            "Ready",
            Style.Empty.Set(LayoutProperties.Height, 24f)
        );
        var retained = composition.Root.Scope.Signal(InitialItems, "uia-fixture.items");
        var invokes = 0;
        var button = composition.Child(group, "button");
        Controls.Button(
            button,
            themeContext,
            "Invoke",
            () =>
            {
                if (invokes++ == 0)
                    throw new InvalidOperationException("fixture action");
                status.Label = "Invoked";
                retained.Value = ["Keep", "Disabled"];
            },
            Style
                .Empty.Set(LayoutProperties.Width, 100f)
                .Set(LayoutProperties.Height, 24f)
                .Set(VisualProperties.Opacity, 0f)
        );
        var list = composition.Child(group, "list");
        Controls.List(list, themeContext, "Choices", Style.Empty.Set(LayoutProperties.Width, 300f));
        _ = composition.ForEach(
            list,
            "items",
            () => retained.Value,
            value => value,
            (value, context) =>
            {
                var item = context.Element("choice");
                var style = Style
                    .Empty.Set(LayoutProperties.Width, 300f)
                    .Set(LayoutProperties.Height, 24f);
                if (value == "Disabled")
                    style = style.Set(InputProperties.Enabled, false);
                Controls.Selectable(item, themeContext, value, style: style);
                return item;
            }
        );
        var viewport = composition.Child(group, "scroll");
        Controls.ScrollViewport(
            viewport,
            themeContext,
            "Scroll",
            new ScrollOffset(0, 36f),
            style: Style.Empty.Set(LayoutProperties.Width, 300f).Set(LayoutProperties.Height, 32f)
        );
        var content = composition.Child(viewport, "scroll-content");
        content.Present(
            themeContext,
            author: Style.Empty.Set(LayoutProperties.Width, 300f).Set(LayoutProperties.Height, 128f)
        );
        var clippedBefore = composition.Child(content, "clipped-before");
        clippedBefore.Present(
            themeContext,
            author: Style.Empty.Set(LayoutProperties.Width, 300f).Set(LayoutProperties.Height, 24f)
        );
        var partlyClipped = composition.Child(content, "partly-clipped");
        Controls.Text(
            partlyClipped,
            themeContext,
            "Partly clipped",
            Style.Empty.Set(LayoutProperties.Width, 300f).Set(LayoutProperties.Height, 24f)
        );
        var clippedBetween = composition.Child(content, "clipped-between");
        clippedBetween.Present(
            themeContext,
            author: Style.Empty.Set(LayoutProperties.Width, 300f).Set(LayoutProperties.Height, 24f)
        );
        var fullyClipped = composition.Child(content, "fully-clipped");
        Controls.Text(
            fullyClipped,
            themeContext,
            "Fully clipped",
            Style.Empty.Set(LayoutProperties.Width, 300f).Set(LayoutProperties.Height, 24f)
        );
        return composition;
    }
}
