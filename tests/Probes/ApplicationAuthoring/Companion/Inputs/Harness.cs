namespace CompanionFixture;

using System;
using System.Collections.Generic;
using System.Linq;
using Lucent.Core;

public static class Harness
{
    public static readonly List<string> Events = [];
    public static readonly List<Counter> Counters = [];
    public static int SetupCalls;
    public static int CounterCleanups;
    public static int FailedInitializerCleanups;

    public static void Record(string value) => Events.Add(value);

    public static int RecordValue(string name, int value)
    {
        Record(name);
        return value;
    }

    public static string Run()
    {
        Func<ComponentRecipe> rootFactory = Counter.Create;
        using var composition = new Composition(new ReactiveGraph(), "companion-probe");
        using var theme = new ThemeContext(composition.Root.Scope, new Theme("companion-probe"));
        var first = composition.Mount(composition.Root, theme, rootFactory());
        var second = composition.Mount(composition.Root, theme, rootFactory());
        Check(Counters.Count == 2 && !ReferenceEquals(Counters[0], Counters[1]), "mount identity");
        Check(
            Events
                .Take(5)
                .SequenceEqual([
                    "ordinary-field",
                    "companion-state",
                    "lui-first",
                    "lui-second",
                    "setup",
                ]),
            "first initialization order"
        );
        Check(
            Events
                .Skip(5)
                .Take(5)
                .SequenceEqual([
                    "ordinary-field",
                    "companion-state",
                    "lui-first",
                    "lui-second",
                    "setup",
                ]),
            "second initialization order"
        );
        Check(SetupCalls == 2 && Counters[0].Snapshot == 17, "initial state and setup count");
        Counters[0].IncrementFromLui();
        Counters[0].IncrementFromCompanion();
        Check(
            Counters[0].CompanionCount == 5
                && Counters[0].LuiCount == 7
                && Counters[1].CompanionCount == 3
                && Counters[1].LuiCount == 2,
            "cross-file methods and independent state"
        );
        first.Dispose();
        Check(CounterCleanups == 1, "first mount cleanup");
        try
        {
            _ = Counters[0].LuiCount;
            throw new InvalidOperationException("disposed state remained readable");
        }
        catch (ObjectDisposedException) { }
        Check(Counters[1].Snapshot == 17, "second mount remains live");
        second.Dispose();
        Check(CounterCleanups == 2, "second mount cleanup");

        try
        {
            composition.Mount(composition.Root, theme, Failing.Create());
            throw new InvalidOperationException("failing initializer mounted");
        }
        catch (InvalidOperationException error) when (error.Message == "initializer failed") { }
        Check(FailedInitializerCleanups == 1, "failed initializer rollback cleanup");
        Check(
            !Events.Contains("unreachable-lui", StringComparer.Ordinal),
            "failure stopped later state"
        );
        return "PASS: " + string.Join(",", Events);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
    }
}
