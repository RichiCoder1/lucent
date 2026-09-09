# Native Lucent roadmap

## Current direction

Lucent is a Windows-first, NativeAOT-compatible desktop UI stack. The Issue Browser remains a maintained reference application. [Light Notes](https://github.com/RichiCoder1/light-notes) is the independently consumed links-and-notes application that expands and refines the framework surface through daily use. It now has app-owned SQLite persistence, capture, multiline draft editing, archive/restore, save retry, orderly close, and backup/export. Focused NativeAOT checks cover startup, durable save/reopen and maintenance commands. The responsive shell and component-local .lui state are implemented. Daily-use capture/edit/open flows, safe restoration and focused design/accessibility refinement are delivered. The desktop refinement adds durable incomplete drafts with explicit discard, per-collection browsing continuity, pointer editing, live sizing and accessible popup menus.

Production follows the decisions and contracts validated by the archived Native spike. The old implementation remains on [archive/avalonia-final](https://github.com/RichiCoder1/lucent/tree/archive/avalonia-final), and the complete spike record remains in the immutable [703d8e2 history tree](https://github.com/RichiCoder1/lucent/tree/703d8e267c6590603350db7819aa553822a30b87/docs/history/native-spike/). They are historical evidence, not active execution instructions.

Execution work lives in GitHub Issues and the Lucent Native Project 4. Issue acceptance is authoritative. Issue #61 records repository cleanup and testing modernization, while issue #62 tracks pinned independent setup for the first links-and-notes application. This page records the supported boundary, agreed development direction, and deferred work. The next-development section is planning guidance, not a claim of implemented support.

## Supported boundary

- Windows 11 24H2+, win-x64, .NET 10 LTS, NativeAOT, and trimming.
- Typed C# composition and preview `.lui` over the same framework contracts. `.lui` is the primary UI authoring direction; C# remains the underlying semantic/runtime API.
- Reactive state, derived state, batching, scopes, explicit source-driven async resources and retry, owned external callback dispatch, deterministic disposal, and stable keyed composition.
- Bounded explicit Grid and Flex-style rows/columns, retained generic layout with custom nonvirtualizing algorithms, named logical window breakpoints, responsive assigned constraints, constrained paragraphs, scrolling, fixed-height keyed virtualization, typed styles, parameterized `.lui` styles and reactive conditions, semantic tokens, live token-valued property choices, inset borders/hairlines and independent focus rings, rounded surfaces/clips, font weights, finite variants, theme settings, reduced-motion settings, and experimental manual transition facilities. Full scheduled transitions are a [dedicated design follow-up (#142)](https://github.com/RichiCoder1/lucent/issues/142) to #119/#140; production animation support is not yet claimed.
- Text, PNG/JPEG Image and Icon with owned asynchronous preparation, panel/layout primitives, button, single-line text field, multiline text area, selectable/list row, scroll viewport, virtualized list, loading/progress, simple error state, stock text/density roles, an optional minimal presentation base, accessible resizable split panes, and context menus with nested command groups and separators.
- Plain-text editing with composition support and a bounded 20,000-unit multiline workload, hoistable editor/viewport sessions, application-owned text focus targets, explicit hidden/collapsed participation, wheel/trackpad scrolling, application command scopes and chords, portable cursor intent, Lucent-rendered popup menus beyond the owner client, live native resizing, plus bounded UI Automation patterns and stale-node handling for the reference controls.
- Optional R3 debounce integration with explicit clocks and owner-thread callbacks.
- Optional Microsoft hosting integration with negotiated asynchronous shutdown and accepted-work recovery.
- Deterministic tree, reactive, layout, style, semantic, scene, and timing dumps.
- Persistent CPU Skia and SDL presentation with explicit backing-pixel scaling, deterministic local data, fake async behavior, and an optional live GitHub adapter.
- External contract, published application, SDK, performance, and accessibility checks that keep proof orchestration out of the application.

The [layout and paragraph decision](adr/0004-layout-and-paragraphs.md) selects the managed extension now used for the bounded Grid/Flex, responsive constraints, constrained virtualization, and wrapped-text contracts.

## Next development: Light Notes

The independent application lives in [RichiCoder1/light-notes](https://github.com/RichiCoder1/light-notes). Its independent package consumption and first durable workflow are established; the sections below describe the remaining product and framework direction.

The [draft application and framework plan](plans/links-and-notes-draft.md) captures the agreed direction, proposed implementation sequence, and open technical choices. The [experience design](design/links-and-notes/README.md) and [responsive visual board](design/links-and-notes/VISUAL.md) provide proposed application flows and authoring examples. Execution specifications and status belong in GitHub Issues and Project 4.

Build a polished, local, single-user link inbox that also supports standalone notes. The owner and coding agents on the owner's Windows machine are the primary audience, with reproducible setup for other contributors. The application should make capture, editing, retrieval, opening, and archiving comfortable across wide, medium, and compact window arrangements.

The first effort includes Grid, a coherent Flex-style Row/Column surface, responsive composition, and strong reusable layout, text, presentation, keyboard, and accessibility capabilities. Establish DI/hosting and local persistence through ecosystem components, with NativeAOT and the existing portable Core boundary intact. The managed layout evaluation selected Lucent-owned Core code; Taffy remains a credited historical alternative rather than an adopted dependency. Light Notes uses Microsoft.Data.Sqlite behind an app-owned serialized worker, schema and close policy; Lucent does not own application storage.

Develop design quality alongside complete application slices. Deliver required `.lui` content composition, live-data, command, and editor-session contracts with those slices; prioritize additional authoring conveniences from observed friction afterward. Higher-level source-owned component recipes can build on the foundations later. Continue using the current risk-based verification policy.

## Current execution

The independent September 8 review is implemented through [#119](https://github.com/RichiCoder1/lucent/issues/119): reactive ownership, compiler binding, text rendering, input, popup placement and desktop editing fixes, together with measured reactive, paragraph-cache, accessibility-navigation and tooling improvements. [#143](https://github.com/RichiCoder1/lucent/issues/143) organizes the stock component families and adds the `.lui`-authored ErrorNotice used by Issue Browser. CI covers managed and NativeAOT contracts plus package-only consumption. Issue comments record publication, Light Notes integration, source-bound checks and remaining limitations. Focused follow-ups remain explicit; physical mixed-DPI validation is tracked in #153 and public `.lui` XML documentation in #152.

The accepted [desktop capabilities plan](plans/desktop-capabilities.md) applies the fresh review through #96–#100: controlled selection, single-line editing, durable drafts and collection continuity, live sizing/cursor intent, and Lucent-rendered menus that extend beyond the owner window. #95 covers observed authoring friction and documentation consistency. #102 adds reactive token selection inside already-live individual style assignments while retaining construction-time token choice for snapshots and standalone styles; whole-`Style` replacement remains deferred. Package identities and focused verification are recorded in the tickets. Draft persistence uses one ordered writer per note; popup commands retain their application owner after dismissal. Opt-in native platform presentation is delivered in #101.

Owner interaction review exposed a new refinement batch: [#91 long-note freeze and Archive exit](https://github.com/RichiCoder1/lucent/issues/91), [#92 hover/pressed and app presentation](https://github.com/RichiCoder1/lucent/issues/92), [#93 Windows caret/cursor](https://github.com/RichiCoder1/lucent/issues/93), and [#94 default themeable scrollbars](https://github.com/RichiCoder1/lucent/issues/94). The interaction changes and Issue Browser theme adoption are implemented; the local NativeAOT app passed its four maintained desktop checks. Final package/app delivery is recorded in those issues. The owner accepted closure of #91 after no further crashes; the original intermittent Archive exit still has no confirmed root cause. Bounded diagnostics remain available if it recurs. The previous closeout covered its recorded automated paths; it did not validate every interaction state. See [interaction refinement](plans/desktop-interaction-refinement.md).

The [daily-use execution](plans/daily-use-execution.md) is delivered: #80–#90 cover the responsive shell, 750 ms autosave, complete workflow, safe restoration, presentation, explicit async resources, optional R3 integration and CI/review improvements. Published native interaction and targeted accessibility checks are complete. Use the [Light Notes review guide](https://github.com/RichiCoder1/light-notes/blob/main/docs/MANUAL-REVIEW.md) to collect concrete product, framework and .lui feedback before selecting the next chunk. Records, expression-bodied markup and a component registry remain deferred.

## Original foundation sequence

The [architecture/code review](../plans/architecture-review.md) informs this sequence; it is a historical audit, not an acceptance gate. GitHub records implementation status.

1. Correct parser/tooling, empty-field, wake-delivery, and UIA defects; design the responsive experience in parallel.
2. Establish retained current-item updates, `.lui` content composition, host shutdown/service ownership, editor sessions, and explicit responsive participation.
3. Resolve constrained paragraph measurement and the layout engine; prove pinned independent startup and ordered local persistence; reduce per-event input work.
4. Implement shared Grid/Flex/wrapped text, constrained virtualization, multiline editing, wheel/trackpad routing, and application commands.
5. Compose the responsive `.lui` inbox/editor (#80), then review the app and framework/authoring experience with the owner before completing the durable capture/edit/find/open/archive/reopen loop (#81).
6. Refine daily-use design, recovery and accessibility, then select further authoring QoL from observed friction.

GitHub blocker relationships determine what must finish first. Independent fixes and the hosting/storage and layout tracks need not be serialized. Keep focused verification, NativeAOT/trimming correctness, and source-map/freshness guarantees; no new milestone gates or routine full-release runs.

Track execution in [#63](https://github.com/RichiCoder1/lucent/issues/63), its 21 sub-issues, and [Project 4](https://github.com/users/RichiCoder1/projects/4). Start with independent fixes [#64–#68](https://github.com/RichiCoder1/lucent/issues/63) and design [#69](https://github.com/RichiCoder1/lucent/issues/69). The [plan ticket index](plans/links-and-notes-draft.md#ordered-implementation-sequence) records the full order and blockers; GitHub owns live status.

## Deferred directions

- Broader platform styles and native controls. Opt-in standard Windows menus and retained scrollbar presets are delivered in [#101](https://github.com/RichiCoder1/lucent/issues/101); nested menus and bounded safe-triangle pointer intent are implemented in [#104](https://github.com/RichiCoder1/lucent/issues/104).

- Additional substantial samples and maintained ports after the first useful links-and-notes application.
- Stable API and package compatibility, a 1.0 contract, and a broader control catalog.
- Runtime CSS/selectors, general templates, runtime token/theme import, and Linux desktop theme integration.
- Sync, automatic page extraction, rich-text editing, and elaborate organization for the links-and-notes application.
- Exhaustive IME or accessibility certification, password input, drag/drop, tables, trees, tabs, broader menu capabilities, dialogs, and plugin loading until a selected application flow justifies their scope.
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

The confirmed layout findings from the independent follow-up review are addressed through [#112](https://github.com/RichiCoder1/lucent/issues/112) for predicate short-circuiting, [#113](https://github.com/RichiCoder1/lucent/issues/113) for constrained measurement and allocation edge cases, and [#115](https://github.com/RichiCoder1/lucent/issues/115) for breakpoint readers mounted by responsive branches. Follow-ups are [#117](https://github.com/RichiCoder1/lucent/issues/117) for a fractional paragraph-width repro, [#116](https://github.com/RichiCoder1/lucent/issues/116) for Grid/overflow semantics, [#118](https://github.com/RichiCoder1/lucent/issues/118) for custom-layout diagnostics and collapsed algorithm lifetime, and [#114](https://github.com/RichiCoder1/lucent/issues/114) for measurement-led projection optimization. Broad dirty-subtree caching and a fixed latency budget are not claimed as delivered behavior.

[#103](https://github.com/RichiCoder1/lucent/issues/103) delivers a small headless harness from existing composition, scene and application tests. It mounts real `.lui` components, routes simulated input through production code, controls time and queued work, and optionally renders frames with Skia. Avalonia.Headless is an architectural reference, not a Lucent dependency. The maintained suite includes representative test migrations; retain native desktop checks for Windows focus/capture, resizing, popup placement, clipboard and UIA. Headless semantics do not validate the Windows accessibility bridge. Nested menus and safe-triangle pointer intent are implemented in [#104](https://github.com/RichiCoder1/lucent/issues/104).

## Component organization and framework authoring

The [component organization and framework-authored `.lui` plan](plans/core-component-organization.md), tracked in [#143](https://github.com/RichiCoder1/lucent/issues/143), is implemented. Stock recipe, presentation and state code now live in component-family folders while the public `Lucent.Core.Components` type remains intact. Clean same-assembly generation produces the stock ErrorNotice used by Issue Browser. C# retains runtime primitives and platform behavior; `.lui` remains the preferred composition and component-local-state surface. See [Core components](COMPONENTS.md). A separate controls package and broad conversion remain deferred.

## Images, icons and packaged assets

[#144](https://github.com/RichiCoder1/lucent/issues/144) is active. Typed packaged assets (#145) and owned asynchronous PNG/JPEG Image/Icon loading (#146) are implemented, including `.lui` authoring, shared preparation, independent retained-frame leases, Skia rendering and headless package consumption. Next is a verified secure static SVG adapter (#147), followed by Lucide and accessible stock icon controls (#148), Windows and executable icons (#149), tooling and package documentation (#150), and Issue Browser adoption (#151). [Light Notes #4](https://github.com/RichiCoder1/light-notes/issues/4) is the independent proving consumer. [#154](https://github.com/RichiCoder1/lucent/issues/154) separately tracks progressive/CMYK JPEG measurements and less conservative admission where evidence permits. The [accepted design](https://github.com/RichiCoder1/lucent/issues/144#issuecomment-5592602334) defines the contract and dependencies; [packaged assets](ASSETS.md) documents the implemented authoring surface. SVG preparation and application artwork remain pending until their respective delivery checks pass; commit, package and CI identities belong in the delivery issues.
