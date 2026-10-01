# Current work and handoff

Updated September 30, 2026. The owner authorized the remaining review corrections,
then the broader roadmap, with completed chunks committed and pushed as needed.
GitHub acceptance and the [roadmap](../ROADMAP.md) govern scope. Recheck live source,
CI and issue state before continuing from this snapshot.

## Resume here

1. Implement trusted build and worker orchestration
   [#230](https://github.com/RichiCoder1/lucent/issues/230). Explicit owned scenarios
   #229 pass 18 preview and 51 headless tests, plus graph/output dependency checks.
   The [implementation record](../plans/native-preview-scenarios.md) documents
   the development-only catalog and builder/culture integration. Serialize shared
   checkout builds and keep new outputs on C: while D: is constrained. The prior composition-scoped
   `Design.IsDesignMode` #228 passes runtime, compiler/editor and SDK/NativeAOT
   checks, with no remaining adversarial finding. Its
   [execution record](../plans/design-mode-execution.md) retains exact candidate
   evidence and review corrections. No new owner decision is pending.
2. Native preview [#227](https://github.com/RichiCoder1/lucent/issues/227) feasibility
   passed on official 108.1: three fresh workers, attributable real Skia text/SVG/icon
   output, and failed builds rejected before launch. The retained
   [report](../../tests/Probes/Preview/NativeFeasibility/REPORT.md) records a 3.016-second
   edit-to-frame observation and bounded allocation/memory/idle measurements. It
   supports the existing headless seam, not continuous-preview performance claims.
3. Onboarding [#249](https://github.com/RichiCoder1/lucent/issues/249) is verified.
   The [execution record](../plans/onboarding-execution.md) distinguishes the fresh
   107.1 app/debug/native proof from the final coherent official 110.1 editor
   completion, hover and unsaved-diagnostic recovery. CI110 passed all jobs;
   CI109 was superseded, not accepted. Continue [preview #224](https://github.com/RichiCoder1/lucent/issues/224),
   then diagnostics #243 and transfer #244 according to their technical dependencies.

The onboarding fixture is located by `artifacts/onboarding249-current.txt`.
The isolated editor and test apps are closed; Computer Use has ended. UI/focus
checks remain authorized. The owner manually trusted only the generated app
folder. No account sign-in or security setting was automated.

## Delivered baseline

| Work | Current boundary and evidence owner |
| --- | --- |
| Review corrections #320–326 and Notes #11/#12 | Delivered and closed; [review execution](../plans/comprehensive-review-execution.md) retains exact-source evidence and measured deferrals |
| Navigation restoration #221 and activation #222 / #205 | Delivered; [restoration/activation plan](../plans/navigation-restoration-activation.md) and [journal design](../plans/navigation-journal-implementation.md) describe the contracts |
| Release identity #245 | Closed after authenticated CI101 publication; [release contract](../RELEASE-SETS.md) owns exact identities |
| Templates #246 | Closed after official 107.1 package-only generated consumers and NativeAOT verification |
| Editor lifecycle #247 | Closed after authenticated CI107 delivery and reviewed cache/import/acquisition proof; [lifecycle plan](../plans/editor-tooling-lifecycle.md) owns behavior |
| Environment doctor #248 | Closed and Project Done after CI108 publication; [doctor plan](../plans/environment-doctor.md) owns supported capabilities |
| Fresh-consumer onboarding #249 / #242 | Verified authenticated/local-package fallback, mapped debugging, managed/native interaction and final official 110.1 installed-editor semantics; [execution](../plans/onboarding-execution.md) distinguishes artifact versions |
| Performance #313–319 | Delivered through 89.1, including physical resize proof; [performance execution](../plans/performance-execution.md) preserves characterization and failures separately |

The latest confirmed Lucent release is `0.3.0-dev.112.1`, source `d834ac78`.
[CI112](https://github.com/RichiCoder1/lucent/actions/runs/36808337607) passed all jobs.
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
No pending Notes package-adoption work remains in this batch.

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
- D: has limited space. The owner approved removal of only the failed repository
  `.nuget/packages` cache, reclaiming 710 MB. That cleanup is complete. Keep the
  normal C: package cache and failed logs; place substantial new artifacts on C:.
- If D: disappears, inspect `C:/DevDrive/Dev.vhdx` attachment before changing
  anything. Do not format, repartition or relax ACLs.
