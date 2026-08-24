# Plan 009: Add the Shadcn theme and theme-aware CSS completion

> **Executor instructions**: Keep three seams distinct: Lucent's compiler owns
> live CSS syntax and applicability, the LSP owns bounded immutable caches and
> protocol adaptation, and Avalonia owns runtime style matching. Never execute a
> project or third-party assembly in the language-server process. Reuse Plan
> 008a's manifest reader and class-catalog contract; do not create theme-specific
> metadata infrastructure.
>
> **Drift check**: `git diff --stat e2a9a5f..HEAD -- build src editors examples tests docs design plans`

## Status

- **Priority**: P1
- **Effort**: XL
- **Risk**: HIGH
- **Depends on**: plans 007, 008a
- **Category**: styling / theme / tooling / DX
- **Planned at**: commit `e2a9a5f`, 2026-08-23
- **Design authority**: [`docs/SHADCN_THEME.md`](../docs/SHADCN_THEME.md)

## Implementation evidence

- Focused compiler tests cover OKLCH lowering, alpha, gamut clipping, and the
  embedded Shadcn catalog manifest.
- Focused language-server protocol tests cover adjacent class literals and
  direct `Application.Styles.Add(new Theme())` activation from the generation
  snapshot.
- `examples/shadcn-gallery` builds as the deterministic light/default and
  `--dark` review surface. No existing example was redesigned.

## Why this matters

Plan 007 proved adjacent, statically typed CSS, but each example still owns
duplicated visual resources and `Class:` deliberately has no class-name
completion. The Shadcn design supplies the concrete application need that the
roadmap previously lacked: one reusable opt-in theme, author-facing native
classes such as `secondary`, `destructive`, `outline`, `ghost`, `link`, and
`card`, and exact OKLCH source colors.

This plan adds the reusable theme and gallery first, then makes Lucent class
completion consume live CSS and Plan 008a's non-executable package metadata. It
does not redesign the examples or add project-global CSS in the same delivery;
Plan 009a owns global styles and Plan 010 performs the visual migration after
the gallery is accepted.

## Current state

- Adjacent `.lui` and `.css` files compile into native Avalonia styles attached
  to each native root produced by the owning component.
- `CssPropertyCatalog` is shared by parsing, validation, lowering, diagnostics,
  and CSS-property completion.
- `EditorIntelligence` intentionally returns no completion inside `Class:`
  values, and Plan 008 records that boundary.
- The LSP has project-aware Roslyn semantics and bounded project generations,
  but no style-class catalog or active-theme model.
- Plan 008a defines the immutable style-class contract, versioned module
  manifest, non-executing reference reader, and native-compatibility matrix;
  this plan supplies theme/live-CSS producers and completion behavior.
- Examples activate Fluent programmatically with `Styles.Add(new FluentTheme())`;
  App.axaml-only theme detection would miss every checked-in example.
- `docs/SHADCN_THEME.md` is proposed authority. `DESIGN.md`,
  `design/tokens.css`, and Plan 007 remain current visual authority until the
  later example migration is reviewed and accepted.

## Plan 008a handoff

Reuse exactly one internal manifest schema, immutable `StyleClassCatalog` entry
contract, non-executing `PEReader`/`MetadataReader` reader, and bounded
generation-owned identity/fingerprint cache. Its once-per-generation diagnostics
remain authoritative. Do not add a theme schema, reader, cache, runtime
registry, or assembly activation path. Current live project CSS remains
authoritative; manifest entries are package metadata only. A publicly
installable theme catalog type must use Plan 008a's verified public generated
catalog-type seam; this plan still owns installation and Avalonia
precedence/runtime proof.

## Decisions

### One class-catalog contract, separate producers

Normalize every completion source to Plan 008a's immutable `StyleClassEntry`
and catalog interface. Do not extend that interface merely because a theme has
private template classes or values that completion does not consume.

The compiler frontend tolerantly extracts selector classes from live Lucent
CSS, even while declarations or later rules are incomplete. Applicability is
recorded for the compound selector containing each class: `Button.accent`
associates `accent` with `Button`, while `.surface` is untyped. Do not add a
second CSS parser or selector schema to the LSP.

Theme packages provide reviewed entries through Plan 008a's versioned manifest.
Because Fluent/Simple are not Lucent packages, the first checked-in tooling
catalogs cover their documented author-facing classes and exact resolved
assembly identities (name, version, culture, and public-key token). The Shadcn
manifest covers its own author-facing classes; internal template-part classes
are excluded.
The Shadcn package's embedded metadata is authoritative and tested against its
runtime styles. Because Fluent/Simple are not Lucent packages, a developer-only,
out-of-process generator may load allowlisted pinned official theme assemblies
to refresh checked-in immutable catalog source using the same class-entry
contract. That source is compiled into tooling and does not add a serialized
manifest reader. User completion never executes those assemblies, downloads
metadata, or guesses across versions.

Enable native theme metadata only when every assembly-identity field resolves
and the theme is visibly installed through App.axaml or a direct, semantically
resolved `Application.Styles.Add(new FluentTheme())` / `Styles.Add(new
ShadcnTheme())` call (including the equivalent direct `this.Styles.Add(...)`
inside the `Application` subclass). Bare construction, fields, aliases,
factories, and indirect flows are not installation evidence. When installation
evidence or exact metadata is missing, omit those entries silently.

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

The LSP owns bounded per-document and per-project immutable snapshots. Plan
008a's reference reader publishes package entries at the project-generation
seam; this plan adds live CSS and active-theme producers to that same snapshot.
Document, watched-file, project, and active-theme changes rebuild snapshots off
the request path. Completion serves the last snapshot immediately while a
replacement is computed, then swaps atomically. A completion request performs
no file read, MSBuild evaluation, assembly load, or network access and cannot
publish a stale document/project generation.

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
  active-theme metadata without request-path I/O.
- Add checked-in official immutable catalogs using Plan 008a's class-entry
  contract and the bounded developer generator used to refresh them.
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
completion result as the failing baseline. Include bare theme construction
without `Styles.Add` and prove that it contributes no active-theme entries.

Reuse Plan 008a's immutable entry/catalog contract and expose only its existing
small query surface to completion. Preserve source identities for future
navigation, but do not implement navigation here.

**Verify**: tests fail because `EditorIntelligence` still excludes class values;
the implementation adds no theme-specific entry type, manifest reader, runtime
registry, or mutable cache surface.

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
exact-version Fluent/Simple immutable catalog source with the out-of-process
developer tool and publish authoritative Shadcn entries through the package's
Plan 008a manifest. Detect direct App.axaml and C# theme activation from the
existing project snapshot.

Add context-aware completion, ranking, replacement ranges, duplicate filtering,
origin details, string quick-suggestion defaults, background refresh, and the
bounded “No indexed style definition found” information diagnostic.

**Verify**: edits to an unsaved adjacent CSS file update completion; incomplete
CSS still contributes recognizable classes; `Button` offers documented active
theme variants; an incompatible control ranks them after applicable entries;
missing manifests produce no native result or diagnostic; malformed,
unsupported-version, duplicate, or identity-mismatched Lucent manifests follow
Plan 008a's one-generation-scoped project diagnostic and contribute no entries;
benchmark instrumentation proves no completion-path I/O and preserves Plan 008
budgets.

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

- [x] Lucent CSS accepts and deterministically lowers the required OKLCH forms.
- [x] Fluent plus `ShadcnTheme` supplies complete reviewed light/dark treatment
      for the promised gallery surface without compiler/runtime coupling.
- [x] `Class:` completion handles live adjacent CSS and exact active first-party
      theme metadata in ordinary and interpolated literal segments through Plan
      008a's catalog.
- [x] Completion performs no request-path I/O and remains inside Plan 008's
      accepted correctness, cancellation, latency, allocation, and cache bounds.
- [x] No arbitrary assembly execution, runtime CSS parser, Actipro dependency,
      template framework, compatibility token layer, or false invalid-class
      diagnostic is introduced.
- [x] Theme, gallery, manifest, and tooling evidence is reviewed before Plan 009a
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
test its Plan 008a manifest entries against the package so completion cannot
silently drift from the theme. Checked-in catalogs for non-Lucent official
themes use the same entry contract but remain explicitly pinned external
evidence compiled into tooling, not a second package-manifest format.

Plan 008's deliberate `Class:` exclusion remains correct for its accepted scope;
this plan supersedes that boundary only after its own protocol and performance
gates pass. Plan 009a may then reuse the catalog for explicit global styles;
Plan 010 packages the result and performs the separately reviewed migration.
