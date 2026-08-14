# Design decisions and open questions

This page separates project direction from illustrative syntax. Update it when a prototype changes an assumption, and link a focused design note once a decision needs more reasoning than a table can hold.

## Accepted direction

| Area | Decision |
| --- | --- |
| Platform | Build on Avalonia instead of replacing its renderer, windowing, input, accessibility, text, or native integration. |
| Authoring model | Use a compiled declarative C#-superset rather than XAML or ordinary C# object construction. |
| Logic | Preserve normal C# semantics and vocabulary unless UI authoring needs a targeted extension. |
| Components | Use compiler-owned, class-like logical instances with ordinary members, but no required framework base class or Avalonia control wrapper. |
| Component body | Require one `Fragment Render()` method in a block-bodied component; keep stateless expression bodies as shorthand. See [ADR 0003](adr/0003-class-shaped-components.md), which supersedes [ADR 0001](adr/0001-explicit-render-regions.md). |
| Slots | Use implicit `children`, explicit `slot name { ... }` supply, and one syntactic `yield` site per slot; see [ADR 0002](adr/0002-explicit-single-site-slots.md). |
| Reactivity | Prefer compiler-derived, fine-grained updates over a generic runtime virtual DOM. |
| APIs | Keep essential parameters, secondary options, supplied UI slots, and ambient context visibly distinct. |
| Options | Do not promote option-record members into component arguments or add implicit object merging. |
| Styling | Use CSS as the authoring language and compile it toward a typed style representation. |
| Interop | Keep raw Avalonia and .NET integration available, but do not let legacy observable types define the native model. |
| Controls | Make direct Avalonia controls and exact native members the default surface; infer native child placement from Avalonia content metadata. Optional Lucent controls must add substantial semantics rather than wrap controls one-for-one. See [ADR 0004](adr/0004-native-avalonia-controls.md). |
| Native content | Allow both explicit `Content: value` and concise trailing scalar content; both occupy the same resolved Avalonia content route. |
| Native values | Apply convenience conversion from the resolved target .NET type. Keep explicit C# unchanged and do not infer from control/property names. |
| Tooling | Share frontend infrastructure between the compiler and language server; include a minimal language server in the proof of concept. |
| Scope | Prove one Avalonia backend and a narrow language before pursuing other platforms or broad compatibility. |

## Working choices

| Area | Current choice | What would validate or change it |
| --- | --- | --- |
| Name | Lucent | Revisit only for a material package, trademark, or ecosystem collision. |
| Tagline | Compiled declarative UI for .NET. | Adjust with product positioning, not implementation churn. |
| Source extension | `.lui` | Validate through editor integration and public file-association research. |
| Compiler strategy | Dedicated frontend, generated C#, then Roslyn | Replace only if expression integration or source mapping proves unworkable. |
| State surface | Explicit `State<T>` members with `.Value` and `.Update`; concise `state T name = value` member sugar remains a candidate | Validate that sugar removes ceremony without hiding ownership, initialization, or invalidation. |
| Component identity | Lexical call site for static structure, call site plus key for repeated structure | Validate with stateful conditional and reordered-list tests. |
| Initial keyed loops | Require `keyed by`, one native row root, and a dedicated native collection host; retain controls by key without a virtual DOM. | Expand only after nested regions and component-row lifetime have executable coverage. |
| Context syntax | `context Theme = value` and `using context Theme` | Finalize after symbol, shadowing, and type-inference experiments. |
| Token model | CSS variables with typed compiler representations where possible | Validate against Avalonia property conversion and theme changes. |

## Explicitly deferred

### Typed slots

Named slots are accepted, but nominal versus structural typing and cardinality are open. The core composition model must not wait on this choice.

### Reusable state behavior

Class-shaped components remove the need for order-sensitive state hooks in the basic model. The design still needs a concise way to compose reusable state, subscriptions, and cleanup without hiding ownership; no `hook` declaration syntax is accepted.

### Effect cleanup syntax

Effects need deterministic cleanup, but the exact `cleanup { ... }` form is not committed.

### Context and dependency injection

Context is tree-scoped data. The boundary with `Microsoft.Extensions.DependencyInjection`, service resolution syntax, and lifetime ownership still need a design.

### Async resources

Loading, error, cancellation, stale-result, and component-lifetime behavior must be defined before adding an API such as `useAsync`.

### Scheduling

Synchronous invalidation, batching, frame scheduling, priorities, and event boundaries remain open. The runtime should isolate this policy behind an explicit scheduler seam.

### CSS scope and platform conditions

Global, component, and module scoping can be combined in several reasonable ways. Media or platform concepts also need a mapping that fits native desktop behavior instead of imitating browsers without a reason.

### Animation

The relationship among CSS transitions, state transitions, and Avalonia animation primitives is not defined.

### First-class component values

Routing and render callbacks may eventually need typed component values. Their representation should wait until a real use case requires it.

### Hot reload

The architecture should preserve a path to state-compatible patching, but exact patch boundaries and compatibility rules are deferred until the compiler and runtime exist.
