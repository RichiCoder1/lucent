namespace StatefulTrial;

using System;
using System.Collections.Generic;
using Lucent.Core;

public static class DesignHarness
{
    private static readonly List<Capture> Captures = new();

    [LucentComponent]
    public static ComponentRecipe DesignProbe(
        [DefaultContent] Func<string> content,
        bool initial,
        Func<bool> read
    ) =>
        ComponentRecipe.Create("design-probe", (_, _) => Captures.Add(new(content, initial, read)));

    public static void Run()
    {
        Captures.Clear();
        var graph = new ReactiveGraph();
        using var application = new Composition(graph, "sdk-application");
        using var preview = new Composition(graph, "sdk-preview", CompositionPurpose.Preview);
        using var normalTheme = new ThemeContext(application.Root.Scope, ControlThemes.Light);
        using var previewTheme = new ThemeContext(preview.Root.Scope, ControlThemes.Light);
        var recipe = Components.DesignTrial();
        var normalRoot = application.Mount(application.Root, normalTheme, recipe);
        var previewRoot = preview.Mount(preview.Root, previewTheme, recipe);
        if (
            Captures.Count != 2
            || Captures[0].Text() != "application"
            || Captures[1].Text() != "preview"
            || Captures[0].Initial
            || !Captures[1].Initial
            || Captures[0].Read()
            || !Captures[1].Read()
        )
            throw new InvalidOperationException(
                "Generated design context did not remain composition-scoped."
            );
        normalRoot.Dispose();
        previewRoot.Dispose();
        if (Captures[0].Read() || !Captures[1].Read())
            throw new InvalidOperationException(
                "A retained design callback depended on a disposed mount context."
            );
        Captures.Clear();
        Console.WriteLine("composition-scoped design SDK proof: PASS");
    }

    private sealed record Capture(Func<string> Text, bool Initial, Func<bool> Read);
}
