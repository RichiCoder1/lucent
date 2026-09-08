namespace Lucent.Core;

public sealed partial class InputRouter
{
    private readonly Dictionary<long, MenuRegistration> _menus = [];
    private (int Pointer, ElementIdentity Target)? _contextPointer;
    private ContextMenuRequest? _activeMenu;
    private ElementIdentity? _pendingMenuFocusAfter;

    /// <summary>Raised during an invocation route; hosts defer popup construction until routing returns.</summary>
    public event Action<ContextMenuRequest>? ContextMenuRequested;

    /// <summary>Resolves the cursor from the nearest eligible authored override or control intent.</summary>
    public CursorIntent CursorAt(float x, float y)
    {
        Check();
        if (
            !float.IsFinite(x)
            || !float.IsFinite(y)
            || _scene is null
            || !ValidateScene(_scene)
            || HitScrollBar(x, y) is not null
            || Hit(x, y) is not { } hit
        )
            return CursorIntent.Default;
        foreach (var identity in Path(hit).Reverse())
        {
            var element = _composition.Find(identity);
            if (element is null)
                continue;
            var intent = element.Resolve(InputProperties.Cursor).Value;
            if (intent != CursorIntent.Auto)
                return intent;
            if (_textFields.ContainsKey(identity.ElementId))
                return CursorIntent.Text;
            if (
                element.DeclaredSemanticRole
                is SemanticRole.Button
                    or SemanticRole.ListItem
                    or SemanticRole.MenuItem
            )
                return CursorIntent.Pointer;
        }
        return CursorIntent.Default;
    }

    internal void RegisterContextMenu(
        long elementId,
        ReactiveScope scope,
        Func<ComponentRecipe> menu,
        ThemeContext theme,
        Action<bool>? onOpenChanged
    )
    {
        _menus.Add(elementId, new(menu, theme, onOpenChanged));
        scope.OnDispose(() => _menus.Remove(elementId));
    }

    private bool HandleContextPointer(PointerCommand command, ElementIdentity hit)
    {
        if (
            command.Kind == PointerCommandKind.Cancel
            || command is { Kind: PointerCommandKind.Down, Button: PointerButton.Primary }
        )
        {
            _contextPointer = null;
            return false;
        }
        if (command is { Kind: PointerCommandKind.Down, Button: PointerButton.Secondary })
        {
            var target = MenuTarget(hit);
            _contextPointer = target is { } identity ? (command.PointerId, identity) : null;
            return target is not null;
        }
        if (
            command.Kind != PointerCommandKind.Up
            || _contextPointer is not { } pending
            || pending.Pointer != command.PointerId
        )
            return false;
        _contextPointer = null;
        if (MenuTarget(hit) == pending.Target)
            OpenContextMenu(pending.Target, new(command.X, command.Y, 1, 1));
        return true;
    }

    private bool HandleContextKey(KeyCommand command, ElementIdentity focus)
    {
        if (
            command.Kind != KeyCommandKind.Down
            || command.IsRepeat
            || !(
                command.Key == Key.ContextMenu && command.Modifiers == KeyModifiers.None
                || command.Key == Key.F10 && command.Modifiers == KeyModifiers.Shift
            )
        )
            return false;
        if (
            MenuTarget(focus) is not { } target
            || !_input.TryGetValue(focus.ElementId, out var retained)
        )
            return false;
        OpenContextMenu(target, retained.Bounds);
        return true;
    }

    private ElementIdentity? MenuTarget(ElementIdentity hit)
    {
        foreach (var identity in Path(hit).Reverse())
            if (
                _menus.ContainsKey(identity.ElementId)
                || _textFields.ContainsKey(identity.ElementId)
            )
                return identity;
        return null;
    }

    private void OpenContextMenu(ElementIdentity target, LayoutRect anchor)
    {
        if (ContextMenuRequested is null || !Eligible(target))
            return;
        Func<ComponentRecipe> create;
        ThemeContext theme;
        if (_menus.TryGetValue(target.ElementId, out var registered))
        {
            create = registered.Content;
            theme = registered.Theme;
        }
        else if (
            _textFields.TryGetValue(target.ElementId, out var editor)
            && _composition.Find(target)?.Presentation is { } presentation
        )
        {
            // Text menus preserve the existing range; only the editing target receives focus.
            var errors = new List<Exception>();
            RequestFocus(target, FocusChangeReason.Pointer, errors);
            Throw(errors);
            create = () => EditorMenu(target, editor);
            theme = presentation.Theme;
        }
        else
            return;
        var content =
            create()
            ?? throw new InvalidOperationException("A context menu factory returned no recipe.");
        _activeMenu?.Dismiss();
        var notify = registered?.OpenChanged;
        var request = new ContextMenuRequest(
            _composition,
            target,
            anchor,
            content,
            theme,
            notify is null ? null : () => notify(false)
        );
        _activeMenu = request;
        try
        {
            notify?.Invoke(true);
            ContextMenuRequested(request);
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private ComponentRecipe EditorMenu(ElementIdentity target, TextFieldState editor)
    {
        bool Valid() =>
            !_composition.IsDisposed
            && Eligible(target)
            && !editor.IsDisposed
            && !editor.HasPreedit;
        void Execute(Key key)
        {
            if (!Valid())
                return;
            if (FocusedElement != target && !FocusSemantic(target))
                return;
            DispatchKey(new(KeyCommandKind.Down, key, KeyModifiers.Control));
        }
        return Components.Menu([
            Components.MenuItem("Undo", () => Execute(Key.Z), () => Valid() && editor.CanUndo),
            Components.MenuItem("Redo", () => Execute(Key.Y), () => Valid() && editor.CanRedo),
            Components.MenuSeparator(),
            Components.MenuItem(
                "Cut",
                () => Execute(Key.X),
                () => Valid() && editor.SelectedText.Length != 0
            ),
            Components.MenuItem(
                "Copy",
                () => Execute(Key.C),
                () => Valid() && editor.SelectedText.Length != 0
            ),
            Components.MenuItem("Paste", () => Execute(Key.V), Valid),
            Components.MenuSeparator(),
            Components.MenuItem(
                "Select all",
                () => Execute(Key.A),
                () => Valid() && editor.Value.Length != 0
            ),
        ]);
    }

    internal void DismissMenu() => _composition.MenuSession?.Dismiss();

    internal void MoveMenuFocus(FocusTraversalDirection direction)
    {
        var errors = new List<Exception>();
        MoveFocusCore(direction, errors);
        Throw(errors);
    }

    internal bool FocusMenuBoundary(bool first)
    {
        if (_scene is null || !ValidateScene(_scene))
            return false;
        var targets = MenuFocusTargets();
        if (targets.Length != 0)
        {
            var errors = new List<Exception>();
            SetModality(InputModality.Keyboard, errors);
            RequestFocus(
                (first ? targets[0] : targets[^1]).Identity,
                FocusChangeReason.Traversal,
                errors
            );
            Throw(errors);
            return true;
        }
        return false;
    }

    /// <summary>Focuses the next eligible item after a trigger, or defers until its scene is installed.</summary>
    internal bool FocusMenuAfter(ElementIdentity trigger)
    {
        if (_disposed)
            return false;
        if (_scene is null || !ValidateScene(_scene))
        {
            _pendingMenuFocusAfter = trigger;
            return false;
        }
        _pendingMenuFocusAfter = null;
        var errors = new List<Exception>();
        var focused = FocusMenuAfterCurrentScene(trigger, errors);
        Throw(errors);
        return focused;
    }

    internal void ClearPendingMenuFocus() => _pendingMenuFocusAfter = null;

    private RetainedInputElement[] MenuFocusTargets() =>
        _scene!
            .Input.Where(item =>
                _focusable.TryGetValue(item.Identity.ElementId, out var focusable)
                && focusable.TabStop
                && Eligible(item.Identity)
            )
            .OrderBy(item => item.Order)
            .ToArray();

    private bool FocusPendingMenuTarget(List<Exception> errors)
    {
        if (_pendingMenuFocusAfter is not { } trigger)
            return false;
        if (_scene is null || !ValidateScene(_scene))
            return false;
        _pendingMenuFocusAfter = null;
        return FocusMenuAfterCurrentScene(trigger, errors);
    }

    private bool FocusMenuAfterCurrentScene(ElementIdentity trigger, List<Exception> errors)
    {
        var targets = MenuFocusTargets();
        if (targets.Length == 0)
            return false;
        var start = 0;
        if (_input.TryGetValue(trigger.ElementId, out var prior))
        {
            var next = Array.FindIndex(targets, item => item.Order > prior.Order);
            start = next >= 0 ? next : 0;
        }
        var target = targets[start].Identity;
        SetModality(InputModality.Keyboard, errors);
        RequestFocus(target, FocusChangeReason.Traversal, errors);
        return _focused?.Identity == target;
    }

    private sealed record MenuRegistration(
        Func<ComponentRecipe> Content,
        ThemeContext Theme,
        Action<bool>? OpenChanged
    );
}
