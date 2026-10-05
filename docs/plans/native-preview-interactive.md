# Unsaved and interactive native preview

Execution of [#232](https://github.com/RichiCoder1/lucent/issues/232), following
the [saved-file panel](native-preview-panel.md). The four implementation slices
are recorded in the [roadmap](../ROADMAP.md#finish-232-in-reviewable-slices).
This record separates feasibility evidence from delivered interactive behavior.

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

Unsaved preview remains disabled. The next decision is a bounded compiler adapter
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

This process boundary alone does not implement a live renderer, named-pipe
worker protocol, frame acknowledgments, input forwarding or unsaved builds.
Those remain incomplete under #232.

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

## Design review correction

The preview design review found that changing scenario or presentation after
Stop implicitly relaunched the worker. The existing controller regression now
first reproduces that defect, then verifies that settings remain editable while
stopped and are used only by an explicit restart. Hide/show also preserves Stop.
The full controller suite passes 11 contracts. Broader presentation improvements
belong to the native preview design handoff; they are not implied by this fix.
