# Lucent design language

<!-- impeccable:design-schema 1 -->

## Thesis

**Registration Overlay** expresses Lucent as separate typed layers resolving into one exact native interface. The system combines compiler source-map precision with print registration: graphite and paper establish the working field, teal identifies source and active state, and coral identifies dependencies, change, or misalignment.

Folded geometry is reserved for the icon and for visualizing compilation or reactive propagation. Ordinary controls remain compact, rectangular, and native. This keeps the metaphor meaningful instead of turning it into decoration.

The north-star composition is [Dark fold ribbon](.impeccable/mocks/workbench/dark-ribbon.webp): a wide editor and aligned native preview joined by a narrow fold-map that traces selected source into output.

## Mark

The Lucent mark is a folded **L**: teal faces describe the declared surface, a coral edge exposes the dependency layer, and paper-colored negative space keeps the form open and legible.

- Master: [`design/lucent-icon.svg`](design/lucent-icon.svg)
- Grayscale: [`design/lucent-icon-monochrome.svg`](design/lucent-icon-monochrome.svg)
- Both assets use the same 45° extrusion geometry. Use the grayscale mark where color is unavailable; its tonal facets must remain intact.
- Use the color mark at 24 px and above. At 16–20 px, prefer grayscale when the teal/coral distinction is not clear.
- Keep clear space equal to the vertical stem width.
- Do not rotate it, add glow, round its geometry, or place it inside a generic gradient tile.
- Platform packaging may add a solid graphite or paper backdrop when the OS requires an app tile.

The wordmark is simply **Lucent** in the display/UI face at semibold weight. Do not stylize individual letters or repeat the folded geometry in the wordmark.

## Color

| Role | Light | Dark | Use |
|---|---:|---:|---|
| Canvas | `#F2F0E9` | `#101820` | Main working field |
| Surface | `#FFFEFA` | `#17232C` | Panels and controls |
| Raised surface | `#FFFFFF` | `#1E2E38` | Menus, dialogs, popovers |
| Text | `#101820` | `#F2F0E9` | Primary copy and code chrome |
| Muted text | `#56636A` | `#AAB6B6` | Secondary metadata |
| Border | `#C7CCC8` | `#32444D` | Hairline structure |
| Teal | `#007A7B` | `#39C6C4` | Focus, links, selection, source |
| Vivid teal | `#00A6A6` | `#39C6C4` | Non-text geometry and mark |
| Coral | `#C23F45` | `#FF8A72` | Change and dependency |
| Success | `#287A4B` | `#59C987` | Successful system status |
| Warning | `#8A6200` | `#F2C14E` | Warning status |
| Danger | `#A52E34` | `#FF6670` | Errors and destructive actions |

Teal and coral are semantic layers, not decoration. Do not scatter both across every control. Large regions stay graphite or paper; color appears where source aligns, state changes, or focus moves.

The color mark uses brighter non-text registration inks: teal `#00A6A6`, teal shadow `#007C7D`, and coral `#F05D5E`. These are exposed as mark-only tokens and do not replace accessible UI text colors.

Canonical CSS tokens live in [`design/tokens.css`](design/tokens.css). Avalonia resources should retain the same `Lucent.*` role names rather than copying raw hex values into controls.

## Typography

- UI and documentation: `Segoe UI Variable`, `SF Pro Text`, `Noto Sans`, system sans-serif.
- Code: `Cascadia Code`, `SFMono-Regular`, `Noto Sans Mono`, monospace.
- Use regular and semibold weights. Bold is reserved for short status or document anchors.
- Keep UI copy compact; use sentence case and normal tracking.
- Documentation body text targets 65–75 characters per line. Code size is never smaller than adjacent secondary UI text.

Native system stacks are deliberate: Lucent should feel at home on the desktop and should not require a font payload before its theme works.

## Geometry and layout

- Base spacing unit: 4. Common steps: 8, 12, 16, 24, 32.
- Corner radii: 2 px for compact controls, 4 px for standard controls, 8 px only for dialogs or large floating surfaces.
- Borders are 1 px. Use a 2 px offset only for registration or active-layer emphasis.
- Prefer aligned panes, rails, split views, and continuous work surfaces over floating card collections.
- Dense application views use one dominant editor or content region, one supporting output region, and a compact status/diagnostic edge.
- Folded facets belong in dependency graphs, compiler pipelines, onboarding explanations, and empty-state diagrams—not buttons, fields, or content cards.

## Controls and state

- **Default:** paper/graphite surface with a quiet border.
- **Hover:** shift the surface one tonal step; do not lift the control.
- **Focus:** 2 px teal outline with at least 2 px separation from content.
- **Selected/source:** teal edge or field.
- **Changed/dependent:** coral edge or annotation.
- **Disabled:** reduce contrast while preserving shape; never use opacity alone for critical state.
- **Warning and error:** use their status token plus an icon or label; color alone is insufficient.

Shadows are limited to popovers, menus, and dialogs. Panels are separated by alignment, tone, and hairlines rather than elevation.

## Iconography

Use simple 1.5–2 px outline icons with square terminals and minimal rounding. Icons describe actions; the folded Lucent geometry is not a general icon style. Filled icons are reserved for selected navigation or status that needs stronger scanning.

## Motion

Motion should feel like registration: offset layers settle into alignment.

- Hover and press: 120 ms.
- Pane, selection, and source-to-output alignment: 180 ms.
- Prefer opacity and short translation; one layer may begin 2–4 px offset before resolving.
- Use fold/unfold motion only when structure is actually expanding or dependency flow is being explained.
- Reduced-motion mode removes translation and folding while preserving the final state and timing hierarchy.

## Surface translation

### Avalonia theme and examples

Start with the dark graphite shell for rails, title bars, workbench chrome, and developer-focused examples. Use paper surfaces for native previews, documents, and editable content. Keep native control behavior and platform window conventions; Lucent branding appears through palette, density, focus, and pane alignment rather than custom control silhouettes.

### Documentation

Use the light paper canvas by default with graphite navigation and code panels. Teal marks the current section, links, and API anchors. Coral is reserved for changed behavior, caveats, and experimental status. Registration marks can anchor page position or connect syntax to generated output, but should not frame every section.

### Example applications

Examples should demonstrate the same token roles and control states without all becoming miniature Workbench clones. Use folded diagrams only where an example exposes data flow or compilation. Product-specific content remains the dominant visual subject.

## Voice

Lucent is direct, technical, and candid about its experimental status. Prefer concrete mechanism over hype: “compiled fine-grained updates” rather than “blazing-fast magic.” Labels are short; documentation explains trade-offs plainly.
