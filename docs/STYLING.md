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

## Package catalog metadata

Style producers share one internal Plan 008a manifest schema and immutable,
producer-neutral `StyleClassCatalog` entries: name, optional applicable native
type, origin, optional source identity, and detail. The deterministic UTF-8 JSON
manifest is embedded before `CoreCompile` and retained as an isolated
intermediate artifact; it is not a public stable format, runtime style registry,
or component invocation mechanism.

Tooling consumes references through the shared non-executing
`PEReader`/`MetadataReader` reader and bounded project-generation cache. Local
and open CSS remains authoritative. Theme, global-style, and utility plans may
use this one seam and its public installable catalog-type verification, but they
own installation, precedence, and runtime evidence. Metadata alone never
activates styles.

## Avalonia CSS surface (Plan 007)

The executable subset is one compile-time catalog shared by parsing, semantic
validation, native lowering, and editor metadata. It supports type, class,
multiple-class, `#Name`, descendant, direct-child, and one terminal Avalonia
pseudo-class selectors (`:pointerover`, `:pressed`, `:focus`, `:focus-visible`,
`:disabled`, `:checked`, `:unchecked`, and `:selected`). `#Name` matches the
native Avalonia `Name`, not an automation ID.

The catalog covers brushes, opacity, padding/margin, borders, corner radius,
box shadows, spacing, font family/style/size/weight, text alignment/wrapping,
line height, letter spacing, dimensions, alignment, visibility, clipping,
cursor, and native transitions. Values are emitted as typed Avalonia setters;
unsupported units, values, and transitions produce a Lucent diagnostic.

`resource("Key")` lowers to Avalonia's public
`DynamicResourceExtension`, so a coded setter follows changes in the owning
application resource dictionary without polling or reparsing. `var(--token)`
is still only a compile-time alias. Unquoted keys remain accepted for backward
compatibility, but quoted keys are preferred because ordinary CSS tooling parses
dotted resource names correctly. Example:

Adjacent `.lui` and `.css` files are compiled together. The compiler emits
native Avalonia `Style`, `Setter`, and typed `Transition` objects; it does not
ship CSS strings or a runtime CSS parser.

`oklch(L C H / A)` is accepted anywhere this subset accepts a brush. Lucent
converts it to clipped sRGB at compile time; theme packages commit their own
reviewed hex resources and never depend on the compiler at runtime.

```css
:root {
    --surface: resource("Lucent.Surface");
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

Styles are attached to each native root produced by the owning component;
selectors traverse only that root's native Avalonia style tree. Browser layout,
selector lists, `:not`, structural/template selectors, media/platform
conditions, imports, keyframes, transforms, and CSS runtime parsing remain
unsupported. Reduced motion is an application/theme concern; zero-duration
native resources preserve final state.

## Global styles

Project-wide rules are opt-in build inputs:

```xml
<ItemGroup>
  <LucentStyle Include="Styles/app.css" />
</ItemGroup>
```

Evaluated item order is preserved (later files win at equal native priority).
The build emits one public `<RootNamespace>.LucentStyles` for apps and
libraries. Hosts install it explicitly; libraries never install themselves:

```csharp
Styles.Add(new MyApp.LucentStyles());
Styles.Add(new SharedLibrary.LucentStyles());
```

An executable project with `LucentStyle` items and no direct call emits a
warning with that call. Completion reads the same embedded module manifest only
after the direct semantic installation; construction, aliases, and indirect
flows do not activate it. No runtime CSS parser, startup hook, or transitive
library installation exists.

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

The compiler validates typed declarations for colors, lengths, typography,
borders, motion, and elevation. A token that represents a length does not
remain an arbitrary string once it reaches generated code. Static validation
catches category errors and diagnostics name both the expected and supplied
token types.

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
