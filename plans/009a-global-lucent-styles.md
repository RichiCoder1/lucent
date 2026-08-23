# Plan 009a: Add explicit global Lucent styles

> **Executor instructions**: Add one visible application-style seam. Reuse Plan
> 009's CSS parser, typed catalog, class metadata, and LSP cache; do not add a
> second style language, hidden startup hook, or assembly execution.
>
> **Drift check**: `git diff --stat e2a9a5f..HEAD -- build src editors tests docs plans`

## Status

- **Priority**: P1
- **Effort**: L
- **Risk**: HIGH
- **Depends on**: plan 009
- **Category**: styling / build / runtime / tooling
- **Planned at**: commit `e2a9a5f`, 2026-08-23

## Why this matters

Adjacent CSS is intentionally component-scoped. A project-wide completion index
would be dishonest unless those styles also have real runtime semantics. This
plan adds that separate contract after Plan 009 proves the theme and class
catalog, without expanding the Shadcn foundation delivery.

## Decisions

`<LucentStyle Include="..." />` is a build/runtime input, not editor-only
metadata. Preserve evaluated MSBuild item order; later global files win at equal
native priority. Generate one public `<RootNamespace>.LucentStyles` type per app
or library. Hosts install desired catalogs visibly:

```csharp
Styles.Add(new MyApp.LucentStyles());
Styles.Add(new SharedLibrary.LucentStyles());
```

Installed rules enter `Application.Styles` and may style handwritten and
Lucent-generated Avalonia controls. Adjacent component CSS must win over global
CSS for the same property; prove this against public Avalonia behavior rather
than assuming source order.

The generated type exposes safely inspectable compile-time class metadata so
Roslyn can read explicitly installed referenced-library catalogs without loading
their assemblies. Treat a catalog as installed only when the project semantics
contain a direct `Application.Styles.Add(new SomeProject.LucentStyles())`
invocation (including the equivalent direct `this.Styles.Add(...)` inside the
`Application` subclass). Unknown factories, aliases, fields, or indirect flows
do not activate completion metadata. An executable project that declares its own
`LucentStyle` items but has no recognized direct install receives a build warning
with the exact call. Libraries do not warn because installation belongs to the
host.

## Scope

**In scope**:

- Evaluate ordered `LucentStyle` items through the existing MSBuild project seam.
- Compile them through Plan 009's typed CSS frontend and diagnostics.
- Generate public installable style types for apps and libraries.
- Add metadata-only completion for local and explicitly installed library
  catalogs using Plan 009's immutable cache and ranking.
- Add the executable-project missing-install warning.
- Document installation, ordering, precedence, packaging, and limits.

**Out of scope**:

- Hidden or automatic startup, automatic transitive library installation,
  runtime CSS parsing, assembly execution, or an application service locator.
- New selector/value syntax, theme work, example redesign, navigation/references,
  or treating uninstalled project CSS as applicable.

## Steps

### 1. Lock build/runtime contracts

Add temporary app and library fixtures for ordered items, generated type shape,
explicit installation, missing installation, and adjacent/global conflicts.

**Verify**: tests fail before `LucentStyle` exists and record Avalonia's actual
style-priority result for the conflict case.

### 2. Generate and install global styles

Extend the existing project evaluation and generation path with ordered
`LucentStyle` inputs. Emit one public project-root-namespace style type and its
class metadata. Add the executable-project analyzer warning; keep libraries
quiet.

**Verify**: installed rules style handwritten and Lucent controls; MSBuild order
is deterministic; app omission warns with the exact call; library compilation
does not warn.

### 3. Merge completion metadata

Detect the bounded direct `Application.Styles.Add(new ...LucentStyles())`
installation forms above semantically; construction alone is insufficient.
Merge local and installed-library entries into Plan 009's cached completion
catalog without request-path I/O or assembly loading. Adjacent applicable entries
rank before global applicable entries, followed by theme and discovery fallbacks.

**Verify**: library classes appear only after a recognized direct installation,
not after bare construction or an unknown indirect flow; stale project
generations cannot publish entries; Plan 008/009 performance bounds pass.

### 4. Publish evidence

Document project items, generated API, app/library ownership, ordering,
precedence, warning behavior, completion origin, and package requirements.

**Verify**: package-consumer fixtures required by Plan 010 can install app and
library global catalogs without repository paths.

## Done criteria

- [ ] Ordered `LucentStyle` inputs generate installable app/library style types.
- [ ] Installation is explicit and applies across the Avalonia application.
- [ ] Adjacent CSS demonstrably wins over global CSS through public APIs.
- [ ] Completion reads local/installed-library metadata without I/O or execution.
- [ ] Missing app installation warns; libraries remain host-owned and quiet.
- [ ] No hidden startup, runtime parser, transitive install, or duplicate catalog
      implementation is introduced.

## STOP conditions

- Public Avalonia APIs cannot guarantee adjacent-over-global precedence.
- The feature requires hidden startup, runtime polling, private APIs, or target
  assembly execution.
- Referenced-library completion cannot identify the bounded direct installation
  forms from metadata and the existing semantic project snapshot.

## Maintenance notes

Global CSS is an opt-in application resource, not a change to adjacent component
scope. Plan 010 packages only the generated API and behavior proven here.
