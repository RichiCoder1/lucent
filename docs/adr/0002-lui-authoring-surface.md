# Lower `.lui` to the C# framework surface

Status: Accepted

## Context

The C# Issue Browser, two frozen feature changes, and the resulting framework surface established the basis for this decision. .lui can now improve authoring without inventing a second runtime, property system, binding engine, component lifetime, or project model.

The archived Avalonia language is evidence, not a compatibility baseline. Blazor/Razor, Mobile Blazor Bindings, GPUIX, QML, StyleX, Panda CSS, shadcn/ui, and the researched layout systems are conceptual references only.

## Decision

Before compiler work, Lucent will seal the author-facing layout and visual primitives used by lowering:

- `LayoutProperties` owns bounded row/column geometry, spacing, padding, clipping, and scrolling;
- `VisualProperties` owns one box-local `Background : Brush` and composited subtree `Opacity`;
- `TypographyProperties` owns inherited text color and typography;
- renderer/text-state projection properties remain internal;
- `Color`, `Brush`, `LinearGradient`, `GradientStop`, and `Insets` are immutable typed values;
- `Brush` initially supports only solid color and bounded linear gradient; clipping, opacity, images, borders, corners, and layers remain distinct capabilities.

`.lui` is a JSX-like compile-time authoring surface with C# expression islands. The first grammar emits one explicit component per document; multiple declarations are reserved until a real compositional family requires them. Components lower to `[LucentComponent]` methods returning `ComponentRecipe` on the declared namespace's partial static `Components` class. Reactive reads retain the existing fine-grained runtime dependency model; there is no component rerender or virtual tree.

Before lowering, the C# framework must replace the provisional imperative authoring surface with the ordinary typed operations that generated code and handwritten C# share:

- a scope-owned reactive style assignment shaped as `Style.Bind(property, read)`. `Present` materializes it as an element-scope effect while preserving the style candidate's source, variant condition, and ordinal; it commits on the UI thread and rejects disposal. Full scene reprojection is acceptable initially.
- one edge-triggered `ReactiveGraph.WorkAvailable` notification when posted work changes from empty to nonempty. Windows may only translate it to a registered SDL wake event; the UI thread drains, projects, and presents normally. Reset/recheck must be lost-wake-safe and bursts coalesce without polling or worker-thread Core mutation.
- reusable `ComponentRecipe` values that create exactly one stable retained root per mount, zero-or-more `ContentRecipe` values for structural contributions, and immutable collection-expression `ComponentContent` for ordered default content. A safe conversion turns one component recipe into one content recipe; retained `When` and `ForEach` return content recipes. Named slots reuse this capability only after a real compositional control proves them;
- `ComponentRecipe.Create(kind, build)` as the one advanced custom-control seam. The framework allocates the root and owns naming, transaction, rollback, and scope disposal. Lucent's built-ins dogfood this public operation; duplicate built-in configurator overloads and mounted-state handles are not the ordinary interface;
- `ComponentRecipe.Named` as an optional universal local diagnostic identity. Unnamed C# and `.lui` recipes receive the same deterministic kind-and-parent-local-ordinal name; dynamic identity remains keyed. Additional universal accessibility or interaction traits require a proven shared contract;
- immutable typed style fluency over `Style.Set`/`Bind`, plus nullable `Style.With` composition. Components expose `Style?` only when their declared interface permits root restyling; there is no compiler-injected style parameter;
- focused `T`/`Func<T>` inputs where static-or-live values are common and direct `Func<T>` for inherently live values. There is no `ReactiveValue<T>` wrapper, generic binding value, or mounted state handle in the ordinary recipe interface.

Ordinary component parameters are validated and captured when a recipe is created and remain construction-time values. A live parameter is explicit in its C# type, such as a signal-bearing model or `Func<T>` read inside a binding/region; the compiler does not make arbitrary values reactive by rerunning components. Inline `.lui` style expressions are the deliberate exception and lower through the public scope-owned binding seam.

The language uses normal C# namespace, using, type, nullability, overload, accessibility, and expression semantics. A bounded handwritten parser owns only `.lui` structure and recovery; Roslyn parses and binds expression islands. C# components and content parameters opt in through explicit metadata. The build uses a reusable compiler library, a thin incremental source generator, and an additive MSBuild SDK. Generated output lives under `obj` and is not a public compatibility surface.

The same compiler and project-context model serves build and editor tooling. The first editor client is VS Code backed by a separate .NET 10 LSP process. Build/runtime packages do not carry editor or Roslyn dependencies. Exact source maps, deterministic formatting, cross-language rename/references, generated navigation, and C#-quality diagnostics are part of the authoring contract.

## Consequences

- `.lui` cannot require reflection, runtime parsing, stale generated files, a compatibility reader, or a parallel runtime.
- Invalid components do not emit; parser recovery continues diagnostics for later independent constructs.
- Application logic and state remain C# models or adjacent partial static helpers initially. Local state sugar, broader C# islands, service injection, relaxed live-reader syntax, literal color sugar, `public style`, named slots, reactive component-root switching, generic component declarations, hot reload, keyframes, and shared source-component tooling are follow-ups over proven contracts.
- The SDK supplies an opt-out ordinary global `Lucent.Core` namespace import. Built-in `Components` methods are implicit only for `.lui` element tags, built-in framework property keys only for style property-name positions, and `VariantState` members only for `when` conditions; all lower through their real Roslyn symbols without polluting C# or expression scope. Application and third-party component modules use ordinary static imports. The compiler does not maintain textual aliases.
- Tags are PascalCase and exact C# parameters are camelCase. Names are optional composition metadata rather than component parameters; explicit names are stable local labels while generated names are non-contractual across structural edits.
- A component keeps one stable element root for its mounted lifetime. Reactive alternative roots remain a caller-owned retained condition until a concrete use case justifies a root-switching module.
- Declarative `transition`/keyframe syntax is deferred until Core owns automatic winner-change sampling, interpolation, clock/frame wake, interruption, and reduced-motion behavior. Existing manual C# transition samples are not sufficient lowering evidence.
- Pre-1.0 language and framework APIs may change cleanly. The initial language version is `preview`; compatibility modes begin only when a revision is intentionally retained.

## Explicit default content and sibling forwarding

Issue #71 adopts `[DefaultContent]` on a single `.lui` component parameter, matching the existing C# metadata. No parameter spelling grants implicit content behavior; the unreleased `content`-name fallback is removed. Scalar input rules stay unchanged. Within `ComponentContent`, an expression contributes a `ComponentRecipe`/`ContentRecipe` or spreads a `ComponentContent` collection in place.

The links-and-notes shell requires scope/container wrappers and sibling header/body/footer composition. Those uses are supported by ordinary markup plus typed `{children}` forwarding. They do not require named-slot tags or multiple-root components yet, so those forms remain deferred. This introduces no additional runtime type, mounting path, or compatibility layer: generated C# uses existing collection expressions, recipe conversion, and transactional content mounting.

[ADR 0005](0005-component-local-state.md) extends the initial local-state and live-reader deferrals with per-mount declarations and setup. The recipe, retained-root, and shared runtime boundaries remain in force.
