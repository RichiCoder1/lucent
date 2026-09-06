namespace StatefulTrial;

using System;
using System.Collections.Generic;
using Lucent.Core;

public static class Constants
{
    public const int Start = 6 * 7;
}

public sealed record Capture(Func<string> Read, Action Toggle, Element Root);

public static class TestComponents
{
    [LucentComponent]
    public static ComponentRecipe Probe([DefaultContent] Func<string> content, Action toggle) =>
        ComponentRecipe.Create(
            "probe",
            (_, root) => Harness.Mounts.Add(new Capture(content, toggle, root))
        );
}

public static class Harness
{
    public static int Setups { get; set; }
    public static int Cleanups { get; set; }
    public static readonly List<Capture> Mounts = new();

    public static string Run()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "state-trial");
        using var theme = new ThemeContext(composition.Root.Scope, new Theme("trial"));
        var input = composition.Root.Scope.Signal("first", "input");
        var recipe = Components.Trial(() => input.Value);
        if (Setups != 0)
            throw new InvalidOperationException("Setup ran at recipe construction.");
        var first = composition.Mount(composition.Root, theme, recipe.Named("first"));
        var second = composition.Mount(composition.Root, theme, recipe.Named("second"));
        if (first.Children.Count != 0 || second.Children.Count != 0)
            throw new InvalidOperationException("Setup added an observable wrapper root.");
        var a = Mounts[0];
        var b = Mounts[1];
        var before = a.Read() + "|" + b.Read();
        a.Toggle();
        input.Value = "next";
        var after = a.Read() + "|" + b.Read();
        first.Dispose();
        if (Cleanups != 1 || second.IsDisposed)
            throw new InvalidOperationException("Disposal crossed mounts.");
        second.Dispose();
        return before + ";" + after + ";" + Setups + ":" + Cleanups;
    }
}
