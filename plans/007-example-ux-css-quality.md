# Plan 007: Make every example a polished Lucent showcase

> **Executor instructions**: Complete Plan 006 first. Improve the four existing
> example applications and the Lucent-to-Avalonia CSS bridge they actually share;
> do not create a component library, browser CSS engine, custom control theme, or
> second application framework. Preserve each example's accepted language/runtime
> proof and reuse native Avalonia controls, FluentTheme, resources, and states.
>
> **Drift check**:
> `git diff --stat 2c97060..HEAD -- examples src/Lucent.Compiler tests docs plans`
> Stop if Plan 006's settings, accessibility, error, or headless-flow contracts
> are still moving, or if an example no longer builds from its documented command.

## Status

- **Priority**: P1
- **Effort**: L
- **Risk**: HIGH
- **Depends on**: plans 001–006
- **Category**: UX / visual quality / styling integration
- **Planned at**: commit `2c97060`, 2026-08-16

## Why this matters

The examples prove increasingly capable language and runtime slices, but they do
not yet present Lucent as a credible way to build finished desktop software.
Counter is visually skeletal, Todo relies on fixed widths and inline colors,
Package Pulse is the only authored visual treatment, and Workbench still reads
as a vertical collection of proof controls rather than a developer tool.

This plan makes the examples one cohesive Lucent family while keeping their
different jobs visible. It also closes the common Avalonia styling gaps exposed
by that work before Plan 008 promises CSS editor intelligence and before Plan
010 treats Workbench as the release oracle.

## Product and visual brief

`PRODUCT.md` is the product authority and `DESIGN.md` is the visual authority.
`design/tokens.css`, `design/lucent-icon.svg`,
`design/lucent-icon-monochrome.svg`, and the approved
`.impeccable/mocks/workbench/dark-ribbon.webp` are Plan 007 inputs, not optional
inspiration. If this plan's prose conflicts with those files, those committed
authorities win and the plan must be corrected before implementation.

All four surfaces are **Operate** experiences: the primary task must be obvious,
keyboard use must remain first-class, and native behavior outranks decoration.

- Apply the accepted **Shadcn New York / neutral** semantic resources through
  Fluent plus `ShadcnTheme`. Color remains semantic, not decoration.
- Use compact native spacing, modest radii, hairline pane structure, and limited
  raised-surface shadows. Each app may vary density and emphasis.
- Hover shifts surface tone without lift; shadows are limited to menus, popovers,
  and dialogs. Disabled state never relies on opacity alone, and warning/error
  state pairs its semantic color with an icon or label.
- Keyboard focus uses the shared Shadcn ring and native focus behavior. Reduced
  motion preserves final state without hiding content.
- Counter is the smallest confidence-building sample: one dominant value, one
  unmistakable action, and no incidental chrome.
- Todo is a calm daily-work surface: fast entry, scannable rows, obvious filters,
  useful empty/completed states, and no fixed-width clipping.
- Package Pulse remains the expressive async/motion sample: search and result
  status lead, stale/loading/error/empty states are legible, and motion is brief
  and nonessential.
- Workbench becomes a credible compact developer tool: menu/command area,
  bounded project sidebar, editor workspace, problems panel, status line,
  command palette, settings, and generated preview have clear hierarchy.

`ShadcnTheme` is the shared application authority. Adjacent CSS uses its
`Shadcn.*` semantic resources; examples do not repeat application resource
dictionaries or retain a compatibility vocabulary. Use the committed Lucent mark
as an icon only; add no icon or font dependency.

The approved Workbench mock is a hierarchy/composition north star, not a feature
specification. Preserve its aligned source/output work surface, compact rails,
paper-on-graphite contrast, and registration cues, but do not add the mock's
illustrative live native preview, dependency graph, or unsupported commands.

## Current styling boundary

- Adjacent CSS supports one type, one class, and one trailing pseudo-class.
- The compiler accepts a small hard-coded property set and lowers values in the
  emitter. Property support, value typing, diagnostics, and future completion do
  not yet share one catalog.
- `:root` variables are compile-time string substitutions. They cannot reference
  Avalonia dynamic resources, so they do not follow theme-variant changes.
- Common native states such as `:focus-visible`, `:disabled`, `:checked`, and
  `:selected` fit Avalonia's model but are not exercised as a quality contract.
- Border treatment, shadows, richer typography, content alignment, visibility,
  and common text behavior are missing from the authored CSS surface.
- FluentTheme and AvaloniaEdit's Fluent styles already exist. Preserve those
  templates and focus behavior instead of recreating them.
- ShadcnTheme's native focus rendering must preserve keyboard-visible focus
  without template replacement.
- `ShadcnTheme` is the executable application theme. Native light/dark
  `Shadcn.*` resources and `design/tokens.css` must remain aligned.

## CSS-to-Avalonia contract

The target is broad parity with the **common author-facing Avalonia styling
surface**, not web CSS compatibility and not every Avalonia property.

### One typed property catalog

Replace the parser/emitter's parallel hard-coded decisions with one internal
catalog consumed by parsing, semantic validation, lowering, diagnostics, and
Plan 008's CSS completion/navigation work. Each entry records:

- CSS name and native Avalonia property name;
- accepted control/property owners resolved through the existing project-aware
  Avalonia symbol resolver;
- value kind and allowed keywords;
- transition kind, when the native property is animatable;
- whether dynamic resource values are valid.

The catalog is compile-time metadata only. Generated applications retain native
`Style`, `Setter`, selector, resource, and transition objects; add no runtime CSS
parser, reflection lookup, or public styling abstraction.

### Selectors and states

Support and test this bounded selector grammar:

- type (`Button`), class (`.primary`), multiple classes
  (`Button.toolbar.primary`), and Avalonia name (`#SearchBox`) selectors;
- direct-child and descendant combinators within a component's styled roots;
- one terminal native pseudo-class;
- the common states `:pointerover`, `:pressed`, `:focus`, `:focus-visible`,
  `:disabled`, `:checked`, `:unchecked`, and `:selected`.

`#SearchBox` matches the native `Name` property, not
`AutomationProperties.AutomationId`; the corresponding control must assign a
static `Name: "SearchBox"`. Whitespace is the native descendant combinator and
`>` is the native direct-child combinator. Both traverse Avalonia's style tree,
not the Lucent source AST.

One adjacent stylesheet is scoped to every native root produced by its owning
component. Its selectors may match native descendants rendered by nested
components and supplied slots because those are part of that root's native style
tree, but may never escape to an ancestor or sibling root. For zero roots there
is no style host; for multiple, conditional, keyed, or slotted roots, attach the
same immutable style set to each root when it mounts. A combinator cannot join
two separate roots. Preserve this contract through component projection instead
of approximating source-level ancestry.

Selectors lower to public Avalonia selector APIs. A selector whose terminal
target cannot resolve to any native control/property owner reports a source
diagnostic rather than silently emitting nothing. Runtime class/name/state
matching remains Avalonia-owned.

Do not add selector lists, `:not`, structural pseudo-classes, `/template/`,
template-part selectors, arbitrary nesting, or specificity tricks in this plan.

### Properties and values

Keep the existing properties and add the common groups required to style all
four examples without re-templating controls:

- borders/surfaces: `border-color`, `border-width`, and `box-shadow`;
- typography: `font-family`, `font-style`, `text-align`, `text-wrap`,
  `line-height`, and `letter-spacing` where the resolved control supports them;
- layout/control: horizontal/vertical content alignment, `visibility`,
  `clip-to-bounds`, and `cursor`;
- transitions for native brush, double, thickness, corner-radius, and box-shadow
  property types.

Values remain typed before emission: colors/brushes, finite numbers and desktop
units, thickness, corner radius, box shadow, booleans, enums, font families, and
transition duration/easing. Reject unsupported units, invalid enum keywords,
non-finite numbers, and values applied to incompatible native controls with a
diagnostic naming the CSS property and resolved Avalonia target.

The Plan 007 catalog is exactly the following inventory. “Resolved property” is
the required public styled property on the actual target type; inherited owners
such as `Visual`, `Layoutable`, `InputElement`, `TextBlock`, `Border`,
`TemplatedControl`, `ContentControl`, and `StackPanel` remain Avalonia's source of
truth. A listed CSS property is not applicable when the target lacks that styled
property.

| CSS property | Required resolved property | Value kind | `resource()` | Transition |
| --- | --- | --- | --- | --- |
| `background` | `Background` | brush | yes | `BrushTransition` |
| `foreground` | `Foreground` | brush | yes | `BrushTransition` |
| `opacity` | `Opacity` | finite double | yes | `DoubleTransition` |
| `padding` | `Padding` | thickness | yes | `ThicknessTransition` |
| `margin` | `Margin` | thickness | yes | `ThicknessTransition` |
| `border-color` | `BorderBrush` | brush | yes | `BrushTransition` |
| `border-width` | `BorderThickness` | thickness | yes | `ThicknessTransition` |
| `border-radius` | `CornerRadius` | corner radius | yes | `CornerRadiusTransition` |
| `box-shadow` | `BoxShadow` | box shadows | yes | `BoxShadowsTransition` |
| `gap` | `Spacing` | finite double | yes | `DoubleTransition` |
| `font-family` | `FontFamily` | font family | yes | no |
| `font-size` | `FontSize` | positive finite double | yes | `DoubleTransition` |
| `font-style` | `FontStyle` | enum | yes | no |
| `font-weight` | `FontWeight` | enum/numeric weight | yes | no |
| `text-align` | `TextAlignment` | enum | yes | no |
| `text-wrap` | `TextWrapping` | enum | yes | no |
| `line-height` | `LineHeight` | non-negative finite double | yes | `DoubleTransition` |
| `letter-spacing` | `LetterSpacing` | finite double | yes | `DoubleTransition` |
| `width` | `Width` | non-negative finite double/`auto` | yes | `DoubleTransition` |
| `height` | `Height` | non-negative finite double/`auto` | yes | `DoubleTransition` |
| `min-width` | `MinWidth` | non-negative finite double | yes | `DoubleTransition` |
| `min-height` | `MinHeight` | non-negative finite double | yes | `DoubleTransition` |
| `max-width` | `MaxWidth` | non-negative finite double/`none` | yes | `DoubleTransition` |
| `max-height` | `MaxHeight` | non-negative finite double/`none` | yes | `DoubleTransition` |
| `horizontal-alignment` | `HorizontalAlignment` | enum | yes | no |
| `vertical-alignment` | `VerticalAlignment` | enum | yes | no |
| `horizontal-content-alignment` | `HorizontalContentAlignment` | enum | yes | no |
| `vertical-content-alignment` | `VerticalContentAlignment` | enum | yes | no |
| `visibility` | `IsVisible` | boolean | yes | no |
| `clip-to-bounds` | `ClipToBounds` | boolean | yes | no |
| `cursor` | `Cursor` | native cursor keyword | yes | no |
| `transition` | `Transitions` | property/duration/easing list | no | special |

Do not infer a transition type from an unknown property. The spike must verify
the five exact Avalonia 12.1.1 transition classes and each property pair listed
above; an unlisted or incompatible pair is a CSS diagnostic, never a fallback to
`DoubleTransition`.

### Theme resources

The candidate CSS value form is `resource(ResourceKey)`, intended to lower to a
native dynamic resource reference in a `Setter` and update when Avalonia's actual
theme variant changes. `var(--token)` may resolve to `resource(...)`, but remains
a compile-time lexical alias rather than a second resource system.

Applications define light/dark brushes and motion durations through Avalonia
theme dictionaries/resources and select variants through native
`RequestedThemeVariant`. Do not invent CSS media queries, a Lucent theme runtime,
or a second resource dictionary format.

This syntax and the catalog's `resource()` column are not accepted until Step 2
records the exact Avalonia 12.1.1 C# setter/resource API and generated form. If a
programmatically emitted dynamic reference does not track theme changes, stop
for approval: either retain native dynamic resources in `.lui`/application setup
and remove `resource()` from this plan's contract, or defer the whole theme bridge.
Never cache a one-time resource lookup and describe it as dynamic.

### Current visual authority

Plan 010 supersedes this plan's former per-example resource dictionaries. The
theme owns the reviewed light/dark values in `Shadcn.*`; `design/tokens.css`
records that same vocabulary. The focused migration test verifies that all four
examples install `ShadcnTheme`, reference it, and contain no legacy visual keys.

## Example quality contracts

### Counter

- Preserve the one-state/one-event teaching surface and compact window.
- Give the count dominant hierarchy, clarify the action, and show pointer,
  pressed, disabled, and keyboard-focus treatment without replacing Button's
  Fluent template.
- Demonstrate shared Shadcn semantic resources and native theme switching. No
  reset/history/settings feature is added.

### Todo

- Replace fixed widths and inline light-green/light-blue rows with bounded native
  layout and authored classes/resources that work at the declared minimum size.
- Make entry, completion, deletion, filtering, remaining count, progress, empty
  state, and completed state visually distinct without color-only meaning.
- Use Shadcn focus, primary, and destructive semantics; completed rows retain
  their explicit status label.
- Preserve keyed row identity, direct native controls, editing behavior, and the
  accepted TodoMVC scope. Add no persistence, drag/reorder, due dates, or routing.

### Package Pulse

- Preserve its dark-forward personality while supporting both theme variants.
- Distinguish initial loading, stale refresh, error, empty, and populated states;
  retain content stability while async work is pending.
- Use Shadcn primary and destructive semantics for query/results and failure.
  Every operation remains understandable without motion.

### Workbench

- Replace the top-level vertical proof stack with a bounded native Grid-based
  application shell while retaining Plan 006's commands, focus routes,
  automation metadata, settings, error paths, and single headless harness.
- Establish clear menu/toolbar, sidebar, editor, problems, status, palette,
  settings, and generated-preview regions. Use AvaloniaEdit's shipped Fluent
  theme; style around it rather than copying or replacing its template.
- Translate the approved dark-ribbon mock into the capabilities Workbench
  actually has: aligned panes and diagnostic edges, not its illustrative live
  preview/dependency features. Record every deliberate compositional deviation.
- Support the saved sidebar width and problems visibility from Plan 006, minimum
  window constraints, keyboard-visible focus, empty workspace/document/problems
  states, and long paths/messages without clipped primary actions. Physical 200%
  Windows scaling is an approved manual follow-up.
- Keep placeholder project/problem content until Plan 011. This plan changes its
  presentation, not the loader, filesystem, compiler, or release behavior.

## Visual evidence contract

Commit a short review record under `docs/quality/007-example-ux/` and exactly
eight **final** reference captures—two per app. Captures are review evidence,
not pixel-golden tests. The executable gate is their declared logical Avalonia
size; physical Windows display scaling is not controlled by this runner. The user
approved 200% Windows scaling as a manual follow-up, so no 200% pass is claimed:

| Profile | Theme/state | Window size | Evidence |
| --- | --- | --- | --- |
| `counter-light` | light, incremented | 420×300 | logical capture |
| `counter-dark-focus` | dark, keyboard focus visible | 420×300 minimum | logical capture |
| `todo-light-populated` | light, populated | 900×760 | logical capture |
| `todo-dark-empty` | dark, empty/completed | 700×560 minimum | logical capture |
| `pulse-dark-results` | dark, populated | 820×760 | logical capture |
| `pulse-light-error` | light, error after stale content | 600×560 minimum | logical capture |
| `workbench-light-shell` | light, full shell | 1280×800 | logical capture |
| `workbench-dark-palette` | dark, palette and problems visible | 960×680 minimum | logical capture |

Each app accepts only its internal deterministic `--quality-capture <profile>`
profiles. The profile sets the theme, data/state, focus target, and logical window
size, then leaves the native window open for capture; it does not bypass normal
rendering or interaction code. Exact launch commands are:

```powershell
dotnet run --project examples/counter/Counter.csproj -- --quality-capture counter-light
dotnet run --project examples/counter/Counter.csproj -- --quality-capture counter-dark-focus
dotnet run --project examples/todo/Todo.csproj -- --quality-capture todo-light-populated
dotnet run --project examples/todo/Todo.csproj -- --quality-capture todo-dark-empty
dotnet run --project examples/package-pulse/PackagePulse.csproj -- --quality-capture pulse-dark-results
dotnet run --project examples/package-pulse/PackagePulse.csproj -- --quality-capture pulse-light-error
dotnet run --project examples/workbench/Lucent.Workbench.csproj -- --quality-capture workbench-light-shell
dotnet run --project examples/workbench/Lucent.Workbench.csproj -- --quality-capture workbench-dark-palette
```

Record commit, Windows version, scaling, theme, window size, command, and profile
for every capture. Review all eight once against this rubric, fix findings in one
bounded batch, then recapture only affected profiles. Replacements overwrite the
superseded files; the committed final set remains exactly eight:

- primary task and current state are identifiable without reading every label;
- palette, typography, geometry, motion, icon use, and semantic color follow
  `DESIGN.md` and `design/tokens.css`; raw app colors do not fork the identity;
- hover is tonal without lift; shadows appear only on raised transient surfaces;
  disabled state remains shaped/legible; warnings and errors include an icon or
  label; reduced motion removes spatial effects without hiding final state;
- spacing, alignment, typography, and control density are internally consistent;
- text and state indicators remain legible in both themes and never rely only on
  color;
- keyboard focus is visible and native disabled/selected/checked states remain
  recognizable;
- long, empty, loading, error, and minimum-size layouts do not overlap or hide
  primary actions;
- no example presents unsupported behavior as a product capability.
- the Workbench capture preserves the approved mock's hierarchy without copying
  unsupported content or turning fold geometry into ordinary control chrome.

Do not add automated screenshot diffs, per-platform golden images, or subjective
pixel thresholds. The eight logical captures above are the executable evidence.
Physical 200% Windows scaling remains an approved manual follow-up for clipping,
focus, and primary-action reachability. Run the existing supported cross-platform
build/headless gates; Plan 012 owns full release CI.

## Scope

**In scope**:

- CSS parser, typed style IR/catalog, semantic diagnostics, code generation, and
  focused editor metadata needed for the contract above.
- Adjacent CSS and minimal native resources/layout changes for Counter, Todo,
  Package Pulse, and Workbench.
- Consumption of the shared Shadcn semantic resources, committed marks, and
  approved Workbench composition, plus one focused token/resource consistency check.
- Light/dark variants, common interaction states, adaptive minimum-size behavior,
  accessibility preservation, reference captures, and review evidence.
- Documentation of the expanded supported CSS subset and Avalonia boundaries.

**Out of scope**:

- A Lucent component/design-system package, new icon library, CSS reset, shared theme
  runtime, or generic responsive-layout abstraction.
- Browser layout semantics, media/container queries, CSS imports, keyframes,
  transforms, filters, gradients, template replacement, or `/template/` styling.
- New example products, Workbench filesystem/compiler integration, packaging,
  marketplace work, hot reload, or language-server performance work.
- Applying the reading-mode identity to the Blume documentation site; Plan 012
  owns that publication surface using the same committed design authority.
- Pixel-perfect platform uniformity. Native text rendering and Fluent behavior
  may differ while hierarchy and usability remain equivalent.

## Steps

### 1. Capture the unpolished baseline and lock behavior

Build/run all four examples, record current primary flows and before captures,
and add only the smallest missing headless assertions needed to preserve each
accepted interaction while layout/styles change.

Record `PRODUCT.md`, `DESIGN.md`, `design/tokens.css`, the two Lucent marks, and
the approved Workbench mock in the review manifest with their content hashes.

Baseline captures are temporary review inputs and are not part of the committed
eight-file final set.

**Verify**: all current functional gates pass before visual or CSS edits.

### 2. Compile-spike Avalonia selectors, resources, and focus

Against Avalonia 12.1.1, prove public C# construction for child/descendant/name/
pseudo selectors, typed setters, theme-following dynamic resources, and the five
transition families. Also prove whether supported Fluent palette/resources,
focus-adorners, or ordinary styles can produce the shared Shadcn keyboard focus
ring on Button, TextBox, ListBox, and
AvaloniaEdit without replacing templates or suppressing native accessibility.
Record exact APIs in one focused compiler/native fixture.

**Verify**: the spike changes a resource at runtime and the mounted control
observes the new themed value without reparsing CSS or rebuilding the component;
the record enumerates each accepted property/transition pair. This is a decision
gate: only a passing result accepts `resource()` syntax and catalog support. The
focus probe must retain `:focus-visible`, keyboard navigation, and automation
peers while meeting the documented geometry.

### 3. Introduce the typed CSS catalog and broaden lowering

Make parser, validator, emitter, diagnostics, and editor metadata consume one
catalog. Add selector grammar, typed values, property applicability, resources,
and transitions test-first. Preserve existing CSS output and diagnostics.

**Verify**: focused compiler tests cover every property/value group, selector
form, projected-root shape, theme update, and representative invalid input.

### 4. Apply the cohesive family to the examples

Polish Counter, Todo, Package Pulse, then Workbench in that order. The smaller
apps prove tokens/states/adaptation before Workbench combines them. Keep layout
structure in `.lui`, visual treatment in adjacent CSS, and native theme resources
in the application setup where runtime theme switching requires them.

Use `design/tokens.css` as the source of truth for every repeated CSS variable and
native `Shadcn.*` resource value. Add one focused check that fails when the light/
dark role values, spacing/radii, or motion timings drift. Apply the committed mark
as the application/window identity where Avalonia packaging permits it and verify
the documented choice at 16, 20, 24, and 32 px: use the color mark at 24/32; use
grayscale at 16/20 unless the review record demonstrates that teal/coral remain
distinct at that exact rendered size and backdrop.

**Verify**: each documented run command works in both variants at default and
minimum sizes; no example adds a private framework hook for styling.

### 5. Harden states, scaling, accessibility, and motion

Exercise keyboard-only use, long content, empty/loading/error states, theme
switching, zero-duration motion resources, and Plan 006's
automation/focus contracts. Fix the shared CSS/compiler cause before adding
one-off native assignments.

**Verify**: headless functional/accessibility tests and native smoke checks pass;
all eight deterministic profiles pass at their declared logical sizes; all
workflows remain understandable with color removed and motion disabled. Physical
200% Windows scaling is an approved manual follow-up, not an executable gate.

### 6. Record the bounded visual review

Create the eight reference captures and review record, perform one correction
batch, then one confirmation pass. Update example READMEs and `docs/STYLING.md`
with supported syntax and explicit exclusions.

**Verify**: the record accounts for every rubric item and links the exact capture
environment and commands, the committed design-authority hashes, and deliberate
departures from the approved mock; all solution and VS Code gates remain green.

## Done criteria

- [x] All four examples share a coherent visual family without sharing a new
      runtime/component package.
- [x] `PRODUCT.md` and `DESIGN.md` remain authoritative; example CSS/native
      resources match `design/tokens.css`, and approved mark usage passes its
      documented small-size checks.
- [x] Native focus-visible treatment uses the shared Shadcn ring without replacing
      Fluent/AvaloniaEdit templates or weakening
      Plan 006 accessibility behavior.
- [x] Every example is usable in light/dark themes, at its minimum size, with
      keyboard focus, long content, and its material empty/error/loading states.
- [x] CSS properties, values, diagnostics, lowering, and editor metadata derive
      from one typed catalog.
- [x] Common selectors/states, theme resources, property groups, and transitions
      lower to public native Avalonia APIs and are covered by focused tests.
- [x] Plan 006's behavior, accessibility, ownership, and headless contracts remain
      green after the Workbench restructuring.
- [x] Eight reference captures and one bounded review record demonstrate the
      agreed quality bar without introducing screenshot-golden tests.
- [x] `docs/STYLING.md` distinguishes supported Avalonia-targeted CSS from browser
      CSS and deferred template/layout features.

## STOP conditions

- Dynamic resource setters cannot follow Avalonia theme changes through supported
  public APIs; stop for approval to remove `resource()` from the contract or
  defer the theme bridge instead of introducing runtime resource polling.
- The committed focus geometry cannot be achieved through supported Fluent
  resources, focus adorners, or ordinary styles while preserving native templates
  and accessibility; stop for a design decision rather than copying templates.
- Polishing an example requires replacing a native Fluent/AvaloniaEdit template,
  suppressing focus visuals, or adding framework-private hooks.
- Descendant/component selectors cannot retain deterministic component scoping;
  keep simple selectors and record the concrete failing case.
- Workbench visual changes break Plan 006's keyboard, accessibility, settings,
  cancellation, error, or shutdown contracts.
- The pass expands into web CSS compatibility, a reusable design-system package,
  a new application feature, or open-ended screenshot iteration.

## Maintenance notes

The examples are release evidence, not a permanent gallery application. Add a
new CSS capability after this plan only when a real application needs it and the
typed catalog can represent its native Avalonia property/value semantics. Plan
008 consumes the catalog for CSS intelligence, Plan 009 adds the accepted
  Shadcn theme and class completion, Plan 009a adds global styles, Plan 010
  migrates examples, and Plan 012 packages only reviewed behavior while
  translating the updated visual authority to documentation.
