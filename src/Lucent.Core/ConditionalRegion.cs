using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Lucent.Core;

/// <summary>A retained zero-or-one child region.</summary>
public sealed class ConditionalRegion : IDisposable
{
    private readonly Composition _composition;
    private Func<bool>? _active;
    private Func<CompositionContext, Element>? _content;
    private Func<ConditionalChoice>? _select;
    private int _branch = Int32.MinValue;
    private readonly ReactiveEffect _effect;
    private Element? _child;
    private MountedConditionalPayload? _payload;
    private long _nextPayloadId;
    private bool _updating;

    internal ConditionalRegion(
        Composition composition,
        Element parent,
        string name,
        Func<bool> active,
        Func<CompositionContext, Element> content,
        CompositionContext? factory = null,
        ThemeContext? theme = null
    )
    {
        _composition = composition;
        Region = composition.Create(parent, name, attach: true, factory);
        Theme = theme;
        _active = active;
        _content = content;
        Region.Scope.Own(this);
        _effect = Region.Scope.Effect(Refresh, name + ".condition");
    }

    internal ConditionalRegion(
        Composition composition,
        Element parent,
        string name,
        Func<ConditionalChoice> select,
        CompositionContext? factory = null,
        ThemeContext? theme = null
    )
        : this(
            composition,
            parent,
            name,
            static () => false,
            static _ => throw new InvalidOperationException(),
            factory,
            theme
        )
    {
        _select = select;
    }

    /// <summary>Gets the persistent region element that owns mounted content.</summary>
    public Element Region { get; }

    /// <summary>Gets the currently mounted conditional child, if active.</summary>
    public Element? Active => _child;

    /// <summary>Gets whether this retained owner has released its children and reactive resources.</summary>
    public bool IsDisposed { get; private set; }
    private ThemeContext? Theme { get; }

    /// <summary>Re-evaluates the condition. Usual callers let the owned effect invoke this.</summary>
    public void Refresh()
    {
        _composition.CheckThread();
        _composition.ThrowIfBehaviorAttachment();
        _composition.RejectForeignFactory(Region);
        if (_select is not null)
            Update(_select());
        else
            Update(_active!());
    }

    /// <summary>Reconciles the retained region with the supplied current source.</summary>
    public void Update(bool active)
    {
        _composition.CheckThread();
        _composition.ThrowIfBehaviorAttachment();
        _composition.RejectForeignFactory(Region);
        if (IsDisposed)
            return;
        if (_updating)
            throw new InvalidOperationException("A conditional region cannot update reentrantly.");
        _updating = true;
        try
        {
            if (_child?.IsDisposed == true)
            {
                _child = null;
                _payload = null;
            }
            if (active == (_child is not null))
                return;
            if (!active)
            {
                var departed = _child!;
                _child = null;
                _payload = null;
                Region.ReplaceChildren([]);
                departed.Dispose();
                return;
            }

            var created = Create(_content!);
            _child = created;
            created.OnDisposed(() =>
            {
                if (ReferenceEquals(_child, created))
                    _child = null;
            });
            Region.ReplaceChildren([created]);
        }
        finally
        {
            _updating = false;
        }
    }

    private void Update(ConditionalChoice choice)
    {
        _composition.CheckThread();
        _composition.ThrowIfBehaviorAttachment();
        _composition.RejectForeignFactory(Region);
        if (IsDisposed)
            return;
        if (_updating)
            throw new InvalidOperationException("A conditional region cannot update reentrantly.");
        _updating = true;
        try
        {
            if (_child?.IsDisposed == true)
            {
                _child = null;
                _payload = null;
            }

            if (_branch == choice.Branch && _child is not null)
            {
                if (choice.Payload is null && choice.Recipe is not null && _payload is null)
                    return;
                if (
                    choice.Payload is null
                    || choice.Recipe is not null
                    || _payload is null
                    || _payload.ValueType != choice.Payload.ValueType
                )
                    throw new InvalidOperationException(
                        "A retained conditional branch cannot change its payload shape or type."
                    );

                _payload.Stage(choice.Payload);
                _payload.Notify();
                return;
            }

            if (choice.Payload is null && choice.Recipe is null)
            {
                var departed = _child;
                _child = null;
                _payload = null;
                _branch = choice.Branch;
                if (departed is not null)
                {
                    Region.ReplaceChildren([]);
                    departed.Dispose();
                }
                return;
            }

            MountedConditionalPayload? mounted = null;
            try
            {
                mounted = choice.Payload?.Mount(
                    Region.Scope,
                    Region.Name
                        + ".current-item-"
                        + checked(++_nextPayloadId).ToString(CultureInfo.InvariantCulture)
                );
                var recipe = mounted?.Recipe ?? choice.Recipe!;
                var created = Create(recipe.Mount);
                var prior = _child;
                var priorPayload = _payload;
                _child = created;
                _payload = mounted;
                _branch = choice.Branch;
                created.OnDisposed(() =>
                {
                    mounted?.Dispose();
                    if (ReferenceEquals(_child, created))
                    {
                        _child = null;
                        _payload = null;
                    }
                });
                Region.ReplaceChildren([created]);

                List<Exception>? errors = null;
                try
                {
                    prior?.Dispose();
                }
                catch (Exception error)
                {
                    (errors ??= []).Add(error);
                }
                if (prior is null)
                    try
                    {
                        priorPayload?.Dispose();
                    }
                    catch (Exception error)
                    {
                        (errors ??= []).Add(error);
                    }
                Composition.ThrowAll(errors, "Conditional region cleanup failed.");
            }
            catch
            {
                if (!ReferenceEquals(_payload, mounted))
                    mounted?.Dispose();
                throw;
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private Element Create(Func<CompositionContext, Element> content)
    {
        var context = new CompositionContext(_composition, Region, Theme);
        try
        {
            var created = context.Run(() => content(context));
            ObjectDisposedException.ThrowIf(
                IsDisposed || Region.IsDisposed,
                typeof(ConditionalRegion)
            );
            context.Commit(created);
            context.Dispose();
            return created;
        }
        catch (Exception error)
        {
            var errors = new List<Exception> { error };
            try
            {
                context.Dispose();
            }
            catch (Exception cleanup)
            {
                errors.Add(cleanup);
            }
            Composition.ThrowAll(errors, "Conditional region factory failed.");
            throw;
        }
    }

    /// <summary>Releases this object's retained resources and owned reactive lifetime.</summary>
    public void Dispose()
    {
        _composition.CheckThread();
        _composition.ThrowIfBehaviorAttachment();
        _composition.RejectForeignFactory(Region);
        if (IsDisposed)
            return;
        IsDisposed = true;
        List<Exception>? errors = null;
        try
        {
            _effect.Dispose();
        }
        catch (Exception exception)
        {
            (errors ??= []).Add(exception);
        }
        var departed = _child;
        var payload = _payload;
        _child = null;
        _payload = null;
        if (!Region.IsDisposed)
            Region.ReplaceChildren([]);
        try
        {
            departed?.Dispose();
        }
        catch (Exception exception)
        {
            (errors ??= []).Add(exception);
        }
        if (departed is null)
            try
            {
                payload?.Dispose();
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        _active = null!;
        _content = null!;
        _select = null;
        Region.Scope.Detach(this);
        Composition.ThrowAll(errors, "Conditional region cleanup failed.");
    }
}
