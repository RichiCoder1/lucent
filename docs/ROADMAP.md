# Native Lucent roadmap

## Current direction

The owner resumed native preview on October 4, 2026. Continue **native preview
#224, then developer diagnostics #243, then transfer #244**. Hot Reload #225 and
browser/WASM #226 remain conditional follow-ons with separate feasibility work.
The [handoff](agents/remaining-work-handoff.md) holds the source, CI, publication
and environment checkpoint. GitHub issue acceptance remains authoritative;
[Road to 1.0 #223](https://github.com/RichiCoder1/lucent/issues/223) is reconciled
with this resumed direction.

Composition-scoped `Design.IsDesignMode`
[#228](https://github.com/RichiCoder1/lucent/issues/228) is implemented and passes
compiler/editor, runtime and SDK/NativeAOT verification; the
[execution record](plans/design-mode-execution.md) retains review corrections and
candidate evidence. Explicit, owned preview scenarios
[#229](https://github.com/RichiCoder1/lucent/issues/229) pass the preview and
headless suites; their [implementation record](plans/native-preview-scenarios.md)
describes lifecycle ownership and dependency isolation. Trusted build and worker
orchestration [#230](https://github.com/RichiCoder1/lucent/issues/230) adds isolated
real-SDK builds, supervised Windows process trees and verified compiled Skia
frames. Its [execution record](plans/native-preview-orchestration.md) retains
automated and actual editor evidence, including the source-lock isolation fixes.
The bounded [preview panel #231](plans/native-preview-panel.md) adds scenario and
presentation controls, mapped diagnostics, acknowledged frame delivery and hidden
panel suspension, verified in the real development editor. Next is unsaved and
interactive preview #232, followed by external delivery #233. #227–231 are closed;
#232's [October 4 feasibility work](plans/native-preview-interactive.md) rejects
physical staged-source substitution because it loses original compiler paths
and nested analyzer settings. A public compiler-adapter experiment also fails
stock emission parity: diagnostic suppressions and generated checksums differ.
Unsaved preview remains disabled pending an explicit scope decision. Retained rendering reduces paint
allocations in the measured fixture, while PNG encoding remains the larger cost.
Supervisor v2 separates bounded/live ownership and passes its native/editor
checks. The internal persistent host passes its focused suite and is undergoing
adversarial review; the live protocol/editor integration remains in progress.
This partial implementation does not close #232.

The [native preview design](design/native-lui-preview/README.md) now prioritizes
the active `.lui` component: Follow editor by default, explicit Pin, automatic
activation for defaultable components and optional component-scoped data/variants.
The catalog remains execution infrastructure. Its
[authoring contract](design/native-lui-preview/component-preview-authoring.md)
separates verified file association [#327](https://github.com/RichiCoder1/lucent/issues/327)
from generated default activation [#328](https://github.com/RichiCoder1/lucent/issues/328)
and proposed development-only `.lui` declarations
[#329](https://github.com/RichiCoder1/lucent/issues/329). #329 follows #328 and retains
grammar planning review. #233 integrates all three alongside #232; these are not
capabilities implied by the current scenario picker.
Fresh-consumer onboarding #249 is verified: the [execution record](plans/onboarding-execution.md)
distinguishes actual template/debug/native proof from final official 110.1
installed-editor completion, hover and unsaved diagnostic recovery. Commit
`3205cbbd` fixes the Windows path-casing defect exposed by that journey.

At this October 4 checkpoint, the latest confirmed release is
`0.3.0-dev.117.1`, from `c6b6482e` after
[CI117](https://github.com/RichiCoder1/lucent/actions/runs/37254418694) passed managed,
package/native and publication jobs. The preceding 110.1 complete artifact was
independently authenticated for onboarding. Supported distribution uses authenticated GitHub Packages and
verified CI/local packages and VSIX; public NuGet.org/Marketplace publication is
not claimed. #231 is implemented at `77f1c69d`; its failed CI116 did not publish.
The test-only Windows path-alias correction is `c6b6482e`; CI117 confirms its
managed, package and publication jobs. The handoff preserves the failed CI116
and local reproduction evidence separately.

| Delivered work | Evidence and supported boundary |
| --- | --- |
| Review corrections #320–326 and Notes #11/#12 | [Review execution](plans/comprehensive-review-execution.md): data integrity, authoring, SVG/UIA, presentation and measured repeated-work corrections; the final Select gutter correction is published in 107.1 |
| Restoration and Windows activation #205/#221/#222 | [Implementation plan](plans/navigation-restoration-activation.md): explicit journal ownership, browser restart, registered transport in disposable Sandboxes and separate actual host/focus/close behavior |
| Release identity #245 | [Release sets](RELEASE-SETS.md): exact package, SDK, compiler and tool identities with authenticated delivery |
| Templates #246 and editor lifecycle #247 | [Templates](TEMPLATES.md) and [editor lifecycle](plans/editor-tooling-lifecycle.md): published in 107.1; package-only consumers and controlled cache/import/acquisition proof |
| Environment doctor #248 | [Doctor plan](plans/environment-doctor.md): read-only static checks plus explicit project, native-prerequisite and anonymous feed diagnostics; published in 108.1 |
| Onboarding #249 / #242 | [Execution record](plans/onboarding-execution.md): supported authenticated/local-package fallback, mapped debugging, managed/native interaction and official 110.1 editor semantics |
| Application authoring #301–309 | [Execution record](plans/application-routing-component-authoring-execution.md): named components, companions, generated roots and routing; published in 85.1 |
| Performance #313–319 | [Performance execution](plans/performance-execution.md): source-bound measurements and physical resize proof; published through 89.1 |

Light Notes consumes official `0.3.0-dev.101.1` at `8d5cdaa`, with
[green CI](https://github.com/RichiCoder1/light-notes/actions/runs/36776465450),
locked restores, formatting, managed checks and NativeAOT publication. The actual
minimum-window editor and save/reopen check passes. Computer Use walkthroughs are
authorized and their sessions end after use. Injected keypad navigation is
verified separately; physical main-arrow delivery is not claimed.

Design plans are not delivery or performance evidence. Historical CI failures and
the previous
chronological summary remain in the [earlier roadmap](https://github.com/RichiCoder1/lucent/blob/3205cbbd521f8171ffe751b924166d4826076b0d/docs/ROADMAP.md)
and their linked execution records; they are not current blockers.

Lucent is a Windows-first, NativeAOT-compatible desktop UI stack. The Issue Browser remains a maintained reference application. [Light Notes](https://github.com/RichiCoder1/light-notes) is the independently consumed links-and-notes application that expands and refines the framework surface through daily use. It now has app-owned SQLite persistence, capture, multiline draft editing, archive/restore, save retry, orderly close, and backup/export. Focused NativeAOT checks cover startup, durable save/reopen and maintenance commands. The responsive shell and component-local .lui state are implemented. Daily-use capture/edit/open flows, safe restoration and focused design/accessibility refinement are delivered. The desktop refinement adds durable incomplete drafts with explicit discard, per-collection browsing continuity, pointer editing, live sizing and accessible popup menus.

Production follows the decisions and contracts validated by the archived Native spike. The old implementation remains on [archive/avalonia-final](https://github.com/RichiCoder1/lucent/tree/archive/avalonia-final), and the complete spike record remains in the immutable [703d8e2 history tree](https://github.com/RichiCoder1/lucent/tree/703d8e267c6590603350db7819aa553822a30b87/docs/history/native-spike/). They are historical evidence, not active execution instructions.

Execution work lives in GitHub Issues and [Lucent Native Project 4](https://github.com/users/RichiCoder1/projects/4/views/1). This page records the supported boundary, remaining delivery order and deferred work. Completed foundations below are historical context, not instructions to restart them.

## Remaining Road to 1.0

Priority determines where to spend effort; issue dependencies determine what can
be implemented safely. Do not invent parent-level blockers between independent
streams or interrupt the preview work already in flight.

| Order | Bounded outcome | Technical sequence |
| --- | --- | --- |
| 1. [Native preview #224](https://github.com/RichiCoder1/lucent/issues/224) | Component-first activation and optional authored variants, consistent unsaved C#/.lui, interactive compiled frames, owned cleanup, then documented package-only consumption | #232 after completed #231; #327 association and #328 typed activation can proceed independently; #329 follows #328; #233 integrates #232/#327–329. Unsaved sequencing remains an owner decision after failed compiler probes |
| 2. [Developer diagnostics #243](https://github.com/RichiCoder1/lucent/issues/243) | Read-only committed snapshots, redaction, provenance/source origins, inspector and bounded frame diagnostics | #250 first; #251 and #252 can follow independently; #253 needs both; #254 needs #252; #255 integrates #253/#254 with preview |
| 3. [Transfer #244](https://github.com/RichiCoder1/lucent/issues/244) | Typed clipboard/drag transactions, Windows copy/link interoperability, bounded rich import and the two app consumers | #256 proves the native seam before #257; #258/#259/#261 follow #257; #260 follows drag routing, #262 follows clipboard and parser proof; #263 verifies the integrated framework |
| Conditional follow-ons | Hot Reload acceleration and a narrow browser preview target | #234 and #238 each follow #233; their reports decide whether #235–237 and #239–241 proceed. WASM has no Hot Reload prerequisite |

Diagnostics foundations #250–252 do not technically require preview. Final
integration #255 depends on #232's live-session owner, alongside #253/#254.
It reuses that session without making #232 depend on the inspector.
Transfer feasibility #256 has no new technical prerequisite. These facts permit
independent preparation; they do not change the selected priority order.

Transfer includes [Issue Browser #264](https://github.com/RichiCoder1/lucent/issues/264)
after #258/#260, and [Light Notes #10](https://github.com/RichiCoder1/light-notes/issues/10)
after #258/#260/#262 and the completed native picker #166. The framework gallery
can prove its own contracts independently; #244 remains open until its linked
app work is delivered or explicitly rescoped. Notes keeps plain-text storage,
safe HTML/RTF-to-text import and bounded text-file import; image attachments and
a rich-text editor are not implicit requirements.

### Finish #232 in reviewable slices

Keep one acceptance owner, with four implementation slices rather than another
broad framework refactor:

1. **Authoritative unsaved builds.** Prove original-path C#/.lui content overlays
   in both SDK preparation and final compilation. Preserve nested configuration,
   analyzer/generator path observations, linked project items and diagnostics.
   Track actual consumed overlay bytes separately from saved-disk freshness.
   A frozen two-document vector must compile together without saving buffers;
   failed or superseded builds never execute. Path mapping or `#line` alone is
   insufficient. If no supported compiler integration preserves this contract,
   record the failed proof and choose an explicit compiler-adapter design or
   scope change before expanding implementation.
2. **Persistent host and renderer.** Prove a small preview host and persistent Skia
   surface using the existing application lifecycle. Measure the retained
   resources before extracting any shared offscreen abstraction. Prove both
   input and application invalidation cause frames in the same worker, with
   explicit clock/idle behavior; keeping the testing host alive alone does not
   establish a continuously pumped preview.
3. **Supervised live session.** Join the frozen document vector to the persistent
   host through a bounded session protocol. A verified build/vector, supervisor
   started record and matching worker readiness permit live frames; only
   confirmed process-tree termination permits replacement or directory deletion.
   Keep stop independent of frame traffic, tie bounded production to display
   acknowledgment and reject obsolete builds or input. Uncertain cleanup retains
   quarantine. Do not enable unsaved refresh before the compiler proof passes.
4. **Editor interaction and integration.** Map pointer/wheel/key/committed text
   to the displayed frame,
   account for zoom/letterboxing/scale, and release owned input on focus loss or
   restart. Coalesce replaceable work without losing input edges. Rebuild/restart
   resets state. Prove representative actual-editor behavior and resource limits;
   keep detailed behavior matrices with their existing test owners.

The first two proofs can proceed independently; the third consumes both and the
fourth establishes the complete interactive contract. #233
then owns tool/worker distribution, representative package consumers, recovery
and measured support limits. Explicitly list supported workspace/host placements;
remote support must be demonstrated or excluded, not inferred from a webview.
Native chrome, popups, UIA and IME parity, runtime inspection, Hot Reload and
browser execution remain outside #232.

### 1.0 scope and remaining owner decisions

The recommended release cut is the existing Windows-first, .NET 10/NativeAOT,
`.lui`-first framework plus the accepted preview, diagnostics and transfer
program. Preserve attractive stock controls and the independent Notes consumer;
do not add another control catalogue or platform port merely to close 1.0.
This is a proposed release boundary, not a claim that the owner has frozen 1.0.

No new product decision blocks the current #232 proofs. Before declaring 1.0,
record the remaining release choices under #223:

- Which public C#/.lui, SDK/tooling and persisted-schema contracts become stable,
  and how version support and breaking changes will be handled.
- Which globalization, accessibility and real-language IME scenarios are in the
  supported release matrix, and which limitations are explicitly accepted.
- Whether the existing authenticated GitHub Packages/verified VSIX distribution
  is sufficient, or public NuGet.org/Marketplace delivery is part of 1.0.
- Whether successful Hot Reload/WASM feasibility justifies including either in
  1.0. Recommend that neither block the native release; a failed feasibility
  report requires an explicit defer/rescope decision, not fictitious completion.

Closing feature tickets does not make the release decision. Continue focused,
risk-based verification under [repository policy](agents/verification.md); reuse
unaffected evidence and reserve broader checks for concrete release risks.
Historic person-week estimates in parent designs require re-estimation against
current source and feasibility results; this roadmap sets no delivery dates.

## Delivered authoring and navigation

The approved [application roots, routing and component authoring design](plans/application-routing-component-authoring.md)
is delivered under [#301](https://github.com/RichiCoder1/lucent/issues/301) and #302–309.
Named LUI components, ordinary supporting declarations, optional companions, composable
application lifecycle hooks and declarative routing are published as `0.3.0-dev.85.1`
from `e15a43c`. [CI 85](https://github.com/RichiCoder1/lucent/actions/runs/35635336874)
passed managed and package/NativeAOT verification before publication. Component Browser
uses generated roots and route mappings; both inline and companion Windows sample apps
passed managed and NativeAOT execution. The [authoring guide](APPLICATION-AUTHORING.md)
documents the supported surface, and the [execution record](plans/application-routing-component-authoring-execution.md)
records exact evidence and limitations. Navigation restoration and Windows activation
[#205](https://github.com/RichiCoder1/lucent/issues/205) and environment diagnostics
#248 and the installed-editor journey #249 are delivered as recorded above.

Whole-file `.lui` formatting and linting [#295–300](plans/lui-formatting-and-linting.md)
implement the accepted source policy across the compiler, CLI, editor and SDK.
The [formatting guide](LUI-FORMATTING.md) documents configuration, scoped exceptions,
explicit semantic fixes and failure behavior. Repository adoption covers authored
C# and `.lui` in Lucent, every in-tree application and Light Notes; ordinary builds
never rewrite files. The implementation is published as `0.3.0-dev.79.1` after
[CI 79](https://github.com/RichiCoder1/lucent/actions/runs/34892962935) passed managed
and package-only NativeAOT verification. The [integration record](plans/lui-formatting-integration.md)
records preservation, performance and package findings. Tickets #299/#300 carry
the final publication and independent-consumer validation records. Light Notes
`8ac1175` consumes `79.1`, with formatting, tests and NativeAOT publication passing
in [its CI](https://github.com/RichiCoder1/light-notes/actions/runs/34895395190).

The [semantic capability refactor](plans/semantic-capabilities.md) #291–294 is delivered in `d44f265` and published as `0.3.0-dev.74.1`. Its implementation replaces positional declarations with validated typed capabilities and shares immutable payloads through metadata overlays and snapshots. Windows pattern discovery uses explicit capabilities while preserving existing command gates, confidential editing, and generation behavior. [The baseline comparison](plans/semantic-capabilities-baseline.md) records lower allocations in all five measured scenarios, including 16% for the virtualized-list projection. Local affected suites passed 828 tests; [CI 34805441710](https://github.com/RichiCoder1/lucent/actions/runs/34805441710) passed managed and NativeAOT/package-consumer verification before publishing the nine-package set.

The `.lui` assignment-callback diagnostic follow-up [#290](https://github.com/RichiCoder1/lucent/issues/290) is delivered in `5ebb236` and published as `0.3.0-dev.75.1` after [CI 34806634193](https://github.com/RichiCoder1/lucent/actions/runs/34806634193) passed managed, NativeAOT/package-consumer and publication jobs. All 72 compiler and 28 language-server local tests pass. It keeps the expression allowlist unchanged, highlights the assignment itself, and recommends a named method; [the language guide](LUI-LANGUAGE.md#c-expressions-and-reactivity) documents the boundary and workaround.

Typed composition context/service injection [#203](https://github.com/RichiCoder1/lucent/issues/203) and URI navigation [#204](https://github.com/RichiCoder1/lucent/issues/204) are delivered in `b3f3d59` and published as `0.3.0-dev.76.1`. The runtime, compiler, editor and Hosting contracts are implemented, and Issue Browser and Light Notes consume typed routes and declared requirements. [CI 34822437908](https://github.com/RichiCoder1/lucent/actions/runs/34822437908) passed managed and package-only NativeAOT verification before publication. Both applications pass focused native desktop checks; Light Notes is pinned to the official package. The [execution plan](plans/context-navigation-execution.md) maps joint verification #213 to the acceptance cases, and [typed navigation](NAVIGATION.md) describes the supported API. Restoration and Windows activation are delivered under #205 as recorded above; live compilation remains separate.

## Supported boundary

Review follow-ups #194–196, Light Notes #8 focus continuity, and the accepted [production transition plan](plans/transition-implementation.md) #198–202 are delivered. Lucent publishes that implementation as `0.3.0-dev.55.1`, and Light Notes independently consumes that immutable version. See the [handoff](agents/remaining-work-handoff.md) for source/CI records and the [transition guide](TRANSITIONS.md) for the supported contract and measured limits.

The delivered [component program](plans/component-delivery.md) #155–171 expands stock controls through fields and validation, selection, owned surfaces, numeric/date editing, confidential input, asynchronous choices, trees, read-only tables and native file selection. The component program was published as `0.3.0-dev.60.1`; subsequent Light Notes package adoption is recorded in the current execution section above. The maintained Component Browser demonstrates fourteen compiled `.lui` examples using stock presentation. [The component guide](COMPONENTS.md) describes the contracts; the program plan records verification limits, including the pending additional manual Computer Use walkthrough. Context/navigation #203/#204 and their children #206–220 are implemented as described above; optional container and later navigation capabilities remain follow-ups alongside live compilation.

The [C# authoring phase #265](https://github.com/RichiCoder1/lucent/issues/265) is delivered as `0.3.0-dev.73.1` ahead of context/navigation. The preserved [#266 feasibility gate](adr/0008-bounded-csharp-authoring.md) established closed recipe capabilities, conversion and overload behavior, package-only generation and the owned semantic-binding seam. The integrated implementation adds the mount-owned `ComponentContext`, generated style metadata and fluency, capability-bearing stock factories, ordered live accessibility metadata, generated partial component state, shared live-label binding, bounded retained Drawing and a portable Gauge. `.lui` remains the primary stock UI authoring direction; the C# surface uses the same recipes and runtime. `Slider`, `ListBox` and `VirtualizedList` intentionally expose style-only authoring because their semantics live on descendants. Managed suites, the SDK suite, and package-only C#/.lui managed and NativeAOT execution pass. CI 34781813597 verifies and publishes all nine packages. The handoff records exact artifacts and desktop observations; the final focused visual repeat passed after the native Computer Use helper restarted, covering source-note wrapping, counter lifecycle, and Gauge updates and presentation in all three themes. This is prerelease verification, not release certification. Callback diagnostic follow-up #290 is delivered as described above.

Metadata-only Find References [#276](https://github.com/RichiCoder1/lucent/issues/276), interaction review #279–283 and desktop refinements #287–289 are complete. The latter eight workflows pass on a source-bound NativeAOT TestHost, including physical dropdown keys/Tab, viewport paging, numeric stepping, calendar recovery and nested menu dismissal. See the [handoff](agents/remaining-work-handoff.md) for exact package and verification boundaries.

- Windows 11 24H2+, win-x64, .NET 10 LTS, NativeAOT, and trimming.
- Typed C# composition and preview `.lui` over the same framework contracts. `.lui` is the primary UI authoring direction; C# remains the underlying semantic/runtime API.
- Reactive state, derived state, batching, scopes, explicit source-driven async resources and retry, owned external callback dispatch, deterministic disposal, and stable keyed composition.
- Bounded explicit Grid and Flex-style rows/columns, retained generic layout with custom nonvirtualizing algorithms, named logical window breakpoints, responsive assigned constraints, constrained paragraphs, scrolling, fixed-height keyed virtualization, typed styles, parameterized `.lui` styles and reactive conditions, semantic tokens, live token-valued property choices, inset borders/hairlines and independent focus rings, rounded surfaces/clips, font weights, finite variants, theme settings, reduced-motion settings, and composition-owned visual transitions for solid Background, TextColor and Opacity. [Declarative `.lui` policies](TRANSITIONS.md) provide finite scheduling, interruption, reduced-motion suppression and retained paint reuse; layout and entry/exit animation remain outside the first slice.
- Text, PNG/JPEG and bounded static SVG Image/Icon with owned asynchronous preparation, optional Lucide assets, leading-icon buttons and labeled IconButton, shared window/executable artwork, panel/layout primitives, single-line text field, multiline text area, selectable/list row, scroll viewport, virtualized list, loading/progress, simple error state, stock text/density roles, an optional minimal presentation base, accessible resizable split panes, and context menus with nested command groups and separators.
- Plain-text editing with composition support and a bounded 20,000-unit multiline workload, hoistable editor/viewport sessions, application-owned text focus targets, explicit hidden/collapsed participation, wheel/trackpad scrolling, application command scopes and chords, portable cursor intent, Lucent-rendered popup menus beyond the owner client, live native resizing, plus bounded UI Automation patterns and stale-node handling for the reference controls.
- Optional R3 debounce integration with explicit clocks and owner-thread callbacks.
- Optional Microsoft hosting integration with negotiated asynchronous shutdown and accepted-work recovery.
- Deterministic tree, reactive, layout, style, semantic, scene, and timing dumps.
- Persistent CPU Skia and SDL presentation with explicit backing-pixel scaling, deterministic local data, fake async behavior, and an optional live GitHub adapter.
- External contract, published application, SDK, performance, and accessibility checks that keep proof orchestration out of the application.

The [layout and paragraph decision](adr/0004-layout-and-paragraphs.md) selects the managed extension now used for the bounded Grid/Flex, responsive constraints, constrained virtualization, and wrapped-text contracts.

## Light Notes direction and delivered foundations

The independent application lives in [RichiCoder1/light-notes](https://github.com/RichiCoder1/light-notes). Its package consumption and durable daily-use workflow are established. The next accepted application feature is transfer adoption in #10 after the framework work above; further product refinements should follow observed use.

The original [application and framework plan](plans/links-and-notes-draft.md), [experience design](design/links-and-notes/README.md) and [responsive visual board](design/links-and-notes/VISUAL.md) retain the design rationale and authoring examples. Their original sequence is historical; current execution specifications and status belong in GitHub Issues and Project 4.

Build a polished, local, single-user link inbox that also supports standalone notes. The owner and coding agents on the owner's Windows machine are the primary audience, with reproducible setup for other contributors. The application should make capture, editing, retrieval, opening, and archiving comfortable across wide, medium, and compact window arrangements.

The delivered foundations include Grid, Flex-style Row/Column, responsive composition, reusable layout/text/presentation, keyboard and accessibility capabilities, DI/hosting and local persistence. The managed layout evaluation selected Lucent-owned Core code; Taffy remains a credited historical alternative rather than an adopted dependency. Light Notes uses Microsoft.Data.Sqlite behind an app-owned serialized worker, schema and close policy; Lucent does not own application storage.

Continue developing design quality alongside complete application slices and prioritize authoring conveniences from observed friction. Higher-level source-owned component recipes can build on the delivered composition, live-data, command and editor-session contracts later.

## Delivered Light Notes refinements

The independent September 8 review is implemented through [#119](https://github.com/RichiCoder1/lucent/issues/119): reactive ownership, compiler binding, text rendering, input, popup placement and desktop editing fixes, together with measured reactive, paragraph-cache, accessibility-navigation and tooling improvements. [#143](https://github.com/RichiCoder1/lucent/issues/143) organizes the stock component families and adds the `.lui`-authored ErrorNotice used by Issue Browser; [#152](https://github.com/RichiCoder1/lucent/issues/152) delivers public `.lui` XML documentation. CI covers managed and NativeAOT contracts plus package-only consumption. Issue comments record publication, Light Notes integration, source-bound checks and remaining limitations. Physical mixed-DPI popup/input and UIA geometry checks are complete in #153; real-language IME certification remains separate.

The accepted [desktop capabilities plan](plans/desktop-capabilities.md) applies the fresh review through #96–#100: controlled selection, single-line editing, durable drafts and collection continuity, live sizing/cursor intent, and Lucent-rendered menus that extend beyond the owner window. #95 covers observed authoring friction and documentation consistency. #102 adds reactive token selection inside already-live individual style assignments while retaining construction-time token choice for snapshots and standalone styles; whole-`Style` replacement remains deferred. Package identities and focused verification are recorded in the tickets. Draft persistence uses one ordered writer per note; popup commands retain their application owner after dismissal. Opt-in native platform presentation is delivered in #101.

Owner interaction review exposed a new refinement batch: [#91 long-note freeze and Archive exit](https://github.com/RichiCoder1/lucent/issues/91), [#92 hover/pressed and app presentation](https://github.com/RichiCoder1/lucent/issues/92), [#93 Windows caret/cursor](https://github.com/RichiCoder1/lucent/issues/93), and [#94 default themeable scrollbars](https://github.com/RichiCoder1/lucent/issues/94). The interaction changes and Issue Browser theme adoption are implemented; the local NativeAOT app passed its four maintained desktop checks. Final package/app delivery is recorded in those issues. The owner accepted closure of #91 after no further crashes; the original intermittent Archive exit still has no confirmed root cause. Bounded diagnostics remain available if it recurs. The previous closeout covered its recorded automated paths; it did not validate every interaction state. See [interaction refinement](plans/desktop-interaction-refinement.md).

The [daily-use execution](plans/daily-use-execution.md) is delivered: #80–#90 cover the responsive shell, 750 ms autosave, complete workflow, safe restoration, presentation, explicit async resources, optional R3 integration and CI/review improvements. Published native interaction and targeted accessibility checks are complete. Use the [Light Notes review guide](https://github.com/RichiCoder1/light-notes/blob/main/docs/MANUAL-REVIEW.md) to collect concrete product, framework and .lui feedback before selecting the next chunk. Records, expression-bodied markup and a component registry remain deferred.

The original [architecture/code review](../plans/architecture-review.md) and
[#63 foundation plan](https://github.com/RichiCoder1/lucent/issues/63) retain the
completed implementation sequence. They do not reopen #64–81 or add acceptance
gates to the current work.

## Deferred directions

- Broader platform styles and native controls. Opt-in standard Windows menus and retained scrollbar presets are delivered in [#101](https://github.com/RichiCoder1/lucent/issues/101); nested menus and bounded safe-triangle pointer intent are implemented in [#104](https://github.com/RichiCoder1/lucent/issues/104).

- Additional substantial samples and maintained ports after the first useful links-and-notes application.
- A broader control catalog beyond the delivered component program. The 1.0 compatibility policy is an explicit release decision above, not an indefinitely deferred feature.
- Runtime CSS/selectors, general templates, runtime token/theme import, and Linux desktop theme integration.
- Sync, automatic page extraction, rich-text editing, and elaborate organization for the links-and-notes application.
- Exhaustive IME or accessibility certification, editable tables, broader menu capabilities and plugin loading. Bounded drag/drop and rich import are accepted in #244; external Move, virtual-file streams and full rich editing need separate application demand and native proof. Password input, read-only tables, trees, tabs and dialogs are delivered in the component program.
- Production GPU presentation, win-arm64, macOS, and Wayland until measured demand and new platform evidence justify them.
- .NET 11 experiments after GA only when dependency support, warning-clean NativeAOT publication, reproducible tooling, and measured benefit are established.

## Reference application

The deterministic stock-themed Issue Browser exercises adaptive list/detail navigation, retained split-pane resizing, nested context actions, loading/error/retry, filtering, 10,000 keyed rows, keyboard traversal, focus and selection, details presentation, Unicode editing and IME composition, theme and reduced-motion changes, scrolling and keyed reorder/removal, UI Automation Value/Invoke/Selection/Scroll behavior, diagnostic dumps, fake HTTP transport, and win-x64 NativeAOT packaging. Proof and capture code stay outside the application.

## Authoring and verification

The accepted .lui language and SDK/tooling boundaries live in [LUI-LANGUAGE.md](LUI-LANGUAGE.md), [LUI-SDK-TOOLING.md](LUI-SDK-TOOLING.md), and [ADR 0002](adr/0002-lui-authoring-surface.md). The architecture and domain glossary remain the authoritative framework vocabulary.

Use [TESTING.md](TESTING.md) for repository test scope and [pre-release verification](agents/verification.md) for risk-based check selection. Keep issue-specific evidence compact and source-bound; retain large captures, binaries, and traces as external artifacts.

## Headless testing and platform presentation

The selected execution order is #103 followed by #101, described in [Headless tests and Windows presentation](plans/testing-and-platform-presentation.md). #101 now includes the owner-requested menu contrast and depth refinement alongside opt-in Windows presentation. #91 was closed at the owner's request after no further crashes; no root cause is claimed for the historical intermittent Archive exit.

#103 and #101 are delivered. Issue Browser demonstrates stock themes, refined Lucent popup presentation and opt-in native menus through the same `.lui` row commands. Focused published checks cover native keyboard, pointer and UIA invocation, preserved selection, outside-owner placement and owner shutdown. [Windows presentation](WINDOWS-PRESENTATION.md) describes the opt-in API and fallback behavior. Nested menus and safe-triangle pointer intent extend this foundation in #104.

The accepted [stock-theme Issue Browser plan](plans/stock-theme-issue-browser.md) is implemented through #104–#107: submenus, polished shared presentation with an optional minimal base, an accessible split pane, and an adaptive fixture-backed reference application with no application palette/token overrides. [Light Notes #2](https://github.com/RichiCoder1/light-notes/issues/2) consumes that framework release while preserving its existing identity and data behavior. Follow-up #108 corrects wrapped-row auto height through constrained ancestors.

The accepted [style-driven layout design](plans/style-driven-layout.md) and [ADR 0006](adr/0006-style-driven-layout.md) are implemented through [#109](https://github.com/RichiCoder1/lucent/issues/109), [#110](https://github.com/RichiCoder1/lucent/issues/110) and [#111](https://github.com/RichiCoder1/lucent/issues/111): retained layout algorithms, reactive style conditions, named window breakpoints, parameterized `.lui` styles with editor support, and a retained Issue Browser workspace. [Light Notes #3](https://github.com/RichiCoder1/light-notes/issues/3) applies the same mechanism at its 840/1060 logical-width thresholds through immutable framework packages. Application layout decisions live in styles while data, route intent and interaction state retain their owners. Container queries and new containment/ancestor-lookup APIs are explicitly deferred. Package identities and validation results belong in the delivery issues.

The confirmed layout findings from the independent follow-up review are addressed through [#112](https://github.com/RichiCoder1/lucent/issues/112) for predicate short-circuiting, [#113](https://github.com/RichiCoder1/lucent/issues/113) for constrained measurement and allocation edge cases, and [#115](https://github.com/RichiCoder1/lucent/issues/115) for breakpoint readers mounted by responsive branches. The focused follow-ups are implemented and published: [#117](https://github.com/RichiCoder1/lucent/issues/117) unifies rounded, scrollbar-aware paragraph width; [#116](https://github.com/RichiCoder1/lucent/issues/116) specifies Grid allocation and fixes spanning/overflow alignment; [#118](https://github.com/RichiCoder1/lucent/issues/118) identifies custom-layout failures and documents breakpoint/algorithm lifetimes; [#114](https://github.com/RichiCoder1/lucent/issues/114) reuses resolved styles within one projection. [Projection evidence](SCENE-PROJECTION-EVIDENCE.md) records the measured improvements and remaining allocation costs. Broad dirty-subtree caching and a fixed latency budget are not claimed as delivered behavior.

[#103](https://github.com/RichiCoder1/lucent/issues/103) delivers a small headless harness from existing composition, scene and application tests. It mounts real `.lui` components, routes simulated input through production code, controls time and queued work, and optionally renders frames with Skia. Avalonia.Headless is an architectural reference, not a Lucent dependency. The maintained suite includes representative test migrations; retain native desktop checks for Windows focus/capture, resizing, popup placement, clipboard and UIA. Headless semantics do not validate the Windows accessibility bridge. Nested menus and safe-triangle pointer intent are implemented in [#104](https://github.com/RichiCoder1/lucent/issues/104).

## Component organization and framework authoring

The [component organization and framework-authored `.lui` plan](plans/core-component-organization.md), tracked in [#143](https://github.com/RichiCoder1/lucent/issues/143), is implemented. Stock recipe, presentation and state code now live in component-family folders while the public `Lucent.Core.Components` type remains intact. Clean same-assembly generation produces the stock ErrorNotice used by Issue Browser. C# retains runtime primitives and platform behavior; `.lui` remains the preferred composition and component-local-state surface. See [Core components](COMPONENTS.md). Public component XML comments now flow into generated factories and source/metadata tooling through [#152](https://github.com/RichiCoder1/lucent/issues/152), with package delivery recorded in the issue. A separate controls package and broad conversion remain deferred.

## Images, icons and packaged assets

[#144](https://github.com/RichiCoder1/lucent/issues/144) delivers typed packaged assets (#145) and owned asynchronous Image/Icon loading (#146), bounded static SVG (#147), the optional pinned Lucide pack and accessible icon controls (#148), and shared Windows/executable artwork (#149). The SDK and source-tree application use the same asset-generation targets, with package-only `.lui` and NativeAOT proofs (#150). Issue Browser uses stock Lucide controls and generated application artwork (#151); its control palette remains the framework default. [Light Notes #4](https://github.com/RichiCoder1/light-notes/issues/4) independently consumes the published packages, with real-shell resize, draft, accessibility and standalone NativeAOT artwork checks.

[#154](https://github.com/RichiCoder1/lucent/issues/154) adds measured JPEG admission with conservative handling for progressive, sequential multi-scan, CMYK and unknown headers. [Memory evidence](ASSET-MEMORY-EVIDENCE.md) records reproducible process/cache observations and their limits. The [accepted design](https://github.com/RichiCoder1/lucent/issues/144#issuecomment-5592602334) defines the contract and dependencies; [packaged assets](ASSETS.md) documents the implemented authoring surface. Commit, package and CI identities belong in the delivery issues.

The existing focused backlog (#114, #116, #117, #118 and #152) is delivered. The agreed fresh Fable review has returned through the Code Review task; its [remediation plan](plans/fable-review-remediation.md) separates reproduced fixes from unconfirmed risks and later design choices. Confirmed framework fixes #172–193 and #197 are published from `905bd1c` as `0.3.0-dev.53.1`, with green managed, NativeAOT and package-consumer CI. They cover lifecycle and image-cache recovery, input convergence and geometry, focus contrast, compiler/editor correctness and scaling, and executable consumer coverage. Light Notes independently adopts them alongside its recovery/keyboard fixes at `4c3b47c`, with green managed/NativeAOT CI and seven passing published desktop workflows on the app/fix commit. [#172](https://github.com/RichiCoder1/lucent/issues/172) separates internal style-value resolution from public diagnostic provenance while preserving precedence, reactive reads and projection freshness. Physical mixed-DPI validation (#153) passed two opt-in tests on actual 100%/150% displays, covering open popup/submenu transitions, pointer editing, native focus and multiline UIA text geometry. Follow-up investigations #194–196 are resolved in the current batch, alongside opt-in focus recovery for Light Notes #8. Publication and independent application adoption are recorded in the [handoff](agents/remaining-work-handoff.md). The separately designed [Component Gaps](https://github.com/RichiCoder1/lucent/issues/155) work is ordered in its own child waves and does not displace review remediation.
