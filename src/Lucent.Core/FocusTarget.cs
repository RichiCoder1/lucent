namespace Lucent.Core;

/// <summary>Application-owned focus intent for one mounted text control.</summary>
/// <remarks>
/// A request remains pending until the target is present, enabled, and focusable in an installed scene.
/// The request is consumed after focus is accepted, so retained-session remounts do not replay an old focus.
/// </remarks>
public sealed class FocusTarget : IDisposable
{
    private readonly ReactiveScope _scope;
    private readonly Signal<FocusTargetRequest?> _pending;
    private long _nextGeneration;

    /// <summary>Creates a focus target owned by the supplied application scope.</summary>
    public FocusTarget(ReactiveScope owner, string name = "focus-target")
    {
        ArgumentNullException.ThrowIfNull(owner);
        ReactiveGraph.ValidateName(name, nameof(name));
        _scope = owner.CreateChild(name);
        _pending = _scope.Signal<FocusTargetRequest?>(null, name + ".pending");
    }

    /// <summary>Gets whether a focus request is waiting for an eligible mounted target.</summary>
    public bool IsPending
    {
        get
        {
            _scope.Graph.CheckThread();
            ObjectDisposedException.ThrowIf(_scope.IsDisposed, this);
            return _pending.Value is not null;
        }
    }

    /// <summary>Requests keyboard focus, optionally selecting the target text after focus is accepted.</summary>
    public void Request(bool selectAll = false)
    {
        _scope.CheckMutationGuard();
        ObjectDisposedException.ThrowIf(_scope.IsDisposed, this);
        _pending.Value = new(checked(++_nextGeneration), selectAll);
    }

    /// <summary>Withdraws the pending request without changing focus.</summary>
    public void Cancel()
    {
        _scope.CheckMutationGuard();
        ObjectDisposedException.ThrowIf(_scope.IsDisposed, this);
        if (_pending.Value is not null)
            _pending.Value = null;
    }

    /// <summary>Releases this focus request and its reactive lifetime.</summary>
    public void Dispose() => _scope.Dispose();

    internal ReactiveGraph Graph => _scope.Graph;

    internal bool TryGetPending(out FocusTargetRequest request)
    {
        _scope.Graph.CheckThread();
        if (_scope.IsDisposed)
        {
            request = default;
            return false;
        }
        if (_pending.Value is { } pending)
        {
            request = pending;
            return true;
        }
        request = default;
        return false;
    }

    internal bool TryConsume(long generation)
    {
        if (_scope.IsDisposed)
            return false;
        _scope.CheckMutationGuard();
        if (_pending.Value is not { } pending || pending.Generation != generation)
            return false;
        _pending.Value = null;
        return true;
    }
}

internal readonly record struct FocusTargetRequest(long Generation, bool SelectAll);
