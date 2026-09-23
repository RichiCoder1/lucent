# Plan 007: Test two narrow reductions in repeated projection work

Status: planned experiments, no demonstrated speedup. Parent:
[#313](https://github.com/RichiCoder1/lucent/issues/313).
Children: [P1 #316](https://github.com/RichiCoder1/lucent/issues/316) and
[P2 #317](https://github.com/RichiCoder1/lucent/issues/317), both Project 4 Todo.
Reviewed source: `6604eaf02f5686c14203762f4ce584dbd4002dff`, September 23, 2026,
in `D:/src/richicoder1/lucent`. The separate design worktree is not the source
baseline. No production edits, builds or measurements were made during this review.

Preserve Windows-first/NativeAOT support, portable Core boundaries, accessibility,
retained-scene ownership, input freshness and exact layout semantics. Keep source
and binary identities for all experiments. Revalidate excerpts if the source has
changed. Use `codex/` branches if isolation needs a new branch; preserve existing
edits and coordinate with Implementation. This plan authorizes bounded experiments
for an executor, not speculative replacement of layout or caching architecture.

## Priority and evidence

| Order | Work | Expected benefit | Evidence/confidence | Effort / risk |
| --- | --- | --- | --- | --- |
| 1 | P1: reuse unchanged arrangement | Remove redundant pure calculations and temporary collections | Repeated calls source-confirmed; meaningful application saving unmeasured | S–M / low–medium |
| 2 | P2: skip discarded paint output during discovery | Reduce transient nodes/plans and paint-property work in full responsive/virtualized projection | Discarded output source-confirmed; share of total cost unmeasured | M / medium |
| Defer | Stop materializing invisible text lines | Reduce line draft work on overflowing shape-cache misses | Source-confirmed pattern; real workload incidence unknown | M / medium |

The isolated app trace in plan 006 shows projection p95 6.1528/5.4655 ms and
raster p95 0.5390/0.9614 ms for input/resize. That makes projection worth
investigating, but the slow resize tail is largely in Present. These changes do
not promise to solve a 16.7 ms tail dominated by presentation waiting. The old
projection evidence in `docs/SCENE-PROJECTION-EVIDENCE.md` is historical and often
contains combined changes; do not use it as this experiment's current baseline.

## Shared experiment contract

Prerequisite: plan 006 B1's valid ownership, explicit boundaries and durable
failure reports, or equivalent verified measurement fixes. B2's complete native
fixture migration is not required to begin projection-only experiments. P1 and
P2 touch the same Core file: execute serially with a new baseline between them.

Before each change, run the maintained Release scene probe against the exact
baseline and capture all eight scenarios, ten warmups and 40 samples each, plus
its five semantic scenarios. Preserve projection attempts, realized rows, geometry,
allocation and runtime/source identity. Build preparation and correctness tests
must not overlap measured runs. Predetermine three alternating baseline/change
pairs; keep every observation. Do not retry until green or claim speedup from the
best sample. A counts/allocation result can justify removal of redundant work even
when presentation hides elapsed differences, but state that limit explicitly.

Add temporary local counters for attribution only if needed; exclude their overhead
from final timing comparisons and do not create a new public diagnostics API.
Both experiments require one actual Issue Browser scenario and a sensitivity case
that would fail if the shortcut skipped necessary work. Decline the optimization
if representative benefit is negligible relative to its maintenance cost. A
recorded defer result is a valid experiment outcome, not a reason to weaken tests.

## P1 — Reuse the first arrangement when correction changes nothing

### Current state

`src/Lucent.Core/SceneLayout.cs:887–934` and `938–987` always arrange Grid/Flex
twice. Constrained measurement builds `corrected`; explicit-height specifications
return unchanged, and unchanged auto-height results can also leave all inputs equal:

```csharp
if (!spec.AutoHeight)
    return spec;
// Assigned-width measurement computes a possibly different Height.
```

Nevertheless the second `ManagedLayout.ArrangeGrid(corrected, ...)` or
`ArrangeFlex(corrected, ...)` is unconditional. `SceneLayout.cs:1593–1642` has
the same pattern for intrinsic wrapped-row measurement. `ManagedLayout.cs:42–78`
allocates working lists and result collections per call. `LayoutItemSpec` is a
value record, so exact input equality can be evaluated locally.

### Scope and steps

1. Attribute first/second calls and changed corrections across fixed-height,
   unchanged auto-height and changed-width paragraph cases. Use existing scene
   probes; do not add a broad benchmark engine. Record counts per scenario.
2. In `SceneLayout.cs`, retain constrained measurement and validation. Track actual
   input changes while constructing corrections; reuse the first assignments only
   when all arrangement inputs are exactly unchanged. Cover the intrinsic-wrap
   site too if the same proof applies. No approximate floats, global cache, skipped
   paragraph measurement, custom algorithm shortcuts or persistent result cache.
3. Extend the existing owning tests only where coverage is missing. Relevant owners
   are `LayoutReviewRegressionContracts.cs`, `AutoHeightWrapContracts.cs`,
   `GridAllocationContracts.cs` and `LayoutSceneContracts.cs`. Use authored geometry
   as the oracle. For example the existing
   `NonWrappingRowRemeasuresShrinkableParagraphAtAssignedWidth` expects a 10×30
   row/paragraph and following sibling Y=30; those assertions must still detect a
   skipped necessary correction. Preserve rounding, constraints and invalid-input
   behavior, including wide/narrow/wide sequences and Grid spans.
4. Measure the isolated change under the shared contract. Require equivalent
   accepted boxes/semantics and retries, fewer redundant second calls where inputs
   match, reduced attributable work/allocation, and no reproducible representative
   regression. Report latency uncertainty instead of inventing a percentage target.

### Verification and decision

Existing command: `dotnet test --project tests/Lucent.Core.Tests/Lucent.Core.Tests.csproj -c Release`.
Run the relevant layout classes first; the full Core owner is appropriate if the
shared layout change crosses their boundaries. Use the standard MSTest filters
documented in `docs/TESTING.md`; require positive discovery. For consumer wiring,
run affected `Lucent.IssueBrowser.Tests` responsive/list tests. Run
`dotnet run --project tests/Lucent.Performance.Verifier -c Release -- --scene-projection`
in an isolated measurement window after builds. `git diff --check` and repository
formatting must pass. Do not run the whole release/package pipeline merely for a
local pure arrangement change; use B2's native evidence when combining delivery.

Stop/defer if most representative corrections actually change inputs, the extra
decision costs as much as it saves, or the change alters invalid geometry handling.
Completion is either a small correct change with attributable evidence, or a
documented defer with the experiment results and no production workaround.

## P2 — Avoid paint-object construction in geometry discovery

### Current state

`SceneLayout.cs:53–64,122–133,142–153` calls `Layout` for assigned boxes, with
a new `ProjectionCache`, and discards returned scene nodes. Its paint flag is:

```csharp
internal bool CapturePaint => _resolvedStyles is not null;
```

But `SceneLayout.cs:1025–1198` still resolves drawing output and constructs
image/text/caret nodes, decorations and `ElementPaintPlan`, ending in:

```csharp
if (cache.CapturePaint)
    cache.PaintPlans[element.Id] = plan;
var paint = cache.CapturePaint
    ? ReadPresentedPaint(element)
    : (style.Background, style.Opacity, style.TextColor);
return PaintElement(plan, paint, childNodes, viewport);
```

`SceneLayout.Paint.cs:135–163` builds additional contents/result lists and nodes.
CapturePaint currently controls retention, not all materialization. Responsive
and virtualized projections encounter these preliminary passes in real apps.

### Scope and steps

1. Count discarded discovery nodes/plans separately from final output for unchanged,
   breakpoint and scroll cases. Confirm which reads/validation have semantic or
   ownership effects before removing them. Inspect custom drawing, decorations,
   caret, images and clipping; a discarded return value alone does not prove every
   operation producing it is removable.
2. Add one explicit output mode/boundary inside the existing layout traversal:
   geometry discovery records required boxes/measurements; final layout materializes
   paint nodes/plans. Do not duplicate the layout algorithm. Keep shaping needed
   for intrinsic measurement, all responsive discovery rounds, virtual realization,
   validation, feedback rejection and input freshness signatures.
3. Keep custom drawing validation/failure semantics and image request/lease
   ownership intact. If these require side effects during discovery, retain the
   necessary work or extract a small shared validation step. Reject an optimization
   that moves failures later or changes behavior merely to avoid allocations.
4. Extend the relevant existing layout/motion/drawing/image tests with independent
   geometry, semantic, pixel and ownership assertions. Existing owners include
   `LayoutSceneContracts.cs` responsive and nested viewport tests,
   `MotionProjectionContracts.cs`, drawing/image lifetime tests, renderer pixel
   tests and `tests/Lucent.IssueBrowser.Tests/IssueBrowserTests.cs`. Preserve their
   exact contracts rather than taking an after-image as the expected result.
5. Run the shared isolated experiment. Require unchanged discovery/acceptance
   attempts, geometry, realized rows, semantics, pixels and disposal, with the
   discarded paint allocations removed. Paint-only frames already reuse geometry;
   do not claim an improvement for them without separate evidence.

### Verification and decision

Use focused Core layout/motion/image/drawing contracts, then
`dotnet test --project tests/Lucent.Renderer.Skia.Tests/Lucent.Renderer.Skia.Tests.csproj -c Release`
for affected pixel/ownership behavior, and affected Issue Browser tests. The same
existing `--scene-projection` command measures the changed work; preserve exact
scenario/sample counts. Use the versioned published Windows/NativeAOT fixture when
validating final host behavior; success in a managed probe is not NativeAOT frame
proof. Formatting and `git diff --check` must pass.

Stop/defer if discarded paint materialization is a negligible share, the change
requires a second layout implementation, or it cannot preserve drawing validation,
image demand, retained-resource lifetime or accessibility. Write the attribution
and keep/defer result beside the evidence before closing the child ticket.

## Considered and deferred

- **Bounded text-line production:** `SkiaSceneRenderer.cs:115–147` builds/rebases
  drafts before applying MaxLines; `IssueRow.lui:3–6` sets MaxLines=1. Shape misses
  for long rows/changing widths may benefit, but identical warm requests are cached
  and normal short labels may not overflow. First count discarded versus retained
  drafts on real rows. Do not create an implementation ticket until incidence is
  material. Full-span shaping needed for wrap advances remains; preserve hard
  breaks, graphemes, bidi, UTF-16 ranges, ellipsis and cache completeness.
- **Persistent layout/style/input caches or dirty-subtree engine:** existing
  revision and retained paint mechanisms already skip work. No new evidence
  justifies another invalidation system.
- **UIA cache trimming to fit 18:** the trace does not establish a leak. Resolve
  benchmark phases/topology explicitly; retain access to legitimate providers.
- **Vsync/scheduler changes:** Present dominates the slow resize tail in one trace.
  Attribution and an explicit UX/frame-pacing contract are required before change.
- **Generic orchestration or adopting a benchmark dependency:** use maintained
  probes first. The prior multi-suite runner proposal remains deferred; no new
  library is needed for these two local experiments.
- **Navigation #205, live compilation, broad code organization refactoring:**
  outside the delegated performance planning scope.

## Delivery record

Each child records source/binary identity, commands, raw artifact locations,
correctness checks, all comparison runs, and a retain/defer decision. Do not describe
plans as implemented or a red benchmark as passing. The completion report must
distinguish less work, lower allocations, projection latency and native presentation
latency rather than treating them as interchangeable performance wins.
