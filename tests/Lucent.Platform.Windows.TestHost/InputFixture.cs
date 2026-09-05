using Lucent.Core;
using Lucent.Platform.Windows;

namespace Lucent.Platform.Windows.TestHost;

/// <summary>Published .lui command scope and real Windows keyboard/wheel input fixture.</summary>
internal static class InputFixture
{
    internal static int Run()
    {
        try
        {
            var graph = new ReactiveGraph();
            using var composition = new Composition(graph, "input-fixture");
            var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            var model = new InputFixtureModel(composition.Root.Scope);
            composition.Mount(
                composition.Root,
                theme,
                LuiFixtures.Components.InputFixtureView(model)
            );
            return WindowsBootstrap.Run("Lucent Input Fixture", composition, theme);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Lucent input fixture: " + error.Message);
            return 1;
        }
    }
}

internal sealed class InputFixtureModel
{
    internal InputFixtureModel(ReactiveScope owner)
    {
        Status = owner.Signal("Ready", "input-fixture.status");
        Viewport = new ViewportState(owner, new ScrollOffset(0, 40), "input-fixture.viewport");
        var capture = Create(owner, "Capture");
        var find = Create(owner, "Find");
        var save = Create(owner, "Save");
        Bindings = new([
            new(capture, KeyChord.Ctrl(Key.N)),
            new(find, KeyChord.Ctrl(Key.F)),
            new(save, KeyChord.Ctrl(Key.S)),
        ]);
    }

    public Signal<string> Status { get; }
    public ViewportState Viewport { get; }
    public CommandBindings Bindings { get; }

    private ApplicationCommand Create(ReactiveScope owner, string action) =>
        new(
            owner,
            _ =>
            {
                Status.Value = action;
                return Task.CompletedTask;
            },
            name: "input-fixture." + action.ToLowerInvariant()
        );
}
