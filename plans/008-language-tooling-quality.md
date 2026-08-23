# Plan 008: Make language tooling release-ready

> **Executor instructions**: Keep Lucent's LSP host thin and independent. Reuse
> the shared compiler frontend and public Roslyn APIs; do not create a second C#
> semantic model or couple Lucent to another language server's process.
>
> **Drift check**: `git diff --stat 3b27cfe..HEAD -- src/Lucent.Compiler src/Lucent.LanguageServer editors/vscode tests docs`

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: MEDIUM
- **Depends on**: plans 003, 004a, 006a, 007
- **Category**: tooling / performance / DX
- **Planned at**: commit `3b27cfe`, 2026-08-16

## Why this matters

Lucent is a separate authoring language, so it needs its own protocol endpoint,
but it should not pay for an independent C# language implementation. The current
server rebuilds project semantic state too broadly, lacks several maintenance
features, and has no latency or retained-memory contract. Workbench cannot be a
release oracle if completion becomes sluggish or repeated edits retain obsolete
Roslyn compilations.

## Current state

- `ProjectContextLoader` discovers the owning SDK project and caches evaluated
  source/reference paths, but invalidation and failures need stronger contracts.
- `ProjectSemanticCompilation.CreateBaseCompilation` reparses C# sources and
  recreates metadata references for each project compilation.
- `LanguageServer` keeps one Lucent project analysis generation but synchronous
  open/change analysis can still create avoidable allocation and latency.
- Completion, hover, definition, and diagnostics use the shared compiler
  semantics; document symbols, formatting, references/rename, CSS intelligence,
  and complete source mapping remain.
- No repeatable benchmark records completion latency or retained memory.

## Scope

**In scope**:

- Make project evaluation observable, cancellable, and invalidated by relevant
  `.csproj`, reference, and C# source changes.
- Retain one incremental Roslyn semantic base per active project, reuse unchanged
  metadata references and syntax trees, and evict superseded generations.
- Make completion, formatting, Lucent↔generated-C# source mapping, and supported
  embedded-C# semantics explicit quality gates.
- Add document symbols, component references/rename, and CSS property/class/token
  completion and navigation by consuming Plan 007's typed CSS catalog through the
  shared frontend.
- Add a repeatable protocol benchmark for completion latency, edit latency, cache
  behavior, allocations, and retained managed memory.
- Maintain a bounded native-C# quality comparison matrix for Lucent's supported
  seams: native controls/properties/events/attached properties, project types,
  component declarations/invocations, component members and islands, structural
  regions/loops/templates/bindings/slots, CSS documents, malformed edits, and
  unsaved cross-file overlays. It verifies hover, completion, definition,
  diagnostics, UTF-16 positions, triggers, docs, overloads/nullability where
  exposed by Roslyn, cancellation and latency. This is explicitly not general
  C# language-service parity.

**Out of scope**:

- Full C# language-service parity, unsupported embedded-C# refactors, production
  hot reload, alternate editors, or a general extension framework.
- Cross-process workspace sharing or co-hosting with OmniSharp, the Roslyn C#
  language server, or Avalonia's XAML tooling.
- Editor-specific virtual-document delegation unless the benchmark demonstrates
  that in-process Roslyn reuse cannot meet the budgets below.
- A full `MSBuildWorkspace` unless profiling shows the smaller compilation cache
  is insufficient and the added retained-memory cost is measured.

## Performance contract

Use a checked-in benchmark fixture representative of Workbench and record OS,
CPU, .NET SDK/runtime, build configuration, fixture size, and commit with every
result. A checked-in workload manifest fixes the fixture/content hashes, runner
version, document versions, completion positions, edit sequence, and sample
counts. It also records the required completion item sets and stable response
fingerprints for every measured position. Measure Release builds after one
untimed warm-up. Each latency series uses at least 500 sequential,
non-overlapping requests, and accepts a response only when it matches the latest
document version, project generation, and expected semantic fingerprint. Report
median, p95, p99, median/p95 allocated bytes per operation, peak live managed
heap, retained managed heap after forced collection, and active project-
generation count; do not present a single best run.

Initial budgets on the reference development machine are:

- warm completion response: p95 at or below 100 ms and p99 at or below 250 ms;
- one `.lui` edit followed by completion: p95 at or below 150 ms;
- warm-completion allocation: median at or below 1 MiB and p95 at or below 2 MiB;
- edit-followed-by-completion allocation: median at or below 16 MiB and p95 at
  or below 32 MiB;
- 500 alternating edit/completion cycles: at most one current and one in-flight
  project generation, peak live managed heap no more than 64 MiB above the
  post-warm-up baseline, and retained managed heap no more than 10% or 32 MiB
  above that baseline, whichever retained allowance is larger;
- unchanged C# syntax trees and metadata references are reused across `.lui`
  edits; a changed C# file replaces only its affected syntax tree.

Timing budgets are a dedicated benchmark gate, not a shared-runner CI assertion.
CI owns deterministic cache, cancellation, generation-bound, and protocol tests.
If the initial baseline misses a budget, capture the profile and stop for an
explicit budget or scope decision rather than weakening the number silently.

## Steps

### 1. Capture the failing baseline first

Add the protocol benchmark fixture and runner before changing cache behavior.
Exercise cold project load, warm completion, `.lui` edit-to-completion, C# source
invalidation, and the 500-cycle retention scenario. Save a machine-readable
baseline artifact and a short human-readable summary. Reject a run whose fixture
or request schedule does not match the checked-in workload manifest.

**Verify**: the baseline identifies compilation rebuild/allocation costs and can
distinguish reused, current, in-flight, and superseded project generations.

### 2. Make project context incremental and bounded

Invalidate context on `.csproj`, reference, and C# source changes; cancel stale
evaluations; surface evaluation failures through LSP diagnostics/logging instead
of silently losing semantic features. Reuse one project-evaluation module from
build and editor paths where practical.

Retain one Roslyn semantic base per active project, reuse unchanged metadata
references and syntax trees, replace only changed C# inputs, and evict
superseded generations. Do not retain one compilation per open Lucent document.
Replace wildcard design-time generated-source discovery with a deterministic
manifest written atomically after successful generation. Design-time builds
consume only that manifest; after the next successful generation, renamed or
deleted `.lui` inputs cannot leave stale generated `Compile` items.

The evaluated semantic snapshot must include the selected target framework,
language version, nullable mode, preprocessor symbols, global usings, project and
generated `Compile` inputs, resolved metadata/project references, and Roslyn
parse/compilation options that affect name and type resolution. Semantic parity
means matching that snapshot for supported expressions; it does not include
analyzer diagnostics, code fixes, or unsupported C# syntax.

**Verify**: repeated `.lui` edits reuse unchanged C# compilation inputs; changing
a project control refreshes completion/diagnostics without restart; cancellation
cannot publish an older generation; cache tests prove the generation bound. One
parity fixture varies target framework, defines, nullable mode, global usings, a
project reference, and a generated C# input. Before creating the reference Roslyn
compilation, assert every required property, item, and reference in Lucent's
snapshot against independently captured design-time MSBuild evaluation output;
then compare Lucent queries with that reference compilation. An MSBuild test
edits and renames a `.lui`, runs generation, and proves the next design-time
`Compile` set contains the new manifest entries and no stale generated files.

### 3. Make LSP quality release-ready

Implement context-correct completion, deterministic formatting, bidirectional
Lucent↔generated-C# source mapping, document symbols, component
references/rename, and CSS completion/navigation through the shared frontend.
Reuse Plan 007's property/value/selector catalog and diagnostics; do not create a
language-server-only CSS schema.
For supported embedded C#, completion, hover, definition, and diagnostics must
resolve against the same evaluated-project snapshot defined above. Keep ordinary
and generated `.cs` documents owned by installed C# tooling; exchange
deterministic files and source spans rather than another server's workspace.

Source maps are versioned with both Lucent and generated content hashes. A
Lucent span may navigate to multiple generated ranges; a generated range may
report multiple Lucent origins when code combines source constructs; generated
scaffolding is explicitly unmapped. A stale map must return no result rather
than navigate against different content. File identities are normalized document
URIs; ranges use zero-based, half-open UTF-16 offsets consistent with LSP; and
multi-match results sort by normalized URI, start offset, then end offset.

**Verify**: protocol tests cover each advertised capability; formatting is
idempotent; mapped diagnostics and navigation land on the responsible Lucent or
generated-C# span; mapping cases cover one-to-many, multiple origins, unmapped
scaffolding, and stale content; supported embedded-C# queries agree with the C#
project model; the server does not advertise incomplete features.

### 4. Meet and publish the performance contract

Profile before tuning. Prefer eliminating rebuilds and retained generations over
adding caches. Run the benchmark against the baseline, explain regressions, and
publish the result with the tooling documentation.

**Verify**: all performance budgets pass on the recorded reference machine;
deterministic protocol/cache tests pass in CI; the benchmark runner documents
how to reproduce the result.

## Done criteria

- [x] Project changes refresh LSP semantics without restart or stale publication.
- [x] The LSP retains a bounded incremental Roslyn project cache without
      depending on another language server's process or workspace.
- [x] Warm and post-edit completion meet the recorded p95/p99 latency budgets.
- [x] The 500-cycle benchmark meets allocation, peak/retained-memory, and
      generation budgets.
- [x] Completion, formatting, source mapping, and supported embedded-C# semantics
      meet the Plan 008 quality checks.
- [x] Every advertised LSP capability has a protocol test.
- [x] Benchmark inputs, method, environment, and results are reproducible.

## STOP conditions

- The LSP advertises an approximate refactor that can corrupt embedded C#.
- A cache can publish stale results or retains unbounded project generations.
- A proposed optimization requires private APIs or another language server's
  in-memory workspace.
- A performance budget is weakened without a recorded profile and user approval.

## Native-C# quality boundary

Native and project symbols retain available Roslyn XML documentation and clean
display text (no generated `global::` qualification in editor output). Every
supported authoring context is represented in protocol fixtures so a missing
hover or completion result is a regression, not an undocumented dead spot.

CSS completion and navigation consume the shared catalog. Class selector and
resource-token semantics belong to CSS files; Lucent intentionally does **not**
complete CSS class names inside `Class:` values.

## Maintenance notes

The shared compiler frontend is the semantic module; the LSP is its protocol
adapter. Keep performance state at the project seam, not in individual feature
handlers, so completion, hover, diagnostics, and navigation see one generation.
