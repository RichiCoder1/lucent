# Native Lucent roadmap

## Current direction

Lucent is a Windows-first, NativeAOT-compatible desktop UI stack. The Issue Browser remains a maintained reference application. The next development effort is an independently consumed links-and-notes application that expands and refines the framework surface through daily use.

Production follows the decisions and contracts validated by the archived Native spike. The old implementation remains on [archive/avalonia-final](https://github.com/RichiCoder1/lucent/tree/archive/avalonia-final), and the complete spike record remains in the immutable [703d8e2 history tree](https://github.com/RichiCoder1/lucent/tree/703d8e267c6590603350db7819aa553822a30b87/docs/history/native-spike/). They are historical evidence, not active execution instructions.

Execution work lives in GitHub Issues and the Lucent Native Project 4. Issue acceptance is authoritative. Issue #61 records repository cleanup and testing modernization, while issue #62 tracks pinned independent setup for the first links-and-notes application. This page records the supported boundary, agreed development direction, and deferred work. The next-development section is planning guidance, not a claim of implemented support.

## Supported boundary

- Windows 11 24H2+, win-x64, .NET 10 LTS, NativeAOT, and trimming.
- Typed C# composition and preview `.lui` over the same framework contracts. `.lui` is the primary UI authoring direction; C# remains the underlying semantic/runtime API.
- Reactive state, derived state, batching, scopes, async generations, deterministic disposal, and stable keyed composition.
- Bounded rows, columns, layout, scrolling, fixed-height keyed virtualization, typed styles, semantic tokens, finite variants, theme settings, reduced motion, and bounded transitions.
- Text, panel/layout primitives, button, single-line text field, selectable/list row, scroll viewport, virtualized list, loading/progress, and simple error state.
- Basic international single-line editing with composition support, plus bounded UI Automation patterns and stale-node handling for the reference controls.
- Deterministic tree, reactive, layout, style, semantic, scene, and timing dumps.
- Persistent CPU Skia and SDL presentation with explicit backing-pixel scaling, deterministic local data, fake async behavior, and an optional live GitHub adapter.
- External contract, published application, SDK, performance, and accessibility checks that keep proof orchestration out of the application.

## Next development: links and notes

The [draft application and framework plan](plans/links-and-notes-draft.md) captures the agreed direction, proposed implementation sequence, and open technical choices. It is planning guidance; execution specifications and tickets belong in GitHub Issues and Project 4.

Build a polished, local, single-user link inbox that also supports standalone notes. The owner and coding agents on the owner's Windows machine are the primary audience, with reproducible setup for other contributors. The application should make capture, editing, retrieval, opening, and archiving comfortable across wide, medium, and compact window arrangements.

The first effort includes Grid, a coherent Flex-style Row/Column surface, responsive composition, and strong reusable layout, text, presentation, keyboard, and accessibility capabilities. Establish DI/hosting and local persistence through ecosystem components, with NativeAOT and the existing portable Core boundary intact. Exact APIs and dependencies remain implementation choices; Taffy and Microsoft.Data.Sqlite are candidates rather than adopted dependencies.

Develop design quality alongside complete application slices. Deliver required `.lui` content composition, live-data, command, and editor-session contracts with those slices; prioritize additional authoring conveniences from observed friction afterward. Higher-level source-owned component recipes can build on the foundations later. Continue using the current risk-based verification policy.

## Recommended execution order

The [architecture/code review](../plans/architecture-review.md) informs this sequence; it is a historical audit, not an acceptance gate. Source fixes are still pending.

1. Correct parser/tooling, empty-field, wake-delivery, and UIA defects; design the responsive experience in parallel.
2. Establish retained current-item updates, `.lui` content composition, host shutdown/service ownership, editor sessions, and explicit responsive participation.
3. Resolve constrained paragraph measurement and the layout engine; prove pinned independent startup and ordered local persistence; reduce per-event input work.
4. Implement shared Grid/Flex/wrapped text, constrained virtualization, multiline editing, wheel/trackpad routing, and application commands.
5. Compose the responsive `.lui` inbox/editor and complete the durable capture/edit/find/open/archive/reopen loop.
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
