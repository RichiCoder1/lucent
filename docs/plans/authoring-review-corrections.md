# Authoring review corrections

Status: implementation in progress under [#310](https://github.com/RichiCoder1/lucent/issues/310),
authorized September 21, 2026. This corrects the adversarial
review of `247c87e`; the earlier A0–A7 delivery and its recorded evidence remain historical.

## Execution order

1. **R1/R2 — transaction and cleanup safety.** Reserve reactive replacement through
   the navigation session; defer competing navigation until candidate rollback and
   revalidate before publication. Own candidates before nested staging can fail.
   Regressions cover resolver/setup navigation, pending guards, cancellation/veto,
   stale selection and same-update parent replacement plus child failure.
2. **R3/R7 — one routing model.** Consolidate on typed descriptor factories and
   Router/RouterOutlet placement. Migrate current explicit consumers and their
   behavioral tests, including package probes and Light Notes where affected.
   Remove redundant public entry points instead of silently ignoring options/factories.
   Preserve general typed Context.Provide and existing navigation capabilities.
3. **R4/R5/R8 — editor and lint parity.** Keep exact token maps inside named methods;
   prepare named dependency projects from current editor sources in dependency order;
   enforce shared configured lint in the named build pipeline. Verify public editor
   operations and package-backed diagnostics, not only lowered source snapshots.
4. **R6 — companion diagnostics.** Reject unsupported state accessor/required shapes
   with actionable authored diagnostics consistent with standalone state generation.

## Verification and delivery

First reproduce each reported defect against the current source, then preserve the
failure as an affected contract test. Run focused checks during changes; finish with
affected suites, formatting, architecture, package/NativeAOT consumers and required
desktop checks. Keep candidate boundaries explicit. Update the authoring/navigation
guides, roadmap, handoff and tracker, then commit/push and verify publication.
Measure performance leads before treating them as regressions; the additional Fable
design leads are not part of these eight accepted correctness/diagnostic corrections.

## Routing checkpoint

R1/R2/R3/R7 source corrections pass all 674 Core contracts. Eighteen new cases cover
callback navigation, cancellation/veto, initial mounting, retirement reentrancy,
nested cleanup, lazy selection freshness and failure recovery. The independent
source re-review found two additional cases: deferred navigation starting during
exception unwinding and mixed parent/child selection hidden by a derived reread.
Both were reproduced and corrected; the reviewer found no further concrete blocker
in that focused source follow-up. That review did not run tests or certify packages.

Routing uses one typed descriptor representation and Router/RouterOutlet placement.
Current callers and their behavioral tests migrate; arbitrary typed Context.Provide
and direct typed route-context wrapping remain available. Ordinary failed renders
remain retryable after rollback. Failure with a competing queued navigation aborts
the intent before the reserved stage can be reused.

Evidence: `artifacts/authoring-review-routing-red.log`,
`artifacts/authoring-review-routing-second-red.log` and
`artifacts/authoring-review-core-final.log`. Editor/build corrections and independent
package/application verification are still in progress; no new package is claimed here.

## Integrated source verification — September 22

R4/R5 exact maps and current dependency preparation, R8 configured lint, and R6
companion diagnostics are implemented. Compiler **146/146**, Generator **52/52**,
LanguageServer **45/45**, and client **15/15** tests pass. Repository formatting
passes 595 C# and 115 LUI files. The VS Code client package is version 0.3.5.

A bounded independent source follow-up caught four further cases, now corrected:
same-arity overload identity, client synchronization of unsaved configuration,
configuration supplied only through additional files for linked LUI, and a structural
lowering failure incorrectly hidden by diagnostic suppression. Focused regressions
exercise public editor operations, configuration open/change/close and failed preparation.
The reviewer found no remaining concrete blocker in the corrected paths; that review
was source-only. Package/application evidence and publication are still pending.

The September 21 restart checkpoint is superseded. Complete the remaining downstream,
package, application and publication checks before closing #310; preserve the earlier
source and package evidence boundaries.
