# Native Lucent architecture

## Direction

Lucent is a Windows-first, NativeAOT-compatible desktop UI stack. The production implementation is a clean reimplementation of the contracts validated by the archived Native spike, not a promotion of probe code.

Portable concepts remain independent from Windows, SDL, Skia, HWND, UI Automation, and IME transport. Platform abstractions are introduced only for capabilities the Windows implementation consumes; a future second platform may force generalization, but does not justify speculative APIs now.

## Initial repository shape

```text
src/
  Lucent.Core/
  Lucent.Renderer.Skia/
  Lucent.Platform.Windows/
apps/
  Lucent.IssueBrowser/
tests/
  Lucent.Core.Tests/
  Lucent.Platform.Windows.Tests/
  Lucent.IssueBrowser.Tests/
  Lucent.NativeAot.Tests/
```

Start with these three production projects. Split text, scene, controls, SDL hosting, or diagnostics only when test isolation, dependency direction, NativeAOT, packaging, or a real second implementation establishes a boundary.

```text
Issue Browser
    -> Lucent.Core
    -> Lucent.Platform.Windows

Lucent.Platform.Windows
    -> Lucent.Core
    -> Lucent.Renderer.Skia

Lucent.Renderer.Skia
    -> Lucent.Core
```

`Lucent.Core` references no platform, renderer, native-binding, or OpenTelemetry SDK package. Architecture checks reject forbidden references and platform types in its public API.

## Framework pipeline

```text
application state
    -> reactive graph
    -> stable composition tree
       -> resolved typed properties and styles
       -> layout
       -> behaviors, focus, and input
       -> semantic accessibility tree
    -> retained scene
    -> Skia painter
    -> Windows presenter
```

The Issue Browser uses this pipeline directly. Application code cannot draw on a canvas, assign pixel geometry, mirror semantic trees, synchronize framework collections manually, or access SDL/Windows handles.

## Core ownership

### Reactivity

The UI-thread graph uses push invalidation and pull validation, lazy memoized derived values, dynamic dependency replacement, explicit batching, named-cycle diagnostics, and hierarchical scopes. A disposed scope jointly owns reactive subscriptions, structural regions, async cancellation, input capture, focus, semantics, and scene resources.

Async cancellation is resource cleanup, not the correctness mechanism. Generation identity, scope ownership, and UI-thread commit checks prevent stale or disposed work from committing. Runtime-tracked dependencies are the only `0.1` path. A compiler may use a narrow explicit registration seam only after `.lui` work demonstrates a measured need.

### Composition

Composition creates stable retained elements and explicit structural regions. Conditional and keyed collection operations own identity and disposal; there is no virtual DOM or general reconciliation pass.

For `0.1`, source-owned C# component recipes accept explicit content or child factories. General control templates and named `.lui` slot syntax are deferred until the C# framework surface is frozen.

### Styles and behaviors

Style values are typed, immutable, and composed in authored order. Styles own arrangement and visual representation. Behaviors separately own input, focus, and semantics; they register resources and cleanup into the composition-owned scope rather than owning lifetime independently. Composition owns structure, supplied content, and lifetime.

Property resolution proceeds from lowest to highest:

1. property default;
2. documented inherited context;
3. component base style;
4. author base assignments;
5. active variants, with component variants resolved before author variants;
6. active transition sample.

Variant priority is `Disabled > Invalid > Pressed > Selected > FocusVisible > Hover`; compound variants beat single-state variants and authored order breaks ties. Author variants override component variants at the same state priority. Two behaviors that claim exclusive focus, action, or semantic ownership fail closed. Every resolved property reports its winner and overridden candidates in diagnostic dumps.

Only documented text and token context inherits. Transitions are scheduler-owned, reduced-motion-aware, and initially bounded to color, opacity, transform, and focus-ring changes. Runtime CSS, selectors, specificity, reflection-based property discovery, and string property bags are excluded.

Semantic tokens are typed declarations. A later build-time importer may normalize DTCG-compatible token files or mapped Linux desktop theme data into generated typed declarations. Third-party formats are never parsed as executable runtime styling.

### Layout, text, semantics, and scene

The first layout surface is bounded to the reference application: rows, columns, size constraints, spacing, alignment, scrolling, fixed-height keyed virtualization, clipping, and device-scale rounding. Layout consumes resolved arrangement values; it does not define a second property system.

Core owns the text-measurement request/result contract, Unicode-safe single-line text state, selection, editing commands, composition/preedit state, and caret geometry requests. `Lucent.Renderer.Skia` is the only `0.1` implementation of shaping and measurement and owns HarfBuzz/Skia font fallback and painting; Windows owns native composition transport and candidate positioning. Measurement and painting use the same shaped glyph identities and positions.

The bounded `0.1` guarantee is scalar-safe storage, grapheme-boundary movement and deletion, deterministic ligature/combining-mark/fallback/emoji/mixed-direction shaping checks, and basic international composition. Full bidirectional visual-caret editing, exhaustive font/platform matrices, and IME certification are deferred. A real Japanese composition smoke remains required.

Core emits a retained semantic tree with stable identity, roles, names, values, enabled/focus/selection state, actions, bounds, hierarchy, and stale-generation rejection. Windows maps that tree to UIA fragments, patterns, and events. Accessibility never derives from pixels.

The retained scene contains renderer-facing paint commands without SDL, HWND, framebuffer, or GPU handles. Shaping measurement and painting share the same glyph identities and positions.

## Rendering and presentation

The initial internal seam is equivalent to:

```text
Paint(RetainedScene, SKCanvas, FrameGeometry)
```

`FrameGeometry` makes logical viewport, backing-pixel size, scale, and color format explicit. The presenter owns the target surface and frame lifecycle.

Production starts with a persistent CPU Skia raster surface and persistent SDL streaming texture. Resources are recreated only when backing size or format changes. Format, alpha, vsync, logical-to-device transforms, and mixed-DPI behavior are explicit; production rendering does not read frames back.

This is a Skia CPU/GPU seam, not a public arbitrary-renderer abstraction. A GPU spike is authorized only after resource reuse and caching are correct, the Issue Browser misses its declared frame budget on supported hardware, and profiling attributes roughly half the frame cost to raster plus transfer. CPU Skia remains the headless raster oracle if a GPU presenter is later added.

## Windows ownership

`Lucent.Platform.Windows` owns:

- SDL window lifecycle, event translation, and native asset loading;
- HWND retrieval and safe message integration;
- Per-Monitor V2 DPI, backing-size/scale changes, coordinate conversion, cursor, clipboard, timing, reduced motion, and settings;
- text input and Windows IME transport, composition area positioning, and focus cancellation;
- `WM_GETOBJECT`, AOT-compatible UIA COM wrappers, provider lifetime, fragments, patterns, events, and stale-node handling;
- native asset inventory and third-party notices. The `0.1` artifact is an unsigned self-contained publish directory or zip with checksums; installer/MSIX and signing are deferred.

SDL is an implementation detail. SDL structs and handles do not cross into Core. Direct Win32 is not a standing second backend; replacing SDL requires a new evidence-backed architecture decision.

## Diagnostics and observability

Deterministic diagnostic dumps are authoritative, culture-invariant, and include element identity, reactive edges, structure, layout, resolved styles and provenance, pseudo/focus state, semantics, scene invalidation, and frame-phase timings.

Lucent may emit standard .NET `ActivitySource` traces and `Meter` metrics under versioned `Lucent.*` names once the reference application supplies real operations to observe. Applications explicitly choose listeners, OpenTelemetry SDKs, and exporters. Lucent never exports telemetry automatically. Metrics use a fixed low-cardinality tag allowlist; authored text, user input, secrets, paths, exception messages, correlation IDs, and high-cardinality element identities are excluded from metric tags. Correlation IDs may connect traces to diagnostic dumps.

## Authoring surfaces

Typed C# composition is the first supported authoring surface. APIs remain unstable before 1.0 and are not packaged as a compatibility promise merely because projects require public visibility.

`.lui` begins only after the complete C# reference application and two frozen feature changes validate the framework surface. It lowers to the same supported composition, style, and behavior APIs wherever practical, with narrow generated registration or direct-dependency calls only for measured optimization. Generated readability is useful for diagnosis but remains secondary to correctness and source mapping.

The preferred `.lui` experience requires C#-quality completion, hover and XML documentation, diagnostics, rename/references, formatting, exact source maps, inspectable generated output, no implementation-name leakage, and no editor dead spots.

## NativeAOT

.NET 10 LTS is the required baseline. NativeAOT and trimming analyzers are enabled from the first implementation commit. Runtime code generation, dynamic assembly loading, reflection-based discovery, built-in runtime COM marshalling, and unbounded metadata scanning are excluded. Use direct calls or generated/static registration tables.

.NET 11 is not a required preview target. After GA, a non-gating experiment may test new language/runtime features. Promotion requires dependency support, warning-clean NativeAOT publication, reproducible tooling, and measured benefit.

## Test architecture

Tests emphasize meaningful integration and end-to-end behavior. Small unit tests are reserved for nontrivial algorithms and sharp contracts; tautological per-method suites are avoided.

The Issue Browser contains no test mode, capture orchestration, or proof runner. In-process headless test projects assert deterministic dumps. Out-of-process NativeAOT E2E uses ordinary UIA and input; `0.1` adds no private diagnostic IPC server. CI uses deterministic realistic mock data and never requires credentials or network access; a local fake HTTP transport validates the GitHub adapter contract, while an optional live GitHub smoke remains manual application functionality rather than acceptance evidence.

Each pull request runs contract/integration tests, the reference walkthrough, and one `win-x64` NativeAOT publish/smoke. Larger performance corpora, clean-machine packaging, selective visual review, and manual accessibility/IME checks run locally, nightly when economical, or at milestone gates. Large captures, binaries, and traces are CI artifacts; Git retains scripts and compact summaries.
