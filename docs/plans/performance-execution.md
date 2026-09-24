# Performance evidence and projection experiments

Implementation record for [#313](https://github.com/RichiCoder1/lucent/issues/313)
and #314–317, following plans [006](../../advisor-plans/006-performance-benchmark-methodology.md)
and [007](../../advisor-plans/007-performance-opportunity-experiments.md).
Source checkout: `85e4843df3bbf17664886a3f39bc3af69c8486da` plus this implementation.
Measurement date: September 24, 2026. Real-app characterization remains incomplete;
#315 and parent #313 stay open for its native follow-ups and disposition.

## Evidence and workloads

The native verifier writes schema-1 reports on success and failure, with observed
values, limits, units, measurement boundaries, phase/frame attribution, all raw
frame fields and independent operation/cleanup failures. Its adjacent JSONL journal
must contain the complete lifecycle and agree with the collected observations.
Missing shutdown, malformed rows, wrong phases and incomplete corpora cannot pass.
Reports preserve full-lifetime and phase resource maxima side by side.

Release/win-x64 NativeAOT publication writes an adjacent identity manifest bound
to the executable and native binary hashes. Missing identities are explicitly
unknown; mismatched identities fail. Runtime/configuration, publishing SDK, source
revision/dirty state, viewport/DPI and supplied environment metadata remain visible.
Diagnostic file I/O occurs between requests. The frame interval is application
request through presenter completion, not client/UIA dispatch latency or cold boot.

`Performance` now defaults to `fixed-native-v1`: a fixed Light theme, four ordered
buttons and a 100-row virtual list. The real Windows host, Skia and UIA adapter are
unchanged. The corpus remains 500 Tab requests, 500 resizes and ten idle seconds.
Each request drains to 100 ms without a new frame (bounded by five seconds) before
the next dispatch. This now includes input as well as resize, preventing an extra
frame from one Tab from satisfying the next request. Extra frames remain in the
trace; this client pacing is outside measured frame latency.
The separate managed fixture still checks 10,000 rows, exact first/last endpoints,
30-DIP rows, two visible/six realized bounds, 20 cycles and 16 MiB post-GC growth.
Scene owners retain accepted scenes until replacement or teardown, including
failed construction. Tooling reports use schema 2 and retain completed/failed
operations, distinguishing whole-process time from a reported operation time.

Issue Browser characterization is opt-in, versioned separately, and declares
500 operations each for focus, scrolling, selection/details, resize within a
breakpoint and resize across a breakpoint. UIA identities, actual client geometry,
ordered row endpoints and detail content establish that operations did work.
Every attempted operation and additional frame is retained, including failures.
These five scenarios have no newly invented universal latency threshold.

## Legacy disposition and resource contract

The first B1 run of the old `issue-browser-compat-v1` contract is retained:

| Metric | Observed | Existing limit |
| --- | ---: | ---: |
| Input end-to-end p95 / p99 | 11.44 / 11.76 ms | 16.7 / 33.3 ms |
| Resize end-to-end p95 / p99 | 17.70 / 21.75 ms | 16.7 / 33.3 ms |
| UIA lifetime / warmup peak | 22 | 18 |

This is a **failed** historical app gate. It is not made successful by the new
fixture. The archived report, raw diagnostics and journal are
`artifacts/performance-b1-native.json`, `performance-b1-native-diagnostics.log`
and `performance-b1-native-phases.jsonl`. Exact machine and binary identity remains
in the ignored local reports rather than this public summary.
The original runner's merged output interleaved the final JSON brace with stderr;
the archived JSON separates those original streams. New runner outputs use
separate stdout/stderr files and unique evidence directories.

The migration retires that app-specific gate as the default fixed workload; it
remains explicitly invokable as `LegacyIssueBrowser`. Fixed-native-v1 retains
16.7/33.3/8.3 ms, 1 surface, 1 texture, 256 text blobs, **18 UIA providers**, 128
handle delta, managed bounds and zero owned resources after close.

The distinct `issue-browser-characterization-v1` UIA ceiling is **84**, derived
from authored topology rather than measured provider peaks: at most 24 static
semantic nodes plus three nodes per realized issue, with at most
`ceil(762 / 49) + 4 = 20` rows. The scenario permits client height 760±2 DIP; 49 DIP
is the minimum density row height, and overscan is two rows at each end. Focus
traversal opens no menus, and resize retains the same selected issue. No stale
provider overlap is allowed: `WindowsUiaProvider.Update` prunes departed identities
when replacing the semantic snapshot. A topology test freezes wide list/detail
and compact list/detail states (static counts 20, 24, 16, 21 respectively), including
the three-node issue subtree. Authored topology changes require a contract review.

This finite ceiling applies both over the sampled lifetime and within phases.
Repeated focus identities and the repeated scroll endpoint also check cache growth
using each operation's last attributed frame. First-response frames remain the
latency samples and can precede settled projection/cache pruning; they are not the
resource comparison boundary. The last presented frame at settle is still a sample,
not a guaranteed post-UIA-query cache census.
The historical 18-provider comparison is reported separately; it cannot excuse a
breach of the new finite ceiling. Surface/texture/text-blob/handle bounds and
post-close zero remain unchanged. Between-frame resource peaks remain unobserved.

## Fixed native result

The first published `fixed-native-v1` run completed its full 500-input/500-resize
corpus. Input p95/p99 was 11.16/11.66 ms and resize p95/p99 was 19.04/23.39 ms.
**The Performance suite remains failed:** resize p95 exceeds the unchanged 16.7 ms
limit. Raster p95 was 0.30/0.77 ms; ten idle seconds produced zero frames. All
resource and managed invariants passed: sampled lifetime surfaces/textures/blobs/UIA
were 1/1/8/7, handle delta 4, managed growth 33,184 bytes, and post-close owned
resources were zero. All newly authored fixture assertions passed.

The p95 resize frame spent 18.66 of its 19.04 ms in presentation, with projection,
raster and upload at 0.05, 0.15 and 0.16 ms. This identifies the interval to
investigate, not its root cause. [#318](https://github.com/RichiCoder1/lucent/issues/318)
owns the bounded presentation-tail investigation. No vsync, scheduler, display
setting or budget was changed, and the same candidate was not retried for a pass.

Evidence is under
`artifacts/test/performance/20260924-072634-db16239727894e61b23271f0e3e582c7/`:
`report.json`, `frames.log`, `phases.jsonl`, `verifier.log` and `tooling.json`.
The adjacent published identity confirms Release NativeAOT, SDK 10.0.401, 96 DPI
and the initial 800×500 client viewport. Exact host/binary identities remain local.
Tooling completed all six declared operations; those single observations establish
execution/reporting, not an improvement claim.

After review added the input quiet boundary and corrected the managed readiness
scene lifetime, a fresh final-verifier run is retained under
`artifacts/test/performance/20260924-075530-fixed-final/`. It again completed
500 inputs and 500 resizes, idle zero and all resource/managed/cleanup checks.
Input p95/p99 was 2.05/2.38 ms; resize was 20.46/23.38 ms, leaving the same p95
limit failed. Lifetime resources were again 1/1/8/7, handle delta 5 and retained
growth 33,184 bytes. The changed request pacing makes the two native timing runs
different corpora; their input difference is not attributed to a runtime speedup.
Both results remain retained, with #318 still open.

## Application verification boundary

The app driver and its fail-closed verifier are implemented. Native application
characterization has not completed the full five-scenario contract; #315 and #313
remain open. The driver preserves incomplete corpora and continues independent
scenarios only after verifying their preparation. No input/accessibility behavior
or request delay was changed to manufacture a successful run. Full native results
and diagnostic follow-up drafts are retained locally pending their disposition.

## P1: retain exact-height correction reuse

Grid, Flex and intrinsic wrapped-row layout still perform constrained measurement
and validation. They reuse the first arrangement only when the correction leaves
every height unchanged; height is the sole field that correction replaces. There
is no approximate equality, persistent cache or alternate layout algorithm.

Three alternating baseline/candidate pairs were predeclared per experiment, with
identical managed Release binaries except Core. Each run retains eight projection
scenarios (10 warmups, 40 recorded samples) and five semantic scenarios. No other
agent builds/tests ran during timing. External load, power and display refresh were
not controlled; these are local characterizations, not guarantees.

The initial full-record equality candidate reduced allocations but regressed some
timings. Its six runs remain in `artifacts/performance-experiments/p1-pairs`.
The separate exact-height refinement remains in `p1-height-pairs`, with its
comparison in `p1-height-comparison.json`. Across the three refined pairs, Issue
Browser allocations fell about 4–7%; latency was mixed. This change is retained
for reduced work/allocations, **without a consistent latency improvement claim**.

Final geometry/semantic fingerprints, realized rows and acceptance attempts match
in every pair. Fingerprints cover the final scene, not every intermediate state;
the existing independent wrap/Grid/Flex/geometry tests cover corrections that must
change height. A separate instrumented attribution run (excluded from timings)
found 17,112 fixed-height Flex corrections, 31,884 unchanged auto-height Flex
corrections and 903 unchanged intrinsic wrapped-row corrections over the probe.
No representative Grid corrections were exercised by that probe; Grid correctness
is covered by its existing contracts.

Per-run Core/verifier hashes and raw samples are retained with each series.

## P2: measured defer

The candidate skipped discarded plans/list copies only for neutral containers,
forwarding their child paint nodes unchanged. Broader bypasses were rejected during
review: custom drawing, caret/selection, images and ancestor opacity bounds have
validation or ownership obligations even during geometry discovery.

Three fresh pairs against retained P1 are under
`artifacts/performance-experiments/p2-pairs`; `p2-comparison.json` retains all
comparisons. Geometry/semantics/rows/attempts match. Representative unchanged,
same-breakpoint and breakpoint app allocation savings were only 0.58–1.07%,
0.74–0.82% and 0.70–0.90%. Timings were mixed, including a slower same-breakpoint
case in all three pairs. No runtime P2 shortcut is retained.

Separate counters (`p2-attribution.log`) show that 40 unchanged app projections
would avoid 2,320 of 7,680 discovery plans, while still constructing 3,840 final
plans. Breakpoint changes would avoid 2,120 of 6,960 discovery plans. Wide/deep
synthetic scenes performed no discovery passes. Instrumented timings are excluded.
The cheap safe portion saves too little to justify a special branch; a broader
change should wait for evidence that justifies a shared validation/materialization
seam. This closes the experiment as a defer, not an implemented optimization.

## Correctness checks

On the retained P1 source, Core passes 674 tests, Skia 93 (three existing opt-in
motion skips), and Issue Browser 22. Logs are
`artifacts/performance-experiments/final-<project>.log`. The final focused reporting
suite passes 30 Release tests (`artifacts/performance-settled-report-tests.log`),
including timing completeness, actual viewport bounds, malformed/absent journals,
endpoint failures, lifecycle evidence and resource growth. Its paired-frame
regression accepts first-frame differences when settled counts match, but rejects
a one-provider increase in the settled return frame. The earlier 21-test
suite also passed in Debug to verify that metadata describes the actual build.
The final NativeAOT verifier publication is warning-clean
(`artifacts/performance-settled-publish.log`). The standalone NativeAOT input probe
completes all 12 observations, and the scene probe all eight projection and five
semantic scenarios (`artifacts/performance-final-input.json` and
`artifacts/performance-final-scene.json`).

No UIA behavior, accessibility exposure, app palette, layout algorithm or native
scheduler was changed to improve a score. Navigation #205 and live compilation
remain outside this batch.
