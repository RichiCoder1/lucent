namespace Lucent.Core;

/// <summary>One nonmodal anchored surface, retained until dismissal or owner removal.</summary>
public sealed class OwnedSurfaceRequest : PopupSurfaceRequest
{
    private readonly ElementIdentity _target;
    private readonly ComponentRecipe _content;
    private readonly ThemeContext _theme;
    private readonly ElementIdentity? _returnFocus;
    private readonly Action? _closed;
    private readonly Action<bool>? _pointerInsideChanged;
    private readonly LayoutRect? _anchorOverride;
    private Composition? _popup;
    private Element? _root;
    private bool _dismissed;
    private bool _pointerInside;

    internal OwnedSurfaceRequest(
        Element target,
        ThemeContext theme,
        ComponentRecipe content,
        bool interactive,
        bool consumeOutsideClick,
        Action? closed,
        Action<bool>? pointerInsideChanged = null,
        LayoutRect? anchorOverride = null
    )
    {
        Owner = target.Composition;
        _target = new(Owner.Epoch, target.Id);
        _theme = theme;
        _content = content;
        _closed = closed;
        _pointerInsideChanged = pointerInsideChanged;
        _anchorOverride = anchorOverride;
        _returnFocus = Owner.Input.FocusedElement;
        IsInteractive = interactive;
        ConsumeOutsideClick = consumeOutsideClick;
    }

    /// <inheritdoc />
    public override Composition Owner { get; }

    /// <inheritdoc />
    public override LayoutRect Anchor =>
        _anchorOverride ?? Owner.Input.SurfaceAnchor(_target) ?? default;

    /// <inheritdoc />
    public override ThemeAppearance Appearance => _theme.Appearance;

    /// <inheritdoc />
    public override bool IsValid =>
        !Owner.IsDisposed
        && Owner.Find(_target) is { IsDisposed: false } element
        && element.InputAvailable();

    /// <summary>Whether a projected anchor is available for native placement.</summary>
    public override bool HasAnchor =>
        IsValid && (_anchorOverride is not null || Owner.Input.SurfaceAnchor(_target) is not null);

    /// <inheritdoc />
    public override bool IsDismissed => _dismissed || !IsValid;

    /// <inheritdoc />
    public override bool IsInteractive { get; }

    /// <inheritdoc />
    public override bool ConsumeOutsideClick { get; }

    /// <summary>Whether the host currently reports the pointer inside this surface.</summary>
    public bool IsPointerInside => _pointerInside;

    /// <inheritdoc />
    public override float AnchorGap => 8;

    /// <summary>Reports host pointer entry or exit without giving a noninteractive surface focus.</summary>
    public void SetPointerInside(bool inside)
    {
        Owner.CheckThread();
        if (_dismissed || _pointerInside == inside)
            return;
        _pointerInside = inside;
        _pointerInsideChanged?.Invoke(inside);
    }

    /// <inheritdoc />
    public override Composition CreateComposition()
    {
        Owner.CheckThread();
        ObjectDisposedException.ThrowIf(IsDismissed, this);
        if (_popup is not null)
            return _popup;
        var popup = new Composition(Owner.Graph, IsInteractive ? "popover" : "tooltip");
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
                "surface-theme"
            );
            popup.Root.Present(
                theme,
                component: Style.Empty.Set(LayoutProperties.CrossAlignment, LayoutAlignment.Start)
            );
            var presentation = Style
                .Empty.Set(LayoutProperties.Padding, Insets.Uniform(12))
                .Set(LayoutProperties.Spacing, 8f)
                .Set(VisualProperties.CornerRadius, 6f)
                .Set(VisualProperties.Background, ControlThemes.Surface)
                .Set(TypographyProperties.TextColor, ControlThemes.Foreground)
                .Bind(
                    VisualProperties.Border,
                    () => Border.Hairline(theme.Token(ControlThemes.Border))
                );
            _root = popup.Mount(
                popup.Root,
                theme,
                Components.Layout([_content], style: presentation)
            );
            _root.AttachBehaviors(new SurfaceDismissBehavior(this));
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
        var popup = CreateComposition();
        _root!.UpdateControl(LayoutProperties.MaxWidth, available.Width);
        _root.UpdateControl(LayoutProperties.MaxHeight, available.Height);
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
    public override void Dismiss() => Close(notify: true);

    internal void Close(bool notify)
    {
        Owner.CheckThread();
        if (_dismissed)
            return;
        _dismissed = true;
        _pointerInside = false;
        if (notify && IsValid)
            _closed?.Invoke();
    }

    /// <inheritdoc />
    public override bool RestoreFocus()
    {
        if (!IsInteractive || Owner.IsDisposed || _returnFocus is not { } focus)
            return false;
        var current = Owner.Input.FocusedElement;
        return current == focus || current is null && Owner.Input.FocusSemantic(focus);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        Close(notify: false);
        var popup = _popup;
        _popup = null;
        _root = null;
        popup?.Dispose();
    }

    private sealed class SurfaceDismissBehavior(OwnedSurfaceRequest request) : Behavior
    {
        public override string Name => "surface-dismiss";
        public override BehaviorOwnership Ownership => BehaviorOwnership.Focus;

        public override void Attach(BehaviorContext context)
        {
            context.OnKey(route =>
            {
                if (
                    route.Command is { Kind: KeyCommandKind.Down, Key: Key.Escape, IsRepeat: false }
                    && !context.CompositionInput().HasTextComposition
                )
                {
                    request.Dismiss();
                    route.Handled = true;
                }
            });
        }
    }
}
