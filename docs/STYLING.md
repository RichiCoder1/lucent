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

The proof of concept should start with:

- type selectors;
- classes;
- useful pseudo-classes;
- basic descendant and child combinators;
- CSS custom properties;
- a narrow set of common Avalonia visual properties.

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

CSS scoping, IDs, media or platform conditions, transitions, animations, and the exact selector mapping to Avalonia are not settled. Add them only after the initial style IR and control projection prove the basic model.

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
