# September 9 review remediation

The fresh Fable review examined Lucent `bfecfc83643ffad1a0f1f49618387a7bbbba1ab3` and Light Notes `d2d351b3a41c76b3a76f3cd9c2dec539f054fb91`. Its source findings are leads, not executed test results. Confirmed framework fixes are delivered in `905bd1cc5354b7ef4e181ad4bc82dfc10c4345e1`, published as `0.3.0-dev.53.1` after [CI 53](https://github.com/RichiCoder1/lucent/actions/runs/34356784239) passed. This document records the implementation decisions; GitHub Issues and Project 4 own delivery status. The working review and large reproduction logs remain ignored artifacts.

## Confirmed work

| Review area | Execution |
| --- | --- |
| Value resolution allocation, preserving losing/inherited reactive reads and diagnostic provenance | [#172](https://github.com/RichiCoder1/lucent/issues/172) |
| Collapsed scroll extent and responsive scrollbar gutter | [#173](https://github.com/RichiCoder1/lucent/issues/173) |
| Availability before final projection, retaining router freshness rejection | [#174](https://github.com/RichiCoder1/lucent/issues/174) |
| Accent focus contrast and minimal high-contrast focus | [#175](https://github.com/RichiCoder1/lucent/issues/175), [#176](https://github.com/RichiCoder1/lucent/issues/176) |
| Last image lease re-admission and full-queue retry | [#177](https://github.com/RichiCoder1/lucent/issues/177), [#178](https://github.com/RichiCoder1/lucent/issues/178) |
| Derived/readonly increment diagnostics and authored `owner` names | [#179](https://github.com/RichiCoder1/lucent/issues/179) |
| Structural hover and distinct sibling component overloads | [#180](https://github.com/RichiCoder1/lucent/issues/180), [#181](https://github.com/RichiCoder1/lucent/issues/181) |
| Concurrent session posts, exception-safe element disposal, nested ownership guards | [#182](https://github.com/RichiCoder1/lucent/issues/182), [#183](https://github.com/RichiCoder1/lucent/issues/183), [#184](https://github.com/RichiCoder1/lucent/issues/184) |
| Executable Issue Browser retention journey, scene lifetimes, desktop waiting, fractional geometry and same-assembly namespace guard | [#185](https://github.com/RichiCoder1/lucent/issues/185) |
| Exact-source text geometry and combined clamp/focus/caret convergence | [#186](https://github.com/RichiCoder1/lucent/issues/186) |
| Duplicate C# warnings and physical debugger source paths | [#187](https://github.com/RichiCoder1/lucent/issues/187), [#188](https://github.com/RichiCoder1/lucent/issues/188) |
| Immutable compiler fingerprints and per-document semantic diagnostics | [#189](https://github.com/RichiCoder1/lucent/issues/189) |
| Keyboard dismissal of disabled menus and wheel routing through disabled content | [#190](https://github.com/RichiCoder1/lucent/issues/190), [#191](https://github.com/RichiCoder1/lucent/issues/191) |
| Candidate-caret bounds through clipped ancestors | [#192](https://github.com/RichiCoder1/lucent/issues/192) |
| Deferred shutdown completion after a failed wake observer | [#193](https://github.com/RichiCoder1/lucent/issues/193) |
| Source-build asset tool exclusion and current runtime inventory | [#197](https://github.com/RichiCoder1/lucent/issues/197) |
| Declined close recovery, visible capture errors, collection continuity | [Light Notes #5](https://github.com/RichiCoder1/light-notes/issues/5), [#6](https://github.com/RichiCoder1/light-notes/issues/6), [#7](https://github.com/RichiCoder1/light-notes/issues/7) |
| Windowed startup, failure reporting, pin verification, reactive dirty state and capture/back keys | [Light Notes #9](https://github.com/RichiCoder1/light-notes/issues/9) |

The deferred-shutdown regression uses a real application owner pump and controlled observer failure; it verifies natural completion without a rescue wake and retains aggregated failure reporting. The integrated managed run covers 655 passing contracts, with a warning-clean solution build and positive/negative architecture checks. The SDK matrix passes its NativeAOT consumer, including explicit live and snapshot retained values. Nine published desktop checks pass, alongside editor-session continuity and asynchronous lifecycle smoke paths. The clean source publication passes its exact runtime inventory, six negative cases and copied-app startup/resize/focus/close checks. CI independently passed managed, NativeAOT and package-only consumers and published the immutable package set. Issue comments retain source/artifact identities and final independent-consumer results.

Light Notes independently consumes 53.1 at `4c3b47ccdad8fcd847b47b620b01003fdfe7ade7`; [its final CI](https://github.com/RichiCoder1/light-notes/actions/runs/34379115230) passed managed tests, desktop-test compilation and NativeAOT publication. Local storage 22/22 and app 40/40 tests passed. The app/fix commit `cd3f865` passed its full published desktop suite 7/7 after the test driver adopted per-monitor-v2 awareness for physical mouse and capture coordinates. The final SDK-only follow-up pins SDK 10.0.400 exactly: the first CI attempt exposed implicit linker dependency drift under `latestPatch`, and locked restore correctly rejected it. The configuration-only follow-up passed publication; a second desktop pass on that follow-up binary is not claimed. The [projection record](../SCENE-PROJECTION-EVIDENCE.md#review-fix-package-integration) contains the independent consumer measurements.

## Corrections to review claims

- Input signatures are correctness guards used for freshness rejection. They are not dump-only overhead and remain intact. Removing correction passes, signatures or inherited reads needs a separate measured invalidation design.
- The malformed-SVG probe exercised 27 inputs against the pinned parser: 22 prepared and five returned the expected image-load failure. No escaping parser exception was reproduced. This does not certify complete SVG conformance; no speculative broad exception handler was added.
- The SDK warning fixture reproduced duplicate `LUI2000`/`CS8602`, but `NoWarn=CS8602` already suppressed both. Original-ID suppression is not claimed broken.
- Portable PDB inspection found separate exact sequence points for successive authored statements. The defect was the physical file path, not lost statement granularity.
- Unqualified self-recursion is legal and can be intentional. A blanket prohibition would break recursive components. Distinct-overload indexing is fixed independently.
- The earlier scene evidence attributed every responsive retry to a remount. The consumers retain their responsive elements; that attribution is withdrawn. The availability fixture proves one specific source of retries, without retroactively claiming a trace of historical runs.
- The 20,000-unit editor workload is a tested target, not an enforced length limit. Scrollbar `Auto` reserves a stable gutter. Documentation now states both explicitly.

## Follow-up boundaries

These items require a concrete reproducer or a deliberate API/behavior decision before implementation. They are not prerequisites for publishing the confirmed fixes:

The next investigations are explicitly tracked as [authoring ergonomics #194](https://github.com/RichiCoder1/lucent/issues/194), [native interaction boundaries #195](https://github.com/RichiCoder1/lucent/issues/195), and [image parity/cache behavior #196](https://github.com/RichiCoder1/lucent/issues/196). Their open status is intentional; no unexecuted reproduction is recorded as a pass.

- Retained `.lui` item/pattern-local snapshot diagnostics, member recovery diagnostics, generated-name completion filtering and state-inference hints. Preserve intentional snapshots and current-item callback semantics.
- Pointer capture across structural reorder, multi-button drags, touch/pen translation, popup focus/IME transport and shutdown/UIA ordering. Preserve existing capture cancellation and owner-thread policies until focused native sequences establish a defect.
- JPEG/PNG orientation metadata parity, SVG build/runtime admission parity and large-image render-cache churn. Compare the exact pinned decoders and maintained limits; do not bypass image budgets or downgrade font identity checks.
- Explicit mount-factory drain behavior, disposed-source dependents and counter overflow. These are ownership/contracts work, not justification for silently changing scheduling or swallowing lifetime errors.
- Static-style lowering, additional inherited-value caching, shape-cache policy and optional icon preloading. Measure a representative consumer first; retain reactive dependencies and startup/error behavior.
- Broader package architecture policies should specify each package's legal dependency direction. Applying Core's native-dependency restrictions to renderer/testing packages would be incorrect.
- Focus intent across responsive hiding or virtualized eviction, retained conditional shorthand and other authoring conveniences belong with an explicit component/authoring design. Unmount still releases component state by default.

The component-family organization and `.lui` ErrorNotice seam are already delivered by #143. A package split or wholesale conversion of Core components is not required by this remediation. The separately planned Component Gaps work (#155–#171) and transitions (#142) keep their own scope. The owner subsequently supplied a physical 150% monitor for #153; its two opt-in desktop tests passed 100% to 150% and return transitions for popup/input and multiline UIA geometry. The issue records the exact artifacts and excludes real-language IME candidate-window certification.

## Verification and delivery

Use focused red/green regressions while implementing, followed by affected managed suites, warning-clean builds, formatting and architecture negative/positive checks. Run the SDK consumer for compiler/source-map changes and published desktop input, resize, menu, accessibility and startup/close paths for affected native behavior. Independently pin Light Notes to the resulting immutable packages before its app checks.

Allocation and tooling measurements are diagnostic evidence with source/binary identity and stated boundaries, not new timing gates or claimed native frame rates. Broad manual IME/accessibility certification is not implied by automated checks, and synthetic scale tests alone do not establish physical mixed-DPI behavior. Final issue comments record actual runs, package versions, commits and remaining limitations.
