# Lucent

Lucent defines the author-facing concepts of a native desktop UI stack for .NET.

## Language

**Reference application**:
The first complete application built with Lucent to discover and validate the framework surface; it is expected to mature into a maintained example.
_Avoid_: Spike app, demo harness, test app

**Framework surface**:
The coherent set of Lucent capabilities available to application authors, independent of any one authoring syntax.
_Avoid_: Library API, engine API

**Authoring surface**:
A supported way to declare Lucent UI. Typed C# composition and `.lui` are authoring surfaces over the same framework model.
_Avoid_: Frontend, binding syntax, wrapper

**Diagnostic dump**:
A complete deterministic snapshot of relevant Lucent state used to explain behavior and make debugging decisions. It is distinct from sampled operational telemetry.
_Avoid_: Trace, log bundle, telemetry export

**Style**:
An immutable set of typed assignments that determines an element's arrangement and visual representation. It does not own interaction, semantics, lifecycle, or content structure.
_Avoid_: CSS rule, property bag, modifier

**Behavior**:
A reusable interaction contract that adds input, focus, semantics, and lifecycle ownership to an element without changing its content structure.
_Avoid_: Control subclass, event bundle, style

**Composition**:
The authored structure and ownership of elements and supplied content. Composition is distinct from both visual styling and interactive behavior.
_Avoid_: Template expansion, render tree, layout
