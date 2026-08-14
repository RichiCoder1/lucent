# Counter example

This is the north-star proof-of-concept slice, not an executable application yet. It captures the smallest example that exercises a class-shaped component, its explicit `Render()` method, control creation, an event, component-owned state, a fine-grained property update, CSS classes, and a design token.

The first end-to-end compiler milestone should make these files produce a real Avalonia window. The later styling milestone should consume `Counter.css` without runtime parsing of its static declarations.
