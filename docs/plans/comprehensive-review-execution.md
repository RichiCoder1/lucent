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

After the owner resumed UI/focus tests, the published 93.1 consumer fixture passed
a real native pending-save check. An independent bounded SQLite writer transaction
held the save while Computer Use typed text, invoked Ctrl+S and typed newer text
without refocusing. The same editor remained focused; releasing storage saved the
latest text, which survived closing and reopening in a new process. A deterministic
InputRouter test additionally verifies selection/caret offsets and rejects an older
acknowledgement overwriting the newer draft. Exact timestamps and binary identity
are in `artifacts/notes12-native/computer-use-proof.md`. The previously declined
offscreen substitution is not part of the passing evidence.
#12 is closed and marked Done; presentation adoption remains separate under #323.

### Stock presentation and consumers — #323

`caaba2d7`, published in `0.3.0-dev.93.1`, supplies semantic button roles, borrowed
command buttons, Field-aware multiline editing and label/help/error style hooks,
independent placeholder colors and a RadioGroup options style. Component Browser
uses selection semantics for theme/state choices; Issue Browser action rows gain
spacing without theme overrides. Focused Core/browser checks pass 110 cases;
affected builds and formatting pass. Headless light/dark/high-contrast images are
recorded under `artifacts/review-323`; they do not constitute a native walkthrough.
Light Notes' exact package restore, locked restore, consumer build, contrast and
multiline relationships pass. Its default-size native editing/save/reopen proof
passes. Minimum-height verification exposed a route-outlet sizing defect; `9f3da89a`
passes 41 outlet contracts and is published in `0.3.0-dev.94.1`. The fresh consumer
then isolated a second constraint: CommandScope and two application layout styles
did not allow shrinking. `3c95e5f1` adds CommandScope shrink participation and passes
all ten command contracts, including the independently failing 520-pixel regression.
Notes needs only `MainShrink: 1` on its Shell and Workspace styles. The unchanged
responsive contract passes against the corrected Core in an isolated diagnostic
output; this is not public-package proof. The final package consumer check remains
pending, with the original failed reports retained. No height threshold was changed.
Both browsers publish as NativeAOT. After the app restart and renewed permission,
Computer Use exercised fourteen Component Browser examples and Issue Browser's
selection, filters and context menus. Calendar columns, slider thumb, submenu
alignment, bounded dialog, password reveal, editable ComboBox reuse, table sorting
and native-picker cancellation were observed. The recorded walkthrough is under
`artifacts/review323-native-final`; it does not establish every state or accessibility
certification. Compact header wrapping and a status popup label wrapping at a narrow
width remain visual follow-ups. Native arrows failed to move both a radio and text
caret through Computer Use; equivalent compiled application checks pass, so attribution
to the tool, native transport or framework remains open.
Heading semantics and broader system high-contrast palette mapping remain deferred;
this slice preserves the existing stock high-contrast theme and optional minimal
presentation mode. It does not claim those additional capabilities.

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
persistent ownership/cache complexity.

Notes filtering needs no optimization from the bounded result: at 5,000 records
with roughly 2 KiB bodies, changing queries cost a median 0.873 ms / 5,072 bytes;
equivalent text cost 0.660 ms / 8,076 bytes. Repeated identical queries and unrelated
draft edits publish no new filter result. The two dataset characterizations pass;
retain the opt-in probe, not a cache. Evidence is in Notes'
`artifacts/review324-filtering` and is not native interaction latency.

The UIA no-listener experiment is deferred without a production change. Windows
still reports real accessibility clients listening after the Computer Use session
and fixture close. The experiment therefore exits before window creation with
zero passed and one skipped check; no timing samples were collected. Preserve
`artifacts/review324-uia/decision.md` and do not call this a measured speedup.
All scoped performance decisions are recorded. CI 94 passed managed, package and
publication checks, delivering the retained projection change in `0.3.0-dev.94.1`.
#324 is closed and marked Done.

### Navigation restoration and activation — #205

The [implementation plan](navigation-restoration-activation.md) separates portable
restoration from Windows activation. #221's active-location codec and guarded
startup replay are published in 93.1, with 62 codec/session checks and no required
corrections from independent Code Review. A package-only managed and NativeAOT
console fixture also proves root/nested outlet replay, typed route contexts,
guarded fallback and the public OnMounted lifecycle. Evidence is under
`artifacts/context-navigation-aot/generated-navigation-6c16340c2cb348c9967658bd30fe82cd`.
Journal/interaction implementation is committed in `ec092e68`. Independent review
found extreme imported scroll coordinates could overflow before clamping and
navigation-owned focus requests could outlive their owner. Both were reproduced
before correction in `ba41f502`: imported positions wait for measured extents,
and pending focus/scroll requests expire only for their owning generation. Newer
application requests win; ordinary in-memory transfers keep their existing
behavior. A disposed semantic callback is also guarded. The warning-clean build,
architecture check and 207 affected contracts pass. Follow-up review found two
further ownership cases: reconciliation replaced an application's newer select-all
request, and old-route retirement canceled a shared viewport's new-route request.
Three independent regressions reproduced those failures. `cd625961` yields to
newer application focus (including outside the route) and retains the target owner
alongside each pending viewport generation. All 210 affected contracts, build,
formatting and architecture checks pass; root reviewed the correction delta.

The package-only fixture now also proves fixed journal wire input, fresh runtime
entry identities, dormant interaction state, generated root/nested routes and
Back/Forward through `OnMounted`. Managed and NativeAOT runs pass against local
candidate `0.3.0-dev.local.activation.2`; this candidate predates `cd625961`, so it
establishes codec/distribution behavior rather than that final ownership correction.
Evidence is under `artifacts/journal-package-review`; the normal package CI now
runs the expanded fixture. CI 94 now publishes the final ownership corrections.
Issue Browser's opt-in application persistence passes 44 managed contracts, including
real hosted replay, startup readiness, atomic generation-fenced writes, close decline
and fresh interaction capture. Captures use the application event queue because
ReactiveScope.Post can execute during navigation retirement. Review then reproduced
close-before-first-layout losing the pending imported scroll position (saved zero
instead of 90), and then navigate-before-layout losing the same state for Back.
Both are fixed and independently reviewed in `57245db1`; 149 Core navigation cases
pass. A fresh NativeAOT build (SHA-256 `FAE94457DD1B2F533D24941B81F54D901F93E827FE06D178996EBF6065282715`)
saved `/issues/9915`, exited, and restored that detail in a new process. Evidence
is under `artifacts/issue221-native-reviewed`; search and list state are explicitly
outside this persistence snapshot. Final coherent package replay remains separate.
#222's optional Foundation/C#/WinRT dependency probe has
passed locked NativeAOT publication and hidden-process redirection without a
window. The optional adapter and its reviewed startup/close/reentrant-policy fixes
are committed in `45d00d6f`; all 22 adapter model contracts and the focused host
attention contract pass. `1b6d081a` adds isolated registered and MSIX preparation
fixtures. The unsigned MSIX manifest validates with MakeAppx, but its old probe
payload is only a packaging test. No registration, signing, installation or native
foreground proof has run. Review corrections in `7c4be47b` make Sandbox startup
explicit, map evidence outside virtualized AppData, retain verified process handles
before ACK, and bind preparation to descriptor/package bytes. An isolated child
exit-code proof passes; it does not establish protocol or MSIX transport. Final
coherent package proof is still required.
The dependency probe identified the requirement to register and release
AppInstance ownership on the same native STA thread. Actual OS protocol registration,
MSIX delivery, application integration and foreground behavior remain unverified.

### Compatible release sets — #245

`3baa1827` adds project-free language-server identity, exact source/compiler/package
and client compatibility validation, immutable candidate descriptors, and CI
completion records tied to uploaded artifact IDs/digests. The
[release contract](../RELEASE-SETS.md) keeps an available verified bundle distinct
from successful NuGet publication and retains explicit application package pins.

The packed/extracted language-server passes three affected contracts; 16 extension
contracts and 63 structural/rejection/finalizer cases pass. The latter use explicitly
synthetic package metadata and completion records, not real CI evidence. Independent
review has no confirmed outstanding findings: a path-escaping report was retracted
after inspecting literal AST values and file bytes. Actual producer-generated nested
Windows paths pass as well. Evidence is under `artifacts/issue245`.

CI 95–97 failed before publication; their failed evidence is retained. Corrections
cover dependency-lock and optional metadata handling, BOM-aware XML reads and
Windows command-line batching. CI 97 passed formatting and produced all ten packages,
the server and VSIX, then failed architecture preflight and a NativeAOT wrong-thread
test that allowed task inlining. The preflight correction is `a4b1ba96`; the
dedicated-thread correction `e5bbada1` passes managed and NativeAOT execution.
No new version is claimed published.

Templates #246 pass four generation variants and await actual consumer proof against
a coherent bundle. They are not yet added to the release inventory. Editor #247's
trust, selected-project isolation, identity and restart work is in independent review;
the first review found hung-startup cancellation, late-notification disposal and an
excluded-root document-symbol path needing corrections. Bundled server delivery,
cache/import and authenticated acquisition remain separate planned work.
