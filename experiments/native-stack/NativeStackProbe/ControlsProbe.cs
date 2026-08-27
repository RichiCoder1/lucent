using System.Text;

internal interface IClipboard
{
    string? GetText();
    void SetText(string text);
}

internal sealed class MemoryClipboard : IClipboard
{
    public string? Text { get; private set; }
    public string? GetText() => Text;
    public void SetText(string text) => Text = text;
}

/// <summary>Composes an existing element with pointer, focus, semantic, and style facets; it is not a control base class.</summary>
internal interface IElementBehavior : IDisposable
{
    StableElement Element { get; }
    StyleState State { get; }
    void Focus(bool visible);
}

internal sealed class Button : IElementBehavior
{
    private readonly InputRouter _input;
    private readonly FocusScopes _focus;
    private readonly StableElement _root;
    private readonly Action _activate;
    private readonly Action? _invalidated;
    private readonly bool _attached;
    private bool _pressed;
    private bool _focusVisible;
    public StableElement Element { get; }
    public StyleState State => (_focus.Focused == Element.Id && _focusVisible ? StyleState.FocusVisible : StyleState.None) | (_pressed ? StyleState.Pressed : StyleState.None);

    public Button(StableElement root, InputRouter input, FocusScopes focus, string id, string name, Action activate)
        : this(root, new(new(id), new(0, 0, 20, 10), new("#18181b", "#fafafa", 4), new("button", name, Actions: ["press"])), input, focus, activate, false) { }
    public Button(StableElement root, StableElement element, InputRouter input, FocusScopes focus, Action activate)
        : this(root, element, input, focus, activate, true, null) { }
    public Button(StableElement root, StableElement element, InputRouter input, FocusScopes focus, Action activate, Action invalidated)
        : this(root, element, input, focus, activate, true, invalidated) { }
    private Button(StableElement root, StableElement element, InputRouter input, FocusScopes focus, Action activate, bool attached, Action? invalidated = null)
    {
        _root = root; _input = input; _focus = focus; _activate = activate; _attached = attached; _invalidated = invalidated; Element = element;
        if (!attached) root.Children.Add(Element);
        input.Set(Element, new("press", true, true, Pointer: Pointer));
    }
    public void Focus(bool visible) { _focusVisible = visible; _focus.Focus(_root, Element); _invalidated?.Invoke(); }
    public void Key(string key) { if (_focus.Focused == Element.Id && key is "Enter" or "Space") _activate(); }
    public void InvokeSemanticAction(string action) { if (action != "press") throw new ArgumentException("Button supports only press.", nameof(action)); _activate(); }
    private void Pointer(RoutedPointer pointer)
    {
        if (pointer.Kind == PointerKind.Down) { _pressed = true; Focus(false); }
        if (pointer.Kind == PointerKind.Up && _pressed) { _pressed = false; _invalidated?.Invoke(); if (Contains(pointer.X, pointer.Y)) _activate(); }
    }
    private bool Contains(int x, int y) => x >= Element.Bounds.X && y >= Element.Bounds.Y && x < Element.Bounds.X + Element.Bounds.Width && y < Element.Bounds.Y + Element.Bounds.Height;
    public void Dispose() { _input.Release([Element]); _focus.Release([Element]); if (!_attached) _root.Children.Remove(Element); }
}

/// <summary>Single-line scalar-indexed editing composed at the portable semantic Value seam.</summary>
internal sealed class TextField : IElementBehavior
{
    private readonly InputRouter _input;
    private readonly FocusScopes _focus;
    private readonly StableElement _root;
    private readonly IClipboard _clipboard;
    private readonly Action<string>? _changed;
    private readonly bool _attached;
    private readonly Action? _invalidated;
    private readonly TextState _text = new();
    private int _anchor;
    private bool _focusVisible;
    public StableElement Element { get; }
    public int Caret { get; private set; }
    public StyleState State => _focus.Focused == Element.Id && _focusVisible ? StyleState.FocusVisible : StyleState.None;
    public string Text => _text.Committed;
    public string Preedit => _text.Preedit;

    public TextField(StableElement root, InputRouter input, FocusScopes focus, string id, string name, IClipboard clipboard)
        : this(root, new(new(id), new(0, 12, 80, 10), new("#ffffff", "#09090b", 2), new("edit", name, "", ["set-value"])), input, focus, clipboard, null, false) { }
    public TextField(StableElement root, StableElement element, InputRouter input, FocusScopes focus, IClipboard clipboard, Action<string>? changed)
        : this(root, element, input, focus, clipboard, changed, true, null) { }
    public TextField(StableElement root, StableElement element, InputRouter input, FocusScopes focus, IClipboard clipboard, Action<string>? changed, Action invalidated)
        : this(root, element, input, focus, clipboard, changed, true, invalidated) { }
    private TextField(StableElement root, StableElement element, InputRouter input, FocusScopes focus, IClipboard clipboard, Action<string>? changed, bool attached, Action? invalidated = null)
    {
        _root = root; _input = input; _focus = focus; _clipboard = clipboard; _changed = changed; _attached = attached; _invalidated = invalidated; Element = element;
        if (!attached) root.Children.Add(Element);
        input.Set(Element, new("set-value", true, true, Pointer: Pointer));
    }
    public void Focus(bool visible) { _focusVisible = visible; _focus.Focus(_root, Element); _invalidated?.Invoke(); }
    public void SetPreedit(string text, int start, int length) { if (!Focused) return; ValidateSingleLine(text); _text.SetPreedit(text, start, length); }
    public void CommitPreedit(string text) { if (Focused) ReplaceSelection(text); }
    public void Input(string text) { if (Focused) ReplaceSelection(text); }
    public void InvokeSemanticSetValue(string text) { _anchor = 0; Caret = Count(Text); ReplaceSelection(text); }
    public void Key(string key, bool shift = false, bool control = false)
    {
        if (!Focused) return;
        if (control && key is "C" or "X" or "V") { Clipboard(key); return; }
        var count = Count(Text);
        switch (key)
        {
            case "Left": Move(Math.Max(0, Caret - 1), shift); break;
            case "Right": Move(Math.Min(count, Caret + 1), shift); break;
            case "Home": Move(0, shift); break;
            case "End": Move(count, shift); break;
            case "Backspace": if (!DeleteSelection() && Caret > 0) { _anchor = Caret - 1; DeleteSelection(); } break;
            case "Delete": if (!DeleteSelection() && Caret < count) { _anchor = Caret + 1; DeleteSelection(); } break;
        }
    }
    private bool Focused => _focus.Focused == Element.Id;
    private void Pointer(RoutedPointer pointer) { if (pointer.Kind == PointerKind.Down) Focus(false); }
    private void Move(int caret, bool extend) { Caret = caret; if (!extend) _anchor = caret; }
    private void Clipboard(string key)
    {
        if (key == "V") { if (_clipboard.GetText() is { } text) ReplaceSelection(text); return; }
        var selected = Selection(); if (selected.Length == 0) return;
        _clipboard.SetText(selected); if (key == "X") DeleteSelection();
    }
    private void ReplaceSelection(string text)
    {
        ValidateSingleLine(text);
        var start = Math.Min(_anchor, Caret); var end = Math.Max(_anchor, Caret);
        var replacement = string.Concat(text.EnumerateRunes());
        _text.ReplaceCommitted(Prefix(Text, start) + replacement + Prefix(Text, Count(Text)).Substring(Prefix(Text, end).Length));
        _anchor = Caret = start + Count(replacement);
        Element.Semantics = Element.Semantics with { Value = Text }; Element.Text = Text; Element.Mark(DirtyFacet.Paint | DirtyFacet.Semantics); _changed?.Invoke(Text);
    }
    private bool DeleteSelection()
    {
        if (_anchor == Caret) return false;
        ReplaceSelection(""); return true;
    }
    private string Selection() => Prefix(Text, Math.Max(_anchor, Caret)).Substring(Prefix(Text, Math.Min(_anchor, Caret)).Length);
    private static int Count(string text) => text.EnumerateRunes().Count();
    private static string Prefix(string text, int count) => TextState.RunePrefix(text, count);
    private static void ValidateSingleLine(string text) { if (text.Contains('\r') || text.Contains('\n')) throw new ArgumentException("TextField accepts one line only.", nameof(text)); }
    public void Dispose() { _input.Release([Element]); _focus.Release([Element]); if (!_attached) _root.Children.Remove(Element); }
}

/// <summary>Reusable selection behavior owns its portable selected semantic, pointer registration, focus, and cleanup.</summary>
internal sealed class Selectable : IElementBehavior
{
    private readonly StableElement _root;
    private readonly InputRouter _input;
    private readonly FocusScopes _focus;
    private readonly Action _select;
    private bool _focusVisible;
    private readonly bool _attached;
    private readonly Action? _invalidated;
    public StableElement Element { get; }
    public StyleState State => (Element.Semantics.Selected ? StyleState.Selected : StyleState.None) | (_focus.Focused == Element.Id && _focusVisible ? StyleState.FocusVisible : StyleState.None);
    public Selectable(StableElement root, InputRouter input, FocusScopes focus, string id, string name, Action select)
        : this(root, new(new(id), new(0, 24, 80, 10), new("#fff", "#09090b", 2), new("option", name, Actions: ["select"])), input, focus, select, false) { }
    public Selectable(StableElement root, StableElement element, InputRouter input, FocusScopes focus, Action select)
        : this(root, element, input, focus, select, true, null) { }
    public Selectable(StableElement root, StableElement element, InputRouter input, FocusScopes focus, Action select, Action invalidated)
        : this(root, element, input, focus, select, true, invalidated) { }
    private Selectable(StableElement root, StableElement element, InputRouter input, FocusScopes focus, Action select, bool attached, Action? invalidated = null)
    {
        _root = root; _input = input; _focus = focus; _select = select; _attached = attached; _invalidated = invalidated; Element = element;
        if (!attached) root.Children.Add(Element); input.Set(Element, new("select", true, true, Pointer: Pointer));
    }
    public void Focus(bool visible) { _focusVisible = visible; _focus.Focus(_root, Element); _invalidated?.Invoke(); }
    private void Pointer(RoutedPointer pointer)
    {
        if (pointer.Kind != PointerKind.Up) return;
        Element.Semantics = Element.Semantics with { Selected = true }; Element.Mark(DirtyFacet.Semantics); _invalidated?.Invoke(); _select();
    }
    public void Dispose() { _input.Release([Element]); _focus.Release([Element]); if (!_attached) _root.Children.Remove(Element); }
}

internal static class ControlsProbe
{
    public static ControlsCheckResult Run()
    {
        var root = new StableElement(new("controls.root"), new(0, 0, 100, 40), new("#000", "#fff", 0), new("window", "Controls"));
        var input = new InputRouter(); var focus = new FocusScopes(input); var clipboard = new MemoryClipboard(); var presses = 0;
        using var button = new Button(root, input, focus, "controls.button", "Save", () => presses++);
        using var field = new TextField(root, input, focus, "controls.field", "Title", clipboard); var selections = 0;
        using var selectable = new Selectable(root, input, focus, "controls.selection", "Issue 22", () => selections++);
        button.Key("Enter");
        input.Dispatch(root, PointerKind.Down, 1, 1); input.Dispatch(root, PointerKind.Up, 30, 30);
        input.Dispatch(root, PointerKind.Down, 1, 1); input.Dispatch(root, PointerKind.Up, 1, 1); var pointerFocusHidden = !button.State.HasFlag(StyleState.FocusVisible);
        button.Focus(true); button.Key("Enter"); button.Key("Space"); button.InvokeSemanticAction("press");
        var buttonActivation = presses == 4 && pointerFocusHidden;
        var buttonSemantics = button.Element.Semantics is { Role: "button", Name: "Save", Actions: ["press"] };
        field.Input("ignored"); field.Focus(true); field.Input("a😀中"); field.Key("Left"); field.Key("Backspace"); field.Key("Home"); field.Key("Right", shift: true); field.Input("Z");
        var editing = field.Text == "Z中" && field.Caret == 1;
        field.Key("End"); field.SetPreedit("😀中", 1, 1); var ime = field.Preedit == "😀中"; field.CommitPreedit("語"); ime &= field.Preedit == "";
        field.InvokeSemanticSetValue("a😀中"); field.Key("Home"); field.Key("Right"); field.Key("Delete");
        editing &= field.Text == "a中";
        field.InvokeSemanticSetValue("Z中語");
        field.Key("Home"); field.Key("Right", shift: true); field.Key("C", control: true); field.Key("X", control: true); field.Key("V", control: true);
        var clipboardWorks = clipboard.Text == "Z" && field.Text == "Z中語";
        var unicodeSafe = field.Caret == 1 && field.Text == "Z中語";
        var focusVisible = field.State.HasFlag(StyleState.FocusVisible) && field.Element.Semantics.Focused;
        field.InvokeSemanticSetValue("value😀");
        var scene = new RetainedScene(); var snapshots = new ProjectedSnapshots(); var projection = new SceneProjection(scene, snapshots); projection.Project([root, .. root.Children]);
        var valueSemantics = field.Text == "value😀" && field.Element.Semantics is { Role: "edit", Value: "value😀", Actions: ["set-value"] } && snapshots.Semantics[field.Element.Id].Value == "value😀";
        field.Key("Home"); field.Key("Backspace"); field.Key("Backspace");
        var newlineRejected = false; try { field.Input("bad\nline"); } catch (ArgumentException) { newlineRejected = true; }
        var preeditNewlineRejected = false; try { field.SetPreedit("bad\rline", 0, 0); } catch (ArgumentException) { preeditNewlineRejected = true; }
        var negative = field.Text == "value😀" && field.Caret == 0 && newlineRejected && preeditNewlineRejected;
        var fieldElement = field.Element; field.Dispose(); var disposal = !root.Children.Contains(fieldElement) && input.Get(fieldElement) is null && focus.Focused != fieldElement.Id;
        input.Dispatch(root, PointerKind.Down, 1, 25); input.Dispatch(root, PointerKind.Up, 1, 25);
        var selectionSemantics = selections == 1 && selectable.State.HasFlag(StyleState.Selected) && selectable.Element.Semantics is { Role: "option", Name: "Issue 22", Selected: true, Actions: ["select"] };
        var semanticDump = SemanticContracts.Dump([button.Element, field.Element, selectable.Element]);
        var selectionElement = selectable.Element; selectable.Dispose(); disposal &= !root.Children.Contains(selectionElement) && input.Get(selectionElement) is null;
        input.Dispatch(root, PointerKind.Down, 1, 1); var buttonElement = button.Element; button.Dispose(); disposal &= input.Capture is null && !root.Children.Contains(buttonElement) && input.Get(buttonElement) is null;
        var result = new ControlsCheckResult(buttonActivation && buttonSemantics && editing && unicodeSafe && ime && clipboardWorks && focusVisible && valueSemantics && selectionSemantics && negative && disposal, buttonActivation, buttonSemantics, editing, unicodeSafe, ime, clipboardWorks, focusVisible, valueSemantics && selectionSemantics, negative, disposal, semanticDump);
        if (!result.Ok) throw new InvalidOperationException($"Controls self-check failed: {result}.");
        return result;
    }
}
