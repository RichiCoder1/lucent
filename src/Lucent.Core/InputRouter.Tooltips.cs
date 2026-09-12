namespace Lucent.Core;

public sealed partial class InputRouter
{
    private readonly Dictionary<long, TooltipRegistration> _tooltips = [];

    internal void RegisterTooltip(
        long elementId,
        ReactiveScope scope,
        Action<bool, float, float> onHoverChanged,
        Action<bool> onFocusChanged,
        Action? onEscape
    )
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(onHoverChanged);
        ArgumentNullException.ThrowIfNull(onFocusChanged);
        if (_tooltips.ContainsKey(elementId))
            throw new InvalidOperationException("An element has one tooltip registration.");
        var registration = new TooltipRegistration(
            elementId,
            onHoverChanged,
            onFocusChanged,
            onEscape
        );
        _tooltips.Add(elementId, registration);
        scope.OnDispose(() =>
        {
            if (
                _tooltips.TryGetValue(elementId, out var current)
                && ReferenceEquals(current, registration)
            )
                _tooltips.Remove(elementId);
        });
    }

    private void UpdateTooltipHover(
        ElementIdentity? hit,
        float pointerX,
        float pointerY,
        List<Exception> errors
    )
    {
        var path = hit is { } identity && Eligible(identity) ? Path(identity) : [];
        foreach (var registration in _tooltips.Values.ToArray())
        {
            var inside = path.Any(identity => identity.ElementId == registration.ElementId);
            if (!inside && inside == registration.Hovered)
                continue;
            registration.Hovered = inside;
            InvokeTooltip(registration, inside, pointerX, pointerY, focused: null, errors);
        }
    }

    private void UpdateTooltipFocus(ElementIdentity target, bool focused, List<Exception> errors)
    {
        var path = Path(target);
        foreach (var registration in _tooltips.Values.ToArray())
        {
            var inside =
                focused && path.Any(identity => identity.ElementId == registration.ElementId);
            if (inside == registration.Focused)
                continue;
            registration.Focused = inside;
            InvokeTooltip(registration, hovered: null, 0, 0, focused: inside, errors);
        }
    }

    private bool HandleTooltipKey(
        KeyCommand command,
        ElementIdentity target,
        List<Exception> errors
    )
    {
        if (
            command is not { Kind: KeyCommandKind.Down, Key: Key.Escape, IsRepeat: false }
            || HasTextComposition
        )
            return false;
        var path = Path(target);
        TooltipRegistration? registration = null;
        for (var index = path.Count - 1; index >= 0; index--)
            if (_tooltips.TryGetValue(path[index].ElementId, out registration))
                break;
        if (registration?.OnEscape is not { } escape)
            return false;
        try
        {
            escape();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
        return true;
    }

    private static void InvokeTooltip(
        TooltipRegistration registration,
        bool? hovered,
        float pointerX,
        float pointerY,
        bool? focused,
        List<Exception> errors
    )
    {
        try
        {
            if (hovered is { } hover)
                registration.OnHoverChanged(hover, pointerX, pointerY);
            if (focused is { } focus)
                registration.OnFocusChanged(focus);
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
    }

    private sealed class TooltipRegistration(
        long elementId,
        Action<bool, float, float> onHoverChanged,
        Action<bool> onFocusChanged,
        Action? onEscape
    )
    {
        internal long ElementId { get; } = elementId;
        internal Action<bool, float, float> OnHoverChanged { get; } = onHoverChanged;
        internal Action<bool> OnFocusChanged { get; } = onFocusChanged;
        internal Action? OnEscape { get; } = onEscape;
        internal bool Hovered { get; set; }
        internal bool Focused { get; set; }
    }
}
