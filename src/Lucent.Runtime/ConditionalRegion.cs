namespace Lucent.Runtime;

public sealed class ConditionalRegion : IDisposable
{
    private readonly ComponentOwner _owner;
    private readonly Action<Fragment> _setRoots;
    private ComponentOwner? _branchOwner;
    private Fragment _roots;

    public ConditionalRegion(ComponentOwner owner, Action<Fragment> setRoots)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(setRoots);
        _owner = owner;
        _setRoots = setRoots;
        owner.OnDispose(Dispose);
    }

    public int? ActiveBranch { get; private set; }

    public void Show(int branch, Func<ComponentOwner, Fragment> mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        ObjectDisposedException.ThrowIf(_owner.IsDisposed, this);
        if (ActiveBranch == branch)
        {
            return;
        }

        var nextOwner = _owner.CreateChild();
        Fragment nextRoots;
        try
        {
            nextRoots = mount(nextOwner);
        }
        catch (Exception mountFailure)
        {
            throw DisposeAfterFailure(nextOwner, mountFailure);
        }

        Publish(nextRoots, nextOwner, branch);
    }

    public void Clear()
    {
        if (ActiveBranch is null)
        {
            return;
        }

        Publish(Fragment.Empty, null, null);
    }

    public void Dispose()
    {
        var oldOwner = ClearState();
        oldOwner?.Dispose();
    }

    private void Publish(Fragment nextRoots, ComponentOwner? nextOwner, int? nextBranch)
    {
        var oldOwner = _branchOwner;
        var oldRoots = _roots;
        try
        {
            _setRoots(nextRoots);
        }
        catch (Exception publicationFailure)
        {
            try
            {
                _setRoots(oldRoots);
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
        _roots = nextRoots;
        ActiveBranch = nextBranch;
        oldOwner?.Dispose();
    }

    private ComponentOwner? ClearState()
    {
        var oldOwner = _branchOwner;
        _branchOwner = null;
        _roots = Fragment.Empty;
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
