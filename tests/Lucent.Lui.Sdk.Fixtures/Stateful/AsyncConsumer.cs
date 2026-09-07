namespace StatefulTrial;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lucent.Core;

public static class AsyncHarness
{
    public static void Run()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "async-trial");
        using var theme = new ThemeContext(composition.Root.Scope, new Theme("trial"));
        var input = composition.Root.Scope.Signal("first", "input");
        var requests =
            new List<(string Key, CancellationToken Token, TaskCompletionSource<string> Work)>();
        Task<string> Load(string key, CancellationToken token)
        {
            var work = new TaskCompletionSource<string>();
            requests.Add((key, token, work));
            return work.Task;
        }
        var recipe = Components.AsyncTrial(() => input.Value, Load);
        if (requests.Count != 0)
            throw new InvalidOperationException("Async recipe started work before mount.");
        var mounted = composition.Mount(composition.Root, theme, recipe);
        var capture = Harness.Mounts[^1];
        Expect("Loading", capture.Read());
        input.Value = "second";
        Expect("Loading", capture.Read());
        if (
            requests.Count != 2
            || !requests[0].Token.IsCancellationRequested
            || requests[1].Key != "second"
        )
            throw new InvalidOperationException("Authored source did not replace the generation.");
        requests[0].Work.SetResult("obsolete");
        requests[1].Work.SetException(new InvalidOperationException("offline"));
        graph.Drain();
        Expect("Try again", capture.Read());
        capture.Toggle();
        Expect("Loading", capture.Read());
        requests[2].Work.SetResult("ready");
        graph.Drain();
        Expect("ready", capture.Read());
        capture.Toggle();
        Expect("Loading", capture.Read());
        mounted.Dispose();
        if (!requests[3].Token.IsCancellationRequested)
            throw new InvalidOperationException("Unmount did not cancel the owned load.");
        requests[3].Work.SetResult("late");
        graph.Drain();
        Console.WriteLine("source-driven async SDK proof: PASS");
    }

    private static void Expect(string expected, string actual)
    {
        if (actual != expected)
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
