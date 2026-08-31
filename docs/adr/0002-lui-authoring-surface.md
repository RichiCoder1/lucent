# Lower `.lui` to the C# framework surface

Status: Accepted

## Context

The C# Issue Browser, two frozen feature changes, and the M6 viability gate established the first complete framework surface. `.lui` can now improve authoring without inventing a second runtime, property system, binding engine, component lifetime, or project model.

The archived Avalonia language is evidence, not a compatibility baseline. Blazor/Razor, Mobile Blazor Bindings, GPUIX, QML, StyleX, Panda CSS, shadcn/ui, and the researched layout systems are conceptual references only.

## Decision

Before compiler work, Lucent will seal the author-facing layout and visual primitives used by lowering:

- `LayoutProperties` owns bounded row/column geometry, spacing, padding, clipping, and scrolling;
- `VisualProperties` owns one box-local `Background : Brush` and composited subtree `Opacity`;
- `TypographyProperties` owns inherited text color and typography;
- renderer/text-state projection properties remain internal;
- `Color`, `Brush`, `LinearGradient`, `GradientStop`, and `Insets` are immutable typed values;
- `Brush` initially supports only solid color and bounded linear gradient; clipping, opacity, images, borders, corners, and layers remain distinct capabilities.

`.lui` is a JSX-like compile-time authoring surface with C# expression islands. A document declares one or more explicit components, though one public component per file is the initial convention. Components lower to partial static C# composition recipes over existing `CompositionContext`, styles, behaviors, controls, `When`, and `ForEach`. Reactive reads retain the existing fine-grained runtime dependency model; there is no component rerender or virtual tree.

Before lowering, the C# framework must expose two ordinary typed operations that generated code can call:

- a scope-owned reactive style assignment shaped as `Style.Bind(property, read)`. `Present` materializes it as an element-scope effect while preserving the style candidate's source, variant condition, and ordinal; it commits on the UI thread and rejects disposal. Full scene reprojection is acceptable initially.
- one edge-triggered `ReactiveGraph.WorkAvailable` notification when posted work changes from empty to nonempty. Windows may only translate it to a registered SDL wake event; the UI thread drains, projects, and presents normally. Reset/recheck must be lost-wake-safe and bursts coalesce without polling or worker-thread Core mutation.
- one atomic root/nested recipe mount operation over `Func<CompositionContext, Element>`, used equally by handwritten and generated recipes and typed default/named content. Root mount receives `ThemeContext` explicitly; nested contexts inherit it. The operation owns creation, exactly-one-root validation, commit, rollback, scope disposal, and nested parentage.

Ordinary component parameters are construction-time values. A live parameter is explicit in its C# type, such as a signal-bearing model or `Func<T>` read inside a binding/region; the compiler does not make arbitrary values reactive by rerunning components.

The language uses normal C# namespace, using, type, nullability, overload, accessibility, and expression semantics. A bounded handwritten parser owns only `.lui` structure and recovery; Roslyn parses and binds expression islands. C# components and content parameters opt in through explicit metadata. The build uses a reusable compiler library, a thin incremental source generator, and an additive MSBuild SDK. Generated output lives under `obj` and is not a public compatibility surface.

The same compiler and project-context model serves build and editor tooling. The first editor client is VS Code backed by a separate .NET 10 LSP process. Build/runtime packages do not carry editor or Roslyn dependencies. Exact source maps, deterministic formatting, cross-language rename/references, generated navigation, and C#-quality diagnostics are release gates.

## Consequences

- `.lui` cannot require reflection, runtime parsing, stale generated files, a compatibility reader, or a parallel runtime.
- Invalid components do not emit; parser recovery continues diagnostics for later constructs and valid sibling components.
- Application logic and state remain C# models or adjacent partial static helpers initially. Local state sugar, broader C# islands, service injection, bind syntax, literal color sugar, exported `.lui` styles, generic component declarations, hot reload, keyframes, and shared source-component tooling are follow-ups over proven contracts.
- SDK-provided ordinary C# global/static usings may make author-facing properties implicit, with an opt-out. The compiler does not maintain aliases or a hidden symbol registry.
- Declarative `transition`/keyframe syntax is deferred until Core owns automatic winner-change sampling, interpolation, clock/frame wake, interruption, and reduced-motion behavior. Existing manual C# transition samples are not sufficient lowering evidence.
- Pre-1.0 language and framework APIs may change cleanly. The initial language version is `preview`; compatibility modes begin only when a revision is intentionally retained.
