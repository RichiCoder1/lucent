# Native Lucent roadmap

## Goal

Deliver a Windows-first `0.1` developer preview of Native Lucent through one production-quality reference application. The Issue Browser is both the design pressure for the framework surface and a future maintained example; it is not a test harness.

The exact implementation and generated proof corpus from the validation spike remain on `archive/avalonia-final`. Production is rebuilt cleanly against the retained decisions and contracts.

Execution lives in [Lucent Native Project 4](https://github.com/users/RichiCoder1/projects/4/views/1). One roadmap issue owns milestone sub-issues and native dependency edges. This document records outcomes, gates, and sequence rather than duplicating the ticket catalog.

## `0.1` product boundary

### Included

- Windows 11 24H2+, `win-x64`, .NET 10 LTS, NativeAOT and trimming.
- Typed C# composition with unstable pre-1.0 APIs.
- Reactive state, derived state, batching, scopes, async generations, and deterministic disposal.
- Stable composition, rows/columns, bounded layout, scrolling, and fixed-height keyed virtualization.
- Typed styles, semantic tokens, finite variants, light/dark/high-contrast settings, reduced motion, and bounded transitions.
- Text, panel/layout primitives, button, single-line text field, selectable/list row, scroll viewport, virtualized list, loading/progress, and simple error state.
- Basic international single-line editing and a real Japanese IME smoke.
- Keyboard and Narrator completion of the defined Issue Browser walkthrough; UIA Value, Invoke, Selection, Scroll, focus/events, stale-node rejection, and virtualization for that control set.
- Rich deterministic dumps plus late-`0.1` opt-in application-owned standard .NET/OpenTelemetry export.
- A persistent CPU Skia/SDL presentation path with correct backing-pixel scaling.
- Deterministic local data and fake async behavior, with an optional live GitHub adapter.

### Excluded

- API compatibility promises, stable NuGet packages, or a 1.0 contract.
- `.lui`, runtime CSS, selectors, specificity, or general templates.
- Runtime token/theme import; Linux desktop theme integration.
- Multiline/rich text, exhaustive IME or accessibility certification, password input, drag/drop, tables, trees, tabs, menus, dialogs, plugin loading, persistence, or a broad control catalog.
- A production GPU path, `win-arm64`, macOS, or Wayland.

## Reference application walkthrough

The deterministic Issue Browser contains realistic mock issues and exercises:

1. launch and initial loading state;
2. successful async load of 10,000 keyed rows;
3. loading error and retry without losing valid stale state;
4. search plus status and assignee filters;
5. keyboard traversal, focus visibility, selection, and details presentation;
6. Unicode single-line editing, clipboard, undo/redo, and basic IME composition;
7. light/dark/high-contrast and reduced-motion changes without rebuilding application state;
8. scrolling and keyed reorder/removal while selection/focus remain coherent;
9. UIA automation through Value, Invoke, Selection, and Scroll;
10. deterministic tree, reactive, layout, style, semantic, scene, and timing dumps;
11. GitHub-adapter behavior through a local fake HTTP transport, with an optional non-gating live smoke;
12. clean `win-x64` NativeAOT packaging and launch.

Proof and capture code stays outside the application.

## Milestone 0 — repository and AOT foundation

Create the solution, the three production projects, the Issue Browser, and focused test projects. Pin dependency versions/licenses, enable strict warnings and AOT/trimming analyzers, enforce dependency direction, and publish a static NativeAOT window using the selected SDL/Skia stack.

**Gate:** warning-clean locked restore/build/NativeAOT publish with no unexplained suppressions; smoke launches only from a clean copied publish directory; exact native assets and notices are reconciled; a static window opens, presents at one declared non-100% backing scale, and closes repeatedly under an external driver; architecture checks reject both forbidden references and platform types in Core's public API.

**Stop:** missing NativeAOT assets, runtime discovery/code generation, unstable HWND ownership, an undistributable dependency/license, or a proposed abstraction without a current Windows consumer.

## Milestone 1 — headless framework kernel

Implement the UI-thread reactive graph, scopes and async-generation contract; stable composition and structural ownership; typed properties/styles/tokens/variants and behaviors; then bounded layout, text shaping/measurement, retained semantics and scene, and deterministic dumps. Render the Issue Browser's static and state-driven structures headlessly from realistic deterministic data.

The implementation order is deliberate: composition contracts first, property/style resolution second, and layout/scene projection only after arrangement values exist. The Core-owned shaping/measurement request contract and the Skia/HarfBuzz implementation land before controls or text editing consume them.

**Gate:** executable branch switching, batch, cycle, stale-generation, keyed identity, joint disposal, style precedence, theme invalidation, reduced-motion, semantic completeness, and dump determinism checks; no application renderer, geometry, synchronization loop, or semantic mirror.

**Stop:** satisfying the application requires a virtual DOM, general reconciliation, runtime selectors, reflection discovery, application-specific framework hooks, or separate C#/future-compiler models.

## Milestone 2 — Windows host, presentation, and input

Build the production SDL/Windows adapter around the headless kernel. Reuse persistent CPU raster and streaming-texture resources; establish Per-Monitor V2 DPI and backing-pixel authority. Implement portable hit testing, routing, focus, and capture before translating SDL/Windows pointer and keyboard events; then add clipboard, settings, and frame scheduling.

**Gate:** visible Issue Browser shell, correct 100/125/150/200% scale through 3840×2160 backing pixels, resize and monitor-change behavior, zero idle frames, headless/native structural parity, explicit projection/raster/upload/present timing, and no SDL/Windows types in Core.

**GPU decision:** after persistence/caching fixes, authorize only a bounded Skia GPU spike when the real application misses its frame gate and profiling attributes roughly half of frame cost to raster plus transfer.

**Stop:** ambiguous coordinate/scale authority, stale input/capture after disposal, nondeterministic frame scheduling, or platform leakage into Core.

## Milestone 3 — controls, text, and accessibility

Compose the bounded `0.1` controls from elements, styles, behaviors, semantics, and composition. Implement production single-line text state and basic international input. Map retained semantics into AOT-compatible UIA fragments, patterns, and events.

**Gate:** a frozen control-by-control UIA matrix defines roles, names, properties, Value/Invoke/SelectionItem/Selection/Scroll patterns, actions, events, focus, runtime IDs, and stale behavior. External UIA validates that matrix against bounded fixtures; deterministic text-state tests plus one real Japanese composition smoke pass. Complete Narrator/keyboard and virtualized-child acceptance occurs after the reference application and virtualized list exist in Milestone 4.

This is meaningful best-effort support for the defined surface, not exhaustive IME or accessibility certification.

**Stop:** delegated native controls or a second UI framework are required, provider ABI/lifetime is unstable, stale UIA nodes are observable, or essential controls need application-owned behavior/semantics.

## Milestone 4 — complete reference application

Add fake and optional live data sources, async loading/error/retry with stale retention, filters, details, 10,000-row keyed virtualization, and the complete externally driven walkthrough. Keep application code free of framework synchronization, renderer access, proof modes, and duplicated semantics.

**Gate:** every gating walkthrough step passes from external integration/E2E tests under managed and NativeAOT builds; 10,000-row reorders/removals preserve keyed identity, focus, selection, and bounded UIA providers; departed scopes cannot commit input, async, semantics, or scene work; keyboard and Narrator complete the walkthrough; Accessibility Insights receives a manual milestone review; the semantic dump has zero suppressions for the declared matrix.

**Stop:** the Issue Browser needs application-specific framework hooks, manual collection synchronization, direct bounds/drawing, or test-only startup behavior.

## Milestone 5 — framework-surface challenge

Freeze two representative feature changes before implementation. Implement both against the C# Issue Browser and record touched authoring sites, framework changes, diagnostics, dump changes, and lifecycle implications.

**Gate:** both changes require no application-specific framework hooks, no compatibility shims, and no avoidable framework churn. Freeze the first compiler-facing composition/style/behavior contract only after this gate, then rerun every invalidated reference-application check before the viability gate.

## Milestone 6 — `0.1` viability and developer preview

Harden diagnostics, documentation, packaging, performance, accessibility evidence, and visual quality. Perform parent-owned visual review and selective storybook-like captures without making screenshots the primary correctness gate.

On a baseline recorded before measurement—including machine/CPU/GPU/RAM, display and scale, power mode, OS/runtime/build/source identity, warm-up, vsync policy, GC procedure, and exact event-to-present clock points—record at least 500 input samples and 500 resize samples separately:

- p95 input/resize-to-present at most 16.7 ms;
- p99 at most 33.3 ms;
- renderer phase early warning at p95 8.3 ms;
- zero idle frames over ten seconds;
- 10,000 rows realizing no more than three times visible rows;
- at most 16 MiB live managed growth after 20 full-list cycles.

Also require post-GC return to the predeclared retained baseline and bounded growth for Skia surfaces/text blobs, SDL textures, UIA providers, and native handles. Record cold launch, working set, and publish size as non-gating baselines.

A miss triggers diagnosis and optimization first. Changing hardware, scope, or a budget requires a new explicit evidence-backed decision; thresholds are not silently weakened after results are known.

**Gate:** clean-machine launch from an unsigned self-contained NativeAOT directory/zip, checksums, exact dependency/license/native-asset inventory, warning-clean tests/publish, complete walkthrough, compact proof summary, no unexplained AOT suppressions, and an explicit pass for the declared visual/accessibility/IME gates. Add standard .NET activities/metrics only for real observed operations, with no listener/exporter by default and a fixed low-cardinality tag allowlist.
## Milestone 7 — `.lui` and optimized DevX (`0.2`)

Design `.lui` as the preferred opinionated authoring surface over the frozen framework model. Lower to ordinary supported C# APIs where practical and to narrow generated dependency/registration calls only for measured optimization. Generated readability is useful but secondary because output remains an implementation detail.

**Gate:** equivalent C# and `.lui` produce identical framework behavior and dumps; warning-clean NativeAOT; exact bidirectional source maps; C#-quality completion, XML documentation hover, diagnostics, rename/references, formatting, and generated-code navigation; no implementation-name leakage or editor dead spots.

**Stop:** `.lui` requires a parallel runtime, reflection fallback, runtime parser, compatibility bridge to archived behavior, or lower-quality language tooling than the declared matrix.

## Later options

- Build-time DTCG-compatible token import and research-backed Linux desktop theme mapping.
- `win-arm64`, after the `win-x64` developer preview is stable.
- Bounded macOS and Wayland adapter falsification spikes in the order recorded by the archived adoption decision.
- Multiline/document text, richer controls, GPU presentation, and additional renderer/platform seams only after measured demand.
- .NET 11 after GA, dependency validation, warning-clean NativeAOT, and measured benefit.

## Test and evidence policy

- Prefer meaningful integration and E2E tests; add small unit tests only for nontrivial algorithms and sharp contracts.
- Keep application code free of proof orchestration. External harnesses use ordinary diagnostics, automation, and input seams.
- Use deterministic realistic mock data for acceptance; network access is optional functionality.
- Run core tests, the walkthrough, and one NativeAOT smoke on every PR. Reserve larger performance, clean-machine packaging, and manual visual/accessibility/IME work for local or milestone execution and economical scheduled CI.
- Store proof scripts and compact summaries in Git. Store screenshots, binaries, traces, and large sample arrays as CI artifacts.
- Diagnostic dumps are authoritative. OpenTelemetry export is optional, app-owned, and never automatic.
- Review after every milestone. Implementation fixes invalidate prior review verdicts.
