# Plan 008: Package Lucent and finish the Workbench release gate

> **Executor instructions**: Package only interfaces exercised by Workbench and
> proven by tests. Do not declare a stable public API merely because it compiles.
>
> **Drift check**: `git diff --stat 3b27cfe..HEAD -- .github build src editors examples tests docs README.md`

## Status

- **Priority**: P1
- **Effort**: L
- **Risk**: MEDIUM
- **Depends on**: plans 001–007
- **Category**: direction / DX / distribution
- **Planned at**: commit `3b27cfe`, 2026-08-16

## Why this matters

The current build imports repository-relative task binaries and the extension is
packaged manually. A real dogfood release needs a clean project install,
cross-platform verification, and one application that can be built from a fresh
checkout using the language tooling proven by Plan 007.

## Current state

- `build/Lucent.Compiler.props` points at local `src/.../bin` assemblies.
- `build/Lucent.Compiler.targets` exposes the last successful generated files
  to design-time C# compilation but does not regenerate changed `.lui` files
  during design-time builds.
- There is no `.github/workflows` verification and no `dotnet new` template.
- The VS Code extension bundles a .NET 9 server through a local prepare script.

## Scope

**In scope**:

- Produce NuGet packages for the runtime, compiler/MSBuild integration, and any
  required build assets with reproducible package tests.
- Add a minimal `dotnet new` template containing a Lucent/Avalonia desktop app.
- Build/package the VSIX reproducibly.
- Add Windows, Linux, and macOS CI where supported, plus package-consumer tests.
- Finish Workbench's real project loading, compiler diagnostics, source editing,
  generated-C# preview, settings, themes, and release documentation.
- Publish the existing Markdown through Blume, including its generated
  `llms.txt`, `llms-full.txt`, and raw Markdown routes.

**Out of scope**:

- Marketplace publication, automatic updating, production hot reload, alternate
  editors, or API stability beyond the documented preview surface.
- Native AOT as a promise; it may be an informational experiment after normal
  self-contained publishing passes.
- New language-server capabilities or performance work beyond fixing a release
  regression against Plan 007's accepted contracts.

## Steps

### 1. Add package-consumer tests first

Create temporary sample projects that install local `.nupkg` files, compile
`.lui` and adjacent CSS, run generated code, and report source diagnostics.
These tests must not reference repository `bin` paths.

**Verify**: tests fail against the current local-path build assets.

### 2. Produce build/runtime packages

Pack runtime and compiler assets using standard NuGet `build`/`buildTransitive`
layout. Pin package versions centrally and include license/credits metadata.

**Verify**: a clean temporary consumer restores, builds, tests, and cleans.

### 3. Add template, VSIX, and CI

Create a minimal app template and reproducible VSIX/package commands. Add CI for
restore, build, .NET tests, VS Code tests, package-consumer tests, Plan 007's
deterministic LSP gates, and headless Workbench flows. Cache dependencies but do
not cache generated correctness artifacts as test results.

**Verify**: all jobs pass from clean runners on the supported OS matrix.

### 4. Complete the Workbench release gate

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

**Verify**: the walkthrough is automated headlessly where possible and manually
recorded only for native dialogs/platform behavior. The release record lists
which Plan 007 capabilities are exercised through VS Code and which Workbench
flows consume diagnostics, navigation, and generated-source mapping.

### 5. Publish preview documentation, not compatibility promises

Document install, template use, supported syntax, interop boundaries, debugging,
known limits, package versioning, Plan 007's tooling-performance evidence, and
the Workbench evidence. Mark the release experimental and list unsupported
behavior explicitly.

Use the repository's Blume site as the publication surface so the same source
serves people and AI clients. Keep MCP deferred until server deployment is
needed; the static AI endpoints cover the preview release.

**Verify**: every README command runs from a clean temporary directory.
On the recorded Plan 007 reference machine, rerun its benchmark against the
staged VSIX/server and package-consumer fixture using the exact accepted workload
manifest; the packaged result must meet the latency, allocation, memory, and
generation budgets.

## Done criteria

- [ ] A clean consumer uses NuGet packages, not repository-relative binaries.
- [ ] Template, packages, VSIX, and Workbench build in CI.
- [ ] Plan 007's deterministic LSP gates remain green in packaged form.
- [ ] The staged VSIX/server passes Plan 007's benchmark on its recorded
      reference machine.
- [ ] Workbench completes the documented keyboard-first workflow.
- [ ] Preview docs distinguish proven behavior from deferred design.

## STOP conditions

- Packaging requires consumers to reference this repository checkout.
- CI is green only because Workbench or headless interaction tests are skipped.
- Packaging regresses Plan 007's accepted LSP correctness or performance
  contracts.
- Publication or marketplace credentials are required; stop for user approval.

## Maintenance notes

Treat Workbench as the release oracle, not as a special case. Any private hook
added only to make it pass is evidence that the public module seam is wrong.
