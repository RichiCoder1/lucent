using Lucent.Core;

namespace AuthoringConsumer;

internal static class Integration
{
    public static void Verify()
    {
        VerifyView(useLui: false);
        VerifyView(useLui: true);
    }

    private static void VerifyView(bool useLui)
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(
            graph,
            useLui ? "lui-integration" : "csharp-integration"
        );
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var states = new List<CounterState>();
        var completions = new List<TaskCompletionSource<string[]>>();
        var resources = new List<AsyncValue<string[]>>();
        var cleanups = 0;
        var recipe = Component.Define<CounterState>(
            "counter",
            (ui, state) =>
            {
                states.Add(state);
                var completion = new TaskCompletionSource<string[]>();
                completions.Add(completion);
                var resource = ui.Resource(_ => completion.Task);
                resources.Add(resource);
                ui.OnDispose(() => cleanups++);
                return useLui
                    ? Components.IntegrationView(state, resource)
                    : BuildView(state, resource);
            }
        );
        var first = composition.Mount(composition.Root, theme, recipe);
        var second = composition.Mount(composition.Root, theme, recipe);
        graph.Drain();
        Require(composition.Root.Children.Count == 2, "Authoring inserted a retained wrapper.");
        Require(first.Children.Count == second.Children.Count, "Mount topology differs.");
        Require(
            states.Count == 2 && !ReferenceEquals(states[0], states[1]),
            "Generated state was shared."
        );
        var button = Find(composition, first, SemanticRole.Button);
        Require(
            button.Name == "Activate counter",
            "Author name did not override the initial live label."
        );
        Require(
            composition.ExecuteSemanticCommand(button.Identity, new(SemanticCommandKind.Invoke))
                == SemanticCommandResult.Applied,
            "Counter invocation failed."
        );
        graph.Drain();
        Require(states[0].Count == 1 && states[1].Count == 0, "Counter state escaped its mount.");
        var gauge = Find(composition, first, SemanticRole.ProgressBar);
        Require(
            gauge.Range is { IsReadOnly: true, Value: 1 } && gauge.Actions == SemanticAction.None,
            "Packaged Gauge lost its controlled read-only range."
        );
        var nextButton = Find(composition, first, SemanticRole.Button);
        Require(
            nextButton.Identity.ElementId == button.Identity.ElementId
                && nextButton.Name == "Activate counter",
            "Behavior update replaced the target or overwrote author metadata."
        );

        var editor = Find(composition, first, SemanticRole.TextField);
        Require(
            composition.ExecuteSemanticCommand(
                editor.Identity,
                new(SemanticCommandKind.SetValue, Value: "edited")
            ) == SemanticCommandResult.Applied,
            "Controlled editor request failed."
        );
        graph.Drain();
        Require(
            states[0].Draft == "edited" && states[1].Draft == "seed",
            "Controlled form did not apply its request."
        );
        Task.Run(() => completions[0].SetResult(["alpha", "beta"])).GetAwaiter().GetResult();
        graph.Drain();
        var loaded = Descendants(composition.SemanticSnapshot()!)
            .Where(node => node.Name is "alpha" or "beta")
            .ToArray();
        Require(loaded.Length == 2, "Owned async/keyed content was not mounted.");
        var alpha = loaded.Single(node => node.Name == "alpha").Identity.ElementId;
        states[0].Count++;
        graph.Drain();
        Require(
            Descendants(composition.SemanticSnapshot()!)
                .Single(node => node.Name == "alpha")
                .Identity.ElementId == alpha,
            "An unrelated state edit replaced keyed content."
        );
        second.Dispose();
        Task.Run(() => completions[1].SetResult(["late"])).GetAwaiter().GetResult();
        graph.Drain();
        Require(
            resources[1].IsDisposed && cleanups == 1,
            "Unmount did not cancel owned resources."
        );
        Require(
            !Descendants(composition.SemanticSnapshot()!).Any(node => node.Name == "late"),
            "Late async completion escaped its owner."
        );
        first.Dispose();
        Require(cleanups == 2, "Component cleanup did not run exactly once.");
        try
        {
            _ = states[0].Count;
            throw new InvalidOperationException("Disposed state remained readable.");
        }
        catch (ObjectDisposedException) { }
        Console.WriteLine($"Packaged {(useLui ? ".lui" : "C#")} integration: PASS");
    }

    private static ComponentRecipe BuildView(CounterState state, AsyncValue<string[]> items) =>
        Lucent
            .Core.Components.Column([
                Lucent
                    .Core.Components.Button(() => "Count " + state.Count, () => state.Count++)
                    .Height(40)
                    .Aria.Name("Activate counter")
                    .End,
                Lucent.Core.Components.Field(
                    "Draft",
                    field =>
                        Lucent.Core.Components.TextField(
                            field,
                            () => state.Draft,
                            value => state.Draft = value
                        )
                ),
                Lucent
                    .Core.Components.Gauge("Count range", () => state.Count, new(0, 10))
                    .Width(96),
                Items(items),
            ])
            .Spacing(8);

    internal static ContentRecipe Items(AsyncValue<string[]> items) =>
        ContentRecipe.ForEach(
            "loaded-items",
            () => items.Value ?? [],
            value => value,
            item => Lucent.Core.Components.Text(() => item.Value)
        );

    private static SemanticSnapshot Find(Composition composition, Element root, SemanticRole role)
    {
        var ids = ElementIds(root).ToHashSet();
        return Descendants(composition.SemanticSnapshot()!)
            .Single(node => ids.Contains(node.Identity.ElementId) && node.Role == role);
    }

    private static IEnumerable<long> ElementIds(Element root)
    {
        yield return root.Id;
        foreach (var child in root.Children)
        foreach (var id in ElementIds(child))
            yield return id;
    }

    private static IEnumerable<SemanticSnapshot> Descendants(SemanticSnapshot root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var node in Descendants(child))
            yield return node;
    }

    private static void Require(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }
}

[ComponentState]
internal sealed partial class CounterState
{
    [State]
    public partial int Count { get; set; }

    [State("seed")]
    public partial string Draft { get; set; }

    public void SetDraft(string value) => Draft = value;
}
