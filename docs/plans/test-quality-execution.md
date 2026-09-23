# Test-quality execution

Tracked in [#311](https://github.com/RichiCoder1/lucent/issues/311), following the
delivered authoring corrections #310. Scope follows advisory plans
[003](../../advisor-plans/003-existing-test-cleanup.md),
[004](../../advisor-plans/004-test-harness-improvements.md) and
[005](../../advisor-plans/005-agent-test-authoring-guidance.md), including their
[Fable disposition](../../advisor-plans/reviews/test-quality-fable-disposition.md).
The changes affect tests, preparation and guidance; no production runtime change
or new dependency is intended.

## Retained coverage

| Removed or replaced assertion/setup | Retained owner |
| --- | --- |
| Test-local Roslyn formatting prototype | `WholeFileFormattingTests.FormattedMemberExecutionPreservesRawVerbatimAndInterpolatedValues` uses Lucent's formatter, retains comment/newline/raw/verbatim/interpolation inputs, both runtime branches and explicit brace/layout expectations; original/formatted specimens compile once each. |
| Equal signal write described as drawing coalescing | `DrawingContracts.TrackedRecordingCoalescesEqualCommandsAndDoesNoIdleWork` forces a rerecord with equal commands, then changed commands; recording counts, drawing identity, idle work and old-scene resource lifetime remain checked. |
| BOM plus production-reader-only encoding check | `ToolingCommandTests.AtomicReplacementPreservesEncodingAndBom` compares complete independently encoded bytes for six encoding/BOM classes, including BMP and supplementary characters. Concurrent-edit rejection and temporary-file cleanup remain separate. |
| Three self-posted callbacks in the application context test | `ReactiveDrainBoundaryContracts.DefaultApplicationEventDrainIncludesSelfPostedCallbacksInItsBudget` owns bounded reposting and recovery. The application test keeps context restoration, thread affinity, foreign Send rejection and late posts. |
| Private companion field/construction spelling | Named-component runtime tests retain independent mounts, initialization order and cleanup; public identity, Signal/Derived kinds and both sole-emitter negatives remain asserted. |
| Right-file/non-null/count-only named editor results | Marker-based protocol checks compare complete authored definition/reference/rename sets, reject duplicates and unexpected edits, distinguish same-arity overloads and require the relevant completion. Compiler maps and graph/config freshness remain separate. |
| Global nonzero-pixel count for many drawing primitives | Each ellipse, arc, rounded rectangle and filled/stroked path has isolated painted/unpainted samples at 1×, 1.5× and 2×. Combined clip/transform/opacity and recorder lifetime tests remain. |
| Nine positive asset metadata consumer builds | One fixture checks exactly nine distinct inventory entries and each format/dimension/density/relative-size result. All seven metadata negatives and existing distribution/invalidation/NativeAOT cases remain. |
| Implicit Native invocation after Published | Native remains an explicit suite and package-CI job; Published retains its actual NativeAOT applications, TestHost and desktop/UIA checks. |
| Unused TestHost publication in Performance | Performance prepares Issue Browser and its verifier; Published and Accessibility still request their TestHost. |

## Measurements and bounded decisions

C6 keeps the 10,000-node dependency replacement and individual disposal cases.
The September 22 focused run passed all 11 scaling tests; those two cases took
51.93 ms and 21.39 ms respectively in the TRX, excluding build. Their direct-effect
ownership/disposal coverage remains useful at negligible observed cost. No stress
filter, timing threshold or new opt-in mechanism was added.

C5 removes eight positive-consumer restores and eight builds. Full asset verification
against the same `0.3.0-dev.review310.20260922.1` candidate passed before and after:
108.36 seconds before, 111.28 seconds after. These single runs do not demonstrate an
elapsed-time improvement; they establish less setup with retained checks. Logs and
inventories are under `artifacts/test-quality-assets-*` and `artifacts/lui-assets-proof`.

H4 multi-suite preparation reuse is deferred. Required H3 already removes the known
unused work; current CI selects Managed and Native separately, and there is no measured
repeated desktop workload justifying a larger orchestration API. Grammar tokenizer
dependencies and wholesale headless-suite migration remain outside this package.

## Verification

Policy links/content and whitespace checks pass. Renderer checks pass: 93 tests,
with three existing opt-in motion characterizations skipped. The focused drawing
matrix passes 10 cases. Compiler passes 145 tests, Core passes 674 and Tooling passes
8, with clean builds. LanguageServer passes 46/46, including all five focused helper
and named-component contracts. Repository formatting passes (596 C# files checked by
CSharpier; 115 LUI files). The complete Published runner passes, including exact
inventory and six negative inventory cases, editor-session/lifecycle/settings
proofs and 25 desktop passes with two existing physical mixed-DPI opt-in skips.
It publishes only its three desktop fixtures and no longer runs Native implicitly.
Performance prepares only Issue Browser and its verifier; no TestHost is published.
Its tooling measurements pass, but the benchmark remains separate under #313 below.
No new manual visual or accessibility certification is claimed.

## Findings exposed by stronger checks

The exact editor oracle found [#312](https://github.com/RichiCoder1/lucent/issues/312):
Go to Definition on named component state returned its use span rather than the
authored declaration. The initial failure is retained in
`artifacts/plan004-h1-focused.log`; the correct expected range remains unchanged.
This is a separate production correction, not a relaxed test expectation.
The fix is saved in `c367351`. The deployed server at
`C:/Users/richa/.lucent/lui/review312-20260923/server` matches the published local build
and passes initialize/shutdown. Global and Lucent workspace settings point to it,
with prior settings backed up; open VS Code windows need reload. The VSIX client
remains 0.3.5 because only the backend changed.

The real Published path found a stale inventory manifest after Issue Browser's
earlier Hosting adoption. Its existing project reference legitimately publishes
`Lucent.Hosting.pdb` and two Microsoft.Extensions notices. Those three explicit
entries were added to `publish-inventory.json`; exact inventory equality and all
negative missing/undeclared-file checks remain enabled.

The Published desktop batch exposed an intermittent physical-click failure. The
unchanged focused test failed twice in twelve runs. The instrumented failure kept
the same foreground window and editor geometry, but the editor lost focus and its
value remained unchanged. FlaUI's point-click overload positions the cursor and
immediately injects button input. The corrected test moves the pointer, drains
input, clicks, and drains input before asserting focus and insertion. It passes
the focused check and twelve comparison runs, retaining drag/replacement checks.
An intermediate TextPattern observation was rejected because the single-line native
editor does not expose that pattern; a focus-diversion experiment also failed.
Those diagnostic attempts and the original failures remain separately recorded in
`artifacts/test-quality-published-*.log` and are not passing evidence.

The Performance path exposed another fixture drift: its fixed row height was 30,
but stock Selectable now has a minimum height of 32. At the scrolled end, the
preceding row overlapped the viewport, producing three visible rows. The fixture
now explicitly sets its 30-pixel minimum and asserts every realized row has that
height. The original two-visible/six-realized bounds remain unchanged. The initial
failure and native diagnostics are preserved under `artifacts/test-quality-performance*`.

The subsequent performance corpus completed, but resize p95 was 16.9705 ms against
the existing 16.7 ms limit while the LSP suite was running. Full-lifetime UIA provider
peak was 22 against the older cap of 18; both measured corpora remained at 15 and
post-close count was zero. No leak is established by that trace. Follow-up
[#313](https://github.com/RichiCoder1/lucent/issues/313) owns isolated measurement and
explicit benchmark workload/resource boundaries. Existing timing and full-lifetime
limits remain unchanged; this batch does not claim a Performance suite pass.
One isolated follow-up passes the existing timing, virtualization, managed-growth
and idle checks: input p95/p99 11.7262/12.2005 ms and resize 16.4562/17.7174 ms.
Its remaining failure is the full-lifetime UIA peak 22 versus 18 (steady-state 15,
post-close zero). Other native resource bounds pass. Thus the earlier concurrent
resize result is not treated as an established regression. The isolated trace is
`artifacts/test-quality-performance-isolated-diagnostics.log`.
