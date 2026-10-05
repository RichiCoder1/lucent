# Unsaved and interactive native preview

Execution of [#232](https://github.com/RichiCoder1/lucent/issues/232), following
the [saved-file panel](native-preview-panel.md). The implementation slices
are recorded in the [roadmap](../ROADMAP.md#finish-232-in-reviewable-slices).
This record separates feasibility evidence from delivered interactive behavior.

## Accepted delivery scope

On October 4 the owner accepted saved-source interactive preview first. #232
now owns the retained host, supervised live session and editor interaction.
[#330](https://github.com/RichiCoder1/lucent/issues/330) separately preserves the
unsaved compiler-fidelity investigation and its original acceptance. It does
not block saved-source #232 or package delivery #233. No failed compiler adapter
is integrated; preview never saves a buffer on the user's behalf.

## Original-path compilation: staged stock inputs fail fidelity

The October 4 probe used SDK 10.0.401 and its Roslyn 5.9 compiler, with independent
analyzer and generator observations. A saved baseline compiled original C# and
`.lui` AdditionalText inputs beneath a nested `.editorconfig`. A second build
substituted frozen unsaved files using stock SDK Compile/AdditionalFiles items,
PathMap, Link, original logical metadata and a C# `#line` directive.

Both builds succeeded and executed mutually dependent generated code. Source
and configuration hashes were unchanged. The staged C# diagnostic mapped back
to the exact original source path. Those successes did **not** preserve the
compiler contract:

| Observation | Original input | Staged input |
| --- | --- | --- |
| Analyzer/generator `SyntaxTree.FilePath` | Original nested C# path | Temporary frozen C# path |
| C# nested analyzer configuration | `nested-csharp` | Missing |
| `AdditionalText.Path` | Original nested `.lui` path | Temporary frozen `.lui` path |
| `.lui` nested analyzer configuration | `nested-lui` | Missing |

This probe exercises real final Csc consumption and independent observers. Its
`.lui` observer is a purpose-built generator, not Lucent preparation/emission;
the failed path/configuration contract makes expansion to that next proof
unnecessary for rejecting this substitution approach. Mapped diagnostics alone
cannot establish original source identity.

Unsaved preview remains disabled. The separate #330 investigation needs a bounded compiler adapter
design that preserves authoritative SDK arguments and original-path content in
both preparation and final compilation. A broad `MSBuildWorkspace.Emit` path or
reflection into compiler internals is not an accepted replacement. Public API
feasibility, option fidelity and explicit rejection of unsupported features must
be reviewed before implementation. Saved-file preview remains supported.

Raw builds, independent observations, SARIF, binary logs, source hashes and the
probe are retained under the path in
`artifacts/preview232-overlay-locator.txt`. Initial sandbox permission failures
are preserved alongside the successful experiment.

## Retained renderer: allocation improvement demonstrated

An isolated Release probe at source `c6b6482e` used the existing owner-thread
headless host to compare a phase replica of one-shot capture with a retained
Skia bitmap, canvas and renderer. Forty-eight paired frames had identical PNG
hashes. Independent pixel samples changed with the scene's paint invalidations.

| Per-frame measurement | One-shot phase replica | Retained resources |
| --- | ---: | ---: |
| Paint managed allocation | 981,632 B | 2,536 B |
| Paint median | 1.7041 ms | 0.48735 ms |
| PNG encode mean managed allocation | 58,988 B | 58,988 B |
| PNG encode median | 12.3891 ms | 12.0411 ms |

The actual existing `CapturePngAsync` API was measured separately: twelve calls
averaged approximately 1,052,284 B of whole-process managed allocation and
14.26 ms. That includes queue/settling/projection work and must not be compared
as though it were the isolated paint-phase metric. These timings characterize
one fixture and machine; they establish no latency threshold or desktop proof.

The retained 960×540 RGBA surface used 2,073,600 pixel bytes. Twelve text blobs
occupied an estimated 42,624 bytes and stayed stable through paint invalidation.
The fixture contains no images, so it does not exercise image-cache churn.
Owner-thread pointer input invoked a stock button; committed text changed the
editor's text and attributable pixels. Disposal cleared native caches, later
capture was rejected, and application work was rejected after disposal.

No frames were captured during a three-second idle observation, but process CPU
was 750 ms. Low idle CPU is **not proven**. The deterministic test host does not
establish the production live clock or autonomous frame scheduling. Sampled
memory observations are not peak-memory or long-lived cache-bound proof.

This supports a small `PreviewRenderSession` and internal host in the existing
Preview.Hosting project. Keep Core application ownership and Skia disposal
boundaries; defer a generic Offscreen project or testing-facade migration.
PNG encoding remains the larger measured cost. Transport, browser decoding,
continuous pumping, timers, resize, image/text churn and cleanup under lost
connections still need their own evidence.

The full method, raw samples, images, independent hashes, source/binary inventory,
build failures and reproduction script are retained at the path in
`artifacts/preview232-renderer-locator.txt`.

## Live process ownership

Supervisor v2 distinguishes bounded operations from a live process lifetime.
The saved-file build/catalog/capture path continues to use bounded mode.
Supervisor-started confirms successful job ownership, not worker readiness.
The worker's matching readiness handshake separately admits a live session;
only final confirmed process-tree reaping permits replacement or deletion.

Live mode must not expire a healthy worker on the old capture timeout. Startup,
worker handshake, control failure and shutdown remain bounded. Stop and parent
EOF are independent of frame delivery, and output budgets remain enforced.
Unconfirmed cleanup retains quarantine. The editor adapter exposes started,
completion and Stop separately; its bounded adapter still waits for completion.

That process-boundary checkpoint preceded the integrated renderer, named-pipe
worker protocol, frame acknowledgments and input forwarding recorded below.
Unsaved compilation remains separate under #330.

The supervisor slice passes 13 native contracts and 11 JavaScript process
contracts; the complete editor suite passes 183. The warning-clean native build
and scoped formatting check pass. A direct JavaScript-to-compiled-supervisor
check verifies bounded argument delivery plus started/live/cooperative Stop and
confirmed reaping. The live worker remains alive beyond the bounded timeout
value. This takes no desktop focus. Evidence is retained under
`C:\Users\richa\AppData\Local\Temp\lucent-preview232-supervisor`, with the
integrated proof located by
`artifacts/preview232-supervisor-integration-location.txt`. Initial sandbox
configuration and child-process permission failures are retained; the authorized
run succeeds without changing user settings.

Adversarial review exposed an open protocol pipe after its writer had already
ended: a live supervisor could emit started and then close stdout without a
final reaping record, leaving the client waiting indefinitely. EOF now rejects
immediately as cleanup-uncertain; late process exit cannot manufacture reaping
proof. A regression first reproduced the failure. The process suite now passes
12 contracts, and the complete editor suite passes 189. The native cancellation
test also drains its owned process in a finally block while retaining the primary
assertion failure. Its warning-clean build and all 13 native contracts pass.
Scoped formatting passes. Logs are under
`artifacts/preview232-supervisor-{eof,review}-*.log` and the existing C: evidence
directory's `review-test-results`.

## Public compiler adapter: zero-overlay parity fails

A second disposable experiment used the actual SDK Csc task's `CscToolPath` and
`CscToolExe`, with shared compilation disabled. It captured the authoritative
argument vector, response-file bytes, working directory and compiler identity.
Delegating that invocation unchanged to stock Csc succeeded, including an output
assertion inside `TargetsTriggeredByCompilation`. Public parser/configuration
APIs also preserved original source paths and nested analyzer settings.

Final compilation still failed the prerequisite comparison with saved source:

- The ordinary SDK analyzer set already contains diagnostic suppressors. An
  independent warning-as-error sentinel succeeds with its suppressor under stock
  Csc. `GetAllDiagnosticsAsync` marks that warning suppressed, but a separate
  public `Emit` returns the same warning unsuppressed as an error and fails.
- The stock PDB records a generated document with SHA256; the public driver
  retains the generator's default SHA1. The stock override is internal. No
  generated-source rewriting was used to hide the difference.
- A separate missing dynamic-binder fixture produces CS0656 only during emit,
  confirming that successful analysis alone cannot gate final output.

The experiment stops before frozen-vector emission. No actual Lucent preparation,
emitter, graph/resource/cancellation or final-output parity claim follows from
the successful binding observations. Original source/configuration hashes and
membership are preserved. The harness completed its expected probes, but its
`ZeroParityPassed` result is false; this is failed feasibility, not a delivered
compiler adapter. The exact report and retained logs are located by
`artifacts/preview232-adapter-experiment.md` and the existing overlay locator.

Do not integrate the adapter, disable SDK analyzers or globally lower warning
severity. The recommendation is to continue saved-source interactive preview
while deciding whether unsaved support warrants a maintained compiler frontend.
The owner accepted that saved-source delivery sequence. #330 retains the compiler
investigation; the integrated live transport and editor proof follow below.

## Internal live host implementation

An internal `PreviewRenderSession` now owns a continuously pumped Core session
and retained Skia surface/renderer. It has one in-flight frame plus dirty state,
exact frame/input identities, autonomous async/timer/motion wakeups and explicit
input-loss cleanup. Existing one-shot `PreviewWorker` behavior is unchanged.

The initial warning-clean Hosting build and all 26 contracts passed, including 11 new live
contracts covering attributable input/timer pixels, acknowledgments, backpressure,
startup cancellation, queue overflow and sticky cleanup failure. Idle owner-turn
and frame counters remain unchanged over the sampled interval. Whole-process
CPU observations range from 0 to 15.62 ms over approximately 155–157 ms and are
diagnostics, not a general low-CPU guarantee. The implementation report and TRX
are under the renderer locator's `LIVE-IMPLEMENTATION-REPORT.md` and
`live-results-final` directories. These original measurements retain their source
and timing limitations after the review corrections below.

Adversarial review identified six focused gaps: shutdown callbacks lost their
application context, cancellation could arrive before setup yet still invoke
author code, failing tests could abandon owners, cleanup could authorize an old
frame against newly projected geometry, one synthetic key release could stale the
next, and the initial theme factory could receive Light for a dark request.

Corrections keep shutdown under the application context, fence startup before
author callbacks, own test cancellation/barriers/drain from launch, revoke input
independently of transport acknowledgments, reconcile synthetic releases with
bounded explicit failure, and bind the theme factory before setup. Independent
pre-fix failures demonstrated null cleanup context, cancelled setup invocation,
wrong factory appearance, activation of unseen geometry and the missing second
key release. The layout repro uses public composition operations; no Core
behavior was changed to satisfy a test.

After correction the warning-clean Hosting build and all 30 contracts pass
(15 live and 15 existing), with six-file pinned formatting and whitespace checks.
The renderer evidence directory contains `LIVE-REVIEW-CORRECTIONS.md`, source and
binary identities, retained intermediate fixture mistakes, and
`live-review-after-second-results/review-after-second.trx`. The scoped source
recheck resolves all six findings against the frozen hashes; it does not claim
new execution or transport verification. It is recorded separately in the
[review record](../../advisor-plans/reviews/2026-10-04-live-preview-host-review.md).

That host checkpoint preceded the live worker protocol and editor input
forwarding described below. Active-file discovery and unsaved compilation remain
separate work.

## Design review correction

The preview design review found that changing scenario or presentation after
Stop implicitly relaunched the worker. The existing controller regression now
first reproduces that defect, then verifies that settings remain editable while
stopped and are used only by an explicit restart. Hide/show also preserves Stop.
The full controller suite passes 11 contracts. Broader presentation improvements
belong to the native preview design handoff; they are not implied by this fix.

## Saved-source live integration

The October 5 implementation joins verified SDK artifacts to the retained host
through a current-user named pipe. The public worker entry accepts a live request
with bounded strict JSON metadata, matching session/build/presentation identities
and hash-checked PNG payloads. One image may await actual webview display; pending
state coalesces without collecting images. Parent Stop and process supervision
remain independent of that frame traffic. Reaping, author cleanup success and
readiness are separate outcomes; failed cleanup retains diagnostics, while
unknown process ownership blocks replacement and directory release.

The editor forwards bounded pointer, wheel, key and committed-text events.
Pointer releases retain their original Down owner through paint updates. Core's
capture-only release path cannot hit another element after the original owner
disappears. Key releases cannot activate a replacement focus owner. For a press
or wheel event racing hover paint, native admission accepts only the last
display-acknowledged frame with identical ordered input records, viewport and
scrollbar geometry. New geometry or targets reject it; key/text/move admission
stays tied to the current frame. Only acknowledged/current input metadata is
retained, and focus loss invalidates that compatibility. No old scene is restored.

Actual editor testing exposed two first-click failures: showing the Leave button
shifted the image, and hover could publish a frame before the following press.
Toolbar/caption layout now stays fixed across entry/exit. The external regression
first reproduced the missing counter activation, then passed after the bounded
compatibility correction, asserting the complete counter sequence is exactly
`[1]`. It also changes compiled `.lui` state through text input and proves
cooperative Stop with an unacknowledged final image. Its fixture identity values
are explicit transport-test values, not SDK build attestation; the actual editor
journey owns the verified-build integration proof.

Verification includes 239 editor contracts, 53 Hosting contracts, 17 Core input
contracts, a compiled Core API metadata preflight, warning-clean affected builds
and pinned C#/.lui formatting. The five initial integration review findings,
verifier-failure precedence, cleanup cancellation classification and the hover
regression retain their independent failing evidence. Current local evidence is
located by `artifacts/preview232-native-worker-evidence.md`,
`artifacts/preview232-live-integration-location.txt`,
`artifacts/preview232-live-editor-final.log` and
`artifacts/preview232-editor-walkthrough.md`.

Final review also corrected startup cancellation and key ownership. A Stop during
setup exits successfully only after clean author cleanup. An authored cleanup
`OperationCanceledException`, including one carrying the same cancellation token,
remains a failure. Current-frame and synthetic key releases both retain the
original focused owner rather than activating a replacement after Tab navigation.

The final VS Code development-host walkthrough verified text changing both the
field and compiled component state, Shift+Tab navigation and Space activation.
The corrected pointer candidate already proved first-click activation at 100%
and 50% zoom without shifting the canvas, wheel scrolling, Shift+Escape and F6
escape. A saved whitespace change in the isolated project made the old frame
read-only, rebuilt through the real SDK, and reset counter/text in the accepted
replacement. Stop removed the generation directory. The isolated editor was
closed and the Computer Use session ended. Retained build reports and verifier
logs distinguish this source-linked development proof from packaged delivery.
Code Review's final scoped source recheck found no remaining actionable issue;
it reused the independently executed, hashed reports rather than claiming a
second test run.

This delivery remains managed, in-tree development tooling. Native chrome, UIA,
IME/preedit and automatic synchronization with VS Code's Tab Moves Focus toggle
are not certified. The visible Leave control, local Shift+Escape handler and
host command release interaction synchronously; F6 remains host-owned. Component
association/default activation/authored variants belong to #327–329; package-only
consumer delivery belongs to #233; unsaved compiler fidelity remains #330.
