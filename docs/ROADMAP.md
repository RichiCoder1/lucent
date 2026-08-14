# Proof-of-concept roadmap

The proof of concept exists to validate Lucent's riskiest claim:

```text
React-like authoring semantics
    -> compiler dependency analysis
    -> fine-grained Avalonia updates
```

A parser that only emits a static control tree is a useful first checkpoint, but it is not the proof.

## Success criteria

The proof of concept should demonstrate:

- `.lui` parsing and component compilation;
- Avalonia window and control creation;
- events and component-owned state;
- state persistence across structural updates;
- direct reactive property updates;
- component composition;
- conditional UI;
- basic keyed list identity;
- CSS parsing, class application, and variables;
- basic raw Avalonia control interop;
- a minimal `.lui` language server with diagnostics, completion, hover, symbols, and go-to-definition.

Existing .NET observable interop matters, but it should not derail validation of the compiler-native reactive model.

## Implementation order

### 0. Language and parser spike

Define the smallest grammar for components, UI declarations, properties, nested children, literals, and a deliberately narrow C# expression subset. Generate a static Avalonia application.

Exit condition: a `.lui` Counter creates a real Avalonia window through generated C#.

Completed for the Counter slice: `lucentc` parses the checked-in `.lui`, emits deterministic C#, the POC compiles that output, and the native smoke path exercises the generated event and direct property update. The accepted grammar remains intentionally narrower than the full language proposal; see [POC 0002](poc/0002-counter-compiler.md).

### 1. Shared frontend

Build the syntax tree, diagnostics, component symbols, namespace and import resolution, and property or event resolution as reusable services.

The compiler module already exposes a side-effect-free syntax/diagnostic result used by the CLI. This milestone must deepen that seam with bounded Roslyn C# islands, stronger recovery, semantic control descriptors, and the symbol model needed by the language server.

The first shared-frontend slice is complete: CLI, MSBuild, and LSP adapters now consume the same compiler interface; bounded Roslyn syntax islands, recursive control descriptors, absolute diagnostics, and generated `#line` mappings are executable. Cross-file symbols, imports, type-aware island binding, and a public semantic-query interface remain before this milestone is fully complete. See [POC 0003](poc/0003-shared-compiler-tooling.md).

Exit condition: the compiler has no private parser or symbol model that the language server would need to reproduce.

### 2. Minimal language server

Add document recognition, diagnostics, symbols, hover, component completion, and go-to-definition.

The stdio protocol, document synchronization, push diagnostics, UTF-16 range mapping, VS Code registration, and TextMate grammar are now implemented. Symbols, hover, completion, and navigation remain, so this milestone's exit condition is not yet met.

Exit condition: the Counter source is meaningfully editable without reading generated code.

### 3. Component IR and reactivity

Introduce the initial component IR, component-owned `State<T>` members, dependency analysis, event lowering, and direct property invalidation.

The Counter and TodoMVC slices now execute generalized `State<T>`, direct
property refresh, explicit reverse event updates, and deterministic cleanup for
the native `Click` and `TextChanged` events used by the examples. Dependency
tracking is still lexical and conservative rather than symbol-bound.

Exit condition: clicking the Counter button updates the existing Avalonia text property and preserves component state.

### 4. Structural UI and identity

Add reactive `if` and `else`, keyed loops, subtree lifetime, insertion, removal, movement, and state preservation.

The first keyed-loop subset is executable: a dedicated native panel reconciles
one-root rows by key, retains existing Avalonia controls, refreshes changed row
properties, reorders roots, and disposes removed row subscriptions. Conditional
regions, nested loops, component rows, and optimized move operations remain.

Exit condition: a keyed list can reorder stateful rows without recreating their logical state.

### 5. Composition

Add cross-file component symbols, implicit `children`, named slots, `yield`, leaf invocation, trailing content blocks, nested context, reusable state behavior, and effects.

Exit condition: a small multi-component screen exercises state, slots, context, and cleanup without framework-only imperative wiring.

### 6. Styling

Add the CSS frontend, typed style IR, classes, a narrow selector set, pseudo-classes, CSS variables, and common Avalonia property mappings.

Exit condition: the Counter example builds with statically validated CSS and applies its class and token values without runtime CSS parsing.

### 7. Interop and developer experience

Complete project-aware native and third-party Avalonia control resolution,
property and collection notification adapters, formatting, richer completion,
source mapping, debugging support, and hot-reload experiments. Direct native
controls are the default surface rather than a late raw-control escape hatch;
see [ADR 0004](adr/0004-native-avalonia-controls.md).

Exit condition: the dogfood application can use an existing control without hiding it behind a Lucent-specific rewrite.

## Dogfood application

Counter and Todo examples test mechanisms, not product usefulness. A small developer tool should exercise navigation, forms, validation, lists, asynchronous work, dialogs, menus, settings, theming, custom components, a third-party control, keyboard interaction, and persisted state.

The important question is whether Lucent reduces application complexity or merely relocates it into generated code and framework machinery.

## Evidence to capture

Each milestone should leave behind executable examples, focused tests, representative generated C#, diagnostic snapshots, and a short note about measured allocations or invalidation behavior where relevant. Design claims should become testable contracts as soon as the implementation can support them.
