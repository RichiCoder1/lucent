namespace Lucent.Core;

/// <summary>Typed mount context shared by a field and its one primary editor.</summary>
public sealed class FieldContext
{
    private readonly Derived<ValidationState> _validation;
    private readonly Signal<bool> _touched;
    private readonly Signal<long> _relationshipRevision;
    private ElementIdentity? _label;
    private ElementIdentity? _help;
    private ElementIdentity? _error;
    private readonly FormSession? _form;
    private ElementIdentity? _editor;

    internal FieldContext(
        ReactiveScope owner,
        string id,
        string accessibleName,
        Func<ValidationState>? validation,
        FormSession? form
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        Id = Required(id, nameof(id));
        AccessibleName = Required(accessibleName, nameof(accessibleName));
        _form = form;
        FocusTarget = owner.Own(new FocusTarget(owner, id + ".focus"));
        _validation = owner.Derived(
            () => validation?.Invoke() ?? ValidationState.Valid,
            id + ".validation"
        );
        _touched = owner.Signal(false, id + ".touched");
        _relationshipRevision = owner.Signal(0L, id + ".relationship-revision");
    }

    /// <summary>Gets the stable identity used by an optional containing form session.</summary>
    public string Id { get; }

    /// <summary>Gets the accessible name without visual suffixes or required markers.</summary>
    public string AccessibleName { get; }

    /// <summary>Gets the retained focus capability shared by label activation and the primary editor.</summary>
    public FocusTarget FocusTarget { get; }

    /// <summary>Gets the current immutable application validation result.</summary>
    public ValidationState Validation =>
        _validation.Value
        ?? throw new InvalidOperationException("A validation reader returned null.");

    /// <summary>Gets whether the primary editor has lost focus at least once during this mount.</summary>
    public bool IsTouched => _touched.Value;

    /// <summary>Gets whether the current errors should be presented after blur or submit.</summary>
    public bool ShowErrors =>
        Validation.IsInvalid && (IsTouched || (_form?.SubmitAttempts ?? 0) != 0);

    /// <summary>Gets stable relationships for the primary editor's existing semantic declaration.</summary>
    public SemanticRelationships Relationships
    {
        get
        {
            var validation = Validation;
            var show = ShowErrors;
            _ = _relationshipRevision.Value;
            var errors = show && _error is { } error ? new[] { error } : [];
            return new(
                _label,
                _help,
                errors,
                HelpText,
                show ? ErrorText : null,
                show && validation.IsInvalid
            );
        }
    }

    internal string? HelpText { get; set; }
    internal string ErrorText => string.Join(Environment.NewLine, Validation.Messages);

    internal string ReadErrorText() => ErrorText;

    internal void AttachFieldRoot(Element root, FieldParticipation participation) =>
        _form?.Register(Id, root, () => Validation, FocusTarget, participation);

    internal void AttachLabel(Element element) => SetIdentity(ref _label, element);

    internal void AttachHelp(Element element) => SetIdentity(ref _help, element);

    internal void AttachError(Element element)
    {
        SetIdentity(ref _error, element);
        var published = false;
        _ = element.Scope.Effect(
            () =>
            {
                if (published)
                    return;
                published = true;
                _relationshipRevision.Value = checked(_relationshipRevision.Value + 1);
            },
            element.Name + ".relationship"
        );
    }

    internal void AttachEditor(Element element)
    {
        var identity = Identity(element);
        if (_editor is not null && _editor != identity)
            throw new InvalidOperationException(
                "A FieldContext permits exactly one primary editor."
            );
        _editor = identity;
        element.Scope.OnDispose(() =>
        {
            if (_editor == identity)
                _editor = null;
        });
    }

    internal void Blur() => _touched.Value = true;

    private static void SetIdentity(ref ElementIdentity? target, Element element)
    {
        target = Identity(element);
    }

    private static ElementIdentity Identity(Element element) =>
        new(element.Composition.Epoch, element.Id);

    private static string Required(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(
                "A field identity and accessible name are required.",
                parameter
            );
        return value;
    }
}
