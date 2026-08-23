# Plan 009b: Add a finite Avalonia utility-style catalog

> **Executor instructions**: Adapt familiar Tailwind utility names only where
> they map cleanly to public Avalonia properties and pseudo-classes. Generate
> ordinary Avalonia styles plus Plan 008a manifest entries from one checked-in
> finite specification. Do not run Tailwind, scan application source, or add a
> second CSS/runtime styling engine. Publish metadata only through Plan 008a's
> module manifest and Plan 009's existing catalog.
>
> **Drift check**: `git diff --stat e2a9a5f..HEAD -- build src examples tests docs plans`

## Status

- **Priority**: P2
- **Effort**: M
- **Risk**: MEDIUM
- **Depends on**: plans 008a, 009, 009a
- **Category**: styling / utilities / tooling / DX
- **Planned at**: commit `e2a9a5f`, 2026-08-23
- **Research**: [`docs/research/AVALONIA_TAILWIND.md`](../docs/research/AVALONIA_TAILWIND.md)

## Why this matters

Plans 009 and 009a provide theme resources, class metadata, completion, and an
explicit application-style installation seam. A small utility catalog can make
common Lucent layouts terse without inventing component wrappers or making the
compiler understand individual utility names.

This is not a Tailwind port. Tailwind targets browser CSS; Avalonia has different
layout, inheritance, selector, template, and responsive behavior. The useful
product is an optional finite Avalonia catalog with familiar names where the
semantics are honest.

## Decisions

### One finite specification, two generated artifacts

Add an internal checked-in utility specification as the only source of truth.
Each entry contains the class name, native target/property/value, applicable
control type, optional supported Avalonia pseudo-class, canonical emission
order, documentation, and Plan 008a manifest class-entry fields consumed through
Plan 009's catalog/cache.

Generate:

1. `global::Lucent.Styles.Utilities.LucentStyles`, an ordinary Avalonia style
   catalog installable through Plan 009a's direct `Application.Styles.Add(new
   ...LucentStyles())` seam; and
2. Plan 008a manifest class entries consumed through Plan 009's existing
   catalog/cache.

The style type and manifest ship in the same exact-versioned assembly. Reuse
Plan 008a's manifest schema/reader and Plan 009a's bounded semantic installation
detection; a package reference or bare construction does not activate
completion. Do not introduce an interface, registry, second manifest, generated
reflection metadata, or utility-specific activation rule.

Keep the generator internal. Do not expose a plugin interface, Tailwind config,
arbitrary-value parser, runtime source scanner, or separate metadata model.
Generated runtime styles and completion entries must be tested against the same
specification so they cannot drift.

The provisional package surface is `Lucent.Styles.Utilities`. Plan 010 owns
packing and release naming. Do not use Tailwind branding in the package name or
claim compatibility without a separate naming/trademark review.

### Ship only mappings Avalonia can own directly

The first catalog is deliberately finite:

- whole-value `p-*`, `m-*`, fixed `w-*`/`h-*`, and supported `gap-*` utilities;
- font size/weight/style, text alignment/wrapping, and line-height utilities;
- Shadcn semantic foreground/background/border colors;
- border width/radius, opacity, visibility, clipping, and supported alignment;
- finite `hover:`, `focus:`, `focus-visible:`, `disabled:`, `checked:`, and
  `selected:` variants only where the target control exposes the corresponding
  Avalonia pseudo-class and property.

Use Plan 009's `Shadcn.*` dynamic resources for semantic colors and theme
changes. Do not duplicate the theme palette in the utility project.

Exclude per-side/axis margin and padding (`pt-*`, `px-*`, and equivalents) from
this delivery. Avalonia stores each value as one `Thickness`; correct composition
would require runtime attached properties and a conflict arbiter. Add that only
after a concrete application proves whole-value spacing inadequate.

Also exclude browser flex/grid shorthands, responsive/container prefixes,
arbitrary values or variants, plugins, preflight/reset rules, transforms,
filters, pseudo-elements, animations, template-part selectors, and browser dark
mode. Native panels, theme variants, transitions, and templates remain native
Avalonia/Lucent concerns.

### Exact state names without widening general CSS

Preserve familiar state tokens such as `hover:bg-primary` in `Class:` values.
Extend only the shared selector/class-name frontend needed to represent an
escaped literal colon in a class selector, for example
`.hover\:bg-primary:pointerover`. The decoded class name is
`hover:bg-primary`; the final unescaped colon still introduces the Avalonia
pseudo-class.

The same decoded name must flow through CSS parsing, generated/native selector
construction, `CssProjectTokenIndex` tokenization/source spans, Plan 008a
manifest class entries, completion replacement, and static `Class:` literals.
Share the decoded class tokenizer with the compiler frontend rather than
teaching the project index a second escape grammar. Do not add general CSS
escapes, arbitrary selector variants, or runtime parsing under this work. If
public Avalonia selector APIs cannot match a literal colon safely, stop and use
unprefixed utilities only rather than inventing a private matcher.

### Deterministic conflicts, not class-order semantics

Avalonia and Tailwind do not use `Class:` token order as declaration priority.
Generate styles in one canonical order and document that conflicting utilities
for the same native property have deterministic catalog order, independent of
their order in `Class:`. Tests must prove the chosen order for representative
spacing, size, color, and state conflicts.

That deterministic order applies only inside the utility catalog. When utility,
theme, or application catalogs overlap, host `Application.Styles` installation
order and native priority remain authoritative as defined by Plan 009a. Test
both representative host orders and do not claim a universal
utility-over-global or global-over-utility winner.

Do not add a runtime conflict-resolution engine or claim that reordering class
tokens changes the winner. The gallery and docs should avoid conflicting
utilities except where conflict behavior is being demonstrated.

## Scope

**In scope**:

- Add the finite utility specification and deterministic generator.
- Generate ordinary Avalonia styles and Plan 008a manifest class entries,
  consumed through Plan 009's existing catalog/cache.
- Install utilities explicitly through the Plan 009a application-style seam.
- Add the bounded escaped-colon support required by the named state variants.
- Add a light/dark utility gallery covering every utility family, applicable
  control type, supported state, and representative conflict.
- Add compiler, runtime, completion, accessibility, and package-consumer-ready
  fixtures for the generated catalog.
- Document exact supported names, native meanings, installation, conflicts,
  limitations, and the non-compatibility boundary with Tailwind CSS.

**Out of scope**:

- A Tailwind dependency, `tailwind.config.*`, browser CSS input/output, runtime
  source scanning, dynamic class generation, or ecosystem plugin compatibility.
- Per-side/axis spacing composition or any new runtime attached-property system.
- Responsive/container utilities, arbitrary values/variants, browser layout,
  preflight, transforms/filters, pseudo-elements, animations, or template-part
  styling.
- Migrating existing examples to utility-first styling or installing utilities
  in the default template; Plan 010 may package the opt-in catalog but must not
  make it the product default without a separate accepted design decision.
- General CSS escape support, class navigation/refactoring, or a new completion
  cache/protocol.

## Steps

### 1. Lock the vocabulary and failing contracts

Check in the exact class inventory and native mapping table before implementing
the generator. Add failing tests for specification uniqueness, applicable native
types, Shadcn resource references, deterministic order, direct installation,
metadata parity, and representative light/dark behavior.

Add focused parser/completion tests for `.hover\:bg-primary:pointerover` and
`Class: "hover:bg-primary"`, including active-token replacement and duplicate
filtering. Add a project-index test that returns the full decoded class name and
the exact escaped source span rather than the current truncated `hover` token.
Record the current parse/index failure as the baseline.

**Verify**: every proposed utility has one documented public Avalonia mapping;
tests reject duplicate names, unsupported pseudo-classes, missing resources,
and metadata/runtime drift.

### 2. Generate the unprefixed whole-property catalog

Implement the smallest generator that emits installable Avalonia styles and
Plan 008a manifest class entries from the specification. Start with whole-value
spacing, dimensions, typography, semantic colors, borders/radii,
opacity/visibility, clipping, gap, and alignment.

Keep values finite and token-backed where practical. Do not parse application
source or synthesize utilities on demand.

**Verify**: direct `Application.Styles.Add(new ...LucentStyles())` installation
styles both handwritten Avalonia and Lucent-generated controls; applicable
entries complete through Plan 009's immutable cache with no request-path I/O;
generated output is deterministic across clean builds.

### 3. Add bounded state variants

Add the minimal escaped-colon class-name support to the shared CSS frontend and
route `CssProjectTokenIndex` through that shared decoded tokenizer so indexing,
completion, and future navigation preserve the same name and source span. Emit
only the approved pseudo-class variants. Prefer public Avalonia selector
construction; do not depend on internal template parts or private APIs.

Test each state on a control that publicly owns it. Inapplicable variants remain
available only as lower-ranked discovery entries under Plan 009's rules and must
not be presented as universally valid.

**Verify**: parser, lowerer, runtime matching, metadata, completion, and source
ranges—including the project index's escaped source span—agree on the decoded
class name; keyboard focus, disabled, checked, and selected states have
non-color-only gallery evidence where required.

### 4. Prove utility semantics in the gallery

Add a deterministic light/dark gallery section that exercises every family,
control applicability, state, and representative same-property conflict. Review
layout at minimum supported window sizes and confirm that utility use does not
weaken Plan 007/009 accessibility gates.

For every state variant, prove that deactivation removes only that style
contribution and reveals the correct lower-priority value. Record representative
theme/global/utility/local-value behavior for both tested host installation
orders in Plan 008a's native-compatibility matrix; do not add a runtime conflict
arbiter to force token- or package-order semantics.

**Verify**: gallery review records the exact catalog version, both theme modes,
keyboard traversal, focus visibility, disabled/selected/checked treatment, and
canonical conflict/restoration outcomes without pixel-golden tests.

### 5. Publish evidence and hand off packaging

Document the supported inventory and native mappings, explicit install call,
Shadcn dependency, conflict order, unsupported Tailwind features, and extension
rules. Update Plan 010 to pack and test the opt-in catalog, but keep it out of
the default template and example migration.

**Verify**: Plan 010's clean consumer fixture can install the generated style
catalog through `new global::Lucent.Styles.Utilities.LucentStyles()` and receive
matching exact-assembly metadata only after that direct installation, without
repository paths, assembly execution, or runtime scanning.

## Done criteria

- [ ] One finite checked-in specification generates runtime styles and exactly
      matching Plan 008a manifest/Plan 009 completion metadata.
- [ ] The documented utility inventory maps only to public, tested Avalonia
      properties, resources, and pseudo-classes.
- [ ] Exact supported state tokens work through CSS parsing, native matching,
      `Class:` completion, and source ranges without generalizing the grammar.
- [ ] Canonical conflict behavior is deterministic and does not pretend class
      token order controls priority.
- [ ] Light/dark gallery evidence covers every family, state, applicability rule,
      and representative conflict while preserving accessibility gates.
- [ ] Installation is explicit, completion performs no request-path I/O, and no
      runtime parser, source scan, Tailwind dependency, or plugin system exists.
- [ ] Plan 010 can package the catalog as opt-in without adding it to the default
      template or migrating examples.

## STOP conditions

- A proposed utility lacks one honest public Avalonia property/state mapping.
- Literal-colon class matching requires private Avalonia APIs, runtime parsing,
  or a broad CSS escape implementation.
- Correct first-release behavior requires per-side composition, responsive
  state, template-part coupling, or a general conflict engine.
- Completion requires a second catalog/cache or any request-path file, project,
  assembly, or network access.
- The implementation starts claiming Tailwind compatibility or migrating the
  examples before the finite catalog and gallery are accepted.

## Maintenance notes

The utility catalog is an optional authoring vocabulary over native Avalonia
styles, not a new styling runtime. Keep the specification finite and reviewed;
adding one utility requires a native mapping, applicability, docs, metadata, and
gallery/test evidence in the same change.

Lucent keeps one `Class:` channel for native/theme/global/utility selectors.
Do not copy AKCSS's separate `class`/`Classes` surface; make origin and precedence
visible through metadata, completion detail, native priority, and tests instead.

Prefer deletion or an ordinary adjacent/global style when a proposed utility
needs special runtime machinery. Reconsider per-side spacing only with a real
consumer and a separately reviewed composition design.
