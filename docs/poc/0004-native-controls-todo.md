# POC 0004: Direct native Avalonia controls

This slice replaces Lucent's three-control authoring surface with direct
Avalonia control names and members. The TodoMVC source uses only native
controls:

- `Border`, `StackPanel`, `TextBlock`, `TextBox`, `Button`, `CheckBox`,
  `Separator`, and `ProgressBar`;
- exact PascalCase Avalonia properties such as `Padding`, `Spacing`, `Text`,
  `PlaceholderText`, `IsChecked`, and `Value`;
- native `Click` and `TextChanged` events;
- explicit `Content: value` and implicit string content;
- bounded native numeric and tuple conveniences for `Thickness` and
  `CornerRadius`; and
- a keyed `foreach` whose rows retain their concrete Avalonia controls.

The generated tree contains those concrete Avalonia types. There are no Lucent
wrapper controls, property bags, reflection calls, or virtual control nodes.
The existing `Column`, `Text`, `text`, and `onClick` spellings remain only as a
temporary source-compatibility path for earlier examples.

## Native content routes

Avalonia publishes its intended nested-content target with
`Avalonia.Metadata.ContentAttribute`. Important native routes include
`Panel.Children`, `ContentControl.Content`, `Decorator.Child`, and
`ItemsControl.Items`.

The completed PoC uses a bounded compile-time classifier plus generated typed
attachment overloads to validate the authoring and runtime model:

```csharp
private static void AttachChild(Panel parent, Control child) =>
    parent.Children.Add(child);

private static void AttachChild(Decorator parent, Control child) =>
    parent.Child = child;

private static void AttachChild(ContentControl parent, Control child) =>
    parent.Content = child;

private static void AttachChild(ItemsControl parent, Control child) =>
    parent.Items.Add(child);
```

These helpers do not create or wrap controls. C# overload resolution still
checks the concrete parent/child relationship in the consuming build. The next
hardening step is to remove the bounded classifier and resolve the consuming
project's real Roslyn symbols and `[Content]` metadata. That will provide the
same behavior for custom and third-party controls and will move errors from the
generated C# build into source-spanned Lucent diagnostics.

Native content routes are not Lucent slots. A native control receives content
through Avalonia metadata; a Lucent component receives an implicit `children`
slot and places it explicitly with `yield children`.

The explicit and implicit scalar forms share one occupancy rule:

```csharp
Button { Content: "Add task"; }
Button { "Add task"; }
```

Both emit a direct assignment to `ContentControl.Content`. Combining either
form with another scalar value or nested control is a source error.

## Type-directed native values

The PoC binder records a bounded native value kind for the Avalonia properties
it understands and the emitter constructs that type directly:

```csharp
Border {
    Padding: (12, 8);
    CornerRadius: 8;
}
```

`Padding` selects `Thickness` construction, `CornerRadius` selects
`CornerRadius` construction, and a numeric property such as `Width` remains an
ordinary C# expression. This proves the intended source experience without
wrapping every assignment in a runtime adapter. The project-aware binder should
eventually resolve and validate these conversions before emission and extend
the same provider model to enums, brushes, colors, and `GridLength`.

## TodoMVC keyed region

The TodoMVC example owns `State<string>`, `State<TodoItem[]>`, and
`State<TodoFilter>`. It supports adding, editing, toggling, deleting, filtering,
marking all complete, clearing completed items, and reactive counts/progress.

The initial keyed region is deliberately narrow: one `foreach (...) keyed by`
loop is the only dynamic child of a dedicated native panel, and each iteration
has one native control root. Generated code keeps a dictionary by key. Existing
keys reuse and refresh their controls, new keys mount rows, removed keys detach
their known event handlers, and source order is reflected in the panel's child
order. No virtual DOM or wrapper-control layer is introduced.

## Optional Lucent controls

Direct Avalonia controls are the default and escape hatch. A future optional
Lucent control library is still useful where a control centralizes substantial
behavior such as validation plus accessibility, async command lifetime, focus
coordination, or correctly virtualized keyed items.

A one-for-one wrapper does not qualify. If deleting a proposed Lucent control
only reveals one Avalonia control and the same properties at each call site,
the wrapper is too shallow.

## Verified behavior

The desktop smoke path mounts the Todo tree, verifies the typed native values,
adds and edits items through `TextChanged`, toggles completion, switches
filters, deletes rows, clears completed items, and checks that unaffected keyed
rows retain the same Avalonia `Control` instances. It then closes the real
Avalonia window and application lifetime cleanly.

```text
SMOKE: window-opened
SMOKE: loaded-turn
SMOKE: todo-updated
SMOKE: window-closed
SMOKE: app-exit
```

The full solution builds without warnings and the compiler, MSBuild, and LSP
test suites cover direct native properties, nested panel/decorator content,
implicit scalar content, native events, content conflicts, and TextBlock child
rejection.

## Remaining limits

- The compiler is not project-aware yet. Unknown control types, properties,
  conversions, and third-party metadata are still validated by the generated
  C# build rather than by the standalone compiler or LSP.
- The temporary native event set is bounded. Project-aware symbol resolution
  should replace it and validate the real event delegate.
- State dependencies are still found lexically and invalidation conservatively
  refreshes all bindings and the keyed region.
- The keyed subset does not yet support nested loops, conditional regions,
  component rows, multiple dynamic siblings, or optimized native collection
  moves.
- The PoC recognizes removable `Click` and `TextChanged` handlers. Arbitrary
  event delegate resolution and deterministic cleanup require project symbols.
- The bounded value descriptor currently covers `Border.Padding` with scalar,
  two-value, or four-value `Thickness` construction and `Border.CornerRadius`
  with scalar or four-value construction. Static string conversion for enums,
  brushes, colors, and `GridLength` is deferred to the semantic binder.
- Attached properties such as `Grid.Row` need qualified member syntax and
  symbol-aware lowering.
- Direct assignments establish Avalonia local values and can outrank styles or
  pseudo-class setters. Styling ownership remains a separate IR decision.
- Templates, resources, namescopes, and advanced routed-event options are not
  ordinary child controls and need explicit language semantics.

See Avalonia's documentation for [content properties](https://docs.avaloniaui.net/docs/xaml),
[ContentControl](https://docs.avaloniaui.net/controls/data-display/contentcontrol),
[ItemsControl](https://docs.avaloniaui.net/api/avalonia/controls/itemscontrol),
and [property value precedence](https://docs.avaloniaui.net/docs/properties/value-precedence).
