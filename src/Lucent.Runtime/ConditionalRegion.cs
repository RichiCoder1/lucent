using Avalonia.Controls;

namespace Lucent.Runtime;

public sealed class ConditionalRegion : IDisposable
{
    private readonly ComponentOwner _owner;
    private readonly Action<Control?> _setRoot;
    private ComponentOwner? _branchOwner;
    private Control? _root;

    public ConditionalRegion(ComponentOwner owner, Action<Control?> setRoot)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(setRoot);
        _owner = owner;
        _setRoot = setRoot;
        owner.OnDispose(Dispose);
    }

    public int? ActiveBranch { get; private set; }

    public void Show(int branch, Func<ComponentOwner, Control> mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        ObjectDisposedException.ThrowIf(_owner.IsDisposed, this);
        if (ActiveBranch == branch)
        {
            return;
        }

        var nextOwner = _owner.CreateChild();
        Control nextRoot;
        try
        {
            nextRoot = mount(nextOwner) ?? throw new InvalidOperationException("A conditional branch returned no root control.");
        }
        catch (Exception mountFailure)
        {
            throw DisposeAfterFailure(nextOwner, mountFailure);
        }

        Publish(nextRoot, nextOwner, branch);
    }

    public void Clear()
    {
        if (ActiveBranch is null)
        {
            return;
        }

        Publish(null, null, null);
    }

    public void Dispose()
    {
        var oldOwner = ClearState();
        oldOwner?.Dispose();
    }

    private void Publish(Control? nextRoot, ComponentOwner? nextOwner, int? nextBranch)
    {
        var oldOwner = _branchOwner;
        var oldRoot = _root;
        try
        {
            _setRoot(nextRoot);
        }
        catch (Exception publicationFailure)
        {
            try
            {
                _setRoot(oldRoot);
            }
            catch (Exception restorationFailure)
            {
                ClearState();
                var failures = new List<Exception> { publicationFailure, restorationFailure };
                DisposeInto(nextOwner, failures);
                DisposeInto(oldOwner, failures);
                throw new AggregateException(failures);
            }

            throw nextOwner is null
                ? publicationFailure
                : DisposeAfterFailure(nextOwner, publicationFailure);
        }

        _branchOwner = nextOwner;
        _root = nextRoot;
        ActiveBranch = nextBranch;
        oldOwner?.Dispose();
    }

    private ComponentOwner? ClearState()
    {
        var oldOwner = _branchOwner;
        _branchOwner = null;
        _root = null;
        ActiveBranch = null;
        return oldOwner;
    }

    private static Exception DisposeAfterFailure(ComponentOwner owner, Exception failure)
    {
        try
        {
            owner.Dispose();
            return failure;
        }
        catch (Exception cleanupFailure)
        {
            return new AggregateException(failure, cleanupFailure);
        }
    }

    private static void DisposeInto(ComponentOwner? owner, List<Exception> failures)
    {
        try
        {
            owner?.Dispose();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
    }
}
