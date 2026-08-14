---
status: accepted
---

# Use class-shaped components with an explicit Render method

Block-bodied Lucent components are compiler-owned, class-like logical instances with ordinary C# members and exactly one `Fragment Render()` method. The method is the explicit declarative boundary and describes UI from current inputs, component-owned state, slots, and context; this keeps the data-to-UI model visible without a framework base class, virtual render dispatch, or a nested `render` region. Stateless expression-bodied components remain shorthand for the same contract, while concise state-member syntax is deferred until it can preserve the explicit lifetime semantics of `State<T>`.

## Consequences

- `Render()` must be side-effect free apart from constructing declarative output and event callbacks; its invocation count is not observable.
- Component parameters are current read-only inputs, while state members initialize once and persist for the logical component instance.
- Ordinary locals in `Render()` are recomputed values, not hidden reactive cells.
- A component instance is not an Avalonia control and `Fragment` is not a runtime virtual DOM requirement.
- [ADR 0001](0001-explicit-render-regions.md) is superseded; [ADR 0002](0002-explicit-single-site-slots.md) is unchanged.
