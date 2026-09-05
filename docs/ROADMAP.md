# Native Lucent roadmap

## Current direction

Lucent is a Windows-first, NativeAOT-compatible desktop UI stack. The Issue Browser is the maintained reference application and the design pressure for the framework surface; it is not a test harness.

Production follows the decisions and contracts validated by the archived Native spike. The old implementation remains on [archive/avalonia-final](https://github.com/RichiCoder1/lucent/tree/archive/avalonia-final), and the complete spike record remains in the immutable [703d8e2 history tree](https://github.com/RichiCoder1/lucent/tree/703d8e267c6590603350db7819aa553822a30b87/docs/history/native-spike/). They are historical evidence, not active execution instructions.

Execution work lives in GitHub Issues and the Lucent Native Project 4. Issue acceptance is authoritative. Issue #61 records repository cleanup and testing modernization, while issue #62 tracks the separate public samples repository. This page records the current boundary and deferred directions.

## Supported boundary

- Windows 11 24H2+, win-x64, .NET 10 LTS, NativeAOT, and trimming.
- Typed C# composition and preview .lui authoring over the same framework contracts.
- Reactive state, derived state, batching, scopes, async generations, deterministic disposal, and stable keyed composition.
- Bounded rows, columns, layout, scrolling, fixed-height keyed virtualization, typed styles, semantic tokens, finite variants, theme settings, reduced motion, and bounded transitions.
- Text, panel/layout primitives, button, single-line text field, selectable/list row, scroll viewport, virtualized list, loading/progress, and simple error state.
- Basic international single-line editing with composition support, plus bounded UI Automation patterns and stale-node handling for the reference controls.
- Deterministic tree, reactive, layout, style, semantic, scene, and timing dumps.
- Persistent CPU Skia and SDL presentation with explicit backing-pixel scaling, deterministic local data, fake async behavior, and an optional live GitHub adapter.
- External contract, published application, SDK, performance, and accessibility checks that keep proof orchestration out of the application.

## Deferred directions

- A public lucent-samples repository and maintained ports tracked by [issue #62](https://github.com/RichiCoder1/lucent/issues/62).
- Stable API and package compatibility, a 1.0 contract, and a broader control catalog.
- Runtime CSS/selectors, general templates, runtime token/theme import, and Linux desktop theme integration.
- Exhaustive IME or accessibility certification, multiline/rich text, password input, drag/drop, tables, trees, tabs, menus, dialogs, plugin loading, and persistence.
- Production GPU presentation, win-arm64, macOS, and Wayland until measured demand and new platform evidence justify them.
- .NET 11 experiments after GA only when dependency support, warning-clean NativeAOT publication, reproducible tooling, and measured benefit are established.

## Reference application

The deterministic Issue Browser exercises loading/error/retry, filtering, 10,000 keyed rows, keyboard traversal, focus and selection, details presentation, Unicode editing and IME composition, theme and reduced-motion changes, scrolling and keyed reorder/removal, UI Automation Value/Invoke/Selection/Scroll behavior, diagnostic dumps, fake HTTP transport, and win-x64 NativeAOT packaging. Proof and capture code stay outside the application.

## Authoring and verification

The accepted .lui language and SDK/tooling boundaries live in [LUI-LANGUAGE.md](LUI-LANGUAGE.md), [LUI-SDK-TOOLING.md](LUI-SDK-TOOLING.md), and [ADR 0002](adr/0002-lui-authoring-surface.md). The architecture and domain glossary remain the authoritative framework vocabulary.

Use [TESTING.md](TESTING.md) for repository test scope and [pre-release verification](agents/verification.md) for risk-based check selection. Keep issue-specific evidence compact and source-bound; retain large captures, binaries, and traces as external artifacts.
