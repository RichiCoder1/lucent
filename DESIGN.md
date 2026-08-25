# Lucent application design language

## Authority

Application UI uses the reviewed **Shadcn New York / neutral** semantic palette
through `FluentTheme` plus `ShadcnTheme`. `Shadcn.*` resources are the only
application visual vocabulary; [`design/tokens.css`](design/tokens.css) records
the matching light and dark values. Do not add `Lucent.*` visual aliases.

Use native Avalonia controls and Fluent behavior. `ShadcnTheme` supplies semantic
surfaces, controls, focus rings, and button variants; the optional utility catalog
is not installed by applications.

## Semantic resources

- `Shadcn.Background` and `Shadcn.Foreground` form the working surface.
- `Shadcn.Card` and `Shadcn.Popover` distinguish persistent and transient
  surfaces.
- `Shadcn.Primary`, `Shadcn.Secondary`, `Shadcn.Muted`, and `Shadcn.Accent`
  express ordinary hierarchy.
- `Shadcn.Destructive` marks destructive and failure actions; pair it with text
  or an icon.
- `Shadcn.Border`, `Shadcn.Input`, and `Shadcn.Ring` provide structure and
  keyboard-visible focus.

Keep layouts compact and native: four-pixel spacing steps, hairline borders,
modest radii, and shadows only for menus, popovers, and dialogs. Do not replace
Fluent or AvaloniaEdit templates. Keyboard focus, disabled state, and adaptive
minimum sizes are behavior contracts, not decoration.

## Mark

The Lucent mark remains the application icon. Use the color mark at 24 px and
above; use the monochrome mark at 16–20 px. The mark is branding, not an
application color-token layer.
