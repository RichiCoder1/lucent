# Plan 009: Add the Shadcn theme and theme-aware CSS completion

> **Executor instructions**: Keep three seams distinct: Lucent's compiler owns
> live CSS syntax and applicability, the LSP owns bounded immutable caches and
> protocol adaptation, and Avalonia owns runtime style matching. Never execute a
> project or third-party assembly in the language-server process.
>
> **Drift check**: `git diff --stat e2a9a5f..HEAD -- build src editors examples tests docs design plans`

## Status

- **Priority**: P1
- **Effort**: XL
- **Risk**: HIGH
- **Depends on**: plans 007, 008
- **Category**: styling / theme / tooling / DX
- **Planned at**: commit `e2a9a5f`, 2026-08-23
- **Design authority**: [`docs/SHADCN_THEME.md`](../docs/SHADCN_THEME.md)

## Why this matters

Plan 007 proved adjacent, statically typed CSS, but each example still owns
duplicated visual resources and `Class:` deliberately has no class-name
completion. The Shadcn design supplies the concrete application need that the
roadmap previously lacked: one reusable opt-in theme, author-facing native
classes such as `secondary`, `destructive`, `outline`, `ghost`, `link`, and
`card`, and exact OKLCH source colors.

This plan adds the reusable theme and gallery first, then makes Lucent class
completion consume live CSS and non-executable theme metadata. It does not
redesign the examples or add project-global CSS in the same delivery; Plan 009a
owns global styles and Plan 010 performs the visual migration after the gallery
is accepted.

## Current state

- Adjacent `.lui` and `.css` files compile into native Avalonia styles attached
  to each native root produced by the owning component.
- `CssPropertyCatalog` is shared by parsing, validation, lowering, diagnostics,
  and CSS-property completion.
- `EditorIntelligence` intentionally returns no completion inside `Class:`
  values, and Plan 008 records that boundary.
- The LSP has project-aware Roslyn semantics and bounded project generations,
  but no style-class catalog or active-theme model.
- Examples activate Fluent programmatically with `Styles.Add(new FluentTheme())`;
  App.axaml-only theme detection would miss every checked-in example.
- `docs/SHADCN_THEME.md` is proposed authority. `DESIGN.md`,
  `design/tokens.css`, and Plan 007 remain current visual authority until the
  later example migration is reviewed and accepted.

## Decisions

### One class-catalog contract, separate producers

Normalize every completion source to immutable metadata equivalent to:

```csharp
StyleClassEntry(
    string Name,
    string? ApplicableType,
    StyleClassOrigin Origin,
    SourceSpan? Definition,
    string Detail);
```

The compiler frontend tolerantly extracts selector classes from live Lucent
CSS, even while declarations or later rules are incomplete. Applicability is
recorded for the compound selector containing each class: `Button.accent`
associates `accent` with `Button`, while `.surface` is untyped. Do not add a
second CSS parser or selector schema to the LSP.

Theme packages provide reviewed, versioned metadata. The first manifests cover
documented author-facing classes from exact resolved Fluent/Simple assembly
identities (name, version, culture, and public-key token) and from
`Lucent.Themes.Shadcn`; internal template-part classes are excluded.
The Shadcn package's metadata is authoritative and tested against its runtime
styles. A developer-only, out-of-process generator may load allowlisted pinned
official theme assemblies to refresh checked-in manifests. User completion
never executes those assemblies, downloads metadata, or guesses across versions.

Enable a native manifest only when every assembly-identity field resolves and the
theme is visibly activated through App.axaml or direct, semantically resolved
C# construction such as `new FluentTheme()` or `new ShadcnTheme()`. When
evidence or an exact manifest is missing, omit those native entries silently.

### Literal-segment completion

Complete the whitespace-delimited token under the cursor in ordinary strings
and literal segments of interpolated strings. Hide tokens already present and
replace only the active token. Do not evaluate variables, concatenations,
conditionals, or interpolation expressions.

Rank adjacent classes applicable to the receiving native control, then
applicable active-theme classes, then untyped and known-inapplicable entries for
discovery. Every item names its origin. Enable quick suggestions in strings for
Lucent documents; context filtering must return these items only inside an
eligible `Class:` value.

A static unmatched token may receive only the informational wording “No indexed
style definition found.” This is not an invalid-class diagnostic. Suppress it
when expected catalog discovery is unavailable and provide a setting to disable
it. Classes remain an open Avalonia string surface.

### No I/O on completion

The LSP owns bounded per-document and per-project immutable snapshots. Document,
watched-file, project, and active-theme changes rebuild snapshots off the request
path. Completion serves the last snapshot immediately while a replacement is
computed, then swaps atomically. A completion request performs no file read,
MSBuild evaluation, assembly load, or network access and cannot publish a stale
document/project generation.

## Scope

**In scope**:

- Add `oklch(...)` parsing, validation, deterministic compile-time conversion,
  alpha, bounds/out-of-gamut behavior, diagnostics, and focused tests anywhere
  Lucent CSS accepts a color.
- Add dependency-independent `Lucent.Themes.Shadcn` light/dark resources and
  styles layered over an explicitly installed FluentTheme.
- Cover the controls, variants, states, accessibility gates, and semantic
  resources specified by `docs/SHADCN_THEME.md`.
- Add the deterministic light/dark control gallery and reviewed upstream Shadcn
  New York/neutral snapshot.
- Add adjacent `Class:` completion from unsaved/tolerantly parsed CSS and exact
  active-theme manifests without request-path I/O.
- Add checked-in official manifests and the bounded developer generator used to
  refresh them.
- Update tooling, styling, theme, and build documentation plus Plan 009 evidence.

**Out of scope**:

- Redesigning Counter, Todo, Package Pulse, Workbench, or documentation visuals;
  Plan 010 owns the separately gated migration and release packaging.
- Executing arbitrary project/third-party assemblies, automatic transitive
  library-style installation, runtime CSS parsing, or downloading manifests.
- Project AXAML selector indexing, third-party native-theme completion, or
  completion in arbitrary C# class expressions.
- Project-global `LucentStyle` build/runtime semantics; Plan 009a owns that
  separately gated feature.
- Class go-to-definition, references, rename, or an invalid/unknown-class error.
- Reimplementing Fluent templates, a Shadcn component wrapper hierarchy,
  automatic upstream synchronization, bundled fonts/icons, high-contrast or
  density variants, or the controls deferred by `docs/SHADCN_THEME.md`.

## Steps

### 1. Lock failing contracts and the class-catalog seam

Add focused compiler and protocol tests for unsaved adjacent CSS, broken-rule
recovery, ordinary/interpolated literal segments, duplicate suppression, token
replacement, type ranking, exact theme activation/versioning, stale-snapshot
replacement, and zero request-path I/O. Record the currently empty `Class:`
completion result as the failing baseline.

Define the immutable compiler-owned entry/catalog contract and expose only the
small query surface the LSP needs. Preserve source spans for future navigation,
but do not implement navigation here.

**Verify**: tests fail because `EditorIntelligence` still excludes class values;
the proposed API does not expose parser, Roslyn, or mutable cache internals.

### 2. Build the color and theme foundation

Capture the precise Shadcn New York/neutral source URL, retrieval date, original
OKLCH values, and reviewed converted Avalonia values. Evaluate one focused,
permissively licensed color dependency against the design criteria; use it only
if it is smaller and safer than the standard conversion. Do not add Actipro.

Implement compiler OKLCH support and `Lucent.Themes.Shadcn` without dependencies
on the Lucent compiler/runtime. Prefer public Fluent resources and ordinary
styles; record and review every unavoidable template replacement. Add the
deterministic gallery and inspect light/dark states, compact geometry, contrast,
focus, validation, disabled treatment, automation, and keyboard behavior.

**Verify**: focused color tests cover neutral/chromatic/alpha/malformed/bounds/
out-of-gamut cases; normal Avalonia code can install Fluent plus Shadcn; gallery
evidence covers every promised control, variant, and state in both modes.

### 3. Add adjacent and active-theme class completion

Extract classes through the tolerant shared CSS syntax frontend. Build reviewed
exact-version Fluent/Simple manifests with the out-of-process developer tool and
publish authoritative Shadcn metadata with the theme. Detect direct App.axaml
and C# theme activation from the existing project snapshot.

Add context-aware completion, ranking, replacement ranges, duplicate filtering,
origin details, string quick-suggestion defaults, background refresh, and the
bounded “No indexed style definition found” information diagnostic.

**Verify**: edits to an unsaved adjacent CSS file update completion; incomplete
CSS still contributes recognizable classes; `Button` offers documented active
theme variants; an incompatible control ranks them after applicable entries;
missing/mismatched manifests produce no native result or diagnostic; benchmark
instrumentation proves no completion-path I/O and preserves Plan 008 budgets.

### 4. Publish evidence and hand off follow-ups

Update `docs/STYLING.md`, `docs/TOOLING.md`, theme docs, and the roadmap. Record
manifest provenance, gallery review, accessibility results, completion
latency/cache behavior, any template exceptions, and remaining unsupported
sources.

Do not rewrite existing examples or introduce global styles in this change.
Mark Plan 009a ready after completion/tooling review and Plan 010 ready after the
gallery and theme API pass review.

**Verify**: documentation distinguishes adjacent/native origins, open class
strings, incomplete catalog hints, and theme activation evidence without
claiming arbitrary theme or AXAML discovery.

## Done criteria

- [ ] Lucent CSS accepts and deterministically lowers the required OKLCH forms.
- [ ] Fluent plus `ShadcnTheme` supplies complete reviewed light/dark treatment
      for the promised gallery surface without compiler/runtime coupling.
- [ ] `Class:` completion handles live adjacent CSS and exact active first-party
      theme manifests in ordinary and interpolated literal segments.
- [ ] Completion performs no request-path I/O and remains inside Plan 008's
      accepted correctness, cancellation, latency, allocation, and cache bounds.
- [ ] No arbitrary assembly execution, runtime CSS parser, Actipro dependency,
      template framework, compatibility token layer, or false invalid-class
      diagnostic is introduced.
- [ ] Theme, gallery, manifest, and tooling evidence is reviewed before Plan 009a
      adds global styles or Plan 010 starts the example migration.

## STOP conditions

- A promised treatment requires broad Fluent template copying rather than a
  bounded reviewed exception.
- OKLCH conversion cannot define deterministic gamut handling or requires a
  disproportionate dependency.
- Completion requires synchronous I/O, project evaluation, network access, or
  loading a target assembly.
- Active-theme detection requires executing application code or pretending an
  assembly reference proves installation.
- The implementation begins example redesign before the theme/gallery gate is
  accepted.

## Maintenance notes

Theme manifests are completion metadata, not runtime truth. Runtime selector
matching remains Avalonia-owned, and class strings remain open for handwritten
or future style sources. Keep first-party metadata beside the style package and
test it against the package so completion cannot silently drift from the theme.

Plan 008's deliberate `Class:` exclusion remains correct for its accepted scope;
this plan supersedes that boundary only after its own protocol and performance
gates pass. Plan 009a may then reuse the catalog for explicit global styles;
Plan 010 packages the result and performs the separately reviewed migration.
