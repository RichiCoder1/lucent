# Styling and design tokens

CSS is a first-class Lucent authoring language. It gives application developers familiar selectors, variables, and state styling while allowing the compiler to validate and lower styles into an efficient Avalonia representation.

CSS authoring does not imply a browser layout engine or runtime string-based CSS implementation.

## Intended pipeline

```text
.css
  -> CSS parser
  -> typed style IR
  -> generated or Avalonia style representation
```

Static styles should be parsed and validated during the build. The runtime may still resolve values that are inherently dynamic, but it should not repeatedly parse known strings or rediscover known property mappings.

## Initial CSS surface

The executable subset supports:

- one type selector and/or one class per rule;
- one trailing Avalonia pseudo-class such as `:pointerover` or `:focus`;
- compile-time `:root` custom properties;
- colors, opacity, spacing, thickness, corner radius, size, font size and
  weight, and alignment;
- comma-separated `transition` declarations for brush, double, thickness, and
  corner-radius properties.

Adjacent `.lui` and `.css` files are compiled together. The compiler emits
native Avalonia `Style`, `Setter`, and typed `Transition` objects; it does not
ship CSS strings or a runtime CSS parser.

```css
:root {
    --surface: #ffffff;
    --surface-hover: #f5f5f5;
    --accent: #7357e6;
    --space-3: 12px;
    --radius-md: 8px;
}

.card {
    background: var(--surface);
    padding: var(--space-3);
    border-radius: var(--radius-md);
}

.card:pointerover {
    background: var(--surface-hover);
}
```

This is deliberately Avalonia-targeted rather than browser-compatible:

- `gap` maps to `StackPanel.Spacing`;
- `padding` is valid only on controls that expose an Avalonia padding property;
- selectors match projected Avalonia controls and their real pseudo-classes;
- property names must map to an animatable or styled Avalonia property;
- CSS pixels are device-independent Avalonia units;
- transitions use Avalonia animation priority and reveal the latest underlying
  value when interrupted or completed.

Descendant/child combinators, IDs, scoping, media/platform conditions,
keyframes, transforms, enter/exit lifetime animation, and reduced-motion policy
remain deferred. Add them only when a concrete application needs them.

## Design tokens

CSS custom properties are the preferred authoring model for design tokens:

```css
:root {
    --color-primary: #7357e6;
    --color-primary-foreground: white;
    --space-1: 4px;
    --space-2: 8px;
    --radius-sm: 4px;
    --radius-md: 8px;
}
```

The compiler should explore typed declarations for colors, lengths, typography, borders, motion, and elevation. A token that represents a length should not remain an arbitrary string once it reaches generated code. Static validation should catch category errors and diagnostics should name both the expected and supplied token types.

Generating a typed C# surface such as `Theme.RadiusMd` may be useful, but it is not yet a committed API.

## Layout

Fundamental native layout belongs in UI structure where that is clearer:

```csharp
Row {
    gap: 8;

    Sidebar {
        width: 240;
    }

    Column {
        grow: 1;
        Content();
    }
}
```

Lucent should not require browser vocabulary such as `display: flex` merely to express a row or column. CSS can still style layout-related properties when that produces a cleaner API.

## Component strategy

Lucent should ship strong primitives and encourage source-owned higher-level components. Likely primitives include text, buttons, inputs, images, scrolling, rows, columns, grids, overlays, popups, windows, and canvases.

A future workflow may copy editable components such as dialogs, command palettes, or data tables into an application. That direction is preferred over making Lucent a huge catalog of opaque controls with every behavior exposed as an option.
