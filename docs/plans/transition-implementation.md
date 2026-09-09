# Transition implementation breakdown

Status: implementation authorized September 9, 2026, following #194–196 and Light Notes #8. Read [the accepted design](transitions.md); its owner choices were settled September 8. Use CSS and Motion as comparative references without implicitly expanding first delivery. Keep #140's experimental API clarification separate from production delivery.

Execution tickets, in dependency order:

| Work | Ticket | Status |
| --- | --- | --- |
| T1/T2: typed policies, targets and owned motion | [#198](https://github.com/RichiCoder1/lucent/issues/198) | Implemented; full Core 349/349 |
| T3: retained paint reuse | [#199](https://github.com/RichiCoder1/lucent/issues/199) | Geometry, real-Skia and 1/100/1,000-track characterization passing |
| T4: native/headless scheduling | [#200](https://github.com/RichiCoder1/lucent/issues/200) | Managed contracts and published physical checks 3/3 passing |
| T5: `.lui` grammar and tooling | [#201](https://github.com/RichiCoder1/lucent/issues/201) | Compiler/editor and packaged NativeAOT proof passing |
| T6/T7: stock consumers and delivery evidence | [#202](https://github.com/RichiCoder1/lucent/issues/202) | Issue Browser passing; independent Light Notes package adoption in progress |

## Coordination and sequence

The original design baseline was `2bd1c7f`; implementation starts from `4b5bdec` with the intervening fixes preserved. Closed prerequisites with evidence must not be reimplemented. The typed-property migration is already delivered. There is no pending Arrangement/SceneProperties/Fill/Foreground rename to build around.

| Related work | Coordination required |
| --- | --- |
| #124 / #131 | Use delivered session failure/cleanup and deterministic lazy-read/mount contracts. Fatal sampling must cancel preparation and proceed to cleanup; reporting cannot resume or indefinitely delay it. |
| #129 | Appearance generation and reduced-motion settings must propagate to all live themes/popups before sampled presentation. Preserve stock focus contrast. |
| #134 / #135 | Preserve gated condition subscriptions and Derived equal-value suppression; target tracking must not create an alternate reactive graph. |
| #128 / #133 | Share property-symbol discovery and source maps for the new style statement; coordinate compiler/editor grammar work. |
| #114 / #136 / #141 | Agree paint projection reuse/resource ownership and counters for layout, shaping and semantic refresh. Do not optimize against stale live-scene resources. |
| #140 | Own truthful experimental messaging and exact public API/dump migration. It must not build a partial manual-sample scheduler. |
| #121 / #130 | Prove motion respects current virtualization/focus and disabled/requested-versus-applied selection behavior; do not recreate already fixed bugs. |

Recommended dependency chain: T0 → T1 → T2; T3 and T4 build on T2 and share one frame-driver contract; T5 follows T1/T2; T6 follows T3/T4/T5; T7 follows T6. T3 is the largest technical risk: keep stock defaults off until its geometry/resource reuse evidence passes. Distinct tickets may proceed together only with explicit file ownership; shared Core projection/metadata changes need coordination, not simultaneous conflicting patches.

## T0 — accept the bounded contract and publish migration intent

Owner: design/API maintainer. Files: proposed ADR 0007, this plan, `docs/STOCK-PRESENTATION.md`, `docs/ROADMAP.md`, relevant public API baseline and issue records.

The owner decision table and ADR acceptance are complete; runtime execution remains separately authorized. Reconcile the Code Review task's current documentation-only corrections rather than overwriting them. State that current `StartTransition`, `Transition.For` and `AdvanceTransitions` are experimental held samples, not production animation. Audit every call site, test, guide and package consumer. Establish exact warning/removal release and update dump schema notes.

Acceptance: no delivered-support claim exceeds available scheduling/interpolation; immediate stock feedback remains unchanged. Preserve the current hold behavior only as an explicitly named experimental compatibility surface if necessary for a short documented prerelease interval; never silently change `StartTransition(property, value)` into a target write. Preferred migration removes obsolete sample entry points in the next coordinated prerelease and migrates tests to the new frame-driver seam. Do not add archived Avalonia compatibility.

Verification: documentation/link inspection and affected public surface inventory; no desktop tests for this documentation work.

## T1 — typed property eligibility and authoritative resolution

Owner: Core property/style module. Likely files: `PropertyTheme.cs`, `Style.cs`, `StyleFluency.cs`, `ElementPresentation.cs`, `Element.cs`, `LayoutScene.cs`.

Separate target resolution from presentation reads. Add motion policy nodes to immutable Style using the existing property identity and condition machinery. Supply typed opacity, Color and solid-Brush endpoint preparation and sample operations; separate value-pair interpolation support from property permission/invalidation impact. Preserve typed tokens, `StyleValue<T>`, Bind/BindValue and control provenance. Default custom/unknown property eligibility is off. Public custom interpolators are deferred.

Acceptance:

- A control target cannot be overwritten by a presentation sample under the approved authority rule.
- Value and policy resolution retain variant/source/ordinal ordering; nested inactive conditions do not evaluate their policies or values.
- Same-valued winners update provenance without restarting or causing downstream value execution.
- Color interpolation has specified midpoint, alpha and exact-endpoint results; valid incompatible brushes snap with a reason, while invalid endpoint values fail before publication.
- Inheritance separates target and presented reads and avoids a child re-tweening every ancestor sample. Mounting a delegating child mid-track joins its ancestor's sample without an entry track. Explicit None blocks delegation; policy removal returns to delegation or a local target as specified.

Verification: focused `Lucent.Core.Tests` presentation/style contracts, including custom-property negative cases, reused-style mount isolation, token identity switches, inherited color propagation and control precedence. Warning-clean Core build and authored formatting. No timing sleeps or UI tests needed.

## T2 — composition-owned tracks and deterministic frame seam

Owner: Core composition/motion module. Likely files: `Composition.cs`, `ElementPresentation.cs` or a new internal motion file, `Element.cs`, session integration and diagnostic dumps.

Implement the target-commit phase after bounded reactive/responsive settling. Use absolute monotonic timestamps, one prepared entry per mounted property and at most one track per element/property. Coalesce equal changes; retarget current sample to new target; release entries at exact completion. Add demand notification and inspection without public per-element timers or completion tasks.

Acceptance: exact assertions at 0%, midpoint and completion; A→B→C before commit yields one A→C track; reversal starts at the current sample; policy-only duration changes do not rewrite running tracks; None or declaration removal cancels; no acknowledged previous frame means no entry transition. Equal-target control takeover and suppression still cancel/repaint when needed, before the equal-target fast path. Long gaps complete in one sample, fractional time accumulates correctly and backwards time is rejected. Removed/rolled-back/hidden elements release registrations; reused keys cannot see old tracks. Reduced motion and appearance generations snap synchronously. Fatal failure clears demand and follows #124, retaining the original error and cleanup failures.

Verification: focused Core/application tests through frame-driver and composition seams; include target changes caused by nested responsive discovery without relaxing its eight-round/shared work budget. Test a queued wake after disposal and cancellation during terminal shutdown. Expose fake clock and deterministic counters rather than private mutable entry inspection.

## T3 — paint-only frame projection with valid ownership

Owner: Core projection + renderer integration. Likely files: `SceneLayout.cs`, retained-scene paint data, `InputProjectionTracker.cs`, renderer resource leases/caches; coordinate actual ownership with #114/#136.

Build the minimal reusable paint plan from a successfully installed layout. On sample-only change update draw nodes using current committed presentation values and cached layout/shaping; preserve hit/semantic snapshots. A structural/layout/input dirty flag forces the full existing path. Resource reset invalidates renderer-dependent reuse. Maintain source generation and owned paragraph/resource leases until frame consumption completes.

Acceptance: animating only Background, TextColor or Opacity executes zero additional measurement, paragraph shaping or semantic recomputation between target commits. Draw output changes and final values match. Simultaneous resize/DPI/text/participation/input changes invalidate reuse and preserve fresh input geometry; caret-only logic cannot reuse stale motion pixels. Removing an animated text subtree does not free a resource still owned by a live immutable scene. Renderer reset cannot reuse invalid native handles.

Verification: Core scene contracts and real-Skia renderer/headless tests; inspect operation counters, images and resource ownership. Measure representative baseline and active frames. Do not claim a performance improvement from paint tags alone. If this seam needs broad redesign, report a blocking design conflict and revise first-delivery scope before proceeding to stock motion.

## T4 — Windows and headless frame adapters

Owner: host/testing adapters. Likely files: `WindowsBootstrap.cs`, `WindowsFrameContract.cs`, `WindowsPopupHost.cs`, `WindowsLiveResize.cs`, `WindowsWorkDispatcher.cs`, `HeadlessApplication.cs` and headless options/context.

Share the absolute monotonic clock contract. Integrate demand with existing event waits and caret/menu deadlines; install wake handling before composition mount and close the subscribe/check/wait race. Active frames use cadence/vsync with a bounded future fallback deadline when presentation does not block. Minimize/nonrenderable state disarms demand and snaps finite tracks; restoration repaints once. Coalesce independently owned popup demand; no native-menu interpolation. Headless AdvanceAsync advances its one fake time source and samples once at the requested final time; DrainAsync settles current work without advancing time or running an animation to completion.

Acceptance: zero animation wakes and frames after settle, for both idle and minimized hosts. Starting a transition while the owner is waiting wakes it once; active presentation progresses without input events. Immediate-return presenter does not spin. Long advance versus intermediate advances produces the same mathematical sample in the absence of intervening target changes. Fractional advances are not lost. Native/live-resize reentrancy cannot double-commit time. Multiple surfaces release their registrations independently; final disposal leaves no demanded deadline.

Verification: deterministic Windows scheduler/transport contracts and `Lucent.Testing.Tests`. Then a focused published NativeAOT fixture proves real scheduled pixels, reduced motion, minimize/restore, close/failure and popup lifetime. Desktop tests require a coordinated input window; the owner has authorized them for this implementation batch. Headless results cannot substitute for native timed-wait/presentation proof.

## T5 — `.lui` transition statements and tooling

Owner: language/compiler/editor module. Likely files: language syntax/parser/formatter, `LuiCompiler.cs`, shared property discovery, language server, VS Code protocol tests and public C# style fluency.

Add `transition Property: motionExpression;` to named, parameterized and inline style bodies, including nested variants/reactive conditions. Lower directly to typed Style. Keep policy expressions construction-time with diagnostics against unsupported reactive snapshotting. None and typed duration constructors work in C# and `.lui`. No string names, alias table, style wrapper control or runtime parser.

Acceptance: compile every design fixture (with real surrounding consumer definitions); test symbol resolution, rename/completion, source map spans, property eligibility, policy type, malformed grammar, duration bounds, duplicate policy diagnostics, variant composition and formatter idempotence. A custom `Property<T>` is either explicitly supported by the shared descriptor or diagnosed; editor/compiler discovery cannot disagree. Generated methods retain NativeAOT reachability.

Verification: affected parser/compiler/generator/LSP tests and VS Code tests if client/protocol changes; SDK consumer proof for generated-runtime behavior. Measure affected completion/compilation if new metadata discovery changes cost. Builds/format/diff checks follow repository policy.

## T6 — stock controls and independent consumer integration

Owner: stock presentation + separate Light Notes owner. Files: stock style definitions, Issue Browser `.lui` only as needed for proving fixtures, Light Notes files in its own repository.

Enable approved stock hover policy, with immediate press/focus/disabled/invalid and clear applied selection. Exercise Issue Browser without app palette overrides or duplicate stock policy declarations. Preserve 10,000-item virtualization, filtering, density, splitter and wide/narrow identity. In Light Notes add only intended style motion; preserve editor sessions, drafts, selection, menu outlines and autosave. Update its immutable prerelease package/lockfile before calling framework behavior integrated.

Acceptance: immediate semantic/input snapshots under motion, disabled control blocks activation, rejected selection has no selected transition, focus ring is present on the first focused frame. Reduced motion/high contrast remain immediate. Only mounted realized rows allocate tracks. Theme changes never expose a mixed interim palette. Light Notes editor typing/save/close do not wait for animation.

Verification: managed stock/headless real-Skia consumer tests and representative frame/input measurements. Compare no-motion to approved defaults on the same build; set numeric budgets from that evidence. Only targeted native interaction/accessibility checks for paths affected by host changes. Keep broad release/Sandbox/manual IME checks for an actual release decision.

## T7 — completion evidence and public contract

Owner: release/API maintainer. Final docs describe supported implicit visual transitions precisely, with the exact `.lui` grammar, eligibility table, cancellation/retarget policy and experimental removals. Record influences in CREDITS before code adoption. No claim of layout/keyframe/entry support.

Close production execution only when T1–T6's applicable evidence exists: focused managed contracts, warning-clean affected builds, formatting/diff checks, compiled `.lui` consumer proof, published NativeAOT scheduled-frame/lifecycle proof and independently updated Light Notes evidence. Distinguish executed, deferred and waived checks. A design ticket can close with an accepted documented handoff; that does not close production implementation or #119.

## Cross-cutting acceptance matrix

| Risk | Smallest authoritative evidence | Escalation trigger |
| --- | --- | --- |
| Sampling/retarget/equality | Deterministic values and provenance through Core frame seam | Any result depends on read order or frame count instead of time |
| Mount/disposal/callback failure | Lifecycle tests with outstanding demand and injected failure | Native pending-work/fatal cleanup behavior differs |
| Idle spinning/lost wake | Scheduler counters with fake waiter and immediate presenter | Published host never advances or burns idle CPU |
| Paint versus geometry | Layout/shaping/UIA counters plus real draw output | Cache lifetime, text geometry or hit freshness changes |
| Color/contrast/reduced motion | Numeric color fixtures and stock light/dark/high-contrast frames | New compositing or platform preference transport |
| Authoring/AOT | Compiled `.lui` fixtures, exact maps and package consumer | New lowering/runtime metadata or changed public inventory |
| Consumer latency | Measured Issue Browser and Light Notes interactions | Missed frame/input budgets require scope or implementation changes |

The original proposal was documentation-only. Runtime execution is now authorized. The [public guide](../TRANSITIONS.md) describes the implemented contract; execution tickets record source-bound validation and remaining delivery work.
