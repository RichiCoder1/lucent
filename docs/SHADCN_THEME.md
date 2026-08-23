# Shadcn-style Avalonia theme

## Status

**Proposed working choice.** This document records the decisions from the theme
design grilling session. The package and resource names are experimental while
Lucent remains pre-stable.

This proposal intentionally changes the visual direction established by
[`DESIGN.md`](../DESIGN.md), [`design/tokens.css`](../design/tokens.css), and
[Plan 007](../plans/007-example-ux-css-quality.md). Those files remain the current
authority until the theme is implemented and the visual migration is accepted.
The implementation change must update or supersede them rather than leaving two
competing design systems in the repository.

## Decision

Add an experimental `Lucent.Themes.Shadcn` project containing a reusable
Avalonia theme based on the current Shadcn **New York / neutral** defaults.
The theme will:

- layer focused resources and styles over Avalonia's `FluentTheme` rather than
  replacing mature native control templates;
- provide complete light and dark variants;
- use Shadcn semantic resource names instead of the existing `Lucent.*` visual
  token names;
- use a neutral near-black/near-white primary rather than Lucent teal;
- use platform UI fonts without bundling a font;
- target compact 36-DIP buttons and inputs;
- cover the common forms and navigation controls listed below;
- expose Shadcn button variants through Avalonia style classes; and
- preserve AA contrast, visible keyboard focus, validation feedback, and native
  accessibility behavior.

The first delivery is the theme, Lucent CSS `oklch(...)` support, and a control
gallery. A separate follow-up redesigns every example against the proven theme.

## Why

The current examples use Fluent controls with application-specific resource
dictionaries and adjacent CSS. They are polished enough to prove the styling
pipeline, but the control language is inconsistent and carries more visual
identity than the applications need. The desired replacement is quieter,
denser, and more predictable: neutral surfaces, clear borders, compact controls,
limited elevation, and semantic variants that work the same way everywhere.

This is a theme, not a second component framework. Avalonia continues to own
control behavior, templating, input, automation, and platform integration.
Lucent adds a coherent default treatment and the CSS color support needed to
author the source palette accurately.

## Goals

- Give Lucent applications one reusable, opt-in desktop theme.
- Closely mirror a reviewed snapshot of Shadcn New York / neutral values and
  proportions rather than loosely borrowing the aesthetic.
- Keep native Avalonia controls and Fluent behavior as the rendering base.
- Make light and dark modes complete, not a primary mode plus partial overrides.
- Make common control and interaction states reviewable in one gallery.
- Remove duplicated example theme dictionaries when examples migrate.
- Accept Shadcn's OKLCH source colors directly in Lucent CSS.

## Non-goals

- Reimplementing the full Fluent control-template catalog.
- Building Shadcn's React component API or a Lucent wrapper for each control.
- Covering every Avalonia control in the first version.
- Adding responsive density presets, high-contrast dictionaries, or a bundled
  icon/font package in the first version.
- Automatically tracking every upstream Shadcn change.
- Keeping compatibility aliases for the existing `Lucent.*` visual tokens.
- Redesigning examples in the same change that introduces the theme.

## Theme boundary

Applications opt in explicitly and keep the base theme visible:

```csharp
Styles.Add(new FluentTheme());
Styles.Add(new ShadcnTheme());
```

`ShadcnTheme` supplies theme dictionaries, Fluent resource overrides, and
styles for the supported controls. It should not silently install Fluent or
own application theme selection. Hosts continue to use Avalonia's
`RequestedThemeVariant` and `ThemeVariant.Light` / `ThemeVariant.Dark`.

The theme is usable from ordinary Avalonia applications. It may depend on
Avalonia and `Avalonia.Themes.Fluent`, but not on the Lucent compiler or runtime.

## Semantic resources

Use Shadcn semantics as the public vocabulary. Avalonia keys should be namespaced
to avoid collisions while keeping the familiar role names:

- `Shadcn.Background`, `Shadcn.Foreground`
- `Shadcn.Card`, `Shadcn.CardForeground`
- `Shadcn.Popover`, `Shadcn.PopoverForeground`
- `Shadcn.Primary`, `Shadcn.PrimaryForeground`
- `Shadcn.Secondary`, `Shadcn.SecondaryForeground`
- `Shadcn.Muted`, `Shadcn.MutedForeground`
- `Shadcn.Accent`, `Shadcn.AccentForeground`
- `Shadcn.Destructive`, `Shadcn.DestructiveForeground`
- `Shadcn.Border`, `Shadcn.Input`, `Shadcn.Ring`

The project may add namespaced spacing, radius, typography, and duration
resources where a control style needs them. It should not create a second set of
synonymous canvas/surface/text keys. The exact token list is a documented but
experimental package contract.

The palette is a reviewed snapshot, not a live dependency. Record the upstream
source URL, retrieval date, Shadcn style, base color, and original OKLCH values
next to the committed theme values. Future upstream changes are normal reviewed
theme changes, not automatic upgrades.

## Typography, geometry, and elevation

- Use the platform UI font family. Do not bundle Inter.
- Use compact New York proportions with a 36-DIP default button and input
  height.
- Keep a 4-DIP spacing base and restrained radii derived from the selected
  Shadcn snapshot.
- Use one-DIP borders for controls and structural separation.
- Reserve shadows for popovers, menus, tooltips, and dialogs. Hover should
  change tone, not lift controls.
- Keep animations brief and nonessential. Preserve final states when reduced
  motion is requested.

Compact sizing does not justify clipped labels, hidden focus rings, or tiny
interaction targets in controls that need more space. Content may increase a
control's measured size where Avalonia's layout requires it.

## Controls and variants

Version one covers:

- `Button`
- `TextBox`
- `CheckBox`
- `RadioButton`
- `ComboBox`
- `ListBox`
- `Menu`
- `TabControl`
- `ToolTip`
- `ScrollBar`
- a `card` style class for an ordinary `Border`

An unclassed `Button` uses the solid primary treatment, matching Shadcn's
default button. Additional variants use Avalonia classes:

- `secondary`
- `destructive`
- `outline`
- `ghost`
- `link`

The theme must define normal, pointer-over, pressed, disabled, focused,
focus-visible, selected, checked, unchecked, and validation states where the
control supports them. It should override public Fluent resources and attach
ordinary styles first. Replace a `ControlTemplate` only when a required result
cannot be achieved through supported resource/style seams, and record that
exception in the implementation review.

DataGrid, TreeView, Slider, ProgressBar, ToggleSwitch, dialogs, flyouts, and
AvaloniaEdit integration are deferred until an application demonstrates that
the base treatment is insufficient.

## Accessibility

The first version must retain native automation and keyboard behavior and meet
these visual requirements:

- primary and secondary text, controls, and interaction states target WCAG AA
  contrast;
- keyboard focus uses a visible two-DIP ring based on `Shadcn.Ring`;
- focus indication is not communicated by color alone when the surrounding
  geometry would otherwise disappear;
- validation and destructive states pair color with native state, text, or an
  icon supplied by the application;
- disabled controls remain identifiable and do not rely on opacity alone for
  critical meaning; and
- light and dark variants receive the same state review.

An explicit high-contrast theme is out of scope for version one. The theme must
still avoid breaking Avalonia's platform accessibility behavior.

## OKLCH support in Lucent CSS

Add `oklch(...)` as a typed CSS color anywhere Lucent currently accepts a color.
At minimum, support the space-separated CSS Color 4 form used by the selected
Shadcn snapshot, including an optional alpha component:

```css
color: oklch(0.145 0 0);
background: oklch(0.985 0 0 / 80%);
```

Parsing, validation, conversion, diagnostics, and generated Avalonia color
values remain compile-time concerns. Applications should not pay for a runtime
CSS parser or runtime color conversion.

Evaluate a focused open-source color package before writing conversion code. A
package is acceptable only if it has a permissive license, a small dependency
surface, active enough maintenance, correct OKLCH-to-sRGB behavior, and an API
that can be isolated behind the existing CSS value conversion path. If no
package is a good fit, implement the standard conversion internally with one
focused test covering neutral values, chromatic values, alpha, bounds, and
out-of-gamut handling.

Do not add the Actipro controls package solely for `UIColor.FromOklch`. The API
has the right shape, but the helper ships through the broader licensed
`ActiproSoftware.Controls.Avalonia` package. It is a useful behavior reference,
not a sensible color-conversion dependency for Lucent.

The reusable Avalonia theme must not depend on the compiler to resolve its
colors. Commit the converted Avalonia color values alongside their original
OKLCH source values. Lucent CSS uses the compiler conversion path; the theme
package uses normal Avalonia resources.

## Control gallery

Add one small gallery application that can switch between light and dark modes
and displays every supported control, button variant, and relevant state. It is
the visual review surface for the theme, not a general component showcase.

The gallery should make these comparisons easy:

- default, hover, pressed, disabled, and keyboard-focused buttons;
- empty, populated, focused, disabled, and invalid form fields;
- checked/unchecked and selected/unselected controls;
- menus, tabs, lists, tooltips, scrollbars, and cards on background and raised
  surfaces; and
- primary, muted, accent, destructive, border, input, and ring tokens in both
  theme variants.

Keep deterministic light and dark launch modes so reviewers can capture the
same surface. Pixel-golden testing is not required for the first version.

## Example migration

Redesign all examples only after the theme and gallery are accepted:

- Counter
- Todo
- Package Pulse
- Workbench
- the desktop POC where it still represents a user-facing sample

Preserve each example's behavior, teaching purpose, accessibility contracts,
and existing interaction tests. The redesign may replace its colors,
typography, radii, control states, and composition. This is a full visual
redesign, not a thin theme swap.

Rewrite adjacent CSS and application resources to use the new semantic keys.
Delete duplicated `Lucent.*` theme dictionaries and do not retain temporary
aliases. App-specific resources remain valid only for semantics the shared
theme does not own.

Because this supersedes the Registration Overlay treatment for application UI,
the migration must update `DESIGN.md`, `design/tokens.css`, Plan 007's visual
authority, and Plan 010's instruction to preserve that authority. The Lucent
mark and product identity can remain unless the redesign proves they conflict;
this proposal does not require a new logo.

## Delivery sequence

### 1. Color and theme foundation

1. Snapshot and record the selected Shadcn New York / neutral upstream values.
2. Add and test Lucent CSS `oklch(...)` support.
3. Add `Lucent.Themes.Shadcn` with light/dark resources and core styles.
4. Add the control gallery and complete the light/dark accessibility review.

### 2. Example redesign

1. Replace each example's Fluent-only setup with Fluent plus `ShadcnTheme`.
2. Rewrite application CSS/resources against the Shadcn semantic vocabulary.
3. Remove old duplicated visual tokens and dictionaries.
4. Re-run existing build, headless interaction, keyboard, and visual review
   gates.
5. Update the repository's visual authorities and roadmap references.

Do not combine these phases into one large change. The gallery must prove the
theme API before every example depends on it.

## Acceptance criteria

- A normal Avalonia application can opt in with `FluentTheme` plus
  `ShadcnTheme` and switch between complete light and dark dictionaries.
- The documented semantic resources resolve in both variants.
- Every version-one control and button variant is present in the gallery.
- Compact controls retain readable content, validation treatment, keyboard
  navigation, native automation, and visible focus.
- Lucent CSS accepts valid `oklch(...)` colors, rejects malformed values with a
  source diagnostic, and emits deterministic Avalonia colors.
- The implementation records the upstream Shadcn snapshot and original OKLCH
  values.
- No Actipro dependency, runtime CSS parser, bundled font, custom control
  hierarchy, or compatibility token layer is introduced.
- Example redesign begins only after the theme/gallery change is accepted.

## Open implementation checks

These are evidence gates, not unresolved product decisions:

- Confirm which public Fluent resources are sufficient for each supported
  control before replacing any template.
- Compare focused OSS OKLCH packages against the dependency criteria above.
- Define and test the exact out-of-gamut conversion behavior used by the CSS
  compiler.
- Capture the precise upstream Shadcn source and values when implementation
  starts.
