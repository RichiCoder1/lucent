using Lucent.Core;

namespace Consumer;

internal static class TransitionConsumer
{
    internal static void Run()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "sdk-transition");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var active = composition.Root.Scope.Signal(false, "transition-active");
        composition.Mount(
            composition.Root,
            theme,
            global::Consumer.Components.MotionWidget(() => active.Value ? 1f : 0f)
        );
        graph.Drain();

        var shaper = new EmptyShaper();
        using var initial = SceneLayout.Project(
            composition,
            new LayoutViewport(160, 60, 1),
            shaper
        );
        Require(composition.Input.SetScene(initial), "Initial SDK transition scene was rejected.");
        Require(
            composition.TryAcknowledgePresentation(initial.Generation),
            "Initial SDK transition scene was not acknowledged."
        );

        active.Value = true;
        graph.Drain();
        using var target = SceneLayout.ProjectFrame(composition, initial.Viewport, shaper, initial);
        Require(composition.Input.SetScene(target), "Target SDK transition scene was rejected.");
        Require(
            composition.TryAcknowledgePresentation(target.Generation),
            "Target SDK transition scene was not acknowledged."
        );
        Require(
            composition.PresentationDemand.IsActive,
            "Generated transition policy did not request a presentation frame."
        );

        composition.SamplePresentation(TimeSpan.FromMilliseconds(60));
        using var middle = SceneLayout.ProjectFrame(composition, target.Viewport, shaper, target);
        var opacity = Flatten(middle.Nodes).OfType<OpacitySceneNode>().Single().Opacity;
        Require(
            opacity > 0f && opacity < 1f,
            "Generated SDK transition did not produce an intermediate opacity sample."
        );
        Console.WriteLine("transition SDK proof: PASS");
    }

    private static IEnumerable<SceneNode> Flatten(IEnumerable<SceneNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            var children = node switch
            {
                ClipSceneNode clip => clip.Children,
                OpacitySceneNode opacity => opacity.Children,
                _ => [],
            };
            foreach (var child in Flatten(children))
                yield return child;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) =>
            new(
                request.Text.Length == 0 ? "empty" : request.Text,
                request.Text.Length,
                request.FontSize,
                []
            );
    }
}
