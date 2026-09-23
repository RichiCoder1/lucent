# Plan 006: Make performance evidence complete and workloads reproducible

Status: planned, not implemented. Priority P1; effort M for B1 and M–L for B2;
maintenance risk low for reporting and medium for changing workload ownership.
Parent: [#313](https://github.com/RichiCoder1/lucent/issues/313).
Children: [B1 #314](https://github.com/RichiCoder1/lucent/issues/314) and
[B2 #315](https://github.com/RichiCoder1/lucent/issues/315), both Project 4 Todo.
Source baseline: `6604eaf02f5686c14203762f4ce584dbd4002dff`, September 23, 2026,
in `D:/src/richicoder1/lucent`. Prepared in a separate, older design worktree and
received into main as a queued plan; use the reviewed source as the baseline.

This is investigation and implementation planning only. No new benchmark, build,
UI run, or production change was performed. Preserve unrelated changes. Recheck
in-scope files against this SHA before execution. Coordinate with Implementation;
do not reopen delivered #310–312 or start navigation #205 or live compilation.

## What the evidence says

The existing Performance runner is useful but conflates a changing demonstration
app with fixed resource expectations. It also discards structured results when an
assertion fails. Fix those contracts before using it to select optimizations.

The first corrected-fixture run overlapped the full LSP suite: resize p95 was
16.9705 ms against 16.7 ms. One subsequent isolated run passed timing, idle,
virtualization and managed growth, but failed the full-lifetime UIA bound. Neither
run establishes a speedup, and the overlapped run is not a proven regression.

The archived isolated trace is
`artifacts/test-quality-performance-isolated-diagnostics.log`, SHA-256
`604f3202062b8cb6f7d2a95cdb3f84e4f4773c4983dddd7f1e66f056dca1a1af`.
Its final 500 input and 500 resize rows reproduce the reported end-to-end figures:

| Operation | End-to-end p95 / p99 | Projection p95 | Raster p95 | Present p95 |
| --- | ---: | ---: | ---: | ---: |
| Input | 11.7262 / 12.2005 ms | 6.1528 ms | 0.5390 ms | 6.1876 ms |
| Resize | 16.4562 / 17.7174 ms | 5.4655 ms | 0.9614 ms | 15.6898 ms |

The earlier #313 narrative mislabeled 6.1528/5.4655 as raster. The producer writes
projection in field 5 and raster in field 6; the verifier reads raster correctly.
This is an evidence-label correction, not a production timing fix. This trace has
509 input and 501 resize records including setup; reconstructed last-500 selection
is corroboration, not a replacement for durable corpus delimiters in future runs.
Marginal percentiles are not additive. Among the 25 slowest resize samples, mean
Present is 15.8932 ms and mean Projection 0.9184 ms. That points toward presentation
waiting in this trace, but does not establish its cause or justify changing vsync.

Observed UIA cache count starts at 16, reaches 22 during warmup, stays at 15 in
both measured corpora, and returns to zero at shutdown. Other observed lifetime
maxima are surface 1, texture 1, text blobs 63, handle peak delta 31. The existing
UIA limit is 18 over the whole recorded lifetime. This trace does not establish a
leak. `WindowsUiaProvider.cs:143–151,444–461,1303–1325` adds providers lazily, prunes
removed semantic identities and disposes cached providers. Keep all those boundaries.

## Current measurement contracts

| Owner | Current workload and interpretation |
| --- | --- |
| Published verifier | Eight Tab warmups and one 803×501 resize; exactly 500 Tab frames, then 500 alternating 801/803×501 resize frames. Resize notifications drain until 100 ms quiet, bounded by five seconds. These sizes are window API requests, not a claim about client-area DIP sizes. |
| Frame timing | Application-observed request timestamp through presenter completion. Excludes the verifier's prior PostMessage and OS message-delivery interval; does not measure physical pixels appearing on screen. Projection includes host work around layout and UIA; use the focused probe to attribute SceneLayout cost. |
| Percentiles | Nearest rank, p95/p99 ranks 475/495 of 500; limits 16.7/33.3 ms and raster p95 8.3 ms. Do not change them for test convenience. |
| Idle | Zero frames over ten seconds; retain this independent scheduler invariant. |
| Managed fixture | Verifier process, one priming fixture plus 20 mount/start-scroll/end-scroll/dispose cycles; 10,000 rows; root 800×500; list 800×60; exact row height 30; at most two visible/six realized; post-GC retained growth ≤16 MiB. Per-cycle sampled peak is not process peak memory. |
| Native resources | All recorded frames including startup/warmup: surface ≤1, texture ≤1, blobs ≤256, UIA ≤18. Handle peak minus first-frame baseline ≤128. Require pre/post observations and post-close surface/texture/blob/provider zero. Frame samples can miss between-frame peaks; label them as sampled maxima. |
| Scene probe | Ten warmups plus 40 samples for each of eight projection and five semantic scenarios. Projection excludes graph mutation/drain, installation, disposal and paint; sums projection attempts required for acceptance. Allocation is current-thread churn, not retained heap. |
| Input probe | Twelve cases: flat 101/1,001 and deep 21/101 elements × pointer/key/same-target focus. Four warmups then 64 dispatches; reports one batch mean and current-thread allocation per dispatch, not latency percentiles. |
| Tooling | `Measure-LuiTooling.ps1` distinguishes already-built test-process time from operation time using `LuiTooling.Measurements.json`. Its corpus process durations include test infrastructure. Do not reinterpret those as compiler-call latency. |

## B1 — Preserve observations, validate fixtures, and report failures

Priority P1, effort M, high source confidence. This is the first child and has no
dependency on B2 or a runtime optimization.

### Current state and files

`tests/Lucent.Performance.Verifier/Program.cs:57–60` currently does:

```csharp
CheckCorpus("input", input.Frames);
CheckCorpus("resize", resize.Frames);
var resource = CheckResources(frames, resources);
WriteResult(input, resize, managed, resource);
```

The generic overflow exception at line 462 omits the resource, actual value,
limit and phase. A cleanup exception from `finally` can also mask the primary
failure. `MeasureVirtualization` checks only heights and upper counts: an accepted
no-op scroll or empty final realization can pass. The fixture knows the expected
start/end item names and can assert them independently.

`InputDispatchProbe.cs:143–151` does not retain/dispose its accepted setup scene.
`SceneProjectionProbe.cs:411–423` disposes rejected scenes but does not transfer
accepted setup scenes into `Fixture._scene`; its normal replacement owner at
508–520 already provides the local pattern. Verify ownership on current source
before changing it. This is an ownership concern, not evidence of a measured leak.
Installation borrows the scene (`InputRouter.cs:237`); router teardown clears its
reference without disposing it (`1201`). `RetainedScene` owns prepared-resource
leases (`LayoutScene.cs:1520–1552`), so composition disposal does not fill this gap.
`SceneProjectionProbe.cs:98` also reports literal `"Release"` for every build.

### Scope and implementation

1. In the verifier, separate collected observations, independent evaluations and
   report writing. Use small verifier-local types; no universal test framework or
   public telemetry API. Keep raw diagnostics, all available samples, delimiters,
   observed idle count and pre/post resources even on failure. Missing evidence
   is explicitly incomplete, never zero or passing. Report first operational
   failure and additional cleanup failures without masking either.
2. Produce a versioned JSON result for success and failure, with metric name,
   actual, limit, unit, measurement boundary, status and violating phase/frame.
   Preserve full-lifetime sampled peaks and per-phase peaks side by side. Do not
   invent missing phase boundaries from operation names: persist verifier-owned
   delimiters as each phase completes. New schema consumers must reject unsupported
   versions. Reject malformed/nonfinite/out-of-order rows and incomplete corpora.
3. Include all already-emitted projection/raster/upload/present fields. Identify
   actual Debug/Release configuration, framework/runtime, architecture, AOT/JIT,
   source and dirty-state identity, executable hashes, fixture version and raw log.
   Record supplied environment/isolation metadata; mark unknowns honestly. Retain
   instrumentation mode and acknowledge diagnostic file I/O between requests.
4. Add independent start/end fixture assertions: visible `Issue 1`/`Issue 2`, then
   `Issue 9999`/`Issue 10000`, exact row heights and nonempty realization. Preserve
   two/six bounds, 20 cycles and 16 MiB. Give every accepted setup scene a clear
   owner until replacement/teardown; dispose failed construction paths as well.
   Keep installation and disposal outside existing measured intervals.
5. Keep tooling reports on command failure too: record completed operations and
   the failed/incomplete operation before returning nonzero. Preserve the existing
   process-versus-operation distinction. This is local report handling in
   `tools/Measure-LuiTooling.ps1`, not a multi-suite runner rewrite.
6. Add narrow deterministic report/fixture tests, preferably a small MSTest owner
   `tests/Lucent.Performance.Tests/` referencing verifier-local internals. Follow
   existing MSTest/NativeAOT project conventions; do not move runtime types into a
   test framework. Cover simultaneous timing/resource breaches, missing shutdown,
   malformed/truncated rows, wrong-phase samples, first failure plus cleanup failure,
   no-op scroll, empty endpoint, and scene ownership. Assert observable artifacts
   and lifetime validity rather than a second implementation of the evaluator.

Other allowed files: verifier project metadata, solution/test registration if a
new test project is needed, and `docs/TESTING.md`. Production changes are limited
to an essential diagnostics boundary/identity hook; coordinate with #254, which
owns the eventual bounded developer timeline. Do not build its UI or ring buffer.

### Verification and done criteria

- New focused test command: `dotnet test --project tests/Lucent.Performance.Tests/Lucent.Performance.Tests.csproj -c Release` (if that owner is created). All cases pass; controlled failure cases emit parseable failed reports and nonzero verifier status.
- Existing probes: `dotnet run --project tests/Lucent.Performance.Verifier -c Release -- --input-dispatch` and the same command with `--scene-projection`. Preserve 12 input observations, eight projection scenarios and five semantic scenarios with their stated counts. Preparation precedes, and must not overlap, measurement.
- Repeat metadata-only coverage in Debug using deterministic tests; do not confuse that with comparable benchmark timing.
- Run `./tools/Test-Repository.ps1 -Suite Performance` once in a reserved quiet window after preparation. Preserve its first result. At this stage the known UIA 22/18 failure may remain; success means it is precisely reported, not that B1 has made Performance green. Verify NativeAOT failure output still serializes.
- `git diff --check`; focused formatting check under the existing repository policy. No release/package gate for report-only changes beyond this specific AOT verifier path.

## B2 — Separate a versioned fixed fixture from application characterization

Priority P1, effort M–L, depends on B1. High confidence in the workload coupling;
new budget numbers require evidence and are not decided by this planning review.

### Workload design

Keep two useful kinds of measurement:

1. A small versioned native performance fixture, proposed owner
   `tests/Lucent.Performance.Fixture/`, using the real Windows host, Skia presenter,
   UIA adapter and NativeAOT publication path. Freeze its authored data, styles,
   focus order, readiness condition and interaction sequence. No sample-app chrome
   or network dependency. Keep exact 500-input/500-resize compatibility corpora,
   ten-second idle and existing resource/managed invariants. The current managed
   two-visible/six-realized fixture remains a separate assertion owner.
2. Real Issue Browser characterization with explicit source/version identity:
   focus traversal, sustained list scrolling, actual selection/content changes,
   same-breakpoint resize and crossing a responsive breakpoint. Verify named
   outcomes, rows and viewport bucket at endpoints so a no-op is not fast success.
   Reuse the existing eight-scene probe for attribution. App changes may alter
   measured topology, but must leave a visible baseline/contract revision.

Do not copy the application into the fixture or maintain a second runtime pipeline.
Select a small fixed list and controls using existing public component recipes.
For newly added native scenarios declare operation counts before recording; begin
with 500 attributable operations per scenario and run them as opt-in characterization.
Keep extra frames, retries and no-op/missing operations visible instead of quietly
discarding them. New scenarios need no arbitrary universal millisecond threshold.

### Lifecycle and budget design

Persist launch, first usable frame, warmup start/end, each corpus start/end, idle,
close and cleanup boundaries. Launch-to-ready is a separate observation; the
existing frame timer is not cold startup. A fresh process is not a cold filesystem,
font or OS cache. Do not flush system caches to manufacture a cold result.

Use explicit startup, warmup, steady and cleanup resource observations. Retain a
finite full-lifetime ceiling as well as steady bounds; neither may hide the other.
For UIA describe the authored semantic identities, lazy provider realization,
client/listener mode and permitted overlap before pruning. Also exercise repeated
focus/scroll cycles so a monotonically growing cache cannot pass on one endpoint.
Never disable accessibility or shrink user-visible work solely to meet a budget.

Initially retain 16.7/33.3/8.3 ms, 1/1/256/18 native maxima, 128 handle delta,
16 MiB managed growth, two/six rows and zero after close. If a changed workload
requires a budget revision, submit an explicit versioned proposal with topology,
resource ownership rationale, before/after observations and unchanged cleanup
requirements. A sampled maximum plus arbitrary headroom is not enough. Do not
quietly raise 18 to 22 or redefine it as measured-corpus-only.

Run the old Issue Browser contract beside the fixed fixture during migration and
retain its failing result. A passing new fixture does not retroactively make the
old contract pass. Resolve the historical app assumption explicitly, preserve
full-lifetime visibility for both, and document which version each result evaluates
before retiring the old app gate. #313 remains open until this disposition exists.

### Reproducible comparison and ownership

- Record machine/CPU/OS, SDK/runtime and package/native binary identities, display
  refresh and scale, client viewport, fixture/data identity, power/foreground/UIA
  conditions and measurement instrumentation. Unknown information remains unknown.
- Restore/build/publish first, then reserve an uncontended measurement window with
  other agents. Do not overlap builds, tests, index-heavy tooling or other timings.
  Do not kill processes, change power/display settings or disable UIA automatically.
- For an optimization, predeclare paired baseline/change runs (for example three
  alternating pairs on identical workload/configuration). Keep every result and
  first failure. Changed environment or workload invalidates comparison; fixing
  it starts a new labeled series rather than deleting inconvenient samples.
- Keep raw samples; compare allocation/work counts as well as latency distributions.
  No timing claims from one best run, no summed marginal percentiles, no attribution
  to a single change in a mixed diff. Time spent presenting can mask a CPU saving.
- Use current probes and existing diagnostics first. No new benchmark framework,
  profiling service or runtime dependency is required for this work. If existing
  evidence cannot attribute a cost, choose a supported profiler separately and
  record instrumentation overhead before trusting its timings.

### Scope, steps and verification

1. Add the fixed fixture and versioned scenario definitions. Verify meaningful
   UI endpoints and readiness independently, through focused fixture tests.
2. Extend verifier phases and resource observations using B1's schema. Controlled
   fixtures with warmup-only overflow, steady growth, missing post and leaked
   post resources must fail the intended check while preserving every observation.
3. Update `tools/Test-Repository.ps1` Performance-specific preparation/publication
   and documentation. Publish only necessary fixtures; do not undo #311's removal
   of the unused TestHost or couple Published back to Native. Tooling measurements
   remain separately labeled; no generic multi-suite orchestration.
4. Use `./tools/Test-Repository.ps1 -Suite Performance` for a quiet Windows NativeAOT
   run; all newly declared fixture assertions must pass, while the historical app
   outcome is reported honestly until its explicit disposition. Record published
   binary hashes and exact corpus counts, phase maxima, idle and post-close zero.
5. Run current `--scene-projection`/`--input-dispatch` probes only as relevant,
   independently of native timings. For host/UIA changes use the existing focused
   Windows/UIA contracts and relevant published accessibility scenario; no whole
   release suite simply because a benchmark changed.

## Stop conditions and maintenance

Stop a proposed optimization or budget change if the benchmark cannot establish
equivalent work, loses observations on failure, requires hiding warmup/lifetime
samples, changes input/UIA semantics, or depends on global cache/OS manipulation.
Report an unsupported scenario rather than substituting a weaker one.

Keep the benchmark-specific result/fixture modules small. Scenario versions and
metric boundaries belong beside their implementation. Documentation, JSON names
and issue summaries must use the same units and phase names. #254 remains the
owner of the broader developer diagnostics product. General test-quality cleanup,
live compilation, new navigation, broad caching and scheduler redesign are outside
this package. Plans 003–005 are already delivered and are not dependencies to redo.
