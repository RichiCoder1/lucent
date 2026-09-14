# Current work and follow-ups

## Semantic capabilities checkpoint — September 13, 2026

The approved [semantic refactor](../plans/semantic-capabilities.md) #291–294 is
implemented over baseline `910b2dc`. Declarations use validated typed capabilities;
metadata overlays and immutable snapshots share their payload. Every repository
producer and Windows UIA mapping has migrated. Custom declaration/snapshot
construction is a documented prerelease source break; stock `.lui` authoring is
unchanged. Light Notes has no direct construction sites and remains independently
pinned to `0.3.0-dev.60.1`.

Local affected suites pass: Core 548, Windows 130, headless Testing 46,
Component Browser 15, Issue Browser 20 and Compiler 69. The final Windows run
corrected two new fixture setup errors (COM interface selection and image-cache
configuration); both stock disabled-selection assertions now pass. Builds have
zero warnings/errors. Logs are `artifacts/semantic-final-tests.log` and
`artifacts/semantic-final-windows-tests.log`.

The [before/after measurements](../plans/semantic-capabilities-baseline.md) show
lower allocations in all five cases, including 2,216 → 2,120 B for caret updates
and 41,128 → 34,552 B for virtualized-list projection. Generation churn is
unchanged; equality suppression is deferred. No new broad manual accessibility,
IME or mixed-DPI claim is made. CI NativeAOT/package verification and publication
remain pending; the latest published Lucent package is `0.3.0-dev.73.1`.

Next: close this publication, then the bounded `.lui` callback diagnostic #290,
followed by typed composition context/service injection #203 and URI navigation
#204. Live compilation remains a separate workstream.

## September 13 execution checkpoint

The user authorized desktop interaction for the night. The earlier source
`f6e2bd6083c8b1536a838dde15ab318ced7d705b` now has successful
[CI 34735408532](https://github.com/RichiCoder1/lucent/actions/runs/34735408532),
including package verification and publication of `0.3.0-dev.66.1`. This supersedes
the pending-publication statement in the historical checkpoint below.

Editor follow-up #276 now finds authored `.lui` references to metadata-defined
properties without inventing external declarations or enabling external rename.
The regression checks exact spans through both the project API and LSP, with
`includeDeclaration` on and off. A valid source-only-policy reproduction failed;
the corrected implementation passes. A separate isolated restore of the actual
published `0.3.0-dev.60.1` Core package also passes the same regression.

All 27 LSP tests pass in 3m00s. The broad synthetic fixture now uses Core metadata
and took 10.1s; source-navigation fixtures retain project references and all
existing assertions remain. The prior full-suite observation was 6m33s; these are
individual observed runs, not a benchmark guarantee. Logs:
`artifacts/references276-{red,green,package,full}.log`.

Desktop review #279–283 and interaction enhancements #287–289 are complete.
All eight focused published TestHost workflows pass; Core 500/500 and Windows
127/127 pass. The final NativeAOT executable has SHA-256
`323284402F27546440E0461B2AD9256714860311539318DDD021D9405E69FB08`;
its source manifest and per-issue logs are under
`artifacts/review-closeout-279-289/`. These automated physical-input/UIA results
supersede the pending-smoke statements in historical checkpoints below.
The checks additionally fixed popup Tab disposal/owner traversal, calendar
semantic acceptance after reopening and stale submenu refresh after Escape.

The earlier interaction implementation `a18d662` also has successful
[CI 34751028190](https://github.com/RichiCoder1/lucent/actions/runs/34751028190)
and published `0.3.0-dev.68.1`. Publication of the additional native closeout
fixes must be checked against their own subsequent CI commit.

The bounded C# authoring implementation for #265 is delivered. The historical
#266 gate remains integrated in `f7c2712`, `da5d28d`
and `ca45fd7`; it established the closed capability wrapper, C# 14 overload and
conversion behavior, `.lui` source/metadata recognition, package-only generation
and the public scope-owned `BehaviorContext.BindSemantics` seam. The embedded
Roslyn packages remain aligned at 5.0.0.

The current implementation adds `ComponentContext` over the existing deferred
mount owner; generated author-property metadata and style fluency;
capability-bearing stock factories; ordered fixed, live and grouped accessibility
metadata;
generated per-mount partial state; shared live-label binding; bounded retained
Drawing; and a portable Gauge. `.lui` remains the primary stock-control authoring
direction. C# authoring uses the same retained recipes, owner and transaction and
does not add a rerender engine or ambient scope. `Slider`, `ListBox` and
`VirtualizedList` are intentionally style-only because their semantic controls
are descendants of the authored root.

Generated state is limited to top-level, non-generic, sealed partial classes with
no authored instance constructor. Explicit partial properties use constants,
defaults or named static typed initializers, are allocated in deterministic name
order and then run one synchronous partial initialization hook. Async initialization
is rejected. Unattached, off-thread and disposed access continues through the
existing reactive guards, while initialization failures roll back through the
existing mount transaction.

The implementation landed in `0a2ae12546228e953ac06bf65632d6fd51a9eddc`, with
package-proof and CI follow-ups in `ff11a7c` and `15ff070`. All nine packages are
published as `0.3.0-dev.73.1` from `15ff0709f1a938a10e438ed161c36eda3db74a61`;
[CI 34781813597](https://github.com/RichiCoder1/lucent/actions/runs/34781813597)
passes managed, package verification and publication jobs. The first CI attempt
found the authoring script's assumption of a repository-local SDK. It now also
uses the pinned SDK on PATH, matching the existing verification scripts, and
that path passes an isolated managed/NativeAOT proof without a local SDK folder.
The smaller authoring package check runs before the longer asset checks.
Managed verification passes Core **543/543**, Renderer **85/85** with three
intentional opt-in skips, Testing **46/46**, Compiler **69/69**, Generator
**23/23**, Windows **129/129**, Component Browser **15/15**, Issue Browser
**20/20**, Hosting **5/5**, and R3 **7/7**. The final editor suite passes
**27/27** in 2m34s. The full solution builds with zero warnings/errors; Core
architecture positive/negative checks and the maintained SDK suite pass.

Final Gauge containment adds a renderer regression: all **3/3** focused Gauge
pixel checks and **3/3** Core Gauge contracts pass after constraining long text
and clipping to its box. Its full semantic value remains available. The actual
Windows Gauge Name/HelpText/read-only RangeValue check also passes.

Local nine-package set `0.3.0-dev.authoring265.1` is source-bound to `0a2ae12`.
The package-only authoring proof executes C# and `.lui` counter, controlled form,
owned async/keyed content, generated state and Gauge in managed and NativeAOT
modes. The fixture uses a method-group editor callback within the supported
`.lui` expression grammar. Native executable SHA-256:
`0A306F22F87F0DCE5617069C368551F64A466CB7E579BED8BFBB22E76BAF0170`.
The maintained headless package consumer also passes. Logs are
`artifacts/authoring-package-proof.log` and `artifacts/authoring-headless-proof.log`;
package hashes/source commits are recorded with the authoring consumer evidence.
Only its disposable restore cache was removed after execution to recover disk
space; sources, logs, package identity and published executable remain.

Computer Use verified the local C# counter, reset on remount, and Gauge updates
in dark, light and high contrast. The refreshed NativeAOT Component Browser
SHA-256 is `6AF6F18A2A224361DEC566B6BE21D51D5F848D1F117C38BFE5C6CA247D7FB157`.
The final focused Computer Use repeat passed on 2026-09-13 against that rechecked
executable hash: the companion C# source note wraps without clipping, Apply change
increments the local counter, returning to Buttons resets it on remount, and the
Gauge label stays centered and contained while its arc and value advance from 38%
to 54%. Dark, light and high-contrast Gauge presentation remained legible. The app
closed normally, confirmed by a subsequent window list. This was the final focused
authoring check, not another full component-catalog walkthrough.

Earlier launch attempts returned `accessibility window-opened handler did not become ready`.
The successful repeat used a newly started native Computer Use helper; no Lucent
code or executable changed. This supports stale helper state as the explanation,
but the underlying accessibility-listener failure was not established. No visual
repeat or permission question remains pending for this authoring closeout.

The matching language server is installed and configured only for the local
Lucent VS Code workspace; a window reload activates it. Other workspaces keep
their existing version settings. Source-project editor setup and package/version
alignment are documented in the extension README. Follow-up
[#290](https://github.com/RichiCoder1/lucent/issues/290) tracks the assignment-lambda
diagnostic boundary exposed by the controlled-form fixture; it is triaged in Todo.
See [ADR 0008](../adr/0008-bounded-csharp-authoring.md) and the
[C# authoring guide](../CSHARP-AUTHORING.md) for contracts, migration and measured
build/member/allocation costs.

The first integrated CI run, 34753499084, passed managed and NativeAOT suites
but stopped before publication at the asset-workspace helper's stale Roslyn
lockfile. The helper now uses the aligned dependency graph and supported owned
workspace-diagnostic registration. It is included in the solution so future
solution-wide dependency refreshes cover it. Asset verification now includes
the failing command's diagnostic tail in CI errors. This repair changes the
verification helper, not the runtime candidate exercised by the desktop checks.
The full local asset proof passes after the repair, including cold workspace
generation, metadata/negative cases and both package/project-reference NativeAOT
consumers after removing source, feed and cache. Logs are under
`artifacts/authoring-assets-ci-repair/`; solution locked restore also passes.

## Completed interaction repairs — September 12, 2026

[#278](https://github.com/RichiCoder1/lucent/issues/278) repairs the user's additional recordings: calendar weekday centering, tooltip shadow hover interception, popup width/scrolling/restore placement, and ComboBox editing/reopening. Suggestion windows retain editor focus and pointer selection; popup width includes its padding. A shared reactive graph fix preserves later notifications after an effect writes and rereads a derived value. Native popups resize before positioning so the old width cannot constrain their new location. Core 461/461, Windows 125/125 and Component Browser 13/13 pass, including the gallery's 84 theme/density captures. Architecture and changed-file formatting checks pass. Logs are `artifacts/component-input-*.log`; the final Windows resize-order proof is recorded in the execution plan.

Computer Use passed stationary tooltip hover, pointer transit into the description and dismissal after leaving; June/July weekday alignment; popup width, scrolling attachment and off-screen dismissal; and ComboBox filtering, reopening, mouse selection/deletion, immediate full-label updates, first-edit continuity and repeated Beta/Alpha/Gamma selection. The final `11bd2a8` build passed maximize/open/restore with the popup still aligned and a subsequent selection applied correctly. See the execution plan for source boundaries and keyboard/retry limitations.

The final NativeAOT browser is `artifacts/component-input-278-verified/browser/Lucent.ComponentBrowser.exe`, built from `11bd2a8c66dd4eae221458b93e40caeccf634a5f`, SHA-256 `1988530D6D2E63883D1D2B12958FF16E1F74612964A40838D5AA67B685738F46`. Its source manifest is `artifacts/component-input-278-verified/source.json`. This supersedes both earlier #278 candidates. It is a local published executable, not a claim of NuGet publication; consult CI for package status.

The user subsequently authorized UI testing while in VR and confirmed that the
foreground Component Browser did not interfere. The bounded retry below is the
latest evidence; the test app is closed again. If physical Escape interrupts
future Computer Use, ask to resume while continuing independent code work rather
than treating it as cancellation of the whole task.

## Review implementation checkpoint — September 12, 2026

The #279–285 corrections are implemented. Core **486/486**, Windows **125/125**
and Component Browser **13/13** pass, including 84 headless theme/density captures.
The Windows field test uses a hidden window and verifies updated help and error
removal through the same UIA provider. Compiled architecture/public API,
changed-file formatting and diff checks pass. Logs are
`artifacts/review-followups-{core,windows,browser,architecture}.log`.

#279–283 remain open for the bounded published desktop smoke: tooltip then
dialog Escape; pending dialog dismissal/reopen; calendar live availability;
slider Escape/secondary release; and menu separator/disabled-row keyboard
continuity. No new manual/native-focus pass is claimed. #284 and #285 are
complete with the time-boundary and retained-field/UIA automated evidence;
neither needs a broad new walkthrough.

### Computer Use retry and CI repair

Computer Use could attach to the existing Debug browser, whose Browser, Core
and Windows assemblies identify source `1d5bcfa1c8a11ab73a2d191684b6c0da3704d437`.
It verified keyboard-focus tooltip appearance and dismissal, calendar weekday
alignment and June 16 selection, and a slider drag from 64 to 40. Immediate
captures sometimes preceded settled feedback; a later capture confirmed the
date change. Menu keyboard input and slider arrow input were inconclusive.
The existing examples do not expose the nested tooltip/dialog or pending-failure
scenarios, and Computer Use cannot hold a drag while issuing a separate key or
secondary release. These observations do not close #279–283.

The published NativeAOT candidate at `artifacts/review-followups/browser/` was
not launched: the helper reported `accessibility window-opened handler did not
become ready`. Debug observations are not substituted for published evidence.
The retry also found that the date/time example's Disabled selector was not
connected to either field's availability. Both fields now use retained enabled
readers. A compiled-example regression failed on the open calendar before the
fix, then verified dismissal, disabled editors and value-preserving recovery.
Component Browser passes 14/14 including 84 headless theme/density captures;
the build is warning-clean. Logs:
`artifacts/review-browser-availability-{red,green}.log`. The final application
wiring change has headless evidence, not a fresh published UI pass.

[CI 34733774709](https://github.com/RichiCoder1/lucent/actions/runs/34733774709)
passed managed checks but failed NativeAOT test compilation before package
verification. MSTest generated unsupported enum reflection for a test-local
slider enum. The repair preserves the three scenarios as named tests and removes
that enum from the test assembly; no warning or AOT policy is weakened. The exact
Native suite now passes: Core 486/486, R3 7/7, Skia 80 passed with three opt-in
skips, and Windows 125/125. Log:
`artifacts/test/native-enum-metadata-fix-final.log`. This proves the local repair,
not a successful rerun or package publication by GitHub Actions.

The independent Fable High review and primary-source control comparison are complete. The review inspected a frozen source snapshot and ran no tests or UI checks. Active-fix overlap is tracked in #278. The implemented follow-ups are:

1. [#279](https://github.com/RichiCoder1/lucent/issues/279): closed-tooltip Escape routing.
2. [#280](https://github.com/RichiCoder1/lucent/issues/280): pending dialog acceptance, host dismissal and recovery.
3. [#281](https://github.com/RichiCoder1/lucent/issues/281): calendar availability changing while open.
4. [#282](https://github.com/RichiCoder1/lucent/issues/282): slider cancellation and pointer-button ownership.
5. [#283](https://github.com/RichiCoder1/lucent/issues/283): menu keyboard navigation after separator hover.
6. [#284](https://github.com/RichiCoder1/lucent/issues/284): TimePicker stepping at the end of the day.
7. [#285](https://github.com/RichiCoder1/lucent/issues/285): dynamic field help and first-invalid order.
8. [#286](https://github.com/RichiCoder1/lucent/issues/286): design delivered in [desktop component interaction](../plans/desktop-component-interaction.md). Its ordered implementation tickets are [#287 dropdown keys](https://github.com/RichiCoder1/lucent/issues/287), [#288 viewport paging](https://github.com/RichiCoder1/lucent/issues/288), and [#289 compact numeric steppers](https://github.com/RichiCoder1/lucent/issues/289). These enhancements are distinct from the reproduced defects.

The local comparison, review and snapshot provenance are under `artifacts/reviews/control-behavior-20260912-194108/`. Public tickets contain bounded reproductions and acceptance criteria, not the full local review snapshot. Resolve the correctness follow-ups before beginning the next authoring phase.

## Published checkpoint — September 12, 2026

The component program #155–171 is delivered, closed and Done in Project 4. [Final delivery evidence](https://github.com/RichiCoder1/lucent/issues/171#issuecomment-5649183562) records the source boundaries and verification limits.

- Lucent: `f6fbc4ff29fcd9ab4e5c390825e663fba3d6548a`, immutable package `0.3.0-dev.60.1`. [CI 60](https://github.com/RichiCoder1/lucent/actions/runs/34722441727) passed managed, NativeAOT and package-only consumer checks, then published the verified packages.
- Light Notes: `48a404d37ff1f1380ab3b549c86d6c46c24f8f07`, independently consuming `0.3.0-dev.60.1`. [App CI](https://github.com/RichiCoder1/light-notes/actions/runs/34723338988) passed managed tests, desktop-test compilation and NativeAOT publication.

The maintained Component Browser contains fourteen compiled `.lui` examples with their actual source. Issue Browser adopts Select for fixed-choice filters. Light Notes adopts Field for URL semantics while preserving its workspace-owned EditorSession, draft and focus through responsive layouts. The reported dialog width, calendar alignment, suggestion sizing, radio clipping, slider alignment, password toggling, menu padding and submenu placement defects are fixed; hover tooltips anchor near the pointer.

### Verification and performance

Local suites passed Core 449/449, Windows 120/120, Component Browser 11/11 with 84 stock theme/density captures, Issue Browser 20/20, Skia 80 with three opt-in skips, and LSP 26/26. Architecture and formatting checks passed. Six selected desktop tests passed against fresh NativeAOT Component Browser/TestHost outputs; radio and slider captures were inspected across all six theme/density combinations. Logs and captures are under `artifacts/component-native-final*`.

The later Enter hotfix lets single-line TextField bubble Enter to CommandScope when no commit callback owns it. Light Notes verifies this final package with physical Enter capture/autosave/reopen, responsive URL draft/focus continuity, and focus rehoming: all three selected native workflows passed. Its managed suites passed storage 22/22 and app 42 with one intentional opt-in skip. Evidence is under Light Notes `artifacts/delivery-*-60.1.log`.

Test setup now uses Core metadata for synthetic LSP fixtures that do not require Core source navigation, removes duplicate test-discovery processes, rejects empty runs, and records TRX timings. Managed and package verification run concurrently; publication waits for both and consumes the exact verified artifact. No existing assertions or native/package checks were removed. Measured local LSP time fell from 10m05s to 6m33s (35%); [CI 58](https://github.com/RichiCoder1/lucent/actions/runs/34721289961) took 15m23s versus CI 57's 37m59s. These are observed runs, not runtime IDE-performance claims. A separate Skia churn-fixture fix deterministically admits its intended workload while retaining renderer entry/byte limits and the existing timeout.

## Completed manual walkthrough and follow-up

The requested Computer Use walkthrough is complete. The runtime became callable in the resumed task; all fourteen examples were visited in the real NativeAOT browser. The [execution plan's walkthrough record](../plans/component-delivery.md#final-computer-use-walkthrough--september-12) distinguishes observed interactions from the automated theme matrix and keyboard evidence.

[Follow-up #277](https://github.com/RichiCoder1/lucent/issues/277) fixes the remaining Select popup sizing, gallery navigation/scroll reset and popup-to-owner repaint defects at `364033142d869d4c45646b82802a362e6b04a6ac`. Core surface/list contracts pass 13/13, Windows 121/121 and Component Browser 13/13 with 84 captures. Architecture/public API and formatting checks pass. Geometry/navigation passed a native retest; the final build additionally passed immediate Select feedback, Popover open/close feedback and modal completion through Computer Use.

The current tested browser is `artifacts/component-walkthrough-final/browser/Lucent.ComponentBrowser.exe`; `artifacts/component-walkthrough-final/source.json` records the exact source and SHA-256. The earlier `component-manual-ready` build is superseded. Package `0.3.0-dev.60.1` above is the prior published consumer baseline; consult #277 and its CI run for follow-up publication rather than treating the local executable as a published package.

Computer Use's transient-element cache and arrow/Enter injection were unreliable in some cases; the record does not claim a fresh manual keyboard pass for those paths. This delivery also does not claim broad manual accessibility, real-language IME or fresh physical mixed-DPI certification. Earlier #153 hardware evidence remains valid for its recorded source boundary.

## Current completion boundary

Tooling follow-up #276, desktop interaction #279–289, and C# authoring #265–275
are complete at the source and verification boundaries recorded above. Authoring
is published as `0.3.0-dev.73.1`; its final focused Computer Use repeat passed and
was recorded in `910b2dc`. Semantic capabilities #291–294 are the current work;
their baseline is `910b2dc`. Context/navigation #203/#204 remains a separate later
phase. Callback diagnostics #290 is a separate small authoring follow-up.

## Scope and workspace constraints

The completed component batch was limited to #155–171. Live compilation, context/injection/navigation, and other #203–264 work remain separate. The Windows file picker selects locations without file I/O. TableView remains read-only with fixed-height virtualized rows. Preserve Core portability; Windows owns hosting, input, IME, accessibility, presentation and native-dialog adaptation.

Lucent is at `D:/src/richicoder1/lucent`; Light Notes is at `D:/src/richicoder1/light-notes`. Preserve unrelated `.codex/`, `.dotnet-home/`, `advisor-plans/`, `docs/plans/windows-sandbox-testing.md`, and `docs/research/` content. Use explicit staging paths, serialize shared-tree builds and foreground tests, and follow [risk-based verification](verification.md). The Dev Drive is backed by `C:/DevDrive/Dev.vhdx`; if `D:` disappears after restart, inspect attachment state before changing anything. Do not format the volume, change partitions or relax ACLs.

Earlier asset, motion, focus-continuity, responsive-layout, native-menu and mixed-DPI records remain authoritative for their committed source boundaries. See [the component execution plan](../plans/component-delivery.md), [testing guide](../TESTING.md) and individual delivery issues for details.
