# Native Lucent roadmap

## Current direction

Lucent is a Windows-first, NativeAOT-compatible desktop UI stack. The Issue Browser remains a maintained reference application. [Light Notes](https://github.com/RichiCoder1/light-notes) is the independently consumed links-and-notes application that expands and refines the framework surface through daily use. It now has app-owned SQLite persistence, capture, multiline draft editing, archive/restore, save retry, orderly close, and backup/export. Focused NativeAOT checks cover startup, durable save/reopen and maintenance commands. The responsive shell and component-local .lui state are implemented. Daily-use capture/edit/open flows, safe restoration and focused design/accessibility refinement are delivered. The next step is owner review of the app, framework and .lui experience.

Production follows the decisions and contracts validated by the archived Native spike. The old implementation remains on [archive/avalonia-final](https://github.com/RichiCoder1/lucent/tree/archive/avalonia-final), and the complete spike record remains in the immutable [703d8e2 history tree](https://github.com/RichiCoder1/lucent/tree/703d8e267c6590603350db7819aa553822a30b87/docs/history/native-spike/). They are historical evidence, not active execution instructions.

Execution work lives in GitHub Issues and the Lucent Native Project 4. Issue acceptance is authoritative. Issue #61 records repository cleanup and testing modernization, while issue #62 tracks pinned independent setup for the first links-and-notes application. This page records the supported boundary, agreed development direction, and deferred work. The next-development section is planning guidance, not a claim of implemented support.

## Supported boundary

- Windows 11 24H2+, win-x64, .NET 10 LTS, NativeAOT, and trimming.
- Typed C# composition and preview `.lui` over the same framework contracts. `.lui` is the primary UI authoring direction; C# remains the underlying semantic/runtime API.
- Reactive state, derived state, batching, scopes, explicit source-driven async resources and retry, owned external callback dispatch, deterministic disposal, and stable keyed composition.
- Bounded explicit Grid and Flex-style rows/columns, responsive logical constraints, constrained paragraphs, scrolling, fixed-height keyed virtualization, typed styles, semantic tokens, inset borders/hairlines and independent focus rings, rounded surfaces/clips, font weights, finite variants, theme settings, reduced motion, and bounded transitions.
- Text, panel/layout primitives, button, single-line text field, multiline text area, selectable/list row, scroll viewport, virtualized list, loading/progress, and simple error state.
- Plain-text editing with composition support and a bounded 20,000-unit multiline workload, hoistable editor/viewport sessions, application-owned text focus targets, explicit hidden/collapsed participation, wheel/trackpad scrolling, application command scopes and chords, plus bounded UI Automation patterns and stale-node handling for the reference controls.
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

Owner interaction review exposed a new refinement batch: [#91 long-note freeze and Archive exit](https://github.com/RichiCoder1/lucent/issues/91), [#92 hover/pressed and app presentation](https://github.com/RichiCoder1/lucent/issues/92), [#93 Windows caret/cursor](https://github.com/RichiCoder1/lucent/issues/93), and [#94 default themeable scrollbars](https://github.com/RichiCoder1/lucent/issues/94). Fix task-blocking behavior first and keep reusable behavior in Lucent. The previous closeout covered its recorded automated paths; it did not validate every interaction state. See [interaction refinement](plans/desktop-interaction-refinement.md).

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

- Additional substantial samples and maintained ports after the first useful links-and-notes application.
- Stable API and package compatibility, a 1.0 contract, and a broader control catalog.
- Runtime CSS/selectors, general templates, runtime token/theme import, and Linux desktop theme integration.
- Sync, automatic page extraction, rich-text editing, and elaborate organization for the links-and-notes application.
- Exhaustive IME or accessibility certification, password input, drag/drop, tables, trees, tabs, menus, dialogs, and plugin loading until a selected application flow justifies their scope.
- Production GPU presentation, win-arm64, macOS, and Wayland until measured demand and new platform evidence justify them.
- .NET 11 experiments after GA only when dependency support, warning-clean NativeAOT publication, reproducible tooling, and measured benefit are established.

## Reference application

The deterministic Issue Browser exercises loading/error/retry, filtering, 10,000 keyed rows, keyboard traversal, focus and selection, details presentation, Unicode editing and IME composition, theme and reduced-motion changes, scrolling and keyed reorder/removal, UI Automation Value/Invoke/Selection/Scroll behavior, diagnostic dumps, fake HTTP transport, and win-x64 NativeAOT packaging. Proof and capture code stay outside the application.

## Authoring and verification

The accepted .lui language and SDK/tooling boundaries live in [LUI-LANGUAGE.md](LUI-LANGUAGE.md), [LUI-SDK-TOOLING.md](LUI-SDK-TOOLING.md), and [ADR 0002](adr/0002-lui-authoring-surface.md). The architecture and domain glossary remain the authoritative framework vocabulary.

Use [TESTING.md](TESTING.md) for repository test scope and [pre-release verification](agents/verification.md) for risk-based check selection. Keep issue-specific evidence compact and source-bound; retain large captures, binaries, and traces as external artifacts.
