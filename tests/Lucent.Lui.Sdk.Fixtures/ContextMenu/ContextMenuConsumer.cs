using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lucent.Core;
using SdkMenus;

namespace Consumer;

internal static class ContextMenuConsumer
{
    public static void Run()
    {
        var graph = new ReactiveGraph();
        using var owner = new Composition(graph, "sdk-context-menu");
        using var theme = new ThemeContext(owner.Root.Scope, ControlThemes.Light);
        var enabled = owner.Root.Scope.Signal(false, "menu-enabled");
        var commandRuns = 0;
        var actionRuns = 0;
        using var command = new ApplicationCommand(
            owner.Root.Scope,
            _ =>
            {
                commandRuns++;
                return Task.CompletedTask;
            },
            () => enabled.Value,
            "sdk-command"
        );

        owner.Mount(
            owner.Root,
            theme,
            SdkMenus.Components.Surface(command, () => actionRuns++, () => enabled.Value, false)
        );
        owner.Flush();
        Install(owner);
        Require(
            owner.Input.CursorAt(10, 10) == CursorIntent.Pointer,
            "The .lui Cursor style did not reach the mounted target."
        );

        ContextMenuRequest? request = null;
        owner.Input.ContextMenuRequested += value => request = value;
        OpenContextMenu(owner);
        var opened =
            request
            ?? throw new InvalidOperationException(
                "The .lui ContextMenu binding did not register."
            );
        using (opened)
        {
            var popup = opened.CreateComposition();
            Install(popup);
            Require(
                popup.Root.Children.Count == 1 && popup.Root.Children[0].Children.Count == 3,
                "The .lui MenuSeparator changed the authored menu child structure."
            );
            var initial = Nodes(popup.SemanticSnapshot()!);
            var commandNode = initial.Single(node => node.Name == "Command");
            var actionNode = initial.Single(node => node.Name == "Action");
            Require(
                !commandNode.Enabled && !actionNode.Enabled,
                "Disabled .lui menu inputs were enabled."
            );
            Require(
                popup.ExecuteSemanticCommand(commandNode.Identity, new(SemanticCommandKind.Invoke))
                    == SemanticCommandResult.Disabled,
                "A disabled .lui command menu item executed."
            );

            enabled.Value = true;
            owner.Flush();
            Install(popup);
            var live = Nodes(popup.SemanticSnapshot()!);
            commandNode = live.Single(node => node.Name == "Command");
            actionNode = live.Single(node => node.Name == "Action");
            Require(
                commandNode.Enabled && actionNode.Enabled,
                "Live .lui enabled inputs did not update."
            );
            Require(
                popup.ExecuteSemanticCommand(commandNode.Identity, new(SemanticCommandKind.Invoke))
                    == SemanticCommandResult.Applied,
                "The .lui ApplicationCommand menu item did not invoke."
            );
            owner.Flush();
            Require(commandRuns == 1, "The .lui ApplicationCommand action did not run once.");
        }

        request = null;
        OpenContextMenu(owner);
        var reopened =
            request
            ?? throw new InvalidOperationException("The .lui ContextMenu could not be reopened.");
        using (reopened)
        {
            var popup = reopened.CreateComposition();
            Install(popup);
            var actionNode = Nodes(popup.SemanticSnapshot()!).Single(node => node.Name == "Action");
            Require(
                popup.ExecuteSemanticCommand(actionNode.Identity, new(SemanticCommandKind.Invoke))
                    == SemanticCommandResult.Applied,
                "The .lui Action menu item did not invoke."
            );
            Require(actionRuns == 1, "The .lui Action menu item did not run once.");
        }

        Console.WriteLine("context menu SDK proof: PASS");
    }

    private static void OpenContextMenu(Composition owner)
    {
        owner.Input.DispatchPointer(
            new(PointerCommandKind.Down, 1, 10, 10, PointerButton.Secondary)
        );
        Install(owner);
        owner.Input.DispatchPointer(new(PointerCommandKind.Up, 1, 10, 10));
    }

    private static void Install(Composition composition)
    {
        for (var attempt = 0; attempt < 3; attempt++)
            if (
                composition.Input.SetScene(
                    SceneLayout.Project(composition, new(800, 600, 1), new EmptyShaper())
                )
            )
                return;
        throw new InvalidOperationException("Scene installation failed.");
    }

    private static IEnumerable<SemanticSnapshot> Nodes(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var nested in Nodes(child))
            yield return nested;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            if (request.Text.Length == 0)
                return new("empty", 0, 0, []);
            var glyph = new ShapedGlyph(1, 0, 0, 0, request.Text.Length, 0, 0);
            var run = new ShapedRun(
                "controls",
                "controls",
                400,
                5,
                0,
                "controls",
                0,
                "controls#0",
                request.Direction,
                request.Language,
                request.FontSize,
                0,
                request.FontSize,
                -request.FontSize,
                0,
                request.Text.Length,
                [glyph]
            );
            return new("controls", request.Text.Length, request.FontSize, [run]);
        }
    }
}
