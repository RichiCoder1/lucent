# Comprehensive review execution

The owner authorized the remaining review corrections followed by the broader
roadmap on September 29, 2026. The performance batch #313–319 is complete and
its measurements remain separate. This work follows the consolidated September
24 advisory review, revalidating each finding against current source.

## Order and boundaries

1. Light Notes data integrity: keep editor content and revision coherent,
   reconcile accepted writes by lineage, and retain every actionable failure.
2. Navigation lifecycle: invalidate preparation on root detach and notify idle
   after rejected superseding operations without premature publication.
3. Authoring reliability: incomplete method/setup recovery, consistent setup
   ownership, expression/default handling, protocol positions, diagnostics and
   highlighting. Preserve documented language semantics.
4. Asset and accessibility hardening: rendered SVG transform admission and
   exact UIA text ranges.
5. Notes command/maintenance behavior, then stock presentation and consumer
   improvements. Integrate published packages with explicit consumer evidence.
6. Measure the review's repeated-work opportunities; retain only demonstrated,
   maintainable changes. Static observations do not establish a speedup.
7. Continue the existing roadmap: restoration/activation (#205), onboarding
   (#242), native preview (#224), diagnostics (#243), and transfer (#244), with
   each issue's dependencies and feasibility decisions respected. Managed hot
   reload and browser preview remain subject to their prerequisite probes.

No existing local advisory, research, or machine-diagnosis draft is swept into
implementation commits. Test data is synthetic and stored separately from the
owner's Notes database. Focused tests own each regression; package/native checks
are selected where the changed behavior reaches those boundaries.

## Progress

- Tracking: Notes [#11](https://github.com/RichiCoder1/light-notes/issues/11)
  owns C1–C3 and [#12](https://github.com/RichiCoder1/light-notes/issues/12) C11.
  Lucent [#320](https://github.com/RichiCoder1/lucent/issues/320) owns navigation,
  [#321](https://github.com/RichiCoder1/lucent/issues/321) authoring,
  [#322](https://github.com/RichiCoder1/lucent/issues/322) SVG/UIA,
  [#323](https://github.com/RichiCoder1/lucent/issues/323) presentation/ergonomics,
  and [#324](https://github.com/RichiCoder1/lucent/issues/324) measured repeated work.
- September 29: source/tracker reconciliation complete. Correctness work started
  in Notes, navigation, authoring, and asset/UIA lanes. No correction is claimed
  delivered until its acceptance evidence is recorded here or in its issue.

### Navigation lifecycle — #320

Root detach now supersedes its pending preparation. Supersession clears deferred
work without publishing intermediate idle; a rejected request settles through
the existing deferred-preparation boundary. Replacement requests and callbacks
retain their publication/reentrancy protections.

The pre-fix RouteOutlet run reproduced six failures: unfinished detached work
and stale resolvers after five early-rejection paths. The corrected navigation
and outlet contracts pass 73/73; the Core test project builds without warnings.
Formatting and scoped whitespace checks pass. Evidence is retained under
`artifacts/test/navigation-review-before` and `navigation-review-after`.
The repository's test wrapper discovered zero tests on this invocation; directly
executing the freshly built test assembly ran the required cases. Package/CI
delivery remains to be verified separately; no desktop walkthrough is claimed.

### Notes integrity — #11

`a3a51e8` corrects coherent clean-editor refresh and retains a dirty editor's
original revision. Both regressions failed before the fix; 34 workspace tests
passed afterward. Follow-on replay/failure work remains in progress, with three
additional pre-fix failures retained under `artifacts/review-followups`.
