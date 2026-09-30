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
Source and planning updates are pushed as `6bfb0a4e`. CI 90 was superseded by
the subsequent asset/UIA push. CI 91 passed managed/package verification and
published the combined source as `0.3.0-dev.91.1`.

### Asset/UIA boundaries — #322

SVG admission now carries geometry-instance transforms through `use` placement
and root/symbol viewports, retaining the existing graph expansion limits. The
bounded subset explicitly declines `use` inside clip/mask resources and
font-relative placement; supported and rejected cases are documented in ASSETS.
Culture-sensitive UIA FindText now uses the actual source match length.

Before the fix, eight SVG admission cases and the soft-hyphen UIA match failed.
The corrected Skia suite passes 103 cases with three existing opt-in skips.
The noninteractive hidden-window UIA text contract passes, including seven exact
range cases through the provider ABI. Both affected builds are warning-clean;
formatting and scoped whitespace checks pass. Evidence is under
`artifacts/review-c9-c10`. No app window or focus-taking input was used.
Source is pushed as `59bc11d2`; CI 91 passed and published `0.3.0-dev.91.1`.

### Authoring reliability — #321

Identifiable incomplete methods and Setup bodies now retain editor projection
and useful diagnostics without absorbing following markup. Named authored Setup
uses the documented ReactiveScope owner behind the ComponentContext bridge.
The corrections also cover predefined C# receivers, negative enum defaults,
one CR/LF/CRLF and UTF-16 protocol position policy, raw quoted highlighting,
one-based CLI diagnostic positions and ambiguous symbol lookup.

Focused regressions reproduced the reported failures before the fixes. Compiler
147/147, generator 52/52, tooling 9/9, affected editor 8/8 and extension 16/16
checks pass. Two additional named-method map/diagnostic cases pass after the broad
compiler run, with no intervening production change. Affected builds are
warning-clean, formatting and whitespace checks pass.

Source commit `f825dc43` passed frozen package verification as
`0.3.0-dev.review321.gf825dc43`: managed C#/.lui consumption, lint configuration
and named-component parity, consumer and routed named-component NativeAOT
execution, and prepared-SDK missing-host/nondeterminism negatives. Source and
package hashes remained unchanged. Evidence is under
`artifacts/review321-package-evidence`. No editor restart or desktop check is
claimed; public CI delivery is tracked separately.

### Notes integrity — #11

`a3a51e8` corrects coherent clean-editor refresh and retains a dirty editor's
original revision. Both regressions failed before the fix; 34 workspace tests
passed afterward; its CI build succeeded. Follow-on replay/failure work now
reconciles immutable write identities, conditionally removes only an owned
recovery row, preserves independent draft/archive failures, and fences retry
and discard against late autosave callbacks. Cancelling a debounce for retry
still requests the observed edit before selection changes. Newer editor content
survives older acknowledgements, and successful autosave clears its resolved
failure feedback.

Independent review found additional replay/discard races and offscreen-edit
loss; those findings were reproduced before correction. Current managed checks
pass 57 cases with one existing opt-in probe skipped; storage checks pass 24/24.
NativeAOT publication passed; executable SHA-256 is
`AFE14D2FDA3694F97DF90F3AECD91B2074996FCBA1193152071F9ABC8A3675FA`.
The correction and storage contract are pushed as Notes `85660119`; its CI run
`36649087309` passed.
Evidence is under `artifacts/review-followups`; no desktop walkthrough is claimed.
