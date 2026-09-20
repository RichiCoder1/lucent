namespace Lucent.Core;

/// <summary>Resolves declared closed reference-type services from a lifecycle-owned source.</summary>
public interface IComponentServiceSource
{
    /// <summary>Resolves one exact declared service type synchronously.</summary>
    T Resolve<T>()
        where T : class;
}

/// <summary>Optionally resolves declared closed reference-type services without treating absence as failure.</summary>
/// <remarks>
/// Implementations must return <see langword="false"/> only when the exact service is absent.
/// Provider activation and ownership failures must escape from <see cref="TryResolve{T}"/>.
/// </remarks>
public interface IOptionalComponentServiceSource : IComponentServiceSource
{
    /// <summary>Attempts to resolve one exact declared service type synchronously.</summary>
    bool TryResolve<T>(out T? value)
        where T : class;
}

/// <summary>Controls one lifecycle-owned service source attached to one application root mount.</summary>
public sealed class ComponentServiceBinding
{
    private readonly ApplicationSession _owner;
    private IComponentServiceSource? _source;
    private BindingState _state;
    private int _popupBorrowers;

    internal ComponentServiceBinding(ApplicationSession owner, IComponentServiceSource source)
    {
        _owner = owner;
        _source = source;
    }

    /// <summary>Attaches this binding to one root recipe without resolving a service.</summary>
    public ComponentRecipe Attach(ComponentRecipe root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _owner.CheckOwnerForServices();
        if (_state != BindingState.Created)
            throw new InvalidOperationException(
                "An application service binding can be attached to exactly one root recipe."
            );
        _state = BindingState.Attached;
        return root.WithServiceBinding(this);
    }

    /// <summary>Rejects new mounts and service resolutions while preserving already borrowed values.</summary>
    public void StopAccepting()
    {
        _owner.CheckOwnerForServices();
        if (_state == BindingState.Revoked)
            return;
        _state = BindingState.Stopped;
    }

    /// <summary>Revokes the source after all borrowing composition roots have been disposed.</summary>
    public void Revoke()
    {
        _owner.CheckOwnerForServices();
        if (_state == BindingState.Revoked)
            return;
        if (_state != BindingState.Stopped)
            throw new InvalidOperationException(
                "An application service binding must stop accepting requests before revocation."
            );
        if (!_owner.Composition.IsDisposed)
            throw new InvalidOperationException(
                "An application service binding cannot be revoked while its composition is alive."
            );
        if (_popupBorrowers != 0)
            throw new InvalidOperationException(
                "An application service binding cannot be revoked while popup compositions are borrowing it."
            );
        _source = null;
        _state = BindingState.Revoked;
    }

    internal void ClaimMount(Composition composition, Element parent)
    {
        _owner.CheckOwnerForServices();
        if (!ReferenceEquals(composition, _owner.Composition))
            throw new InvalidOperationException(
                "An application service binding cannot cross its owning application session."
            );
        if (!ReferenceEquals(parent, composition.Root))
            throw new InvalidOperationException(
                "An application service binding can be installed only at its composition root."
            );
        if (_state == BindingState.Stopped)
            throw new InvalidOperationException(
                "The application service binding is no longer accepting mounts."
            );
        if (_state == BindingState.Revoked)
            throw new InvalidOperationException("The application service binding was revoked.");
        if (_state != BindingState.Attached)
            throw new InvalidOperationException(
                "An application service binding can mount its attached root exactly once."
            );
        _state = BindingState.Mounted;
    }

    internal T Resolve<T>(ComponentRequirementSource requirement, string mountPath)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mountPath);
        _owner.CheckOwnerForServices();
        if (_state == BindingState.Stopped)
            throw new InvalidOperationException(
                $"Application services stopped before requirement '{requirement.Member}' could resolve."
            );
        if (_state == BindingState.Revoked)
            throw new InvalidOperationException(
                $"Application services were revoked before requirement '{requirement.Member}' could resolve."
            );
        if (_state != BindingState.Mounted)
            throw new InvalidOperationException(
                "Application services are available only while the attached root is mounting."
            );
        T value;
        try
        {
            value = (
                _source ?? throw new InvalidOperationException("The service source was revoked.")
            ).Resolve<T>();
        }
        catch (Exception error)
        {
            throw new InvalidOperationException(
                $"Application service requirement '{requirement.Member}' for exact type '{requirement.TypeName}' failed at {requirement.FilePath}:{requirement.Line}:{requirement.Column} while mounting {mountPath}.",
                error
            );
        }
        return value ?? throw requirement.NullResult("application service source");
    }

    internal T? ResolveOptional<T>(ComponentRequirementSource requirement, string mountPath)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mountPath);
        _owner.CheckOwnerForServices();
        if (_state == BindingState.Stopped)
            throw new InvalidOperationException(
                $"Application services stopped before optional requirement '{requirement.Member}' could resolve."
            );
        if (_state == BindingState.Revoked)
            throw new InvalidOperationException(
                $"Application services were revoked before optional requirement '{requirement.Member}' could resolve."
            );
        if (_state != BindingState.Mounted)
            throw new InvalidOperationException(
                "Application services are available only while the attached root is mounting."
            );
        var source =
            _source ?? throw new InvalidOperationException("The service source was revoked.");
        if (source is not IOptionalComponentServiceSource optional)
            throw new InvalidOperationException(
                $"Application service requirement '{requirement.Member}' for exact type '{requirement.TypeName}' cannot determine absence because its source does not support optional resolution."
            );
        try
        {
            if (!optional.TryResolve<T>(out var value))
            {
                if (value is not null)
                    throw new InvalidOperationException(
                        "An optional application service source returned a value while reporting the service absent."
                    );
                return null;
            }
            return value ?? throw requirement.NullResult("optional application service source");
        }
        catch (Exception error)
        {
            throw new InvalidOperationException(
                $"Optional application service requirement '{requirement.Member}' for exact type '{requirement.TypeName}' failed at {requirement.FilePath}:{requirement.Line}:{requirement.Column} while mounting {mountPath}.",
                error
            );
        }
    }

    internal void CheckMountAdmission()
    {
        _owner.CheckOwnerForServices();
        if (_state == BindingState.Stopped)
            throw new InvalidOperationException(
                "The application service binding is no longer accepting mounts."
            );
        if (_state == BindingState.Revoked)
            throw new InvalidOperationException("The application service binding was revoked.");
        if (_state != BindingState.Mounted)
            throw new InvalidOperationException(
                "The application service binding has not mounted its attached root."
            );
    }

    internal IDisposable BorrowForPopup(Composition popup)
    {
        ArgumentNullException.ThrowIfNull(popup);
        _owner.CheckOwnerForServices();
        if (ReferenceEquals(popup, _owner.Composition))
            throw new ArgumentException(
                "The application root composition cannot borrow its own service binding.",
                nameof(popup)
            );
        if (!ReferenceEquals(popup.Graph, _owner.Composition.Graph))
            throw new InvalidOperationException(
                "A popup service borrower must share its application session's reactive graph."
            );
        CheckMountAdmission();
        _popupBorrowers = checked(_popupBorrowers + 1);
        return new PopupBorrower(this);
    }

    private void ReleasePopupBorrower()
    {
        _owner.CheckOwnerForServices();
        if (_popupBorrowers <= 0)
            throw new InvalidOperationException("A popup service borrower was released twice.");
        _popupBorrowers--;
    }

    private sealed class PopupBorrower(ComponentServiceBinding binding) : IDisposable
    {
        private ComponentServiceBinding? _binding = binding;

        public void Dispose() => Interlocked.Exchange(ref _binding, null)?.ReleasePopupBorrower();
    }

    private enum BindingState
    {
        Created,
        Attached,
        Mounted,
        Stopped,
        Revoked,
    }
}
