# Plan 012: Package and publish the experimental preview

> **Executor instructions**: Package application-facing interfaces exercised by
> Plan 011's Workbench workflow, plus dedicated package features proven by their
> own accepted gates (including Plan 009b utilities). Reuse Plan 008a's manifest
> and non-executing reader; do not declare a stable public interface merely
> because it compiles.
>
> **Drift check**: `git diff --stat ad0e183..HEAD -- .github build src editors examples tests docs README.md`

## Status

- **Priority**: P1
- **Effort**: L
- **Risk**: HIGH
- **Depends on**: 009b, 011
- **Category**: DX / distribution / documentation
- **Planned at**: commit `ad0e183`

## Why this matters

Lucent's build currently imports repository-relative task binaries and the VS
Code extension is prepared locally. After the examples and Workbench prove the
real interfaces, a preview release needs clean installation, cross-platform
verification, reproducible editor packaging, and honest documentation.

## Scope

**In scope**:

- Produce NuGet packages for runtime, compiler/MSBuild integration, Plan 008a's
  manifest assets, `Lucent.Themes.Shadcn`, global-style assets, and the opt-in
  Plan 009b utility catalog.
- Add clean package-consumer tests that use no repository `bin` path.
- Add a minimal `dotnet new` Avalonia desktop template with explicit Fluent plus
  Shadcn installation. Do not install utilities by default.
- Build/package the VSIX reproducibly.
- Add Windows, Linux, and macOS CI where supported for restore, build, tests,
  package consumers, Plan 008 deterministic LSP gates, and headless Workbench.
- Publish existing Markdown through Blume, including `llms.txt`,
  `llms-full.txt`, and raw Markdown routes.
- Publish Plan 008a's native-compatibility matrix and Plan 011's Workbench
  evidence as the bounded preview contract.
- Rerun Plan 008's accepted benchmark against the staged VSIX/server and clean
  package consumer on the recorded reference machine.

**Out of scope**:

- Example redesign or Workbench feature implementation; Plans 010 and 011 must
  finish first.
- Marketplace publication, automatic updating, production hot reload, alternate
  editors, or compatibility guarantees beyond the documented experimental
  preview surface.
- Native AOT as a promise; it may be recorded as an informational experiment
  after ordinary publishing passes.
- New language-server features or performance work beyond fixing a packaged
  regression against Plans 008–009b.
- Publication credentials; stop for user approval if they become necessary.

## Steps

### 1. Add clean package-consumer tests first

Create temporary projects that install only local `.nupkg` files, compile `.lui`
and adjacent/global CSS, install generated global catalogs, Shadcn, and optional
utilities, execute generated code, and report source diagnostics. Verify every
Lucent style assembly's one embedded Plan 008a manifest, PE identity, public
catalog type, and runtime/catalog parity without loading the target assembly.

Include the accepted referenced custom-control `TemplateContent` fixture or its
documented explicit-C# fallback.

**Verify**: tests fail against repository-relative build assets and pass only
through restored package assets.

### 2. Produce packages

Use standard NuGet `build`/`buildTransitive` layouts, central preview versions,
and license/credits metadata. Package one compiler-owned manifest contract; do
not create theme/global/utility-specific readers or schemas.

**Verify**: a clean temporary consumer restores, builds, tests, and cleans.

### 3. Add template, VSIX, and CI

Create the minimal template and reproducible VSIX/package commands. CI must run
normal solution tests, VS Code tests, package-consumer tests, deterministic LSP
gates, and headless Workbench flows. Cache dependencies, not generated
correctness artifacts.

**Verify**: supported clean OS runners pass without skipped Workbench or headless
interaction gates.

### 4. Publish preview documentation

Document installation, template use, supported syntax, interop boundaries,
debugging, known limits, package versioning, tooling-performance evidence,
native compatibility, and Workbench evidence. Mark the release experimental and
list unsupported behavior explicitly. Apply the accepted Shadcn visual authority
to Blume's reading surface without reviving superseded tokens.

**Verify**: every README command runs from a clean temporary directory and the
static human/AI documentation routes publish from the same Markdown sources.

### 5. Run the packaged release gate

Run Plan 011's walkthrough from packaged artifacts and rerun Plan 008's exact
benchmark workload against the staged VSIX/server and package consumer.

**Verify**: latency, allocation, memory, generation, diagnostics, navigation,
source maps, and Workbench gates match their accepted contracts.

## Done criteria

- [ ] A clean consumer uses NuGet packages, not repository-relative binaries.
- [ ] Shadcn, global styles, and optional utilities work from packages with exact
      Plan 008a manifest/runtime parity.
- [ ] The default template installs Fluent plus Shadcn but not utilities.
- [ ] Template, packages, VSIX, and Workbench build in supported CI.
- [ ] Plan 008's deterministic correctness and performance gates pass in packaged
      form.
- [ ] Plan 011's keyboard-first workflow passes using packaged artifacts.
- [ ] Blume publishes the preview documentation and AI-readable static routes.
- [ ] Documentation distinguishes proven behavior, experimental interfaces, and
      deferred design without compatibility promises.

## STOP conditions

- A consumer must reference the repository checkout.
- CI is green only because Workbench or headless interaction tests are skipped.
- Packaging regresses Plan 008 correctness or performance.
- A package needs a metadata schema/reader outside Plan 008a or embeds private
  application source by default.
- Packaging exposes application-facing interfaces Workbench did not exercise or
  dedicated package features without an accepted feature gate.
- Publication or marketplace credentials are required.

## Maintenance notes

The application-facing package surface follows the dogfood evidence; dedicated
catalog packages follow their accepted feature gates. Keep both explicitly
preview and small; unreleased compatibility policy remains governed by
`AGENTS.md`.
