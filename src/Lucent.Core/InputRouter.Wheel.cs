namespace Lucent.Core;

/// <summary>The per-callback view of a routed finite wheel command.</summary>
public sealed class WheelRoute
{
    private bool _active = true;
    private bool _handled;

    internal WheelRoute(
        WheelCommand command,
        ElementIdentity target,
        ElementIdentity current,
        IEnumerable<ElementIdentity> route
    )
    {
        Command = command;
        Target = target;
        CurrentTarget = current;
        Route = Array.AsReadOnly(route.ToArray());
    }

    /// <summary>Gets the portable wheel command.</summary>
    public WheelCommand Command { get; }

    /// <summary>Gets the hit-tested target.</summary>
    public ElementIdentity Target { get; }

    /// <summary>Gets the retained element whose callback is running.</summary>
    public ElementIdentity CurrentTarget { get; }

    /// <summary>Gets the immutable target-to-root propagation path.</summary>
    public IReadOnlyList<ElementIdentity> Route { get; }

    /// <summary>Gets or sets whether the callback consumed the entire wheel command.</summary>
    public bool Handled
    {
        get
        {
            Check();
            return _handled;
        }
        set
        {
            Check();
            _handled = value;
        }
    }

    internal bool Finish()
    {
        _active = false;
        return _handled;
    }

    private void Check()
    {
        if (!_active)
            throw new InvalidOperationException(
                "A routed wheel context expires when its callback returns."
            );
    }
}

public sealed partial class InputRouter
{
    private bool RouteWheel(
        WheelCommand command,
        ElementIdentity target,
        ElementIdentity[] route,
        List<Exception> errors
    )
    {
        foreach (var callback in Snapshot(_wheel, route))
        {
            if (!Available(callback.Identity))
                continue;
            var context = new WheelRoute(command, target, callback.Identity, route);
            try
            {
                callback.Callback(context);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
            if (context.Finish())
                return true;
        }
        return false;
    }
}
