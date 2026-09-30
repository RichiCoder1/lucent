using System.Text;
using Lucent.Core;
using Lucent.Platform.Windows;
using Lucent.Platform.Windows.Activation;

internal static class Program
{
    private static string _output = "";
    private static int _event;
    private static ApplicationSession? _session;
    private static NavigationSession? _navigation;

    [STAThread]
    private static int Main()
    {
        var settings = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "fixture.txt"));
        if (settings.Length != 3)
            return 80;
        _output = settings[2];
        Directory.CreateDirectory(_output);
        WindowsWindowAttention? attention = null;
        try
        {
            var result = WindowsActivation.Run(
                new WindowsActivationOptions(settings[0], settings[1], "navigation"),
                inbox =>
                {
                    Record("primary-entered", "transport=direct-command");
                    var bundle = Bundle();
                    var restoration = new NavigationRestoration(
                        bundle.Table,
                        "visible-activation-probe",
                        RouteReference.Create(
                            bundle.Table.Patterns.Single(p => p.Id.Value == "home"),
                            []
                        ),
                        _ => true
                    );
                    Signal<bool>? guard = null;
                    var vetoClose = false;
                    var app = LucentApplication
                        .CreateBuilder()
                        .UseWindows(
                            new WindowsWindowOptions
                            {
                                Width = 760,
                                Height = 540,
                                AttentionReady = capability => attention = capability,
                            }
                        )
                        .SetTitle("Lucent visible activation probe")
                        .UseWindowsActivation(
                            inbox,
                            () => _navigation!,
                            restoration,
                            () => ValueTask.FromResult(ReadOnlyMemory<byte>.Empty),
                            new(_ => true, _ => true),
                            () => attention!.Request(),
                            result =>
                                Record(
                                    "binding",
                                    $"status={result.Status}",
                                    $"attentionPresent={result.Attention.HasValue}",
                                    $"restored={result.Attention?.WindowIsRestored}",
                                    $"foregroundAcquired={result.Attention?.ForegroundAcquired}",
                                    $"taskbarAttentionRequested={result.Attention?.TaskbarAttentionRequested}"
                                )
                        )
                        .ConfigureRoot(
                            (session, _) =>
                            {
                                _session = session;
                                _navigation = new NavigationSession(session.Scope, bundle.Table);
                                guard = session.Scope.Signal(false, "guard");
                                return Components.Column([
                                    Components.Text("Visible activation: /home and /second"),
                                    Components.Text(() =>
                                        guard.Value ? "Route guard: VETO" : "Route guard: ALLOW"
                                    ),
                                    Components.Button(
                                        "Toggle route guard",
                                        () =>
                                        {
                                            guard.Value = !guard.Value;
                                            Record("guard-toggle", $"veto={guard.Value}");
                                        }
                                    ),
                                    Components.TextField(
                                        label: "Retained focus editor",
                                        onChange: text =>
                                            Record(
                                                "editor-change",
                                                "textBase64="
                                                    + Convert.ToBase64String(
                                                        Encoding.UTF8.GetBytes(text)
                                                    )
                                            )
                                    ),
                                    Components.Button(
                                        "Record current state",
                                        () => Record("manual-state")
                                    ),
                                    Components.Button(
                                        "Veto next close",
                                        () =>
                                        {
                                            vetoClose = true;
                                            Record("close-veto-armed");
                                        }
                                    ),
                                    Components.Router(
                                        [
                                            Components.RouterOutlet(
                                                new RouteOutletOptions(
                                                    (_, request, _) =>
                                                    {
                                                        var veto =
                                                            guard.Value
                                                            && request.Current is not null;
                                                        Record(
                                                            "route-prepare",
                                                            $"target={request.Target.DefinitionId.Value}",
                                                            $"veto={veto}"
                                                        );
                                                        return ValueTask.FromResult(
                                                            veto
                                                                ? NavigationPreparationResult.Stay
                                                                : NavigationPreparationResult.Allow
                                                        );
                                                    }
                                                )
                                            ),
                                        ],
                                        bundle,
                                        session: _navigation
                                    ),
                                ]);
                            }
                        )
                        .OnMounted(_ =>
                        {
                            Record("mounted");
                            return ValueTask.CompletedTask;
                        })
                        .OnPrepareClose(
                            (_, _) =>
                            {
                                var allow = !vetoClose;
                                vetoClose = false;
                                Record("close-decision", $"allow={allow}");
                                return ValueTask.FromResult(allow);
                            }
                        )
                        .OnDispose(_ =>
                        {
                            Record("binding-disposal");
                            return ValueTask.CompletedTask;
                        })
                        .Build(ComponentRecipe.Create("placeholder", static (_, _) => { }));
                    var exit = app.Run();
                    Record(
                        "host-returned",
                        $"exit={exit}",
                        $"navigationDisposed={_navigation!.IsDisposed}"
                    );
                    try
                    {
                        attention!.Request();
                        Record("attention-after-teardown", "rejected=False");
                        return 81;
                    }
                    catch (ObjectDisposedException)
                    {
                        Record("attention-after-teardown", "rejected=True");
                    }
                    return exit;
                }
            );
            Record(
                "activation-returned",
                $"kind={result.Kind}",
                $"exit={result.ExitCode}",
                $"failure={result.Failure}"
            );
            return result.ExitCode;
        }
        catch (Exception error)
        {
            Record(
                "failure",
                "errorBase64=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(error.ToString()))
            );
            return 1;
        }
    }

    private static void Record(string kind, params string[] details)
    {
        var lines = new List<string>
        {
            $"kind={kind}",
            $"pid={Environment.ProcessId}",
            $"thread={Environment.CurrentManagedThreadId}",
            $"utc={DateTimeOffset.UtcNow:O}",
            "transport=direct-command",
        };
        if (_navigation is { IsDisposed: false } navigation)
        {
            lines.Add($"route={navigation.Current?.DefinitionId.Value}");
            lines.Add($"entry={navigation.Current?.EntryId}");
            lines.Add($"journalCount={navigation.Journal.Entries.Count}");
            lines.Add($"journalIndex={navigation.Journal.CurrentIndex}");
        }
        if (_session is { } session && !session.Composition.IsDisposed)
            lines.Add(
                "focused=" + string.Join(",", Focused(session.Composition.SemanticSnapshot()))
            );
        lines.AddRange(details);
        File.WriteAllLines(
            Path.Combine(_output, $"{Environment.ProcessId}-{++_event:D4}-{kind}.txt"),
            lines
        );
    }

    private static IEnumerable<string> Focused(SemanticSnapshot? node)
    {
        if (node is null)
            yield break;
        if (node.Focused)
            yield return $"{node.Identity.ElementId}:{node.Name}";
        foreach (var child in node.Children)
        foreach (var value in Focused(child))
            yield return value;
    }

    private static RouteBundle Bundle()
    {
        var source = new RouteDeclarationSource("visible-probe", 1, 1);
        var definitions = new List<RouteDefinitionDescriptor>();
        foreach (var name in new[] { "home", "second" })
        {
            var pattern = RoutePattern.Create(
                new(name),
                [RouteSegmentPattern.LiteralSegment(name)]
            );
            var level = new RouteLevelDescriptor(
                pattern.Id,
                [],
                source,
                static (definition, _, live) => new RouteContext<string>(definition, "", live),
                static (context, content) => Context.Provide((RouteContext<string>)context, content)
            );
            definitions.Add(new(pattern, [level]));
        }
        return RouteBundle.Create(
            [
                new RouteModuleDescriptor(
                    "visible-probe",
                    RouteFallbackPolicy.Reject,
                    source,
                    definitions
                ),
            ],
            level => new RouteDestination(
                typeof(Program),
                Components.Text("Active route: " + level.Id.Value),
                level.Id
            )
        );
    }
}
