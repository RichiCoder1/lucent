# Lucent documentation

These documents split the original project handoff into maintained topics. They describe the intended product and the proof-of-concept boundary; they do not document a finished implementation.

| Document | Purpose |
| --- | --- |
| [Design review](DESIGN_REVIEW.md) | Prioritized language/runtime findings and Avalonia feasibility assessment |
| [Vision](VISION.md) | Motivation, goals, principles, influences, and non-goals |
| [Language](LANGUAGE.md) | Components, render methods, UI syntax, state members, slots, context, and construction |
| [Architecture](ARCHITECTURE.md) | Compiler pipeline, runtime responsibilities, reactivity, identity, and interop |
| [Styling](STYLING.md) | CSS authoring, tokens, layout, and component strategy |
| [Tooling](TOOLING.md) | Diagnostics, language server scope, source mapping, and hot reload |
| [Roadmap](ROADMAP.md) | Proof-of-concept success criteria, implementation order, and dogfood scope |
| [Decisions](DECISIONS.md) | Accepted direction, working choices, and explicitly deferred questions |

Supporting research:

- [Avalonia feasibility review](research/AVALONIA_FEASIBILITY.md)

Proof-of-concept notes:

- [POC 0001: Code-only desktop window](poc/0001-desktop-window.md)
- [POC 0002: Counter compiler](poc/0002-counter-compiler.md)
- [POC 0003: Shared compiler and tooling foundation](poc/0003-shared-compiler-tooling.md)
- [POC 0004: Direct native Avalonia controls](poc/0004-native-controls-todo.md)

Architectural decisions:

- [ADR 0001: Require explicit render regions](adr/0001-explicit-render-regions.md) — superseded by ADR 0003
- [ADR 0002: Make named slot supply explicit and single-site](adr/0002-explicit-single-site-slots.md)
- [ADR 0003: Use class-shaped components with an explicit Render method](adr/0003-class-shaped-components.md)
- [ADR 0004: Make native Avalonia controls the default control surface](adr/0004-native-avalonia-controls.md)

## Status language

The docs use three levels of commitment:

- **Accepted direction** is a project constraint or decision that should not change casually.
- **Working choice** is concrete enough to build against in the proof of concept, but may change with evidence.
- **Deferred** means the question is intentionally open. Examples may illustrate a possibility, but they are not a language guarantee.

When the implementation disagrees with these docs, record the decision and update both in the same change. Do not let a prototype accidentally become the specification.
