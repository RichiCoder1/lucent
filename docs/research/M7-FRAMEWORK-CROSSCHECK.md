# M7 framework architecture cross-check

**Status:** pre-implementation research; no code, issue, dependency, or compatibility change

**Prepared:** 2026-08-31

**Lucent snapshot:** `2ed80b1cb5e4993f4952e16e8a0d608365836f59`

**Issue snapshot:** exact GitHub bodies and dependency fields for #40, #41, and #45-#55 retrieved on 2026-08-31 before applying this report's amendment map; the dependency graph did not change

## Scope and method

This report cross-checks the accepted M7 framework and `.lui` decisions against the current Lucent public surface and primary-source designs from GPUI, Solid and alien-signals, Avalonia, WPF/WinUI, Flutter, Jetpack Compose, SwiftUI, Qt/QML, Slint, React/Razor, StyleX, Panda CSS, and shadcn/ui.

The following inherited decisions are constraints, not questions:

- Windows-first and NativeAOT-compatible;
- portable Core concepts with Windows, SDL, Skia, IME, and UIA kept in adapters;
- stable retained composition and fine-grained updates, with no VDOM or general reconciliation pass;
- typed C# as the first framework surface;
- `.lui` as compile-time lowering to that same runtime and ownership model;
- clean pre-1.0 replacement instead of compatibility shims.

**Fact** labels describe repository or upstream behavior. **Recommendation** labels are Lucent design conclusions. Severity means the docs or issue contract should be corrected **before implementation of #45 begins**, even when a later issue owns the resulting implementation.

## Executive verdict

Lucent's direction is unusually coherent and should be preserved. The Core/renderer/platform dependency direction, scope-owned fine-grained graph, retained keyed regions, style/behavior/semantics split, stale-generation rejection, deterministic dumps, and build/editor separation are all stronger than copying a general-purpose framework wholesale.

However, three accepted M7 claims are not implementable through the current public C# surface:

1. reactive `.lui` property and component-parameter expressions have no public fine-grained assignment seam and no general host wake/invalidation path;
2. `Compose(CompositionContext, ...)` recipes and typed content have no complete public invocation/ownership contract, especially at the composition root and for nested content;
3. the accepted `transition Background 120ms` syntax has no automatic value-change sampling, interpolation, frame clock, or `Brush` transition channel.

Those are specification blockers, not requests for a VDOM, runtime binding engine, or compiler-only escape hatch. The smallest correction is to expose and prove the ordinary C# operations that generated code will call, or to defer syntax whose runtime operation does not yet exist.

# 1. Lucent baseline

## 1.1 Current framework surface

| Concern | Fact at the inspected commit | Consequence for M7 |
| --- | --- | --- |
| Reactivity | `ReactiveGraph` is UI-thread-owned; `Signal`, lazy `Derived`, scheduled `ReactiveEffect`, batching, dynamic dependency replacement, named cycle detection, and hierarchical `ReactiveScope` are implemented. | Keep runtime tracking. Generated code does not need a second graph or explicit dependency lists. |
| Async | `AsyncValue<T>` uses a monotonic generation/lease, cancellation, posted UI-thread completion, stale-lease rejection, and scope disposal. | Correctness is stronger than cancellation alone and should remain the model for runtime and editor work. |
| Composition | `Composition.Child`, `When`, `ForEach`, and fixed-height `VirtualizedRegion` retain identity and jointly dispose element scopes. | Preserve ordinary retained elements and explicit structural regions; do not introduce component rerendering. |
| Factory context | `CompositionContext` has an internal constructor, exists only inside conditional/keyed/virtualized factories, and permits exactly one root. Fixed top-level structure uses `Composition.Child` directly. | The documented public static recipe ABI is incomplete; see Blocker B2. |
| Presentation | `Property<T>`, immutable `Style.Set`/`When`/`Compose`, tokens/themes, variants, provenance, and transition metadata exist. Public assignments are fixed values or tokens. | There is no public dynamic property assignment even though `.lui` promises one-region/property updates; see Blocker B1. |
| Property ownership | Public `Arrangement`, `SceneProperties`, and `InputProperties` mix author-facing values with raw text/caret/selection and scene projection values. | #45 correctly needs a clean `LayoutProperties`/`VisualProperties`/`TypographyProperties` split and should remove the unreleased old names rather than alias them. |
| Layout | `SceneLayout` implements bounded row/column intrinsic measurement, size clamps, spacing, alignment, scroll offsets, clipping, and device rounding. It has no explicit reusable constraint value and no padding. | Padding must be specified in Lucent's actual algorithm, not described by analogy to CSS, Flutter, or Compose. |
| Paint | `SceneProperties.Fill : Property<uint>` emits one `PaintSceneNode`; `ClipSceneNode` is the only group node. `SkiaSceneRenderer` paints solid rectangles and shaped text. | `Color`, `Brush`, gradient, and subtree opacity require deterministic Core scene values, not renderer objects. |
| Behaviors/input/focus | Behaviors register input/focus/semantics into scope-owned resources; exclusive ownership conflicts fail closed. `InputRouter` validates installed scene generations and retires stale capture/focus. | Preserve this seam. `.lui` should reference precompiled behaviors, not synthesize a second event/focus system. |
| Semantics/UIA | Core emits stable semantic identities/snapshots; Windows maps them to AOT-safe UIA providers and retires stale cached providers. | Opacity and paint must not determine semantic existence. Virtualization remains jointly responsible for semantics and provider lifetime. |
| Diagnostics | Reactive, composition, input, semantic, layout, and scene dumps are deterministic and culture-invariant; standard .NET telemetry is optional and adapter-owned. | Add serializations for new M7 values, but do not put compiler names, renderer cache identities, or user data into dumps/metrics. |
| Renderer/platform | Core exposes a portable text shaping request/result and retained scene. Skia implements shaping/paint; Windows owns SDL events, DPI, presentation, IME, UIA, clipboard, and settings. | Preserve the existing dependency direction. No general renderer or platform abstraction is justified by #45. |

## 1.2 Exact issue dependency chain

**Fact:** GitHub's structured `blockedBy`, `blocking`, `parent`, and `subIssues` fields agree with the issue-body statements:

```text
#38 (closed)
  -> #45 -> #46 -> #47 -> #48 -> #49 -> #50 -> #51
             parent #40 ----------------------------^

#40 -> #52 -> #53 -> #54 -> #55
          parent #41 ------------------^
```

- #40 owns compiler/lowering and has children #45-#51.
- #41 is blocked by #40 and owns tooling children #52-#55.
- #45 freezes property groups, `Color`/`Brush`/linear gradient, `Insets`/padding, and subtree opacity.
- #46 proves the compiler/generator host and additive SDK.
- #47 owns recoverable syntax and the formatter.
- #48 owns Roslyn binding, lowering, diagnostics, and exact maps.
- #49 owns incrementality and SDK packaging.
- #50 and #51 prove Filter Bar and virtualized Issue Row parity.
- #52-#54 own project context, LSP features, rename, formatting, and budgets.
- #55 cuts over the application and deletes superseded composition code.

**Recommendation:** Keep this serial dependency chain. Amend issue acceptance before #45 rather than discovering a missing runtime/interface operation in #48 or #50.

# 2. Cross-framework facts and bounded lessons

## 2.1 Reactivity, ownership, and async

### Solid and alien-signals

**Fact:** Solid 1.9.15 compiles templates to real DOM nodes, runs component setup once, and reruns only code that depends on changed signals. Its owner tree records computations and cleanup under roots. alien-signals 3.2.1 explicitly describes a push-pull algorithm and provides nested effect scopes that stop their contained effects. [Solid README](https://github.com/solidjs/solid/blob/f47845f9cc16ecbb316aa6560c7161f45af9a3d8/README.md) [Solid owner/cleanup source](https://github.com/solidjs/solid/blob/f47845f9cc16ecbb316aa6560c7161f45af9a3d8/packages/solid/src/reactive/signal.ts) [alien-signals README](https://github.com/stackblitz/alien-signals/blob/c00e63969bf261fc5dce31fae70cb9a90912b06e/README.md)

**Recommendation:** Preserve Lucent's graph and scopes. Copy the architectural requirement that every dynamic expression installs a scope-owned updater; do not copy DOM operations or JavaScript ownership globals.

**False analogy:** A signal implementation does not make a one-time call such as `Style.Set(property, signal.Value)` reactive. Solid's compiler emits an update operation for the dynamic insertion. Lucent likewise needs an ordinary C# update operation; runtime tracking alone cannot update a value that is never reread.

### GPUI

**Fact:** GPUI 0.2.2 stores durable application state in typed entities, but its high-level `Render` method is called at the start of a frame and builds an element tree for layout/style/paint. Async contexts make entity/window access fallible after `await`, and tasks are cancel-on-drop. [GPUI README](https://github.com/zed-industries/zed/blob/770aac345c76d9fcf82c01ba0dea08e5739b27b1/crates/gpui/README.md) [GPUI contexts](https://github.com/zed-industries/zed/blob/770aac345c76d9fcf82c01ba0dea08e5739b27b1/crates/gpui/docs/contexts.md)

**Recommendation:** Retain Lucent's generation checks and scope ownership. GPUI's fallible async contexts reinforce stale-owner checks, but not GPUI's frame materialization model.

**False analogy:** GPUI calls itself retained/immediate in different layers, but its authored element tree is rebuilt for frames. That is not Lucent's stable mounted `Element`/region algorithm and is not evidence for adding a render function or reconciliation pass.

### Compose and SwiftUI

**Fact:** Compose selectively recomposes scopes and lazy collections associate remembered state with optional stable keys. SwiftUI's state and identity follow view identity, and Swift task cancellation is cooperative. These systems recreate value descriptions and reconcile persistent framework state. [Compose lifecycle](https://developer.android.com/develop/ui/compose/lifecycle) [Compose lazy keys](https://developer.android.com/develop/ui/compose/lists) [SwiftUI state](https://developer.apple.com/documentation/swiftui/managing-user-interface-state) [Swift task cancellation](https://developer.apple.com/documentation/Swift/Task/cancel%28%29)

**Recommendation:** Keep Lucent's stronger stale-generation commit rule and exact .NET key contract.

**False analogy:** Compose/SwiftUI identity keys preserve composition state under their recomposition algorithms. They do not specify Lucent's dictionary equality/hash lifetime, mounted element identity, focus preservation, or UIA provider lifetime.

## 2.2 Composition, components, and content

### Razor/Blazor

**Fact:** Razor supplies strong compiler/tooling precedent: partial generated C# types, typed parameters, typed default/named `RenderFragment` content, inspectable generated files, and enhanced `#line` mappings. Its components are runtime instances participating in a render-tree/diff model. [Razor components](https://learn.microsoft.com/aspnet/core/blazor/components?view=aspnetcore-10.0) [Templated components](https://learn.microsoft.com/aspnet/core/blazor/components/templated-components?view=aspnetcore-10.0) [Enhanced `#line`](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-10.0/enhanced-line-directives.md)

**Recommendation:** Reuse normal C# symbol, partial-type, content-typing, generated-inspection, and mapping lessons. Keep Lucent components as static composition recipes with no instance lifecycle.

**False analogy:** `RenderFragment`, Razor component instances, render batches, and Blazor render modes are not a Lucent runtime contract. A Lucent content factory is an in-process capability that mounts ordinary retained elements into a composition-owned scope.

### Avalonia, WPF, WinUI, QML, and Slint

**Fact:** Avalonia/WPF/WinUI controls commonly combine runtime property registration, content/template systems, visual/logical trees, selectors/triggers, and framework-created control instances. QML has runtime object/property binding semantics even when compiled. Slint compiles its declarative language and separates compiler/runtime/backend/renderer concerns, but still owns its own property/component runtime. [Avalonia properties](https://docs.avaloniaui.net/docs/properties/index) [WPF dependency properties](https://learn.microsoft.com/dotnet/desktop/wpf/properties/dependency-properties-overview) [WinUI dependency properties](https://learn.microsoft.com/windows/apps/develop/platform/xaml/dependency-properties-overview) [QML language](https://doc.qt.io/qt-6/qtqml-language-topic.html) [Slint language](https://docs.slint.dev/latest/docs/slint/guide/language/coding/)

**Recommendation:** Preserve compile-time symbol binding and static recipes. Slint's package separation and QML's tooling breadth are useful checks for compiler/LSP seams, not permission to add runtime object lookup or property binding.

**False analogy:** The names "component", "content", "template", and "property" hide different lifetime and lookup algorithms. Lucent should not gain a template instance, `DataContext`, selector engine, `AddOwner`, runtime metatype, or string registry merely to use familiar markup vocabulary.

## 2.3 Layout, padding, and virtualization

### Constraint algorithms

**Fact:** Flutter passes min/max constraints down and sizes up in a depth-first layout; Compose also passes constraints down a modifier chain, and its padding modifier lowers child maxima then adds the insets back to the reported size. WPF uses separate measure/arrange passes and desired size. SwiftUI uses parent proposals and child responses. These are materially different algorithms. [Flutter architecture](https://docs.flutter.dev/resources/architectural-overview) [Compose constraints](https://developer.android.com/develop/ui/compose/layouts/constraints-modifiers) [WPF layout](https://learn.microsoft.com/dotnet/desktop/wpf/advanced/layout) [SwiftUI proposed size](https://developer.apple.com/documentation/swiftui/proposedviewsize)

**Recommendation:** Specify padding in terms of Lucent's current row/column measurement and arrangement. Reuse the invariant "outer size includes padding; children use a nonnegative inner content box; background paints the outer box," but do not claim Flutter, Compose, WPF, or SwiftUI compatibility.

**False analogy:** `MainAlignment`, CSS `justify-content`, Flutter `MainAxisAlignment`, Compose `Arrangement`, Avalonia alignment, and Qt attached alignment distribute or consume free space differently. Keep Lucent's names and finite algorithm.

### Virtualization

**Fact:** Flutter fixed `itemExtent` lets scrolling avoid measuring every child. Compose lazy keys move remembered state with items under Compose's state model. WinUI `ItemsRepeater` deliberately supplies layout/virtualization without default selection, focus, interaction, or accessibility policy, and recycled elements have explicit prepared/clearing lifecycle events. [Flutter ListView](https://api.flutter.dev/flutter/widgets/ListView-class.html) [Compose lazy lists](https://developer.android.com/develop/ui/compose/lists) [WinUI ItemsRepeater](https://learn.microsoft.com/windows/apps/develop/ui/controls/items-repeater)

**Recommendation:** Preserve Lucent's fixed-height, exact-key, scope-disposing `VirtualizedRegion` and its explicit behavior/semantics ownership. Clarify whether #51 compiles a row recipe consumed by the existing C# `VirtualizedList` (the minimal path) or introduces a typed item-content component contract.

**False analogy:** A keyed `foreach` mounts every entry and is not virtualization. A React/Compose key, a recycled `ItemsRepeater` index, and Lucent's exact dictionary key do not establish the same lifetime, focus, or accessibility guarantees.

## 2.4 Color, brush, gradient, opacity, and clipping

**Fact:** WPF distinguishes element opacity, which affects children, from brush opacity, which affects only the painted portion. Flutter's `Opacity` may use an offscreen buffer and opacity zero still permits descendant hit testing. Compose background paint and graphics-layer alpha are separate. QML and Slint likewise separate box paint, clipping, and subtree/layer opacity. [WPF opacity](https://learn.microsoft.com/dotnet/desktop/wpf/graphics-multimedia/how-to-animate-the-opacity-of-an-element-or-brush) [Flutter Opacity](https://api.flutter.dev/flutter/widgets/Opacity-class.html) [Compose graphics layer](https://developer.android.com/reference/kotlin/androidx/compose/ui/graphics/graphicsLayer.modifier) [Qt Item](https://doc.qt.io/qt-6/qml-qtquick-item.html) [Slint common properties](https://docs.slint.dev/latest/docs/slint/reference/common/)

**Recommendation:** Keep three independent values:

- `Background : Brush` paints one arranged box;
- brush/color alpha affects that paint only;
- `Opacity` composites the element's paint, text, and descendant subtree without changing layout, hit testing, focus, or semantics;
- `Clip` independently bounds descendant paint and hit testing according to the existing retained geometry contract.

**Fact:** Surveyed systems disagree on gradient coordinate systems, stop ordering, color interpolation, shape, image layers, caching, and temporal brush interpolation. A common type name is not a common contract.

**Recommendation:** #45 must define Lucent's own immutable value contract: channel encoding, equality/hash behavior, finite stop rules, box-relative gradient geometry, interpolation color space, premultiplication point, invalid input behavior, and deterministic dump text. No renderer brush interface, image path, arbitrary layer list, or cache object belongs in Core.

## 2.5 Styles, property groups, and imports

**Fact:** StyleX statically canonicalizes CSS properties and has explicit conflict behavior for shorthand/longhand properties. Panda recipes provide typed finite base/variant/compound-variant style selection but still generate CSS and retain a small class-name runtime. Neither owns desktop layout, input, focus, semantics, or lifetime. [StyleX](https://stylexjs.com/) [Panda recipes](https://panda-css.com/docs/concepts/recipes)

**Recommendation:** Preserve Lucent's one canonical `Property<T>` identity per concept, ordered `Style.Compose`, finite variant ordering, and provenance. Do not add shorthands that create competing property slots. `Insets.Symmetric(...)` may construct one four-edge value; it must not create separate cascading longhand properties.

**Fact:** C# `global using static` imports accessible static members project-wide, and collisions remain normal C# ambiguities. SDK `<Using Static="True">` emits an ordinary global static using. [C# using directives](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/using-directive) [.NET SDK `Using` items](https://learn.microsoft.com/dotnet/core/project-sdk/msbuild-props#using)

**Recommendation:** The SDK may provide a small opt-out list of ordinary static imports only after #45 freezes exact group/member names. Build and LSP must observe the same evaluated `<Using>` items. Do not repair collisions with compiler aliases or hidden property registries.

## 2.6 Behaviors, focus, semantics, and UIA

**Fact:** Compose maintains a semantics tree distinct from drawing and requires custom low-level content to add semantic meaning. WPF custom controls use automation peers. WinUI `ItemsRepeater` explicitly leaves accessibility policy to the composed control and requires collection metadata to stay current. [Compose semantics](https://developer.android.com/develop/ui/compose/accessibility/semantics) [WPF custom UIA](https://learn.microsoft.com/dotnet/desktop/wpf/controls/ui-automation-of-a-wpf-custom-control) [WinUI ItemsRepeater accessibility](https://learn.microsoft.com/windows/apps/develop/ui/controls/items-repeater#enable-accessibility)

**Recommendation:** Preserve Core semantic snapshots and Windows UIA mapping. Source-owned components must compose or attach ordinary Lucent behaviors that own semantics; `.lui` should not expose raw UIA roles, providers, or imperative focus transport. Opacity zero and transparent brushes must not remove semantic nodes or disable actions.

**False analogy:** A style pseudo-class, Compose modifier, WPF behavior, and Lucent `Behavior` do not own the same resources. Lucent's exclusive focus/action/semantic claims and scope cleanup are intentional and should not be weakened for markup convenience.

## 2.7 Compiler, SDK, LSP, maps, and formatting

**Fact:** Roslyn incremental generators receive declared `AdditionalText` inputs and emit through `AddSource`; filtering per document, immutable/equatable stages, cancellation, and narrow whole-set collection are the relevant mechanisms. Enhanced `#line` directives precisely map diagnostics and PDB sequence points, but the C# compiler decides sequence-point constructs and the directive is not a general bidirectional syntax map. [Roslyn incremental cookbook](https://github.com/dotnet/roslyn/blob/e79586494f629704a0fd18b7afb840144fd5e673/docs/features/incremental-generators.cookbook.md) [Enhanced `#line`](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-10.0/enhanced-line-directives.md)

**Recommendation:** Keep the accepted reusable compiler, thin generator, additive SDK, separate LSP, and shared project-context model. Define a compiler-owned map schema in #48 rather than treating `#line` as the map. At minimum it needs logical source/generated document identity, exact UTF-16 spans in both directions, mapping kind, unmapped scaffolding, deterministic ordering, and the project/document generation that produced it.

**Fact:** QML and Slint ship first-party language servers and formatters, but their compiler/runtime/project models are not Roslyn project models. Razor's history shows stale document versions, projection mapping, and formatting are independent failure surfaces.

**Recommendation:** Build and editor adapters must feed one immutable evaluated project context; every async editor result must carry project/workspace epoch, document version, and compiler/map identity and must be rejected when stale. Cancellation is cleanup, not the commit guard. One formatter implementation must define comment attachment, significant text, line endings, malformed-tree behavior, and whether C# islands are preserved or Roslyn-formatted.

**False analogy:** A PDB mapping, a bidirectional editor map, a generated-document view, and a runtime diagnostic dump are four different artifacts. None should substitute for another, and source maps have no runtime role.

## 2.8 Source-owned components

**Fact:** shadcn/ui explicitly describes itself as open component code and a code-distribution platform rather than a conventional component package. Consumers receive and modify the actual source. [shadcn/ui introduction](https://ui.shadcn.com/docs)

**Recommendation:** Preserve the accepted M7 minimum: shared components are ordinary C# or copied `.lui`/C# source owned by the application. Do not build a registry, updater, manifest compatibility layer, or package abstraction in M7. The prerequisite is a complete static recipe/content contract and normal source-map/project identity, not copy tooling.

**False analogy:** shadcn's distribution model says who owns source; it does not define Lucent runtime composition, behavior ownership, accessibility, styling, or upgrade compatibility.

# 3. Ranked findings that require doc/issue changes before #45

## Blockers

### B1. Reactive expressions lack a public fine-grained assignment and host invalidation seam

**Fact:** `Style.Set<T>` accepts a value or token. `Element.UpdateControl<T>` is internal and reserved for control-owned state. `BehaviorContext.Effect` is internal. A public `ReactiveScope.Effect` can rerun an expression, but it has no public author-assignment operation to commit the result. `CompositionContext` factories are run while structural effects collect dependencies, yet retained children are not recreated when the condition/key set is unchanged. Therefore a read such as `Style.Set(Opacity, signal.Value)` is sampled once, not updated.

**Fact:** `docs/LUI-LANGUAGE.md` promises that a component recipe establishes structure once and that only the property/region reading a changed dependency updates. `docs/ARCHITECTURE.md` names ordinary runtime tracking as the compiler target. The current public interface cannot fulfill that promise for arbitrary properties or ordinary component value parameters.

**Fact:** worker-thread async completions enqueue `ReactiveGraph` posts, but `WindowsBootstrap` waits for SDL events and has no general graph/composition invalidation subscription that wakes and requests a frame. The existing UIA/settings adapters do push SDL user events, proving the Windows consumer exists. A completion can therefore remain queued while the app is otherwise idle.

**Recommendation:** Before #45, update the framework/language docs and #45/#48/#50 acceptance to define one ordinary C# reactive-assignment interface. The smallest plausible shape is an explicit `Style.Bind(Property<T>, Func<T>)` or equivalent typed operation mounted as an element-scope effect; exact naming remains a #45 decision. It must:

- expose liveness in the C# type/interface rather than infer it from arbitrary expressions;
- preserve component/author/variant precedence and report binding provenance;
- commit on the UI thread and dispose with the element scope;
- invalidate layout/paint/semantics according to the affected property;
- coalesce a portable Core invalidation notification into one Windows event/frame request;
- reject work after scope/composition disposal;
- be the same call used by C# and generated `.lui`, with no hidden compiler-only setter or dependency-registration graph.

Also state whether ordinary component parameters are construction-time values or typed reactive accessors. The compiler cannot safely make every arbitrary parameter call reactive without a component rerender model.

**Acceptance to add:** a background async completion updates one bound `Background`/`Padding`/`Opacity` value while the window is idle, produces one current frame, leaves stable structure intact, and cannot commit after scope disposal. C# and `.lui` dumps must match.

### B2. Static component recipes and typed content do not have a complete invocation contract

**Fact:** The language contract specifies a partial static `Compose(CompositionContext, ...)` entry point plus `[LuiComponent]` and `[LuiContent]`. None exists in Core today. More importantly, application root/fixed structure has no public way to obtain a `CompositionContext`; its constructor and `Run`/commit operations are internal. One context permits exactly one root, and there is no public operation by which a component invokes separate default/named content factories below arbitrary owned regions.

**Recommendation:** Before #45, correct `docs/LUI-LANGUAGE.md` and assign implementation/proof explicitly to #46/#48. Define exactly one public mount/invocation interface shared by handwritten and generated static recipes. The contract must cover:

- root invocation and nested invocation;
- who creates, commits, and disposes the recipe scope/root;
- exactly-one versus zero/many root content cardinality;
- the concrete typed default/named content delegate shape and how each gets an owned parent/context;
- how recipes create `When`, `ForEach`, and `VirtualizedList` regions without escaping to inaccessible `Composition` internals or nesting the currently non-nestable factory;
- construction-time versus reactive parameters;
- return value/handle and exception rollback;
- normal C# accessibility, overload, nullability, generic-consumption, and metadata rules;
- static attribute definitions that require no reflection at runtime.

**Acceptance to add to #46:** one annotated handwritten C# component with default and named content must compile and mount at the composition root and under another component through the same interface later used by generated code. Disposal and a failing content factory must leave no mounted structure or leaked scope.

Do not solve this with a runtime template instance, service locator, duck-typed scan, or string component registry.

### B3. Declarative transition syntax targets a runtime operation that does not exist

**Fact:** The accepted language example includes `transition Background 120ms`. Current `TransitionKind.Color` accepts only `uint`, not the proposed `Color` or `Brush`. `TransitionController` stores caller-supplied samples until expiry; it does not observe style-winner changes, calculate intermediate values, or schedule frames. `WindowsBootstrap` does not advance transitions on a clock. Spatial gradient interpolation is also distinct from temporal interpolation between brushes.

**Recommendation:** Make an explicit pre-#45 decision:

1. **Minimal recommended path:** remove/defer initial `.lui` transition syntax and the `Background` example until an automatic transition scheduler is a proven framework primitive; retain existing manual finite samples only as the current C# contract; or
2. add a prerequisite runtime issue and specify start/end winner capture, supported value pairs, easing, clock ownership, frame wake/coalescing, reduced-motion behavior, interruption, nested opacity, gradient stop compatibility, dumps, and NativeAOT performance before #47 freezes grammar.

Do not lower declarative syntax to `StartTransition` and claim animation parity; that would be a semantic mismatch.

## High

### H1. #45 needs exact property ownership and global-import collision evidence

**Fact:** The accepted docs name `LayoutProperties`, `VisualProperties`, and `TypographyProperties`, while code still publicly exposes `Arrangement`, `SceneProperties`, and raw projection properties. SDK global/static usings can make unqualified names ambiguous under ordinary C# rules.

**Recommendation:** Amend #45 to enumerate the public members in each group and the projection members that become internal. Remove the unreleased `Fill`/old groups rather than keep aliases or dual resolution paths. Amend #46 to prove the exact opt-out `<Using>` items in evaluated MSBuild, including collision fixtures with author symbols and build/LSP agreement. The compiler must not own aliases.

### H2. Padding needs algorithmic contract tests, not only pixel examples

**Fact:** "Padding constrains child content" does not settle intrinsic size, explicit outer size, over-constraint, scrolling, virtualization, or rounding in Lucent's current layout algorithm.

**Recommendation:** Add to #45/docs:

- immutable physical `Left/Top/Right/Bottom` logical-pixel edges, all finite and nonnegative;
- constructor/helper semantics and equality/hash behavior;
- outer arranged size versus inner content box;
- intrinsic desired size including padding;
- inner width/height clamped to zero when insets exceed the outer box;
- child origins, row/column spacing, and cross-axis stretch inside the inner box;
- own text, selection, and caret geometry inside the same inner content box;
- scroll extent and clip behavior relative to the inner box;
- whether leading/trailing padding participates in the scrollable extent;
- whether fixed virtual row height includes row padding (recommended: it is the row's total outer extent);
- virtualization realization against the viewport's inner height rather than its padded outer height;
- independent rounding of shared outer/inner edges at 100/125/150/200% without cumulative drift.

Add a compact headless matrix for nested padding, explicit/min/max sizes, over-constrained insets, scrolling, clipping, and virtual rows.

### H3. Brush and opacity need a complete retained-scene contract

**Recommendation:** Add to #45/docs:

- exact sRGB RGBA channel representation and public parse/try-parse corpus;
- `Brush` as a closed immutable value, not `IBrush` or renderer callback;
- solid and box-relative bounded linear gradient only;
- two-to-sixteen finite stops, the allowed position range, ordering/equal-position rule, and degenerate-vector behavior;
- premultiplication and linear-sRGB interpolation defined separately for spatial gradients and any future temporal transition;
- safe `Color -> Brush` and `LinearGradient -> Brush` conversions with one canonical equality/hash result;
- finite `Opacity` range `[0,1]` and failure behavior;
- one explicit opacity group node ordering over background, text, and descendants; nested opacity composition; interaction with clip; and bounded offscreen allocation/fallback behavior;
- group bounds that include visible descendant overflow when `Clip=false`, rather than accidentally using the element box as an implicit clip;
- opacity zero preserving layout/input/focus/semantics and brush alpha affecting paint only.

Scene and composition dumps must serialize values and provenance without Skia objects or cache IDs. Pixel tests must include overlapping descendants, nested opacity, transparent paint, gradients, clipping, and all declared scales.

### H4. Runtime, build, and editor generations need the same stale-commit rule

**Fact:** Runtime `AsyncValue` already models generation checks correctly. Generator cancellation and LSP request cancellation alone do not prove freshness, and `#line` does not identify the project/document snapshot that produced a map.

**Recommendation:** Amend #48/#49/#52/#53 to require an immutable identity tuple (project/workspace epoch, evaluated project identity, document logical identity/version, compiler options/version, generated hint/map identity) on bound/lowered/map/editor results. Map entries must explicitly permit one-to-many and many-to-one spans and mark hidden/synthetic scaffolding; symbol provenance, not line maps alone, must drive rename/references. Publishing diagnostics, completion, navigation, or rename must compare that identity to the current snapshot and reject stale work. Project/document removal and LSP disposal must release owned work. Keep this build/editor-only; do not add runtime source-map metadata.

### H5. #51 must say whether `.lui` owns a row or a virtualized list

**Fact:** `.lui foreach (...) keyed by ...` lowers to nonvirtual `ForEach`. Current virtualization is a generic C# `Controls.VirtualizedList` taking an item-aware row factory. Generic `.lui` declarations are deferred, and the documented `[LuiContent]` shape does not yet explain an item local.

**Recommendation:** Use the smallest initial topology and state it in #51: C# owns `VirtualizedList`; a `.lui` static Issue Row recipe is passed as its typed row factory. C# and `.lui` row fixtures then prove identical keyed identity, focus, selection, UIA, and bounded realization. If #51 instead intends `.lui` to declare the virtualized container, #48 must first define a typed item-content protocol and contextual item symbol. Never lower a keyed `foreach` and call it virtualized.

## Medium

### M1. Diagnostic dump coverage must be frozen with #45

**Recommendation:** Define culture-invariant text for `Color`, solid/gradient `Brush`, every stop/vector, `Insets`, opacity groups, and reactive-assignment provenance. Keep values bounded and deterministic; omit renderer allocation, absolute paths, generated helper names, and user-authored text beyond existing explicit contracts. Runtime dumps remain authoritative; compiler diagnostics/maps and sampled telemetry remain separate artifacts.

### M2. Formatter ownership needs a narrower initial policy

**Recommendation:** Amend #47/#54 to say whether the first formatter preserves C# expression-island token text verbatim or delegates islands to a pinned Roslyn formatter. The lower-risk initial rule is to format `.lui` structure while preserving expression token text, comments, and runtime-significant body text. Whichever rule is chosen must be shared by document/range/CLI/check formatting and tested for idempotence, CRLF/LF, malformed nodes, comments at every recovery boundary, and no source-map drift.

### M3. Source-owned components need a convention, not tooling

**Recommendation:** Add one sentence to the M7 docs/#55: source-copied components are ordinary project `.lui`/C# inputs with normal namespaces, metadata, maps, formatting, and ownership; updates are manual file changes reviewed like application code. Shared registry/install/update tooling remains deferred. This preserves the shadcn-like ownership lesson without creating a package manager or compatibility schema.

### M4. #45 accessibility acceptance must cover changed geometry and invisible paint

**Recommendation:** Extend #45 acceptance so padding updates retained semantic/UIA bounds at 100/125/150/200% scale, opacity zero preserves focus and executable actions through external UIA, and transparent/overflowing paint does not change semantic hierarchy. Keep semantics attached through behavior ownership; do not move semantics into `Style` or derive them from scene pixels.

# 4. Strengths to preserve unchanged

1. **Portable deep seams:** Core owns framework semantics; Skia and Windows are concrete adapters at real seams. No speculative arbitrary-renderer or second-platform interface is needed.
2. **Fine-grained graph:** push invalidation, pull validation, lazy derived values, batching, dynamic dependencies, named cycles, and UI-thread mutation are a strong base.
3. **Joint scope ownership:** elements, regions, effects, async cancellation, behavior registrations, capture/focus, semantics, and scene-related cleanup share deterministic scope disposal.
4. **Async correctness:** generation identity and UI-thread commit checks, not cancellation cooperation, reject stale work.
5. **Stable composition:** explicit `When`, exact-key `ForEach`, and fixed-height virtualization avoid a VDOM and general reconciliation.
6. **Typed presentation:** immutable ordered styles, tokens, finite variants, fail-closed duplicate property identity, and provenance are simpler and more diagnosable than selectors/specificity.
7. **Behavior separation:** input/focus/semantics/lifecycle are not style or content. Exclusive ownership conflicts fail closed.
8. **Accessibility architecture:** semantics are retained Core data; UIA is a Windows adapter with stable identities and stale-provider retirement, never a pixel inference.
9. **Diagnostic/telemetry distinction:** deterministic dumps answer state questions; optional application-owned standard .NET telemetry answers operational questions.
10. **Compiler boundaries:** reusable compiler, thin generator, additive SDK, separate LSP, actual evaluated project context, no runtime Roslyn/editor assets, and no persistent generated source are the right direction.
11. **Pre-1.0 cleanup freedom:** change the unreleased interface once rather than add aliases, fallback readers, dual schemas, or compatibility modes.

# 5. False analogies to reject explicitly

| Shared name | Why the analogy is false for Lucent |
| --- | --- |
| GPUI "retained UI" | Durable entities coexist with a frame-built authored element tree. Lucent retains mounted authored elements and regions. |
| React/Flutter/Compose/SwiftUI "component" | Those systems rerun/rebuild value descriptions and reconcile persistent state. A Lucent component recipe runs once to mount ordinary retained elements. |
| "signal" | A graph tracks reads only inside an updater. It does not turn a one-time property setter or ordinary parameter call into a live assignment. |
| "key" | React position matching, Compose remembered state, WinUI recycling, and Lucent dictionary identity use different equality and lifetime rules. |
| WPF/Avalonia "property" | Runtime registration, owner metadata, inheritance, coercion, selectors, and templates are not Lucent's immutable typed property identity and ordered style resolution. |
| QML "compiled binding" | QML still has its own runtime object/property/binding semantics. `.lui` must call the normal C# Lucent runtime. |
| CSS/Flutter/Compose "alignment" | Their free-space and constraint algorithms differ. Lucent's finite row/column behavior remains its own contract. |
| "background" or "brush" | Frameworks disagree on layers, shapes, images, coordinate systems, clipping, interpolation, and opacity. Lucent ships one bounded box brush. |
| "opacity" | Paint alpha and composited subtree opacity are different operations with different allocation and hit/semantic behavior. |
| ItemsRepeater/LazyList "virtualization" | Recycling/composition reuse is not Lucent's exact-key mounted-scope retention. A keyed nonvirtual loop is not virtualization. |
| Razor "content" | Typed content is useful precedent; `RenderFragment` and a render-tree component instance are not Lucent lifetimes. |
| StyleX/Panda "recipe" | They produce CSS/classes. They do not provide layout, retained identity, behaviors, focus, UIA, or component scope. |
| shadcn "component system" | It is source distribution/ownership, not a runtime or compiler architecture. |
| "source map" | `#line`/PDB mappings, compiler bidirectional maps, generated navigation documents, and runtime dumps are distinct. |

# 6. Issue amendment map

| Issue | Contract amendment to make before #45 starts |
| --- | --- |
| #40 | Add parent acceptance that every reactive `.lui` value lowers to a supported typed C# liveness/assignment interface and every component/content call lowers to the proved static recipe interface. |
| #41 | Add parent acceptance that all editor results are generation-checked against one evaluated project/document snapshot and compiler map identity. |
| #45 | Own public property-group/member list; reactive assignment/invalidation interface; padding algebra; Color/Brush/gradient/opacity/scene/dump contract; decide declarative transition support. |
| #46 | Prove root/nested static recipe invocation and typed default/named content; prove exact SDK global/static usings, opt-out, collisions, and build/editor host loading. |
| #47 | Remove deferred transition syntax or parse only the proved runtime contract; freeze expression-island formatting policy. |
| #48 | Lower reactive assignments and recipe/content calls only through supported C# interfaces; define bidirectional map schema and result generation identity. |
| #49 | Prove stale-result rejection across add/change/delete/rename/failure and evaluated project/global-using changes, not only generator cancellation. |
| #50 | Include an idle async completion and live bound property in C#/`.lui` parity; prove stable structure, one wake/frame, disposal rejection, and matching dumps. |
| #51 | State the C#-virtualized-list/`.lui`-row topology, or explicitly own a typed item-content protocol before claiming `.lui` virtualization. |
| #52 | Define project/workspace/document lifecycle generations and disposal; reuse compiler/map identity. |
| #53 | Require stale diagnostics/completion/navigation to be dropped by identity, not merely canceled. |
| #54 | Require atomic rename against one current snapshot and one formatter policy for C# islands/comments/text/line endings. |
| #55 | Treat source-owned components as ordinary copied project sources; keep registry/update tooling deferred. |

# 7. Primary-source identity and license ledger

No new dependency, copied code, or adopted runtime reference results from this report. The recommendations are independent conclusions from documented behavior. Therefore this report does not require a `CREDITS.md` change. If a recommendation later becomes an adopted architectural reference or source is copied/translated, `CREDITS.md` must be updated first as required by repository policy.

The following identities make this comparison reproducible. Existing Lucent ledger pins remain valid historical baselines; the commit column below records the current source snapshot consulted for this cross-check where an immutable repository exists.

| Source | Exact identity consulted | License/status | Use here |
| --- | --- | --- | --- |
| GPUI/Zed | `gpui` 0.2.2; `zed-industries/zed@770aac345c76d9fcf82c01ba0dea08e5739b27b1` | Apache-2.0 for the crate | Entity/context, frame-built elements, focus/accessibility contrast. |
| Solid | `solid-js` 1.9.15; `solidjs/solid@f47845f9cc16ecbb316aa6560c7161f45af9a3d8` | MIT | Run-once compiled fine-grained updates and owner cleanup. |
| alien-signals | 3.2.1; `stackblitz/alien-signals@c00e63969bf261fc5dce31fae70cb9a90912b06e` | MIT | Push-pull graph and effect scopes. |
| Avalonia | `AvaloniaUI/Avalonia@fc5923acf2afcd2be588d5c285eda8a1a14af228` | MIT | Runtime property/template/style contrast. |
| WPF | `dotnet/wpf@1cfc37f708f91ff4556bd25af414546c446f3a16`; Microsoft Learn `windowsdesktop-10.0` | MIT source; Microsoft platform docs | Measure/arrange, dependency properties, brush/element opacity, automation peers. |
| WinUI 3 | `microsoft/microsoft-ui-xaml@19e3bdc3ccf3361393d623d3a5d2667cb8f33229`; current Windows App SDK docs | MIT source; Microsoft platform docs | ItemsRepeater policy/lifecycle/accessibility and dependency properties. |
| Flutter | `flutter/flutter@f5f6313e8cce24472ebe25db99ae7a57c19f0fe0` | BSD-3-Clause | Constraint flow, persistent element/render trees, opacity, fixed extents. |
| Jetpack Compose / AndroidX | `androidx/androidx@49b0245f26cfd4d0dcf83ee69aa8ff31ee5de9c9` | Apache-2.0 | Constraint/modifier order, lazy keys, semantics. |
| SwiftUI | Apple Developer SwiftUI documentation consulted 2026-08-31 | Apple framework/documentation terms; no source copied | State/identity, proposal layout, accessibility, cooperative task cancellation. |
| Qt Quick/QML | `qt/qtdeclarative@8242ab5e8ce4b3eeaf62524bf9d3cc30d4c3bac9`; Qt 6.11/6.8 docs as cited | Module-specific LGPL/GPL/commercial terms; no dependency | Runtime QML contrast, layout, item paint/clip/layer, LSP breadth. |
| Slint | `slint-ui/slint@4cab77d6e3ab43ffc46fe467359fa61cff0137aa` | Framework: Slint Royalty-free 2.0, GPL-3.0-only, or commercial; docs/examples MIT | Compiler/runtime/backend separation, declarative property and tooling contrast. |
| React | `facebook/react@2dc7da790d6388b95b83198ca9b588b2ad5f5c0b` | MIT | Reconciliation/key/component false analogy only. |
| Razor/Roslyn | `dotnet/razor@58ec96978ef4e5823b54e960b9fd64cff45d7e68`; `dotnet/roslyn@e79586494f629704a0fd18b7afb840144fd5e673`; `Microsoft.CodeAnalysis.CSharp` 4.14.0 | MIT | Partial generated C#, typed content, incremental generator and mapping lessons. |
| StyleX | `facebook/stylex@dac821c8fc61caf2d2e5c4c529eef278e131b7b9` | MIT | Static/canonical property and composition contrast. |
| Panda CSS | `chakra-ui/panda@8cfc19aa88015fd9517a6099807a29a17911f3e3` | MIT | Finite typed recipe/static extraction contrast. |
| shadcn/ui | `shadcn-ui/ui@b4a618b97e35f5dadf3a00d51f410c84a2567d4d` | MIT | Source-owned component distribution. |
| CSSWG | Existing Lucent pin `w3c/csswg-drafts@f89f7a1a0138b072051e65323f49c737152880fb` | W3C specification terms | Vocabulary/algorithm false-analogy checks only. |
| Windows UI Automation | Windows SDK 10.0.26100.0 interfaces already recorded in `CREDITS.md` | Windows platform contract | UIA semantics/provider behavior. |

# 8. Residual risks and research limits

- Public framework docs do not establish renderer allocation, cache, raster, upload, or UIA performance for Lucent. #45 must benchmark the actual Skia/Windows path.
- SwiftUI is documented as a platform framework rather than a reproducible open-source implementation; conclusions are limited to published interface behavior.
- Qt and Slint license choices are file/module/use dependent. This report adopts neither as a dependency and copies no source.
- Online `latest` documentation moves. Immutable repository commits above are the source anchors; implementation package versions must be rechecked when actually added.
- No surveyed framework provides Lucent's exact combination of stable composition, fine-grained C# updates, static `.lui` recipes, NativeAOT, and retained UIA. The missing interfaces must be designed and proved locally rather than inferred from shared names.
- Color management beyond the accepted sRGB/linear-sRGB contract, images, borders, corners, richer gradients, full variable-height virtualization, rich text, hot reload, and component registry/update tooling remain correctly deferred.

## Final assessment

The M7 architecture should proceed after the three interface mismatches are resolved in docs/issues: a real scope-owned reactive assignment plus host invalidation seam, a root/nested static recipe and typed-content invocation contract, and an honest decision on declarative transitions. With those corrected, #45 can safely seal the minimal layout/paint values without importing a foreign runtime or carrying pre-1.0 compatibility debt.
