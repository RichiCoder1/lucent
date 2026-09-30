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

CI 92 passed the compiler, generator and tooling suites but failed one older
editor assertion that chose an arbitrary overloaded imported component. That
failure was independently reproduced against the pre-performance binaries.
`9fde6b46` now asserts rejection of ambiguity and retains exact navigation for an
unambiguous imported component beside malformed C#. Its five affected protocol
checks pass. CI 93 passed the complete managed suite, package consumers and
publication at `09ef196b`; `0.3.0-dev.93.1` is delivered and #321 is closed.

The default-content reader follow-up #325 is also delivered in that package.
Six regression rows and all 154 compiler cases pass; the original implicit-icon
Component Browser example builds without a reader workaround. Its issue is closed.

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

### Notes editing and maintenance — #12

Notes `f61ae316` keeps fields editable during Save and Backup while serializing
other commands. Closing still drains the newly observed draft. Maintenance uses
an existing-database-only open path, checks the schema before migration, and
reports expected failures without creating a replacement database or crash
directory. Six pre-fix failures were reproduced. Workspace/maintenance checks
pass 56/56, storage 31/31, and twelve NativeAOT console cases pass. CI run
`36650745851` passed. The exact console evidence is under Notes
`artifacts/notes12-console/1d327431e53e44a3bca91d63221c4c7a`.

The owner has now resumed UI/focus tests. Automatic approval review declined a
proposed offscreen focus/input substitution during the earlier gaming pause.
Ctrl+S/input continuity remains unverified and #12 stays open until that focused
check passes; no native input pass is claimed yet.

### Stock presentation and consumers — #323

`caaba2d7`, published in `0.3.0-dev.93.1`, supplies semantic button roles, borrowed
command buttons, Field-aware multiline editing and label/help/error style hooks,
independent placeholder colors and a RadioGroup options style. Component Browser
uses selection semantics for theme/state choices; Issue Browser action rows gain
spacing without theme overrides. Focused Core/browser checks pass 110 cases;
affected builds and formatting pass. Headless light/dark/high-contrast images are
recorded under `artifacts/review-323`; they do not constitute a native walkthrough.
Light Notes' exact package restore and locked restore pass. Consumer builds,
contrasts, multiline relationships and the native walkthrough are in progress.

### Measured repeated work — #324

`9fde6b46` avoids repeated full-suffix parsing and creates protocol line indexes
once per request. It adds no persistent cache or freshness shortcut. Fixed
15-sample comparisons, with three warmups and reversed execution order, reduce
parser allocations from 45,660,114 to 1,651,072 bytes for 400 markup elements,
101,383,765 to 2,467,155 for 400 expression islands, and 6,025,842 to 460,157 for
ComponentDetail. Symbol-request medians were 55–58 ms before and 10–11 ms after.
Compiler 148/148 and the five affected protocol checks pass. Measurements and
source hashes are retained under `artifacts/review324-*`.

Font fingerprints now hash the font bytes directly instead of allocating their
hexadecimal and UTF-8 representations first. In a fixed first-use Segoe UI shaping
probe, allocations fall from 6,771,296 to 1,003,736 bytes. Both comparison orders
produce identical glyph geometry and rendered bitmap hashes; the same Core binary
is used throughout. The opaque fingerprint changes intentionally; there is no
persisted-format or cross-version fingerprint compatibility contract. Renderer
checks pass 103 cases with three existing opt-in skips, and its build is warning
clean. Evidence is under `artifacts/review324-font`.

These timings are characterization on an active machine, not frame-budget proof.
No threshold was relaxed. The existing one-of-32-paragraph edit reshapes only the
edited paragraph and reads zero additional font bytes; a larger shaping redesign
is deferred. The source-map experiment improves isolated span lookup substantially
but leaves full compilation near 450 ms and produces inconsistent real hover,
completion and semantic-token timings. Production indexing is therefore deferred;
the reusable bounded measurements are committed in `51eabc9c`. Named preparation
lowers all ten documents after one changed body (~324 ms, 40.1 MB), while parsing
costs ~1.6 ms. A parse-only rewrite and broad graph cache are deferred pending a
design that preserves complete freshness. All experimental compiler changes were
restored and shared binaries rebuilt before subsequent work. Evidence is under
`artifacts/review324-authoring-followup`.

Projection now hashes the existing serialized buffer without copying it first.
The mutation guard, serialized bytes and SHA-256 algorithm remain unchanged.
Fixed 15-sample comparisons in both orders preserve exact scene-input, geometry
and pixel hashes. Allocations fall from roughly 3.51–3.53 MB to 3.35 MB for 100
stock rows and 90.80 MB to 89.16 MB for 1,000 rows. Timings are mixed; retain the
small allocation change without a latency claim. Input/projection and
layout/custom-algorithm checks pass 48 cases; build and formatting pass. Evidence
is under `artifacts/review324-projection`. Broader hashing, tree-walk and scrollbar
redesign is deferred: this bounded change avoids weakening equality or adding
persistent ownership/cache complexity. UIA listener admission and Notes filtering
still need their scoped decisions before #324 can close.

### Navigation restoration and activation — #205

The [implementation plan](navigation-restoration-activation.md) separates portable
restoration from Windows activation. #221's active-location codec and guarded
startup replay are published in 93.1, with 62 codec/session checks and no required
corrections from independent Code Review. A package-only managed and NativeAOT
console fixture also proves root/nested outlet replay, typed route contexts,
guarded fallback and the public OnMounted lifecycle. Evidence is under
`artifacts/context-navigation-aot/generated-navigation-6c16340c2cb348c9967658bd30fe82cd`.
Journal/interaction state implementation and application persistence follow.
#222's optional Foundation/C#/WinRT dependency probe has
passed locked NativeAOT publication and hidden-process redirection without a
window. It identified and now documents the requirement to register and release
AppInstance ownership on the same native STA thread. Actual OS protocol registration,
MSIX delivery, application integration and foreground behavior remain unverified.
