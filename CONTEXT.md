# Lucent Language

Lucent defines the author-facing concepts used to declare compiled .NET desktop UI while keeping ordinary computation in C#.

## Language

**Component instance**:
The logical, state-owning lifetime created for a component invocation. It is not an Avalonia control and may produce zero, one, or many rendered nodes.
_Avoid_: Component control, view object

**Render method**:
The required `Fragment Render()` member of a block-bodied component that describes its current output from inputs, state, slots, and context.
_Avoid_: Render region, view block, template, UI block

**C# island**:
A complete C# expression or statement embedded in Lucent source and interpreted with normal C# semantics.
_Avoid_: Inline C#, code snippet

**Fragment**:
An ordered sequence of zero or more rendered nodes produced by a component or supplied as slot content.
_Avoid_: Virtual DOM, control collection

**State member**:
An explicitly declared reactive value owned for the lifetime of a component instance.
_Avoid_: Hook slot, mutable field

**Renderable symbol**:
A Lucent component or projected Avalonia control that is valid in render position.
_Avoid_: Widget type, node type

**Slot**:
A named or implicit input through which a caller supplies renderable content to a component invocation.
_Avoid_: Template property, control collection

**Yield site**:
The location in a render method where a component places supplied slot content.
_Avoid_: Placeholder, insertion point
