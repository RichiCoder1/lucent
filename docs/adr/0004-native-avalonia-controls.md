---
status: accepted
---

# Make native Avalonia controls the default control surface

Lucent render declarations resolve to real Avalonia control types and members by
default. Authors use names such as `StackPanel`, `TextBlock`, `TextBox`,
`Button`, `Spacing`, `Text`, and `Click`; generated code constructs those types
and writes those members directly. A separate `native` or `raw` syntax is not
required.

Nested content on a native control follows the control's Avalonia content
contract. The semantic binder will derive that contract from the effective
property marked with `Avalonia.Metadata.ContentAttribute`: collection targets
such as `Panel.Children` and `ItemsControl.Items` accept multiple children,
while scalar targets such as `ContentControl.Content` and `Decorator.Child`
accept one compatible child. Lucent must not guess a property by name or
silently insert a layout panel around excess children.

Scalar native content may be supplied explicitly (`Content: "Add"`) or through
trailing scalar syntax (`"Add"`). Both resolve to the same content-property
symbol and therefore conflict with each other or with a nested control.

Native literal conveniences are also target-type-driven. The compiler may
provide adapters for framework value types such as `Thickness` and
`CornerRadius`, but the adapter is selected by the resolved .NET target type,
never by the spelling of a control or property. Explicit C# expressions bypass
the convenience layer.

This native content route is distinct from a Lucent component slot. Native
controls receive children through Avalonia metadata; Lucent components receive
an implicit `children` slot and place it explicitly with `yield children`.

## Optional Lucent controls

An optional Lucent control library is allowed, but its controls use the same
native/component resolution path and require no compiler registry entries.
Direct Avalonia and third-party controls remain the floor and escape hatch.

A Lucent control must pass the deletion test: removing it should make meaningful
layout, validation, accessibility, focus, lifecycle, cancellation, or reactive
coordination reappear across callers. A wrapper that only renames an Avalonia
type or forwards its properties does not earn a place in the library.

## Consequences

- Avalonia documentation, DevTools, accessibility peers, themes, and third-party
  control APIs remain directly relevant to Lucent authors.
- The compiler needs project-aware Roslyn symbols to resolve control types,
  properties, events, conversions, attached properties, and content metadata.
- The MSBuild adapter must supply consuming-project references, and the language
  server eventually needs project context for equivalent native diagnostics.
- Direct property assignments are Avalonia local values and therefore retain
  Avalonia's value-precedence behavior; styling and `SetCurrentValue` remain a
  separate lowering concern.
- Templates, resources, namescopes, attached properties, `ItemsSource`, and
  routed-event options require explicit semantics rather than accidental XAML
  emulation.
