# Plan 010: Package Lucent and finish the Workbench release gate

> **Executor instructions**: Package only interfaces exercised by Workbench and
> proven by tests. Reuse Plan 008a's module manifest and non-executing reader;
> do not declare a stable public API merely because it compiles.
>
> **Drift check**: `git diff --stat 3b27cfe..HEAD -- .github build src editors examples tests docs README.md`

## Status

- **Priority**: P1
- **Effort**: XL
- **Risk**: HIGH
- **Depends on**: plans 001–009b, including 008a
- **Category**: direction / DX / distribution
- **Planned at**: commit `3b27cfe`, 2026-08-16

## Why this matters

The current build imports repository-relative task binaries and the extension is
packaged manually. A real dogfood release needs a clean project install,
cross-platform verification, and one application that can be built from a fresh
checkout using the language tooling proven by Plan 008.

## Current state

- `build/Lucent.Compiler.props` points at local `src/.../bin` assemblies.
- `build/Lucent.Compiler.targets` exposes the last successful generated files
  to design-time C# compilation but does not regenerate changed `.lui` files
  during design-time builds.
- There is no `.github/workflows` verification and no `dotnet new` template.
- The VS Code extension bundles a .NET 9 server through a local prepare script.

## Scope

**In scope**:

- Produce NuGet packages for the runtime, compiler/MSBuild integration,
  Plan 008a's manifest build/reader assets, `Lucent.Themes.Shadcn`, Plan 009's
  theme metadata, Plan 009a's global-style build assets, and Plan 009b's opt-in
  utility catalog, with reproducible package tests.
- Add a minimal `dotnet new` template containing a Lucent/Avalonia desktop app
  with explicit Fluent plus Shadcn theme installation.
- Build/package the VSIX reproducibly.
- Add Windows, Linux, and macOS CI where supported, plus package-consumer tests.
- Migrate every example to the accepted Plan 009 Shadcn theme before finishing
  Workbench's real project loading, compiler diagnostics, source editing,
  generated-C# preview, settings, themes, and release documentation. Preserve
  Plan 007's adaptive and accessibility gates, not its superseded visual tokens.
- Publish the existing Markdown through Blume, including its generated
  `llms.txt`, `llms-full.txt`, and raw Markdown routes.
- Publish Plan 008a's native-compatibility matrix as the bounded interop contract
  for the preview package.

**Out of scope**:

- Marketplace publication, automatic updating, production hot reload, alternate
  editors, or API stability beyond the documented preview surface.
- Native AOT as a promise; it may be an informational experiment after normal
  self-contained publishing passes.
- New language-server capabilities or performance work beyond fixing a release
  regression against Plans 008–009b's accepted contracts.
- Installing Plan 009b utilities in the default template or migrating examples
  to utility-first styling without a separate accepted product decision.

## Steps

### 1. Add package-consumer tests first

Create temporary sample projects that install local `.nupkg` files, compile
`.lui`, adjacent CSS, and ordered `LucentStyle` items, install the generated
global style type, Shadcn theme, and opt-in utility catalog, run generated code,
and report source diagnostics. Inspect each Lucent-produced package assembly's
Plan 008a manifest through the compiler/LSP query surface backed by the shared
internal reader and public PE APIs, then verify style catalog/runtime parity.
Include Plan 008a's referenced custom-control `[TemplateContent]` fixture or its
accepted explicit-C# fallback. These tests must not reference repository `bin`
paths.

**Verify**: tests fail against the current local-path build assets.

### 2. Produce build/runtime packages

Pack runtime, compiler, the one Plan 008a manifest contract, Shadcn theme,
global-style assets, and the opt-in utility catalog using standard NuGet
`build`/`buildTransitive` layout. Pin package versions centrally and include
license/credits metadata. Do not package separate theme/global/utility manifest
readers or schemas.

**Verify**: a clean temporary consumer restores, builds, tests, and cleans.

### 3. Add template, VSIX, and CI

Create a minimal app template and reproducible VSIX/package commands. Add CI for
restore, build, .NET tests, VS Code tests, package-consumer tests, Plan 008's
deterministic LSP gates, and headless Workbench flows. Cache dependencies but do
not cache generated correctness artifacts as test results.

**Verify**: all jobs pass from clean runners on the supported OS matrix.

### 4. Complete the Workbench release gate

First perform the separately reviewed example migration required by Plan 009:
install Fluent plus `ShadcnTheme`, rewrite adjacent CSS and application resources
to `Shadcn.*` semantics, and remove duplicated `Lucent.*` visual dictionaries
without compatibility aliases. Preserve behavior, teaching purpose, keyboard
flows, automation, minimum-size handling, and existing interaction tests. Do not
start this migration until Plan 009's theme gallery has passed its acceptance
gate.

Update `DESIGN.md`, `design/tokens.css`, and Plan 007's visual-authority text as
the migration is accepted. Remove or explicitly supersede every Registration
Overlay application token/instruction so the repository does not retain two
competing application design systems.

Replace placeholder data with real project/compiler integration. Document one
keyboard-first walkthrough: open workspace, navigate tree, edit `.lui`, inspect
problems, jump to source, preview generated C#, change theme/settings, close and
reopen the recent workspace.

Replace Plan 006's `PlaceholderProblemLoader` with the real implementation of
the existing `IProblemLoader`; preserve its OwnedComputed loading/stale/error/
retry and owner-cancellation contract. Reuse Plan 006's settings repository,
save coordinator, intercepted shutdown, accessibility peers, and Plan 005's
single headless harness—do not introduce parallel loader/resource/settings/
lifecycle/test models.

Preserve Plan 007's application shell, visual-state coverage, minimum sizes, and
reference-review contract while applying Plan 009's accepted visual authority.
Real data must not reintroduce clipping, unbounded panels, or color-only status.

**Verify**: the walkthrough is automated headlessly where possible and manually
recorded only for native dialogs/platform behavior. The release record lists
which Plan 008 capabilities are exercised through VS Code and which Workbench
flows consume diagnostics, navigation, and generated-source mapping. Repository
search and review confirm `DESIGN.md`, `design/tokens.css`, and Plan 007 point to
one accepted application visual authority with no compatibility alias layer.

### 5. Publish preview documentation, not compatibility promises

Document install, template use, supported syntax, interop boundaries, debugging,
known limits, package versioning, Plan 008's tooling-performance evidence, Plan
008a's native-compatibility matrix, and the Workbench evidence. Mark the release
experimental and list unsupported behavior explicitly.

Use the repository's Blume site as the publication surface so the same source
serves people and AI clients. Keep MCP deferred until server deployment is
needed; the static AI endpoints cover the preview release.

Apply the visual authorities updated by this plan's Plan 009-directed example
migration and the committed Lucent mark to Blume's reading-mode surface. Do not
preserve superseded Registration Overlay application tokens merely because
documentation previously used them, and do not imply maturity or capabilities
beyond the preview.

**Verify**: every README command runs from a clean temporary directory.
On the recorded Plan 008 reference machine, rerun its benchmark against the
staged VSIX/server and package-consumer fixture using the exact accepted workload
manifest; the packaged result must meet the latency, allocation, memory, and
generation budgets.

## Done criteria

- [ ] A clean consumer uses NuGet packages, not repository-relative binaries.
- [ ] Shadcn theme/manifests and global-style build assets work from packages.
- [ ] Every Lucent-produced style package exposes one valid Plan 008a manifest;
      clean compiler/LSP consumers inspect it without target assembly loading,
      execution, or a public application-facing manifest reader.
- [ ] The optional utility catalog installs explicitly and its runtime styles
      match packaged completion metadata without entering the default template.
- [ ] Every example uses the accepted Shadcn visual authority without old
      `Lucent.*` compatibility tokens.
- [ ] `DESIGN.md`, `design/tokens.css`, and Plan 007 no longer compete with the
      accepted Shadcn application direction.
- [ ] Template, packages, VSIX, and Workbench build in CI.
- [ ] Plan 008's deterministic LSP gates remain green in packaged form.
- [ ] The staged VSIX/server passes Plan 008's benchmark on its recorded
      reference machine.
- [ ] Workbench completes the documented keyboard-first workflow.
- [ ] Preview docs distinguish proven behavior from deferred design.

## STOP conditions

- Packaging requires consumers to reference this repository checkout.
- CI is green only because Workbench or headless interaction tests are skipped.
- Packaging regresses Plan 008's accepted LSP correctness or performance
  contracts.
- A package requires a metadata schema/reader outside Plan 008a's contract or
  embeds private application source by default.
- Publication or marketplace credentials are required; stop for user approval.

## Maintenance notes

Treat Workbench as the release oracle, not as a special case. Any private hook
added only to make it pass is evidence that the public module seam is wrong.
