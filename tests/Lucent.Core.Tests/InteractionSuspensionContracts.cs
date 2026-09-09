using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class InteractionSuspensionContracts
{
    [TestMethod]
    public void ModalLeaseBlocksCommandsButLeavesApplicationStateAndRenderingAlive()
    {
        using var composition = new Composition(new ReactiveGraph(), "interaction-modal");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var calls = 0;
        composition.Mount(composition.Root, theme, Components.Button("Background", () => calls++));
        composition.Flush();
        var button = FindButton(composition.SemanticSnapshot()!);
        using var first = composition.SuspendInteraction();
        using var second = composition.SuspendInteraction();
        Assert.IsTrue(composition.IsInteractionSuspended);
        Assert.IsFalse(FindButton(composition.SemanticSnapshot()!).Enabled);
        Assert.AreEqual(
            SemanticCommandResult.Disabled,
            composition.ExecuteSemanticCommand(button.Identity, new(SemanticCommandKind.Invoke))
        );
        var state = composition.Root.Scope.Signal(0, "background-work");
        state.Value = 1;
        composition.Flush();
        Assert.AreEqual(1, state.Value);
        first.Dispose();
        Assert.IsTrue(composition.IsInteractionSuspended);
        second.Dispose();
        Assert.IsFalse(composition.IsInteractionSuspended);
        button = FindButton(composition.SemanticSnapshot()!);
        Assert.IsTrue(button.Enabled);
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(button.Identity, new(SemanticCommandKind.Invoke))
        );
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void LeaseDisposalAfterOwnerDestructionIsIdempotent()
    {
        using var composition = new Composition(new ReactiveGraph(), "modal-owner-end");
        var lease = composition.SuspendInteraction();
        composition.Dispose();
        lease.Dispose();
        lease.Dispose();
    }

    private static SemanticSnapshot FindButton(SemanticSnapshot root) =>
        root.Role == SemanticRole.Button
            ? root
            : root.Children.Select(FindButtonOrNull).First(node => node is not null)!;

    private static SemanticSnapshot? FindButtonOrNull(SemanticSnapshot root) =>
        root.Role == SemanticRole.Button
            ? root
            : root.Children.Select(FindButtonOrNull).FirstOrDefault(node => node is not null);
}
