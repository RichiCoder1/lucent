using System.Globalization;
using System.Text;

namespace Lucent.Core;

/// <summary>Immutable retained scroll state for semantic adapters; values are logical Core coordinates.</summary>
public readonly record struct SemanticScrollState(ScrollOffset Offset, ScrollOffset Maximum, LayoutRect Viewport);

/// <summary>Reusable selectable action; selection is behavior state, not application-side routing state.</summary>
internal sealed class RowActionBehavior(string name, SemanticDeclaration semantics, Action? activate = null, ControlState? state = null) : Behavior
{
    public override string Name => name;
    public override BehaviorOwnership Ownership => BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics;
    public override void Attach(BehaviorContext context)
    {
        context.SetSemantics(semantics ?? throw new ArgumentNullException(nameof(semantics))); context.MakeFocusable();
        context.OnSemanticCommand(command => command.Kind switch
        {
            SemanticCommandKind.Focus => context.CompositionInput().FocusSemantic(context.Identity),
            SemanticCommandKind.Select => Select(),
            _ => false
        });
        context.OnSelectionChanged(ApplySelection);
        int? armedPointer = null;
        if (state is not null) context.Effect(() => { if (state.Selected) context.SelectSemantic(); else context.SetState(BehaviorState.Selected, false); }, "selected-state");
        context.OnPointer(route =>
        {
            if (route.Command is { Kind: PointerCommandKind.Down, Button: PointerButton.Primary }) { var armed = route.Capture(); armedPointer = armed ? route.Command.PointerId : null; context.SetState(BehaviorState.Pressed, armed); if (armed) route.Focus(); route.Handled = armed; }
            else if (route.Command.Kind is PointerCommandKind.Up or PointerCommandKind.Cancel)
            {
                if (armedPointer != route.Command.PointerId) return;
                var active = context.State.GetValueOrDefault(BehaviorState.Pressed); context.SetState(BehaviorState.Pressed, false);
                armedPointer = null;
                if (active && route.Command.Kind == PointerCommandKind.Up && route.IsInsideCurrentTarget) Select();
                route.Handled = active;
            }
        });
        context.OnKey(route => { if (route.Command is { Kind: KeyCommandKind.Down, IsRepeat: false, Key: Key.Enter or Key.Space }) { route.Handled = Select(); } });
        context.OnCaptureLost(loss => { if (armedPointer == loss.PointerId) { armedPointer = null; context.SetState(BehaviorState.Pressed, false); } });

        bool Select() { if (!context.SelectSemantic()) return false; activate?.Invoke(); return true; }
        void ApplySelection(bool value) { context.SetState(BehaviorState.Selected, value); if (state is not null) state.Selected = value; }
    }
}

/// <summary>Reusable invoke action for buttons; unlike selectable rows it has no selection state.</summary>
internal sealed class ButtonBehavior(string name, SemanticDeclaration semantics, Action? activate = null) : Behavior
{
    public override string Name => name;
    public override BehaviorOwnership Ownership => BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics;
    public override void Attach(BehaviorContext context)
    {
        context.SetSemantics(semantics ?? throw new ArgumentNullException(nameof(semantics))); context.MakeFocusable();
        context.OnSemanticCommand(command => command.Kind switch { SemanticCommandKind.Focus => context.CompositionInput().FocusSemantic(context.Identity), SemanticCommandKind.Invoke => Invoke(), _ => false });
        int? armedPointer = null;
        context.OnPointer(route =>
        {
            if (route.Command is { Kind: PointerCommandKind.Down, Button: PointerButton.Primary }) { var armed = route.Capture(); armedPointer = armed ? route.Command.PointerId : null; context.SetState(BehaviorState.Pressed, armed); if (armed) route.Focus(); route.Handled = armed; }
            else if (route.Command.Kind is PointerCommandKind.Up or PointerCommandKind.Cancel) { if (armedPointer != route.Command.PointerId) return; var active = context.State.GetValueOrDefault(BehaviorState.Pressed); armedPointer = null; context.SetState(BehaviorState.Pressed, false); if (active && route.Command.Kind == PointerCommandKind.Up && route.IsInsideCurrentTarget) activate?.Invoke(); route.Handled = active; }
        });
        context.OnKey(route => { if (route.Command is { Kind: KeyCommandKind.Down, IsRepeat: false, Key: Key.Enter or Key.Space }) { activate?.Invoke(); route.Handled = true; } });
        context.OnCaptureLost(loss => { if (armedPointer == loss.PointerId) { armedPointer = null; context.SetState(BehaviorState.Pressed, false); } });
        bool Invoke() { activate?.Invoke(); return true; }
    }
}

/// <summary>Bounded key-driven scrolling for a clipped retained viewport.</summary>
internal sealed class ScrollViewportBehavior(string name, ScrollViewportState state) : Behavior
{
    public override string Name => name;
    public override BehaviorOwnership Ownership => BehaviorOwnership.Action | BehaviorOwnership.Focus | BehaviorOwnership.Semantics;
    public override void Attach(BehaviorContext context)
    {
        context.SetSemantics(new(SemanticRole.Group, name, actions: SemanticAction.Scroll)); context.MakeFocusable(); context.RegisterScrollable(state);
        context.OnSemanticCommand(command => command.Kind == SemanticCommandKind.Focus ? context.CompositionInput().FocusSemantic(context.Identity) : command.Kind == SemanticCommandKind.Scroll && context.CompositionInput().ScrollSemantic(context.Identity, command));
        context.OnKey(route =>
        {
            var handled = route.Command is { Kind: KeyCommandKind.Down, IsRepeat: false } && route.Command.Key switch
            {
                Key.Left => route.ScrollBy(-40, 0), Key.Right => route.ScrollBy(40, 0), Key.Up => route.ScrollBy(0, -40), Key.Down => route.ScrollBy(0, 40),
                Key.Home => route.ScrollToStart(), Key.End => route.ScrollToEnd(), _ => false
            };
            route.Handled = handled;
        });
    }
}
