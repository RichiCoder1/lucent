namespace Lucent.Core;

/// <summary>Portable modal surface request retained by one dialog controller.</summary>
public sealed class DialogSurfaceRequest : PopupSurfaceRequest
{
    private readonly DialogControllerBase _controller;
    private readonly ElementIdentity _target;
    private readonly ThemeContext _theme;
    private readonly string _title;
    private readonly ComponentContent _body;
    private readonly ComponentContent _actions;
    private readonly bool _destructive;
    private readonly Func<Composition, bool>? _initialFocus;
    private readonly Action? _defaultAccept;
    private readonly Func<ValueTask>? _defaultAcceptAsync;
    private readonly Style? _style;
    private readonly ElementIdentity? _returnFocus;
    private Composition? _popup;
    private Element? _root;
    private bool _dismissed;

    internal DialogSurfaceRequest(
        DialogControllerBase controller,
        Element target,
        ThemeContext theme,
        string title,
        ComponentContent body,
        ComponentContent actions,
        bool destructive,
        Func<Composition, bool>? initialFocus,
        Action? defaultAccept,
        Func<ValueTask>? defaultAcceptAsync,
        Style? style
    )
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(theme);
        Owner = target.Composition;
        _target = new(target.Composition.Epoch, target.Id);
        _theme = theme;
        _title = Required(title, nameof(title));
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        if (defaultAccept is not null && defaultAcceptAsync is not null)
            throw new ArgumentException("A dialog may have one default accept action.");
        _destructive = destructive;
        _initialFocus = initialFocus;
        _defaultAccept = defaultAccept;
        _defaultAcceptAsync = defaultAcceptAsync;
        _style = style;
        _returnFocus = Owner.Input.FocusedElement;
    }

    /// <inheritdoc />
    public override Composition Owner { get; }

    /// <inheritdoc />
    public override LayoutRect Anchor => default;

    /// <inheritdoc />
    public override ThemeAppearance Appearance => _theme.Appearance;

    /// <inheritdoc />
    public override bool IsValid =>
        !Owner.IsDisposed && Owner.Find(_target) is { IsDisposed: false } && _controller.IsOpen;

    /// <inheritdoc />
    public override bool HasAnchor => IsValid;

    /// <inheritdoc />
    public override bool IsModal => true;

    /// <inheritdoc />
    public override bool AllowInitialFocusFallback => !_destructive;

    /// <inheritdoc />
    public override string Title => _title;

    /// <inheritdoc />
    public override bool IsInteractive => true;

    /// <inheritdoc />
    public override bool ConsumeOutsideClick => true;

    /// <inheritdoc />
    public override bool IsDismissed => _dismissed || !IsValid;

    /// <inheritdoc />
    public override Composition CreateComposition()
    {
        Owner.CheckThread();
        ObjectDisposedException.ThrowIf(IsDismissed, this);
        if (_popup is not null)
            return _popup;

        var popup = new Composition(Owner.Graph, "dialog");
        try
        {
            popup.ShareImagesFrom(Owner);
            var theme = new ThemeContext(
                popup.Root.Scope,
                _theme.Theme,
                _theme.ReducedMotion,
                _theme.Appearance,
                _theme.PresentationMode
            );
            _ = popup.Root.Scope.Effect(
                () =>
                {
                    theme.Theme = _theme.Theme;
                    theme.ReducedMotion = _theme.ReducedMotion;
                    theme.Appearance = _theme.Appearance;
                    theme.PresentationMode = _theme.PresentationMode;
                },
                "dialog-theme"
            );
            _root = popup.Mount(
                popup.Root,
                theme,
                Components.DialogSurfaceContent(
                    _controller,
                    _title,
                    _body,
                    _actions,
                    _destructive,
                    _defaultAccept,
                    _defaultAcceptAsync,
                    _style
                )
            );
            _popup = popup;
            return popup;
        }
        catch
        {
            popup.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public override LayoutRect Measure(ITextShaper shaper, LayoutViewport available)
    {
        ArgumentNullException.ThrowIfNull(shaper);
        var popup = CreateComposition();
        _root!.UpdateControl(LayoutProperties.MaxWidth, Math.Max(1, available.Width));
        _root.UpdateControl(LayoutProperties.MaxHeight, Math.Max(1, available.Height));
        using var scene = SceneLayout.Project(popup, available, shaper);
        var bounds = scene.Boxes.Single(box => box.Identity.ElementId == _root.Id).Bounds;
        return new(
            0,
            0,
            Math.Clamp(bounds.Width, 1, available.Width),
            Math.Clamp(bounds.Height, 1, available.Height)
        );
    }

    /// <inheritdoc />
    public override bool FocusInitial()
    {
        if (_popup is null || _popup.IsDisposed)
            return false;
        if (_initialFocus is not null)
            return _initialFocus(_popup);
        // A destructive dialog must opt into a safe target explicitly. The host fallback may
        // otherwise choose the first focusable control, which is unsafe for localized content.
        if (_destructive)
            return false;
        return _popup.Input.MoveFocus(FocusTraversalDirection.Next);
    }

    /// <inheritdoc />
    public override void Dismiss()
    {
        if (_dismissed)
            return;
        _dismissed = true;
        _controller.SurfaceDismissed(this);
    }

    /// <inheritdoc />
    public override bool RestoreFocus()
    {
        if (Owner.IsDisposed || _returnFocus is not { } focus)
            return false;
        var current = Owner.Input.FocusedElement;
        return current == focus || current is null && Owner.Input.FocusSemantic(focus);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        Dismiss();
        var popup = _popup;
        _popup = null;
        _root = null;
        popup?.Dispose();
    }

    private static string Required(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A dialog title is required.", parameter);
        return value;
    }
}
