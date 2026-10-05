# Current work and handoff

Updated October 4, 2026. The owner resumed native preview and requested roadmap
reconciliation, with completed chunks committed and pushed as needed.
GitHub acceptance and the [roadmap](../ROADMAP.md) govern scope. Recheck live source,
CI and issue state before continuing from this snapshot.

## Resume here

1. [CI117](https://github.com/RichiCoder1/lucent/actions/runs/37254418694)
   passed managed, package verification and publication for source `c6b6482e`,
   publishing `0.3.0-dev.117.1`. This commit only
   corrects the diagnostic-navigation test's Windows canonical-path expectation.
   CI116 failed that assertion in both lanes and skipped publication; the
   faithfully reproduced short-path failure and the corrected 176/176 editor
   run are recorded under `artifacts/preview116-alias-location.json`. Keep that
   local evidence separate from the subsequently successful CI publication.
2. Continue [#232](https://github.com/RichiCoder1/lucent/issues/232). Source
   `c6b6482e` precedes unsaved/interactive support. The
   [interactive execution record](../plans/native-preview-interactive.md) records
   the failed original-path stock Csc substitution proof, successful retained
   paint-resource measurement, and supervisor v2 native/editor verification.
   The public compiler-adapter experiment also fails zero-overlay stock parity:
   suppressions are lost at public Emit and generated source checksums differ.
   Unsaved preview remains disabled; the owner has been asked whether to deliver
   saved-source interaction first or invest in a maintained compiler frontend.
   The internal live renderer passes 30 focused contracts after six adversarial
   review corrections and a scoped source recheck. Integrate its transport next;
   it does not yet provide a live worker pipe or editor input. Supervisor/panel
   refinements through `ef8a13a7` are pushed; CI118 managed is green and package
   verification remains pending. Design/roadmap changes are in `8b4f5770`.
   Use the four slices in the
   [roadmap](../ROADMAP.md#finish-232-in-reviewable-slices): original-path SDK
   overlays; a persistent preview host/renderer proof; supervised live-session
   integration; then current-generation input and actual-editor acceptance.
   The first two can proceed independently. Both preparation and final compilation
   must consume the same unsaved contents without changing source/configuration
   identity or saving buffers. A path-mapping workaround is not that proof.
   If no supported compiler hook preserves the contract, record the result and
   settle the compiler-adapter design or scope before expanding implementation.
   The source strategy decision does not block independent saved-source host
   correctness and cleanup work. Do not silently narrow #232's acceptance.
3. Keep live readiness and cleanup separate: a verified build/vector plus the
   started/ready records permit live frames; confirmed tree reaping permits
   replacement and directory deletion. Stop must survive frame backpressure,
   and owned input release/cancel must survive a newer displayed frame. Measure
   the small persistent preview before extracting a general offscreen host.
   `artifacts/preview232-preparation.md` is an earlier read-only proposal; its
   generic Offscreen project and staged-source substitution are not proven or
   accepted prerequisites. Scenario updates still rebuild/restart and reset state.
   The proposed bounded/live supervisor modes fit this split: build/catalog keep
   operation deadlines; live mode needs a bounded worker handshake and shutdown,
   without periodically expiring a healthy session. Supervisor-started is emitted
   only after job ownership; worker-ready arrives separately through its channel.
4. Complete package/consumer delivery [#233](https://github.com/RichiCoder1/lucent/issues/233)
   after #232, then diagnostics #243 and transfer #244. The
   [roadmap sequence](../ROADMAP.md#remaining-road-to-10) records actual child
   prerequisites and Notes #10 adoption. Hot Reload #234 and WASM #238 are
   separate feasibility investigations after #233; neither implementation nor
   inclusion in 1.0 is implied by a ready-for-agent label. No new dates or broad
   verification gates are introduced.

The Design and UI handoff is imported at
[`docs/design/native-lui-preview`](../design/native-lui-preview/README.md). Its
component-first contract supersedes catalog-first onboarding: follow the active
`.lui`, allow Pin, automatically activate eligible components, and use optional
authored preview data/variants for required inputs. The executable catalog stays
internal/advanced. Proposed `preview ... for ...` syntax is not implemented.
The HTML and captured images are design references, not compiled runtime proof.
File association is #327; typed activation is #328; proposed development-only
grammar/profile work is #329, blocked by #328 and labeled needs-triage. They are
linked under #224 and added to Project 4. #233's native dependency edges include
all three as well as #232. The compiler source-strategy decision remains pending;
none of these tickets claims unsaved fidelity is solved.

## Roadmap reconciliation

Code Review and Astra reviewed the remaining sequence on October 4. The changes
are applied and read back from GitHub:

- #223 now orders preview → diagnostics → transfer, preserves completed
  foundations as history and keeps Hot Reload/WASM conditional. Absent local
  plan links point to the retained parent designs and research instead.
- #224 checks off closed #227–231, leaving #232/#233 open. #232 now carries its
  four implementation slices without duplicate acceptance tickets.
- #255's description and native blocked-by edges now name #232, #253 and #254.
  Inspection reuses the live session without adding a reverse dependency.
- Scenario/orchestration prose now assigns mapped diagnostics to #231 and runtime
  inspection/source origins to #243/#251–255.

Before release, #223 needs a bounded owner decision on API/language/tool and
schema compatibility, the supported globalization/accessibility/IME matrix,
distribution, and whether Hot Reload/WASM belongs in 1.0. The recommendation is
to keep those two accelerators/targets optional for the native release. This is
not a prerequisite for continuing the accepted preview work.

The onboarding fixture is located by `artifacts/onboarding249-current.txt`.
At the prior walkthrough checkpoint the isolated editor/test apps were closed
and Computer Use had ended; recheck live sessions before resuming. UI/focus
checks remain authorized. The owner manually trusted only the generated app
folder. No account sign-in or security setting was automated.

## Delivered baseline

| Work | Current boundary and evidence owner |
| --- | --- |
| Preview feasibility #227 | Closed; [report](../../tests/Probes/Preview/NativeFeasibility/REPORT.md) proves three fresh workers, real Skia text/SVG/icon output and rejected failed builds on official 108.1; its 3.016-second observation is not continuous-preview performance evidence |
| Design mode/scenarios/orchestration/panel #228–231 | Closed; [design mode](../plans/design-mode-execution.md), [scenarios](../plans/native-preview-scenarios.md), [orchestration](../plans/native-preview-orchestration.md) and [panel](../plans/native-preview-panel.md) own source-bound contracts and actual-editor evidence; #231 source is `77f1c69d`, published with the test correction in official 117.1 |
| Review corrections #320–326 and Notes #11/#12 | Delivered and closed; [review execution](../plans/comprehensive-review-execution.md) retains exact-source evidence and measured deferrals |
| Navigation restoration #221 and activation #222 / #205 | Delivered; [restoration/activation plan](../plans/navigation-restoration-activation.md) and [journal design](../plans/navigation-journal-implementation.md) describe the contracts |
| Release identity #245 | Closed after authenticated CI101 publication; [release contract](../RELEASE-SETS.md) owns exact identities |
| Templates #246 | Closed after official 107.1 package-only generated consumers and NativeAOT verification |
| Editor lifecycle #247 | Closed after authenticated CI107 delivery and reviewed cache/import/acquisition proof; [lifecycle plan](../plans/editor-tooling-lifecycle.md) owns behavior |
| Environment doctor #248 | Closed and Project Done after CI108 publication; [doctor plan](../plans/environment-doctor.md) owns supported capabilities |
| Fresh-consumer onboarding #249 / #242 | Verified authenticated/local-package fallback, mapped debugging, managed/native interaction and final official 110.1 installed-editor semantics; [execution](../plans/onboarding-execution.md) distinguishes artifact versions |
| Performance #313–319 | Delivered through 89.1, including physical resize proof; [performance execution](../plans/performance-execution.md) preserves characterization and failures separately |

The latest confirmed Lucent release is `0.3.0-dev.117.1`, source `c6b6482e`.
[CI117](https://github.com/RichiCoder1/lucent/actions/runs/37254418694) passed all jobs.
CI113–116 did not produce a newer release. CI116 at `77f1c69d` failed the same
canonical-versus-8.3-path assertion in both lanes, with publication skipped.
The test-only correction `c6b6482e` has a passing local short-path regression
and editor suite; CI117 subsequently verified package delivery and published.
The earlier startup-watchdog and source-lock corrections remain in
#230's evidence.
The preceding 110.1 complete artifact and descriptor were independently
authenticated for onboarding:

- Artifact SHA-256: `692cdd9dab5bd131b9fb817ba7b99827e7fe4f912ec4d6812fd249d092c2ee7a`.
- Descriptor SHA-256: `fba4138c6f0ef3b5c5906aa607166c9a9b2c0b8e2140fbc55ebb7b31e7fdc952`.
- Retained location: `artifacts/ci110-catalog-location.txt`.

Light Notes main `8d5cdaa` consumes official `0.3.0-dev.101.1`. All six locked
restores, 102 managed tests, formatting and NativeAOT publication pass;
[Notes CI](https://github.com/RichiCoder1/light-notes/actions/runs/36776465450) is green.
The unchanged 520-pixel regression and actual 480-by-520 native editor check pass;
typed content survives reopening the isolated database. The consumed Lucent
packages match official-feed bytes. Evidence: `artifacts/notes101-adoption`.
No pending Notes package-adoption work remains from that batch. Future transfer
adoption is tracked separately in [Notes #10](https://github.com/RichiCoder1/light-notes/issues/10).

## Evidence to retain

Consult these only when revisiting their behavior; no broad rerun is required for
unrelated changes:

- Navigation: `artifacts/issue221-package101-replay` contains the exact official
  package-only browser and two-process journal replay. Registered activation is
  under `artifacts/windows-activation-sandbox/64eb810555e14a84966c2ee4e3ff117c`
  and `artifacts/issue222-unpackaged`; actual host behavior is under
  `artifacts/issue222-visible`. Signing/registration occurred in disposable guests,
  not on the owner's host. Foreground denial is separate from an attention request.
- Presentation: `artifacts/review323-native-final/walkthrough.md`,
  `artifacts/review323-layout-final` and #323 retain browser observations. The
  shared Select gutter correction is published in 107.1. These are representative
  interactions, not complete IME or screen-reader certification.
- Input: Computer Use Down/Right arrived as keypad 2/6. Physical main-arrow
  delivery is unverified. Standard Num Lock-off keypad navigation passes managed
  and NativeAOT contracts. Preserve `artifacts/issue245` diagnostic observations.
- Editor and doctor: `artifacts/ci107-catalog-location.txt`,
  `artifacts/issue247-producer-6270d757-v6` and
  `artifacts/doctor-vsix-current.txt` locate reviewed development and authenticated
  release evidence. Local candidates are not relabeled as published releases.
- Failed CI95–106 runs, failed local producers, interrupted Computer Use attempts
  and declined substitutions remain evidence. Their diagnostic history and exact
  paths are preserved in the [pre-consolidation handoff](https://github.com/RichiCoder1/lucent/blob/3205cbbd521f8171ffe751b924166d4826076b0d/docs/agents/remaining-work-handoff.md).
  Historical pending statements there are superseded by this current summary.

## Working constraints

- Subagent coding uses Sol 6.1 (`gpt-6.1-sol`). Astra 6 (`gpt-6-astra`, `xhigh`)
  may advise or adversarially review; Sol implements its findings. This applies
  to nested delegation.
- After a physical Computer Use interruption, ask to resume while continuing
  independent work. At the end of a walkthrough, close owned windows and reset
  its Node session; the helper currently has no explicit stop method.
- Lucent is at `D:/src/richicoder1/lucent`; Light Notes is at
  `D:/src/richicoder1/light-notes`. Preserve unrelated `.codex/`, `.dotnet-home/`,
  `%SystemDrive%/`, `advisor-plans/`, `docs/plans/windows-sandbox-testing.md`,
  `docs/research/`, the performance verifier edit and Notes' pre-existing changes.
  Stage exact owned files.
- Serialize shared-tree builds and coordinate commits. Fresh MTP tests must run
  a nonzero expected case count. Follow [verification policy](verification.md)
  and [test scope](../TESTING.md); measurements on an active machine are
  characterization, not native frame-budget or manual interaction proof.
- At the prior checkpoint D: had limited space; recheck capacity before large
  builds. The owner approved removal of only the failed repository
  `.nuget/packages` cache, reclaiming 710 MB. That cleanup is complete. Keep the
  normal C: package cache and failed logs; place substantial new artifacts on C:.
- If D: disappears, inspect `C:/DevDrive/Dev.vhdx` attachment before changing
  anything. Do not format, repartition or relax ACLs.
