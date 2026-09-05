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

Composition creates stable retained elements and explicit structural regions. Conditional and keyed collection operations own identity and disposal; there is no virtual DOM or general reconciliation pass. Keyed identity follows the normal .NET dictionary contract: equality and hash behavior must remain stable and side-effect-free while an item is mounted. Supporting mutable or composition-mutating key comparisons is outside the `0.1` contract.

The canonical C# authoring interface uses reusable `ComponentRecipe` values for exactly-one-root components, `ContentRecipe` for zero-or-more structural contributions, and immutable ordered `ComponentContent` for transactional default content. The framework allocates every recipe root through the same public `ComponentRecipe.Create` seam used by built-ins and external custom-control authors. These capabilities are not component instances, virtual nodes, serializable templates, or rerender objects. General control templates and named `.lui` slots remain deferred until a real compositional control proves them.

### Application lifecycle

Applications use `LucentApplication.CreateBuilder()` to snapshot a title, appearance-oriented theme factory, and explicit platform host. `Build()` allocates no runtime state. A built application is one-shot: `Run(ComponentRecipe)` uses the service-free lifecycle, while `Run(IApplicationLifecycle)` starts services and constructs the root asynchronously through an owner-thread `ApplicationSession`.

The session keeps asynchronous startup, close preparation, service stop and cleanup on the desktop event loop. A declined or failed preparation leaves the window and composition alive for recovery; repeated requests coalesce. Once preparation accepts close, stop and cleanup are terminal. Core disposes the composition before lifecycle resources, and failures remain observable across independently attempted cleanup stages. Accepted saves must be drained by the application service before acceptance; they are separate from cancellation of obsolete scope-owned reads.

Core defines the portable host boundary without discovering a platform. The Windows adapter is selected explicitly with `UseWindows()` and pumps session continuations through the same wake transport as reactive work, even while minimized. Platform settings update `ThemeContext.Appearance` and `ReducedMotion`; the application lifecycle maps appearance to the effective theme.

Optional `Lucent.Hosting` references Core and Microsoft's Generic Host, independently of Windows. It owns one application DI scope, starts/stops hosted services, and resolves typed models at the application composition root. `.lui` components receive those models as parameters; there are no per-element DI scopes. [ADR 0003](adr/0003-application-services-and-shutdown.md) records recovery, service ownership and Microsoft container disposal boundaries.

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

The bounded `0.1` guarantee is scalar-safe storage, grapheme-boundary movement and deletion, deterministic ligature/combining-mark/fallback/emoji/mixed-direction shaping checks, and basic international composition. Text shaping itemizes common LTR plus Arabic/Hebrew RTL runs with deterministic neutral attachment, text-element fallback, and bounded RTL block reversal; it is not a full Unicode bidi or visual-caret implementation. Full bidirectional visual-caret editing, exhaustive font/platform matrices, IME certification, and mandatory real-language manual smokes are deferred.

Core emits a retained semantic tree with stable identity, roles, names, values, enabled/focus/selection state, actions, bounds, hierarchy, and stale-generation rejection. Windows maps that tree to UIA fragments, patterns, and events. Accessibility never derives from pixels.

The retained scene contains renderer-facing paint commands without SDL, HWND, framebuffer, or GPU handles. Shaping measurement and painting share the same glyph identities and positions.

## Rendering and presentation

The initial internal seam is:

```text
SkiaSceneRenderer.Render(RetainedScene, SKCanvas)
```

Windows owns a `WindowsViewport`: SDL render output is the backing-pixel size, `GetDpiForWindow / 96` is the only scale authority, and logical viewport dimensions are backing pixels divided by that scale. Per-Monitor V2 startup fails unless the effective thread context is already Per-Monitor V2. Skia applies that scale once; SDL receives the matching backing-sized texture without logical presentation scaling. A zero backing size retains its pending frame and waits for the next window event rather than spinning. The CPU presenter owns its target surface and frame lifecycle.

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

The .lui authoring surface uses the validated C# reference application and feature contracts. It lowers to the same supported composition, style, and behavior APIs wherever practical, with narrow generated registration or direct-dependency calls only for measured optimization. Generated readability is useful for diagnosis but remains secondary to correctness and source mapping.

The compiler-facing contract:

- structure lowers to `[LucentComponent]` methods returning `ComponentRecipe`, `ComponentContent` collection expressions, and retained `When`/`ForEach` content recipes. Generated and handwritten components mount through the same atomic recipe operation; the compiler does not call `Composition.Child` or construct factory contexts directly;
- presentation lowers to typed fluent style methods over `Property<T>` and `Style.Set`/`Bind`/`When`/`With`, plus `Token<T>` and `Theme`/`ThemeContext`. Inline style expressions use the public live-binding seam; ordinary component values remain construction-time unless their C# type is explicitly live. Manual C# transition samples are not an initial compiler target;
- behavior ownership remains encapsulated by `[LucentComponent]` recipes. `.lui` does not generate behavior bodies, call `Element.AttachBehaviors`, or register through `BehaviorContext`; a future universal interaction trait must first be proved as a public recipe operation;
- reactive expressions use ordinary `Signal`, `Derived`, `Effect`, and `AsyncValue` reads under runtime dependency tracking;
- generated structure and resources belong to their recipe-owned element scope, so generated code adds no parallel lifetime or synchronization loop;
- retained collection factories receive scope-owned, read-only `CurrentItem<T>` capabilities. Same-key source replacement publishes current payloads without rerunning factories or discarding row-local state. `.lui` structural locals remain source-typed in authored expressions; explicit live readers dereference the current payload when evaluated. Construction-time values remain snapshots. Conditional pattern payloads follow the same rule for a compatible retained branch;
- dynamic fixed-height list density is an explicitly live `VirtualizedList` recipe input, for example a target-typed `Func<float>`; compiler output does not retain or call `VirtualizedRegion` handles.

No direct-dependency registration API is frozen. A later generated-only seam may be added only after measurement proves runtime tracking insufficient. Manual region refresh/update/realization, InputRouter, semantic snapshots and commands, diagnostic dumps, SceneLayout, retained-scene internals, renderer types, and platform transport are not compiler targets. Public visibility before 1.0 does not promote any excluded API into this contract.

The preferred `.lui` experience requires C#-quality completion, hover and XML documentation, diagnostics, rename/references, formatting, exact source maps, inspectable generated output, no implementation-name leakage, and no editor dead spots.

The authoring surface separates author-facing properties from projection internals. LayoutProperties owns bounded geometry, spacing, padding, clipping, and scrolling; VisualProperties owns Background: Brush and subtree Opacity; TypographyProperties owns inherited color and typography. The SDK supplies an ordinary global Lucent.Core namespace import. The .lui compiler resolves built-in Components only in element-tag positions, built-in property keys only in style property-name positions, and VariantState members only in when conditions, then emits fully qualified Roslyn symbols; project-wide static C# and expression scopes remain unchanged. Raw text, caret, selection, virtualization, scene, renderer, and transport values remain internal compiler-excluded state.

Generated and handwritten C# share one recipe vocabulary and one scope-owned typed reactive assignment operation. A `ComponentRecipe` has exactly one stable root per mount; `ContentRecipe` and immutable `ComponentContent` own zero-or-more child structure. `ComponentRecipe.Create` allocates the root and owns naming, child content, commit, rollback, and disposal. Live assignment preserves style precedence/provenance and emits a portable coalescible composition invalidation; the Windows host wakes and requests a frame without polling. Component value parameters are construction-time unless their declared type is explicitly live. Recipe/content failure rolls back provisional structure and scope resources. `.lui` does not expose declarative transitions until the framework owns automatic transition scheduling rather than manual samples.

The accepted .lui language and build/editor boundaries are specified in the .lui language contract, the .lui SDK and tooling contract, and ADR 0002. A reusable compiler, thin incremental generator, and additive MSBuild SDK lower JSX-like structure and Roslyn-bound C# expressions to partial static component recipes. The standalone LSP uses the same compiler and project-context model; runtime applications contain none of those build/editor dependencies.

## NativeAOT

.NET 10 LTS is the required baseline. NativeAOT and trimming analyzers are enabled from the first implementation commit. Runtime code generation, dynamic assembly loading, reflection-based discovery, built-in runtime COM marshalling, and unbounded metadata scanning are excluded. Use direct calls or generated/static registration tables.

.NET 11 is not a required preview target. After GA, a non-gating experiment may test new language/runtime features. Promotion requires dependency support, warning-clean NativeAOT publication, reproducible tooling, and measured benefit.

## Test architecture

Tests emphasize meaningful integration and end-to-end behavior. Small unit tests are reserved for nontrivial algorithms and sharp contracts; tautological per-method suites are avoided.

The Issue Browser contains no test mode, capture orchestration, or proof runner. In-process headless test projects assert deterministic dumps. Out-of-process NativeAOT E2E uses ordinary UIA and input; `0.1` adds no private diagnostic IPC server. CI uses deterministic realistic mock data and never requires credentials or network access; a local fake HTTP transport validates the GitHub adapter contract, while an optional live GitHub smoke remains manual application functionality rather than acceptance evidence.

Repository checks use affected contracts and explicit opt-in published, SDK, performance, and accessibility scopes; see TESTING.md and agents/verification.md. Large captures, binaries, and traces are CI artifacts; Git retains scripts and compact summaries.

The affected-check policy and repository test scopes are documented in TESTING.md and agents/verification.md. Keep the runner interface synchronized with those docs when the repository test entry point changes.

## Editor continuity and participation

An application-owned `EditorSession` carries document text, caret, selection, undo history and viewport state across `.lui` arrangement mounts. Mount-local input and IME resources remain scoped to the retained element. `VisualProperties.Participation` explicitly separates visible, hidden and collapsed subtrees across layout, paint, input, focus and semantics. See [editor sessions](EDITOR-SESSIONS.md) for ownership, synchronization and focus handoff.
