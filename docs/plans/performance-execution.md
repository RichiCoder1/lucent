# Performance evidence and projection experiments

Implementation record for [#313](https://github.com/RichiCoder1/lucent/issues/313)
and #314–317, following plans [006](../../advisor-plans/006-performance-benchmark-methodology.md)
and [007](../../advisor-plans/007-performance-opportunity-experiments.md).
Initial source checkout: `85e4843df3bbf17664886a3f39bc3af69c8486da`.
Native follow-ups start from `719fca1` plus the recorded corrections below.
Measurement date: September 24, 2026. The corrected real-app characterization
completes all five scenarios. #318 and parent #313 retain the physical held-border
verification boundary described below; no successful physical drag is claimed yet.
The initial implementation is published as `0.3.0-dev.88.1` from `719fca1`;
[CI 88](https://github.com/RichiCoder1/lucent/actions/runs/35974075357)
passed managed, package verification and publication jobs.
Native corrections and reporting closeout are published as `0.3.0-dev.89.1`
from `48feed95`; [CI 89](https://github.com/RichiCoder1/lucent/actions/runs/36049540126)
passed all three jobs and published all nine packages. #315 and #319 are complete.

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
Characterization journal schema 2 names its client completion interval
`clientEndpointAndFrameMs`: it includes endpoint validation and observation of at
least one attributed frame. It is not a first-presentation timestamp. Schema 1's
`requestToFirstFrameMs` measured the same broader interval despite its name; retain
those historical journals with their original verifier. Failed operations record
elapsed failure time separately. Application frame latency still comes from the
raw producer timestamps.

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
**That invocation failed:** resize p95 exceeds the unchanged 16.7 ms
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

### Presentation-tail attribution

An instrumented run retained under
`artifacts/test/performance/20260924-184558-28d85f9c145140f99efdeade0c5f39d5`
completed 500 inputs and 500 resizes and retained its failed timing verdict. A
temporary in-memory split around SDL's existing render/Present calls produced a
sidecar on normal shutdown, with no per-frame file I/O added inside the measured
interval. Instrumentation was then removed from source.

All 1,011 raw presentation timestamps matched a sidecar row. The sidecar contained
251 additional presentations: one during startup and exactly 250 during window
expansion. Every expanded measured resize was immediately preceded by an untraced
presentation at the same dimensions. The first presentation recreated the backing
resources; the measured second presentation reused them and waited in
`SDL_RenderPresent`. Its p95 was 16.84 ms, compared with 1.47 ms for the extra
expansion presentations and 1.40 ms for shrinking presentations. `RenderTexture`
was negligible. This attributes the directional tail to duplicate presentation
through the synchronous exposed-event watch and then the ordinary scheduler.

The retained correction limits synchronous rendering to the native modal move/size
loop, which is the reason the watch exists. Ordinary resize/expose events continue
through the scheduler. Coalescing away the measured frame was rejected because it
would leave the actual resize work outside the current trace. Fresh uninstrumented
baseline/candidate runs are predeclared in ABBA order, with frozen binaries and
unchanged timing/resource limits; attribution-run timings are not used as a paired
baseline.

The frozen series is retained in
`artifacts/performance-experiments/318-pair-20260924-185714`. A and B differ only
in the live-resize gate and its bootstrap wiring; both include the same focus/UIA
corrections. The same NativeAOT verifier evaluates every run.

| Run | Resize p95 / p99 (ms) | Present p95 (ms) | Verdict |
| --- | ---: | ---: | --- |
| A1 baseline | 16.1739 / 17.8288 | 15.6017 | Pass |
| B1 candidate | 4.2361 / 4.6107 | 1.6707 | Pass |
| B2 candidate | 4.5344 / 5.0063 | 2.0488 | Pass |
| A2 baseline | 16.7092 / 18.0398 | 16.0812 | Fails 16.7 ms resize p95 |

All four complete 500 inputs and 500 resizes, use 96 DPI and an initial 800×500
client viewport, and retain their full raw traces (1,010/1,010/1,010/1,011 rows).
Ten-second idle observations are zero; resource, managed and post-close zero
checks pass. Projection p95 remains 1.36–1.49 ms and the end-to-end minus recorded
stages residual p95 remains 0.0195–0.022 ms; that residual is not a direct queue-delay
measurement. Marginal phase percentiles are not additive. One baseline passes
and one fails: this is a local comparison under variable display/host timing,
not a claim that every baseline fails or that the improvement is universal.
The separate attribution run establishes the duplicate work; the paired runs
support retaining its removal without changing limits or presentation settings.

The NativeAOT held-border regression has not yet established physical live-resize
behavior for this candidate. Its first attempt failed to change the window width;
an instrumented attempt found the game owned the foreground, the cursor did not
move to the requested point, and the fixture's native bounds never changed. The
point itself reported the expected right resize border. These failures remain in
`artifacts/native-followup-held-border.log` and `-diagnostic.log`. The test now
checks foreground ownership and an unobstructed, positioned cursor before pressing
the border, and orders movement/button injection. Physical input is paused pending
desktop availability. Four Windows live-resize contracts pass, including native
enter/exit messages and collection of the subclass owner after destruction;
they do not substitute for the held-border regression.

## Application verification boundary

The app driver and its fail-closed verifier are implemented. The fresh NativeAOT
application completes the full five-scenario contract. The driver preserves
incomplete corpora and continues independent
scenarios only after verifying their preparation. The native follow-ups below
correct independently reproduced input/accessibility defects; request pacing and
performance limits remain unchanged. Full native results and diagnostic evidence
remain retained alongside the original incomplete run.

`artifacts/test/performance/20260924-native-followup-app` contains the schema-2
journal, all 3,519 raw frames and a passing verifier report: 500 operations each
for focus, scrolling, selection, same-breakpoint resize and breakpoint crossing.
Repeated focus and scroll-return provider growth are both zero. Sampled lifetime
surfaces/textures/text blobs/UIA are 1/1/120/65, below the versioned 1/1/256/84
ceilings; post-close owned resources are all zero. The historical 18-provider
diagnostic still fails at 65 and remains visible. This completes the explicit
app-contract migration; it does not turn the historical failed gate into a pass.

| Application scenario | Frame end-to-end p95 (ms) |
| --- | ---: |
| Focus traversal | 14.3914 |
| List scrolling | 2.4458 |
| Selection and details | 19.9540 |
| Same-breakpoint resize | 19.5754 |
| Breakpoint crossing | 22.3958 |

These are application characterizations with no universal latency threshold. The
app ran at 100% scale in the background while a game owned the foreground; power,
display refresh and external contention are unknown. No agent builds or other
tests overlapped, but this is not an uncontended-machine latency baseline. Every
scenario completed on its first attempt in this run, without side-effect retries.
The earlier selection error at index 372 did not recur in the full sequence; its
root cause remains unidentified and its failed evidence is preserved.

The app report verifies executable/native-binary hashes and records `719fca1`
with dirty source. The driver explicitly marks exact source binding as unknown:
its dirty-path list is not a cryptographic snapshot of compiler inputs. Local
publication logs and `artifacts/native-followup-source.patch` retain the observed
build history, but this run is not proof from a clean, commit-bound app artifact.
Clean package CI is separate evidence and does not replace native interaction.

### Native follow-up diagnosis

[#319](https://github.com/RichiCoder1/lucent/issues/319) tracks the confirmed focus
and clipped-overscan defects. A headless reproduction now reaches the same failure:
Tab down changes focus and invalidates the installed scene; the immediate key-up
rejects that stale scene and clears focus before the next projection. Its regression
also checks that a genuinely removed owner cannot receive stale input.
The correction retains focus intent until fresh-scene reconciliation while still
rejecting stale key releases. Tab traverses controls whose bounds intersect both
the viewport and ancestor clips; it skips fully clipped overscan and preserves
partially visible controls. Explicit semantic focus can reveal a realized target
in its nearest scroll viewport. Existing viewport coverage now distinguishes those
two operations instead of requiring Tab to enter an invisible child.

The first breakpoint-crossing failure is independently reproduced as
`ElementNotAvailableException` / `0x80040201` in a tree read while responsive content
is being replaced. Waiting for the resize's first presented frame before querying
the endpoint completes 30 crossings on the unchanged published binary. The driver
now uses that boundary after native geometry changes; it neither retries the resize
nor relaxes stale-provider rejection. Request attribution and all extra frames remain
in the original operation. Preserved diagnostic directories are
`artifacts/native-uia-Resize-20260924-183021` (failure) and `183350` (synchronized).
This preserves Windows' [unavailable-provider contract](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcoreapi/nf-uiautomationcoreapi-uiadisconnectprovider).
The offscreen correction follows the [UIA visibility definition](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationelement.automationelementinformation.isoffscreen?view=windowsdesktop-10.0):
fully clipped content is offscreen; a partially visible intersection remains onscreen.

The separate 500-selection reproduction completes without retries on the original
binary (`artifacts/native-uia-Selection-20260924-183413`). This does not explain or
erase the earlier full-workload command failure at index 372; full characterization
must still exercise selection after focus and scrolling.

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

The native follow-up source passes Core 676, Issue Browser 23, Windows UIA 29 and
reporting 31 checks. Logs are `artifacts/native-followup-<suite>.log`. The Windows
geometry case first failed on the missing property, then its reveal setup was
corrected to update a bound style instead of presenting the element twice. Final
coverage includes partial/full clipping, revealing retained content and a smaller
viewport. Fresh TestHost and Issue Browser NativeAOT publications are warning-clean;
the five-scenario application run above passes. The final desktop test preconditions
compile without warnings, but physical execution is pending. CI 89 independently
passes managed and package/NativeAOT checks on clean commit `48feed95` and publishes
`0.3.0-dev.89.1`. It does not replace the outstanding physical held-border proof.

The initial layout experiments did not alter UIA, accessibility exposure or the
native scheduler. The separately tracked native corrections above address focus,
visibility and duplicate presentation without changing the app palette, layout
algorithm or performance limits. Navigation #205 and live compilation remain
outside this batch.
