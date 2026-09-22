# Current work and follow-ups

## Paused delivery checkpoint — September 22, 2026

The owner requested a pause at the next valid checkpoint. All local #310 checks
are complete and Lucent is pushed through `6c4573b8`. [CI 86](https://github.com/RichiCoder1/lucent/actions/runs/35785516739)
was still running its managed and package-verification jobs when work paused.
Publication of `0.3.0-dev.86.1` has **not** been confirmed. Recheck that exact run
before changing consumer pins; keep #310 open until delivery is verified.

Light Notes still has its three coherent routing migration edits in `Routes.cs`,
`ShellPresentationTests.cs`, and `WorkspaceTests.cs`. Its official pins, lockfiles
and validation document have not been changed; preserve the unrelated dirty files.
On explicit resume: verify publication, adopt the official version and SDK 10.0.401,
verify/commit/push Light Notes, then close #310 and update the roadmap.

The queued test-quality package remains unimplemented. H1 editor helper design is
prepared read-only. Baseline asset verification against the existing local candidate
passed in 108.36 seconds (`artifacts/test-quality-assets-before-detail.log` and
`test-quality-assets-before-result.json`). The 11 reactive scaling tests passed;
the two 10,000-node cases took 51.93 ms and 21.39 ms respectively, excluding build.
Keep their coverage. Defer optional multi-suite orchestration absent demonstrated
benefit. The baseline TRX is `artifacts/test-quality-baseline/scaling.trx` and an
unpublished execution-ticket draft is `artifacts/test-quality-issue.md`.

Do not start the queued implementation or roadmap #205 until the owner resumes.
No active local build or UI test remains at this checkpoint; remote CI may finish
independently. The older resumed-work instructions below are superseded by this pause.

## Review correction verification — September 22, 2026

The owner resumed #310 and asked to finish verification and delivery. Do not start
roadmap #205. The earlier restart pause is superseded.

Routing R1/R2/R3/R7 is saved in local `1553813`: Core **674/674**, Issue Browser
**22/22**, and architecture checks including negative fixtures pass. Editor/build
corrections now pass Compiler **146/146**, Generator **52/52**, LanguageServer
**45/45**, and VS Code client **15/15**. Repository formatting passes **595 C#**
and **115 LUI** files. VSIX **0.3.5** and its matching versioned language server are
installed; the deployed server's protocol smoke passes. Open VS Code windows need reload.

The independent source follow-up identified and rechecked four additional cases:
same-arity method overload maps, real client synchronization of unsaved configuration,
AdditionalFiles-only configuration for linked LUI, and structural failure hidden by
diagnostic suppression. Those corrections have focused regressions; the reviewer found
no remaining concrete blocker in the bounded source follow-up and did not run tests.

Integrated source is committed as `a2e74ac`. Local candidate
`0.3.0-dev.review310.20260922.1` passes all nine package inventories, package lint/SDK
and NativeAOT proofs, generated navigation and scoped Hosting consumers, four Windows
sample smoke runs, four package editor contracts, and three Issue Browser desktop checks.
Light Notes passes 44 managed checks with one intentional skip, Storage 22/22, NativeAOT
publication and two desktop route/focus/draft checks against that candidate.

Remaining delivery: verify CI publication, then pin Light Notes to the corrected
official package, preserving unrelated changes. Its three routing migration files remain
uncommitted; the isolated candidate fixture is `artifacts/review310-light-notes`.
Do not close #310 before delivery is verified.

PowerShell startup was blocked by Windows volume queries against the external Samsung
T7 on F:. After the owner's repair action, fresh PowerShell and C:/D:/F: metadata probes
all pass. No agent device restart was performed. Details are in
`artifacts/shell-diagnosis/diagnosis.md`.

Use `MSBUILDDISABLENODEREUSE=1` with SDK 10.0.401. Coordinate builds sharing compiler
outputs. Source app projects own their RIDs; do not pass a global RID through the graph.
Worker evidence is in `artifacts/review310-editor-checkpoint.md` and
`artifacts/review310-diagnostics-checkpoint.md`. Preserve unrelated advisor/research
and sandbox-plan files, `.dotnet-home/`, and the diagnostic-time `%SystemDrive%/` folder.

## Queued after #310 — test quality

The Code Review task relayed the owner's request to implement the Fable-reviewed
recommendations after the current closeout. Read `advisor-plans/README.md`, then
`005-agent-test-authoring-guidance.md`, `003-existing-test-cleanup.md`,
`004-test-harness-improvements.md`, and
`reviews/test-quality-fable-disposition.md`. Required scope is 005, C1–C5 and H1–H3;
record proceed/defer decisions for C6 and optional H4. Preserve distinct compiler,
editor, package/NativeAOT, UIA, cleanup and stale-work evidence. No new coverage gates,
headless migration or stress opt-in system is authorized by those plans. Keep the
unrelated security plan001 and research untouched. Do not start roadmap #205.

## Authoring review corrections — September 21, 2026

The owner accepted all eight adversarial findings against `247c87e`. Work is tracked
in [#310](https://github.com/RichiCoder1/lucent/issues/310) and the
[correction plan](../plans/authoring-review-corrections.md): routing transaction and
rollback safety first, consolidation on Router/RouterOutlet and typed descriptors,
then exact editor mappings, dependency preparation, lint parity and companion diagnostics.
The prior A0–A7 delivery evidence below remains tied to its original source and packages.
Finish and verify these corrections before starting roadmap #205.

## A0–A7 delivered — September 21, 2026

Parent #301 and children #302–309 are complete. `d9cf2db`, `f36f9f6` and `e15a43c`
deliver ordinary LUI declarations, named partial components with optional companions,
composable application lifecycle hooks, declarative routing, reactive destinations,
editor integration and reference-app adoption. All nine packages are published as
`0.3.0-dev.85.1` after [CI 85](https://github.com/RichiCoder1/lucent/actions/runs/35635336874)
passed managed, NativeAOT/package verification and publication.

Full managed CI reports 1,222 passes and three existing opt-in renderer skips; the
extension's 15 tests pass separately. This includes Core 656, Issue Browser 22,
Component Browser 20 and the full editor suite's 40 tests. Local isolated package
editor fixtures pass 3/3; formatting, warning-clean builds and architecture checks pass.
CI caught a generated-descriptor dispatch gap in explicit navigation composition;
`e15a43c` fixes both public provider overloads, with regression and NativeAOT proof.

The inline and companion Windows package samples passed managed and NativeAOT
execution: four smoke runs verify routed state and cleanup, and the companion runs
also verify two independent mounts. These are automated desktop checks, not a manual
visual walkthrough. Exact candidates and evidence are in the
[execution record](../plans/application-routing-component-authoring-execution.md).
The [authoring guide](../APPLICATION-AUTHORING.md) describes the supported API.
VSIX 0.3.4 was packaged; this work did not install it or restart the user's editor.
The prior pause records below are historical and superseded by this delivery.

The next ordered roadmap parent is [#205](https://github.com/RichiCoder1/lucent/issues/205),
with opt-in location/journal restoration (#221) and explicit Windows activation (#222).
Their application-policy decisions remain separate from this authoring batch; do not
infer authorization for optional multi-window or custom routing work from this closeout.

## A1–A7 source checkpoint — September 20, 2026

`d9cf2db` saves the named-component/compiler, lifecycle/Hosting, declarative routing,
Component Browser migration, and inline/companion sample work. The owner explicitly
requested a full pause at this checkpoint while gaming. No A1–A7 ticket has been closed
yet, and the checkpoint has not been pushed. Resume only at the owner's request; keep
focus-taking verification paused unless separately authorized.

Current source checks pass: Compiler 144, Generator 51, Core 654, Hosting 12, Tooling
eight, and Component Browser 20 tests; VS Code client 15 tests; full locked solution
restore; authored formatting; and Core architecture checks including negative fixtures.
Before the focus pause, the actual-window Browser history/state check and both managed
Windows sample smoke runs passed. These are not packaged NativeAOT execution evidence.

Remaining closeout: finish the editor rename/map regression and full LSP suite, verify
the integrated solution build, consume the candidate packages (including editor and
NativeAOT), then run the two Windows package smoke variants after focus testing resumes.
Update the execution record and tracker, commit the editor/evidence changes, push and
verify CI. Do not attribute the earlier A0 package or CI result to this source checkpoint.

Candidate `0.3.0-dev.a1a7.20260920.1` is incomplete: packing reached the SDK but failed
because idle MSBuild nodes held `Lucent.Lui.Sdk.PreparationTasks.dll` open. The supported
`dotnet build-server shutdown --msbuild` command completed after the failed run; packing
was not retried before the pause. Preserve `artifacts/a1a7-pack.log` as failure evidence.
Use a fresh candidate version when resuming from a later source commit, and disable
MSBuild node reuse for the pack session if the task assembly remains locked after builds.

## Resume checkpoint — September 20, 2026

The owner resumed work. A0's [CI run](https://github.com/RichiCoder1/lucent/actions/runs/34924315787)
passed managed verification but failed package verification because the two new package
wrappers assumed a repository-local `.dotnet/dotnet.exe`. They now use the existing
repository convention of falling back to `dotnet` on PATH. Both wrappers pass from an
isolated source tree without a local SDK: routed NativeAOT publish/execution and the
production SDK positive/negative cases use the system SDK 10.0.401. Logs are under
`artifacts/a0-path-sdk-routed-retry.log` and `artifacts/a0-path-sdk-negatives.log`.
Remote [CI 35532769554](https://github.com/RichiCoder1/lucent/actions/runs/35532769554)
passes managed, package verification, and publication at `ec3749ba`.
The owner authorized completing A1–A7. Compiler/companion, lifecycle, routing, and editor
implementation are active; consumer migrations follow the tested APIs. The historical
September 14 pause below is superseded. See the execution map for slice dependencies.

## Application and component authoring — September 14, 2026

[A0 #302](https://github.com/RichiCoder1/lucent/issues/302) is complete under
[parent #301](https://github.com/RichiCoder1/lucent/issues/301). The owner paused after A0
at this historical checkpoint and resumed on September 20. Dependencies and remaining product scope
are in the [execution map](../plans/application-routing-component-authoring-execution.md).

The [A0 record](../plans/application-routing-component-authoring-a0.md) selects Roslyn 5.9.0
with SDK 10.0.401, shared authored/early-member projection, one evaluated foreign-generator
preparation pass, semantic signature refinement and a content-addressed emitter whose final
foreign outputs must match exactly. Named components are opt-in. The same preparation engine
serves the SDK and actual language server, including unsaved text and companion members.

Compiler 132, Generator 45, LSP 37 and Tooling eight tests pass. The official authoring-package
entry point passes candidate `0.3.0-dev.a0.20260914.5`, including the cold bootstrap-only routed
NativeAOT consumer and production missing-host/nondeterministic-output negatives. Formatting
passes 584 enumerated C# paths and 108 LUI files; compiled Core architecture checks pass.
Exact commands, observations, hashes and historical failed runs are linked in the A0 record.
Do not attribute earlier CI results to this integration candidate.

Light Notes remains unchanged and freshly passes 33 C# and 16 LUI formatting checks through
Lucent's SDK 10.0.401. Its standalone SDK pin remains 10.0.400. No app migration or
focus-taking walkthrough was required for A0. A1–A7 still cover the broader authoring,
lifecycle, router, tooling and consumer migration contracts; the bounded proof does not
close those tickets or automatically migrate existing consumers.

## Whole-file formatting and linting — September 14, 2026

Lucent `7144a5e` is published as `0.3.0-dev.79.1` after
[CI 79](https://github.com/RichiCoder1/lucent/actions/runs/34892962935) passed
managed, package-only NativeAOT and publication jobs. The compiler, generator,
CLI and editor share formatting/configuration/lint policy. Authored source and
all in-tree apps pass C# and `.lui` formatting, enforced in maintained CI.
The [guide](../LUI-FORMATTING.md) documents commands, explicit fixes and exceptions;
the [integration record](../plans/lui-formatting-integration.md) records preservation,
performance, packaging and verification evidence.

Light Notes `8ac1175` consumes the published version and enables SDK formatting
checks in its build/publish workflow. Pure formatting is isolated in Lucent
`66ebfe8` and Light Notes `5964730`. Locally, Light Notes passes all 33 authored
C# and 16 `.lui` checks, builds without warnings, and passes 22 storage and 44
workspace tests with one intentional opt-in skip. Desktop tests and the review
tool compile; no new focus-taking UI walkthrough was needed for source formatting.
[Light Notes CI](https://github.com/RichiCoder1/light-notes/actions/runs/34895395190)
passes formatting, tests, desktop-test compilation and NativeAOT publication at
`8ac1175`. F01–F06 (#295–300) are complete; no formatter delivery work remains.

VS Code extension `0.3.3` and its matching server are installed. Current user and
Lucent workspace settings point to
`C:/Users/richa/.lucent/lui/formatting-20260914/server/`; Reload Window activates
them. Installation paths in older entries below are historical.

## Context, injection and navigation — September 14, 2026

Implementation source `b3f3d59c91352b89761b9aefde42ef1c149b6e77` is pushed for
#203/#204. It adds typed mount requirements and providers, owned declarations,
Hosting service borrowing, popup environment continuity, generated routes,
bounded navigation transactions, retained outlets and focus/viewport/command
integration. Issue Browser uses the new authoring and routing; Light Notes
commit `9b8cb1b06bb70ed9f1cb478d31e3ee788041bcc4` consumes the official
`0.3.0-dev.76.1` package set.

Local verification passes Core 645, Compiler 92, Generator 35, Hosting eight,
Issue Browser 22 and all thirty editor tests across the full run plus one stale
fixture correction. The full solution and NativeAOT Issue Browser build without
warnings. Architecture positive/negative, formatting and extension checks pass.
Four published Issue Browser tests cover responsive Back/Alt+Left, route commands
through Lucent and Windows menus, and Axe with zero rule errors. Four Light Notes
desktop workflows pass the official package; its app and storage suites pass 44 and 22,
with one intentional app projection skip. The joint package probes execute
managed/NativeAOT with real provider ownership and disposal evidence.

[CI 34822437908](https://github.com/RichiCoder1/lucent/actions/runs/34822437908)
passed managed, package-verification and publication jobs for all nine
`0.3.0-dev.76.1` packages. Light Notes is pushed to `main`;
its [CI 34824660762](https://github.com/RichiCoder1/light-notes/actions/runs/34824660762)
passed storage/workspace tests, desktop-test compilation, locked restore and
NativeAOT publication. The same verified commit was promoted to `main` after
explicit user authorization; no delivery work remains for #203/#204.
[The joint plan](../plans/context-navigation-execution.md) maps evidence to the
acceptance cases; [the navigation guide](../NAVIGATION.md) documents current APIs.

The local editor server is installed under `C:/Users/richa/.lucent/lui/b3f3d59/`
and the updated syntax extension is installed. Existing server settings point to
that version; reload VS Code to activate it. Route
restoration and Windows activation remain follow-up work under #205. Optional
container features, multiple windows, navigation animation and live compilation
remain outside this delivery.


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
IME or mixed-DPI claim is made. Source `d44f265` passed
[CI 34805441710](https://github.com/RichiCoder1/lucent/actions/runs/34805441710),
including NativeAOT/package-consumer verification, and is published as
`0.3.0-dev.74.1`. Architecture positive/negative and formatting checks also pass.

The bounded `.lui` callback diagnostic #290 is implemented in `5ebb236`. All 72
compiler and 28 language-server tests pass; the new tests pin exact authored
assignment spans and named-method recovery for both C# and `.lui` state. The
allowlist and runtime state rules are unchanged. Local logs are
`artifacts/callback290-{red,green,suites}.log`.
[CI 34806634193](https://github.com/RichiCoder1/lucent/actions/runs/34806634193)
passed managed, NativeAOT/package-consumer and publication jobs at `52ff6d5`,
publishing `0.3.0-dev.75.1`. #290 is complete.

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
was recorded in `910b2dc`. Semantic capabilities #291–294 are delivered in
`d44f265`/`0.3.0-dev.74.1`; their baseline is `910b2dc`. Callback diagnostics #290
are delivered in `5ebb236`/`0.3.0-dev.75.1`. Context/navigation #203/#204 is now
authorized; see the joint execution plan for child sequencing and current scope.

## Scope and workspace constraints

The completed component batch was limited to #155–171. Live compilation, context/injection/navigation, and other #203–264 work remain separate. The Windows file picker selects locations without file I/O. TableView remains read-only with fixed-height virtualized rows. Preserve Core portability; Windows owns hosting, input, IME, accessibility, presentation and native-dialog adaptation.

Lucent is at `D:/src/richicoder1/lucent`; Light Notes is at `D:/src/richicoder1/light-notes`. Preserve unrelated `.codex/`, `.dotnet-home/`, `advisor-plans/`, `docs/plans/windows-sandbox-testing.md`, and `docs/research/` content. Use explicit staging paths, serialize shared-tree builds and foreground tests, and follow [risk-based verification](verification.md). The Dev Drive is backed by `C:/DevDrive/Dev.vhdx`; if `D:` disappears after restart, inspect attachment state before changing anything. Do not format the volume, change partitions or relax ACLs.

Earlier asset, motion, focus-continuity, responsive-layout, native-menu and mixed-DPI records remain authoritative for their committed source boundaries. See [the component execution plan](../plans/component-delivery.md), [testing guide](../TESTING.md) and individual delivery issues for details.
