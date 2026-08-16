# Plan 007: Package Lucent and finish the Workbench release gate

> **Executor instructions**: Package only interfaces exercised by Workbench and
> proven by tests. Do not declare a stable public API merely because it compiles.
>
> **Drift check**: `git diff --stat 3b27cfe..HEAD -- .github build src editors examples tests docs README.md`

## Status

- **Priority**: P1
- **Effort**: L
- **Risk**: MEDIUM
- **Depends on**: plans 001–006
- **Category**: direction / DX / distribution
- **Planned at**: commit `3b27cfe`, 2026-08-16

## Why this matters

The current build imports repository-relative task binaries, the language
server shells out to design-time MSBuild and caches the result indefinitely,
and the extension is packaged manually. A real dogfood release needs a clean
project install, predictable diagnostics, cross-platform verification, and one
application that can be built from a fresh checkout.

## Current state

- `build/Lucent.Compiler.props` points at local `src/.../bin` assemblies.
- `build/Lucent.Compiler.targets:16` skips generation during design-time builds.
- `ProjectContextLoader.cs:63-86` caches project context without invalidation and
  silently falls back to no context on common failures.
- `LanguageServer.cs:102-117` advertises only sync, hover, definition, and
  completion.
- There is no `.github/workflows` verification and no `dotnet new` template.
- The VS Code extension bundles a .NET 9 server through a local prepare script.

## Scope

**In scope**:

- Produce NuGet packages for the runtime, compiler/MSBuild integration, and any
  required build assets with reproducible package tests.
- Add a minimal `dotnet new` template containing a Lucent/Avalonia desktop app.
- Make project evaluation observable, cancellable, invalidated on project/source
  changes, and actionable when it fails.
- Finish the editor features required to maintain Workbench: document symbols,
  references/rename for Lucent components, formatting, CSS class/token support,
  generated-source navigation, and semantic diagnostics parity.
- Build/package the VSIX reproducibly.
- Add Windows, Linux, and macOS CI where supported, plus package-consumer tests.
- Finish Workbench's real project loading, compiler diagnostics, source editing,
  generated-C# preview, settings, themes, and release documentation.

**Out of scope**:

- Marketplace publication, automatic updating, production hot reload, full C#
  language-service parity, alternate editors, or API stability beyond the
  documented preview surface.
- Native AOT as a promise; it may be an informational experiment after normal
  self-contained publishing passes.

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

### 3. Make project context reliable

Invalidate context on `.csproj`, reference, and C# source changes; cancel stale
evaluations; surface evaluation failures through LSP diagnostics/logging instead
of silently losing semantic features. Reuse one project-evaluation module from
build and editor paths where practical.

**Verify**: LSP tests change a project-defined control and observe refreshed
completion/diagnostics without restarting the server.

### 4. Finish the maintenance tooling

Implement document symbols, component references/rename, formatting, CSS
completion/navigation, and Lucent↔generated-C# navigation through the shared
frontend. Keep unsupported embedded-C# refactors delegated to C# tooling rather
than approximated.

**Verify**: protocol tests cover each advertised capability; the server does not
advertise incomplete features.

### 5. Add template, VSIX, and CI

Create a minimal app template and reproducible VSIX/package commands. Add CI for
restore, build, .NET tests, VS Code tests, package-consumer tests, and headless
Workbench flows. Cache dependencies but do not cache generated correctness
artifacts as test results.

**Verify**: all jobs pass from clean runners on the supported OS matrix.

### 6. Complete the Workbench release gate

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
recorded only for native dialogs/platform behavior.

### 7. Publish preview documentation, not compatibility promises

Document install, template use, supported syntax, interop boundaries, debugging,
known limits, package versioning, and the Workbench evidence. Mark the release
experimental and list unsupported behavior explicitly.

**Verify**: every README command runs from a clean temporary directory.

## Done criteria

- [ ] A clean consumer uses NuGet packages, not repository-relative binaries.
- [ ] Project changes refresh LSP semantics without restart.
- [ ] Every advertised LSP capability has a protocol test.
- [ ] Template, packages, VSIX, and Workbench build in CI.
- [ ] Workbench completes the documented keyboard-first workflow.
- [ ] Preview docs distinguish proven behavior from deferred design.

## STOP conditions

- Packaging requires consumers to reference this repository checkout.
- CI is green only because Workbench or headless interaction tests are skipped.
- The LSP advertises an approximate refactor that can corrupt embedded C#.
- Publication or marketplace credentials are required; stop for user approval.

## Maintenance notes

Treat Workbench as the release oracle, not as a special case. Any private hook
added only to make it pass is evidence that the public module seam is wrong.
