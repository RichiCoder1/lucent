# Plan 010: Migrate every example to the Shadcn visual authority

> **Executor instructions**: This is a visual migration, not a release or
> application-integration plan. Preserve behavior and tests. Delete superseded
> visual tokens rather than adding aliases or a second theme layer.
>
> **Drift check**: `git diff --stat ad0e183..HEAD -- examples docs design plans`

## Status

- **Priority**: P1
- **Effort**: L
- **Risk**: MEDIUM
- **Depends on**: 007, 009, 009a
- **Category**: application UX / dogfood preparation
- **Planned at**: commit `ad0e183`

## Why this matters

The Shadcn theme and global-style seams are proven, but Counter, Todo, Package
Pulse, and Workbench still present the older Registration Overlay visual
authority. Packaging before migrating them would preserve two competing design
systems and would not prove that the accepted theme works in the real examples.

This plan gives every example one visual authority before Workbench starts its
real project/compiler dogfood pass.

## Scope

**In scope**:

- Install Fluent plus `ShadcnTheme` in Counter, Todo, Package Pulse, and
  Workbench.
- Rewrite adjacent CSS and application resources to use `Shadcn.*` semantic
  resources and the accepted Plan 009 class vocabulary.
- Remove duplicated Registration Overlay and `Lucent.*` application visual
  dictionaries without compatibility aliases.
- Preserve each example's teaching purpose, behavior, keyboard flow,
  accessibility, adaptive layout, minimum sizes, and existing interaction tests.
- Preserve Plan 007's application shell, visual-state coverage, long-content
  cases, and reference-review method while replacing its superseded colors and
  tokens.
- Update `DESIGN.md`, `design/tokens.css`, Plan 007's visual-authority text, and
  related documentation so the repository names one current application visual
  authority.
- Regenerate the bounded light/dark and small/large example evidence required by
  the existing quality gates.

**Out of scope**:

- Replacing Workbench placeholder project/problem data or changing its loading,
  settings, lifecycle, editor, or command interfaces; Plan 011 owns that work.
- NuGet packaging, templates, VSIX production, CI, or documentation publication;
  Plan 012 owns distribution.
- Migrating examples to utility-first styling. The Plan 009b catalog remains
  explicitly opt-in and is not the product default.
- New theme controls, a Lucent wrapper hierarchy, runtime CSS, or compatibility
  aliases for the old visual tokens.

## Steps

### 1. Record the migration authority

Inventory every example-owned resource dictionary, CSS token, capture, and
document that still describes Registration Overlay or `Lucent.*` application
colors. Record the accepted Shadcn replacement or deletion for each item before
editing examples.

**Verify**: the inventory covers Counter, Todo, Package Pulse, Workbench,
`DESIGN.md`, `design/tokens.css`, and Plan 007.

### 2. Migrate the small examples first

Migrate Counter, Todo, and Package Pulse independently. Use native Avalonia
controls, adjacent Lucent CSS, and Shadcn semantic resources. Preserve their
existing state, async, error, stale-result, keyboard, automation, and smoke-test
contracts.

**Verify**: each project builds and its existing focused tests/smokes pass before
moving to the next example.

### 3. Migrate Workbench presentation only

Install the same visual authority in Workbench and migrate its shell, menus,
workspace tree, editor chrome, problems list, dialogs, palette, settings, and
empty/loading/error states. Do not replace placeholder adapters or change
application interfaces in this plan.

**Verify**: all seven headless Workbench flows still pass and no visual migration
requires a private runtime/compiler hook.

### 4. Remove the superseded authority

Delete old application tokens, dictionaries, aliases, and instructions. Update
the design documents and bounded visual evidence. Keep the Lucent mark unless
the migration demonstrates a concrete conflict.

**Verify**: repository search and review find one application visual authority,
no `Lucent.*` compatibility token layer, and no stale Registration Overlay
instruction presented as current.

## Done criteria

- [x] Counter, Todo, Package Pulse, and Workbench install Fluent plus
      `ShadcnTheme` and use the accepted semantic resources.
- [x] Existing behavior, keyboard, accessibility, adaptive-layout, and smoke
      gates remain green.
- [x] Every superseded application visual token is removed rather than aliased.
- [x] `DESIGN.md`, `design/tokens.css`, Plan 007, and example documentation name
      one current visual authority.
- [x] Reviewed bounded evidence covers every migrated example in light and dark
      modes and at required minimum sizes.
- [x] The utility catalog remains optional and is not installed merely to
      complete the migration.

## STOP conditions

- A migration requires a compatibility alias layer or runtime CSS engine.
- Preserving behavior requires changing compiler/runtime interfaces unrelated to
  the visual migration.
- Headless accessibility or keyboard gates must be skipped to make captures pass.
- The Shadcn theme lacks a semantic resource or state needed by multiple
  examples; stop and amend the theme contract rather than copying local tokens.

## Maintenance notes

The migration should mostly delete local visual vocabulary. If it adds more
theme-specific code than it removes, the shared theme is not yet carrying enough
of the implementation.
