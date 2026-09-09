namespace Lucent.Core;

public sealed partial class Composition
{
    private int _interactionSuspensions;

    /// <summary>Whether an owner-modal surface currently blocks input and semantic actions in this composition.</summary>
    public bool IsInteractionSuspended => _interactionSuspensions != 0;

    /// <summary>Blocks background pointer, keyboard, text and automation commands until the returned owner-thread lease is released.</summary>
    /// <remarks>Rendering and application work continue. The surface host restores a still-valid focus identity after releasing its lease. Nested leases cannot prematurely unblock the owner.</remarks>
    public IDisposable SuspendInteraction()
    {
        CheckThread();
        ThrowIfDisposed();
        _interactionSuspensions = checked(_interactionSuspensions + 1);
        try
        {
            if (_interactionSuspensions == 1)
            {
                InvalidateSemantics();
                _input?.SuspendInteraction();
            }
            return new InteractionLease(this);
        }
        catch
        {
            _interactionSuspensions--;
            InvalidateSemantics();
            throw;
        }
    }

    private static SemanticSnapshot DisableInteraction(SemanticSnapshot node) =>
        node with
        {
            Enabled = false,
            Focused = false,
            Children = Array.AsReadOnly(node.Children.Select(DisableInteraction).ToArray()),
        };

    private sealed class InteractionLease(Composition owner) : IDisposable
    {
        private Composition? _owner = owner;

        public void Dispose()
        {
            if (_owner is not { } current)
                return;
            current.CheckThread();
            _owner = null;
            current._interactionSuspensions--;
            if (!current.IsDisposed && current._interactionSuspensions == 0)
                current.InvalidateSemantics();
        }
    }
}
