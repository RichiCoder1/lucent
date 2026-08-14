# Project vision

Lucent is a compiled declarative UI framework and C#-superset language for .NET, built on Avalonia. It replaces the developer-facing XAML and binding model while retaining Avalonia's mature desktop platform.

The product thesis is:

> Normal C# for logic, declarative syntax for UI, compiler-managed reactivity, explicit component APIs, and CSS for presentation.

## Why Lucent exists

Cross-platform .NET desktop development has strong runtime foundations, but the common authoring models often require XAML, object-graph ceremony, manual synchronization, or framework-specific binding machinery. Lucent explores a different model:

```text
state + data
    -> component
    -> UI
```

Instead of treating UI as a graph that application code constructs and mutates, a Lucent component declares what should exist for its current inputs. The compiler uses that declaration to create controls, preserve logical identity, and update only the properties or regions whose dependencies changed.

## Product goals

- A concise curly-brace UI language that remains close to C#.
- Strong static typing through the .NET type system.
- Fine-grained updates with minimal runtime bookkeeping.
- Cross-platform desktop behavior through Avalonia.
- CSS-inspired styling with compile-time analysis and design tokens.
- First-class component composition, component-owned state, context, conditionals, and keyed lists.
- Practical interop with existing Avalonia controls and .NET observable types.
- Diagnostics and editor features that explain the language in its own terms.

## Design principles

### C# until C# becomes annoying for UI

Computation, namespaces, generics, parameters, lambdas, records, pattern matching, `if`, `switch`, and `foreach` should keep their normal C# meaning. Lucent-specific syntax must earn its place by improving UI authoring or giving the compiler useful static knowledge.

### Prefer visible structure over implicit convenience

Sugar is useful when it removes redundant ceremony. It is harmful when it hides API boundaries, object construction, merging, subscription behavior, or lifetime. One extra explicit token is a reasonable price for predictable code.

### Compile away bookkeeping

The compiler should derive effect dependencies, reactive dependency graphs, static versus reactive properties, component and state-member identity, generated subscriptions, and style conversions when it can do so reliably.

### Performance is a product feature

The design should favor direct Avalonia property updates, efficient keyed structural changes, predictable scheduling, low startup cost, minimal intermediate allocation, and little or no reflection on hot paths. A generic virtual DOM is not the default architecture.

### Native interoperability over ecosystem isolation

Lucent should remain porous. Existing Avalonia controls, third-party libraries, custom controls, platform APIs, and familiar .NET types need a practical escape path even when Lucent's native model is different.

### Tooling is part of the language

A custom `.lui` format without syntax diagnostics, completion, navigation, and formatting would be a poor developer experience. The proof of concept includes a minimal language server because tooling constraints need to shape the frontend from the beginning.

## Research influences

Lucent combines lessons from several ecosystems without trying to clone one of them:

- QML, GNOME Blueprint, and Slint show that native UI can use a purpose-built declarative language without XML.
- SwiftUI and Jetpack Compose show the value of normal language control flow and typed composition.
- React supplies the component, state, hook, context, and unidirectional-data-flow mental model.
- Compiler-oriented frameworks such as Octane motivate direct generated updates and compiler-derived identity or dependencies.
- Razor and Mobile Blazor Bindings demonstrate a source-language-to-component-model-to-native-control pipeline on .NET.
- StyleX motivates static style analysis, deterministic output, and low runtime cost.
- shadcn motivates strong primitives and source-owned higher-level components instead of a giant opaque widget catalog.

These are design inputs, not compatibility targets.

## Initial non-goals

The first implementation will not attempt to replace Avalonia rendering, support every Avalonia control, target mobile or web, add alternate rendering backends, build a custom GPU stack, provide a visual designer, achieve full XAML compatibility, implement all CSS features, reproduce every React hook, or ship production-grade hot reload and language tooling.

The project should prove one narrow, coherent system before it expands.
