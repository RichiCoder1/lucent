# Lucent language and Avalonia design review

**Review date:** 2026-08-13  
**Scope:** The current language, compiler, runtime, styling, tooling, and proof-of-concept proposal, plus current Avalonia and .NET platform constraints.

## Verdict

The direction is credible and Avalonia is a good substrate. There is no obvious platform limitation that makes Lucent unworkable.

Code-only control construction, typed compiled bindings, and property observables provide important runtime integration seams.[9][14][17]

Avalonia’s build targets expose compiler inputs in a way that suggests a workable build-time generation seam for Lucent.[23]

The proposal is not ready to become a grammar or runtime contract yet. It describes the desired authoring model more precisely than it describes evaluation, identity, ownership, scheduling, and property lowering. Those gaps are concentrated enough to fix, but several are foundational. Building more syntax before resolving them would likely produce a pleasant demo that has to be redesigned as soon as it encounters a form, a large list, a third-party control, or an update from a background thread.

The most important adjustment is to narrow the headline claim. Lucent can derive dependencies from **recognized reactive sources in analyzable expressions**. It cannot soundly infer every dependency hidden inside arbitrary C# methods, mutable objects, LINQ pipelines, service calls, or aliases without introducing an effect system or broad invalidation fallback.

## Findings at a glance

| Priority | Finding | Recommended decision |
| --- | --- | --- |
| P0 | C# embedding and Lucent brace forms do not yet have a deterministic frontend/build contract. | Prove one narrow C#-island grammar, generated-source shape, and diagnostic mapping before expanding syntax. |
| P0 | Reactivity has no explicit source, evaluation, rewiring, or equality semantics. | Define a small reactive contract and treat opaque computation conservatively. |
| P0 | Call-site identity does not define a complete ownership or lifetime model. | Retain a Lucent region tree keyed by owner, region, call site, and optional key. |
| P0 | Scheduling is deferred even though Avalonia thread affinity makes it correctness-critical. | Introduce the dispatcher/scheduler seam before reactive state. |
| P0 | “Direct property update” ignores Avalonia value priority and heterogeneous control models. | Make property ownership and control projection explicit in the IR. |
| P1 | Component output, controls, events, children, slots, and context lack a formal renderable contract. | Define one narrow fragment and slot model before named slots or reusable state composition. |
| P1 | Counter does not exercise input, validation, content models, or virtualization. | Add a form field and a virtualized list to the platform proof. |
| P1 | CSS semantics do not yet say which tree, theme layer, or value priority they target. | Compile a deliberate Avalonia CSS subset, not browser CSS in general. |
| P1 | Effects, async work, and exceptions have syntax direction but no lifecycle contract. | Defer the surface syntax while defining cleanup and error routing in the runtime. |
| P2 | Hot reload, Native AOT, and broad interop are correctly later concerns, but early metadata choices can block them. | Generate stable IDs and static metadata now; validate hot reload and AOT later. |

## P0 findings

### 1. Define the C# island, not just “C# semantics”

Lucent currently has several brace forms:

```csharp
Button { ... }                  // UI declaration
Card(...) { ... }               // trailing children
onClick: { ... }                // statement-valued event
options: { ... }                // contextual construction
slot actions { ... }            // named slot content
```

None is inherently unparseable, but together they make incremental parsing, recovery, formatting, and Roslyn handoff a major part of the language design. Interpolated strings, lambdas, object initializers, pattern matching, and nested statement blocks make “read tokens until the UI parser thinks the C# expression ends” too fragile.

The first spike should choose a conservative contract:

- Lucent owns declarations and UI blocks.
- Roslyn owns complete expression and statement islands with explicit syntactic boundaries.
- Event bodies use typed lambdas, such as `onClick: () => { ... };`, until block sugar is proven.
- Options use ordinary `new ButtonOptions { ... }` until contextual construction is proven.
- Generated C# is deterministic, readable, and contains source mapping for syntax, type, and event-handler diagnostics.
- The compiler either rejects unsupported C# constructs or preserves their exact semantics. It does not silently reinterpret them.

The contextual construction feature may still be worthwhile later. It should not be bundled into the parser spike because it adds expected-type binding, member-name normalization, required/init-only member handling, and diagnostics before the component model itself is stable.

Build integration also needs an explicit choice. A source generator can consume additional files, add generated C#, and report diagnostics through Roslyn.[23][27] Lucent still owns `.lui` parsing and semantics, and it must prove whether one generator pass provides enough semantic context for embedded C# or whether an MSBuild precompile step is the deeper module. Do that experiment before committing the repository layout to either path.

### 2. Define a finite reactive contract

This expression is only reactive if the compiler knows what can change and how it announces that change:

```csharp
text: $"Hello {user.Name}";
```

The current proposal does not say whether `user` is a component input, a plain reference, an `INotifyPropertyChanged` adapter, or a Lucent-native cell. It also does not say how replacing `user` rewires a nested `Name` subscription, how equality suppresses work, or what happens when the dependency is hidden:

```csharp
text: BuildLabel(store);
```

Start with a small table that the compiler and runtime can enforce:

| Source | Initial POC behavior |
| --- | --- |
| Component input | Generated reactive input cell; a changed input invalidates its dependent computations. |
| `State<T>.Value` | Compiler-visible reactive read. |
| Reactive context value | Compiler-visible reactive read scoped to a provider region. |
| `INotifyPropertyChanged` member path | Explicit adapter that subscribes, rewires intermediate objects, and disposes with the region. |
| `INotifyCollectionChanged` | Structural adapter; keys still come from Lucent. |
| Plain property or local | Snapshot unless it is derived from a recognized source in the same analyzable expression. |
| Opaque method call | Recomputed only when a visible reactive argument/source invalidates it; hidden dependencies are unsupported or require an explicit reactive wrapper. |

The component execution model then becomes explainable:

1. Mount creates controls, regions, subscriptions, and reactive computation thunks.
2. Recognized source changes enqueue only dependent thunks or structural regions.
3. Property thunks reevaluate and assign according to equality and value-ownership rules.
4. Structural regions reconcile children and dispose removed ownership subtrees.
5. Events and effects are explicit side-effect zones; reactive property expressions should be side-effect free.

This is still React-like authoring, but it is not “rerun arbitrary C# and somehow infer what changed.” That distinction should be part of the public language model.

### 3. Keep a retained ownership tree

Avoiding a generic virtual DOM does not remove the need for retained logical state. Lucent needs a compact region/ownership tree for component inputs, state, subscriptions, effects, context, conditional branches, keyed children, slots, queued work, and cleanup.

Identity should be local, not globally based on a source location:

```text
owner component instance
    + structural region
    + lexical call site
    + optional key
```

The minimum contract should define:

- duplicate, null, and mutable keys;
- branch switch, unmount, remount, and move semantics;
- whether state is destroyed when a conditional branch disappears;
- child-before-parent, idempotent disposal;
- cancellation or suppression of queued updates after disposal;
- event and observable unsubscription;
- the difference between detaching an Avalonia control and disposing Lucent-owned work.

This retained tree is not a second visual tree and should not perform generic property diffing. It is the runtime module that gives identity and lifetime a small, testable interface.

### 4. Scheduling cannot remain deferred

Avalonia uses a single UI thread; control creation, property access, layout, input, and tree mutation must occur through its UI-thread model, with `Dispatcher.UIThread` as the marshal point.[3] Lucent state may be changed by UI events, `PropertyChanged`, collection notifications, observables, or asynchronous continuations, so this is a semantic issue rather than a later performance choice.

The first runtime policy can be deliberately simple:

- every Avalonia read/write and structural mutation runs on the UI dispatcher;
- off-thread source changes enqueue work rather than touching controls;
- nested invalidations have deterministic FIFO ordering;
- repeated invalidations of the same binding can be coalesced within one queue turn;
- structural reconciliation is queued separately from a property-notification callback;
- disposed regions ignore queued work;
- tests use a deterministic scheduler adapter.

Detailed frame priorities and batching can stay deferred. The scheduler seam and its ordering guarantees cannot.

### 5. Model Avalonia value ownership in the IR

Avalonia resolves values by priority: animation, local value, style trigger, template, style, inherited value, and default. Direct assignments and ordinary bindings commonly occupy local priority, so they outrank class and pseudo-class style setters; `SetCurrentValue` exists for changing the effective value without introducing another local-value entry.[1]

That behavior is not automatically wrong. If a Lucent property is equivalent to an inline value, it should beat CSS. The problem is that the proposal has not decided which layer owns each value. Add a lowering category to every projected property:

```text
instance local value
current-value update
Avalonia binding
style setter
pseudo-class/style trigger
control-theme/template setter
inherited/resource value
attached property
```

The CSS compiler, UI compiler, and runtime should all target this shared representation. Otherwise a reactive `background` update can accidentally disable hover or theme styling, or a style can unexpectedly replace a value Lucent considers component-owned.

## P1 findings

### 6. Formalize control projection and renderable output

“Controls and components share syntax” is a good authoring goal, but they need different semantic symbols and adapters. The frontend must resolve collisions among Lucent components, projected Avalonia controls, imported .NET types, overloads, and generic names.

A generated control descriptor should capture at least:

- construction and theme requirements;
- styled, direct, and attached properties;
- conversion and default binding mode;
- CLR and routed events, arguments, and subscription cleanup;
- zero-child, single-content, panel-children, items, template, or custom child policy;
- style classes and supported pseudo-classes;
- accessibility/automation properties;
- any custom projection adapter.

This cannot be inferred from one universal `Children` rule. Avalonia `ContentControl` accepts one direct child, while panels and items controls have different collection and templating models.[29][30] Components also need a formal output type: zero, one, or many control roots represented as a Lucent fragment, not an assumed Avalonia control.

Prefer generated descriptors over reflection. That keeps the projection module deep, improves diagnostics, and preserves a path to trimming and Native AOT, where runtime code generation, dynamic loading, and incompletely analyzable reflection are constrained.[24][25]

### 7. Make slots and context narrower initially

Slots are delayed computations with identity and ownership, not just syntax. The proposal must decide what happens when `yield children` is omitted, repeated, placed in a loop, or moved between regions. Typed slot shape can wait; cardinality, context, identity, and cleanup cannot.

For the POC, use one implicit `children` fragment with exactly one legal yield site per component invocation. The fragment keeps caller lexical captures, takes rendered context from the yield location, and is disposed with the invocation that owns it. Add named and repeatable slots only after those tests pass.

> **Resolved:** Named slot supply now uses `slot name { ... }`, and each slot initially has at most one syntactic yield site. See [ADR 0002](adr/0002-explicit-single-site-slots.md).

The current sequential context form is also too subtle:

```csharp
context Theme = lightTheme;
Sidebar();
context Theme = darkTheme;
PreviewPane();
```

It looks like mutation of an ambient environment and leaves the end of each provider scope implicit. Prefer visible provider structure:

```csharp
context Theme = darkTheme {
    PreviewPane();
}
```

Context also needs a typed key, missing-provider behavior, equality/update semantics, and disposal rules. Its relationship with dependency injection can remain deferred.

### 8. Add a real input control to the proof

Counter proves output updates but misses the hardest everyday control behavior. Avalonia target properties have default binding modes, and `TextBox.Text` is normally two-way; binding validation also integrates with `INotifyDataErrorInfo` and presents validation state through the control.[10][31][32]

Lucent needs an explicit input-ownership model:

- one-way display value;
- two-way state binding;
- uncontrolled initial value plus control-owned edits;
- change/commit event timing;
- parsing and validation errors;
- focus, selection, undo, and IME preservation.

Repeatedly assigning `Text` from state may disturb selection, undo, or composition even when the visible string is unchanged. That is an integration risk to test, not a reason to reimplement text input. Add a small form with `TextBox`, validation, focus movement, and async submission to the POC success criteria.

### 9. Separate structural loops from virtualized collections

A keyed `foreach` inside `Column` is useful for small structural regions, but it will normally realize one control subtree per item. Avalonia uses controls such as `ItemsRepeater` or `ListBox` to virtualize and recycle realized containers for large collections.[30]

Do not promise that every keyed loop is virtualized. Keep two interfaces:

- keyed structural regions for small, arbitrary UI; and
- a projected virtualized collection control whose item template creates Lucent fragments and whose recycling callbacks reset identity, state, classes, and subscriptions correctly.

The dogfood application should test both. A list move test proves identity; a large scrolling list proves the renderer can cooperate with Avalonia virtualization instead of bypassing it.

### 10. Define CSS as an Avalonia-targeted language

Avalonia maintains distinct logical and visual trees. Resource lookup, `DataContext`, and ordinary style traversal follow the logical tree, while templates introduce visual-only parts and require explicit template traversal.[2][4] Classes are author-assigned values; pseudo-classes are generally control-owned state.[6]

The first CSS contract should say:

- selectors target projected Avalonia logical controls, not invisible Lucent component instances;
- a class on a multi-root component is invalid unless the component declares a style host or forwards the class;
- supported pseudo-classes map to Avalonia control states;
- template traversal and control-theme authoring are an explicit advanced mode;
- static tokens compile to typed constants, while runtime theme tokens map to resources with defined lookup and invalidation;
- declaration order and value ownership are deterministic.

Templated controls require a `ControlTheme`/`ControlTemplate`, and custom templated controls have lifecycle rules around template parts.[7] Lucent primitives should initially reuse a known Avalonia theme and compose existing controls. Generating fully themeable custom controls is a separate feature, not an automatic consequence of declaring a Lucent component.

### 11. Specify events, effects, async work, and errors before exposing sugar

Event properties need delegate typing, routed-event handling, arguments, replacement, unsubscription, exception routing, and async-handler behavior. The conservative `onClick: () => { ... }` form lets the target delegate type carry most of that information.

Effects need initial-run timing, cleanup-before-rerun, cleanup-on-unmount, self-invalidation handling, scheduler phase, and exception behavior. Async work needs cancellation and stale-result suppression when an owning region disappears. The exact `useEffect` and `cleanup` syntax can wait until one narrow runtime contract passes deterministic tests.

Add one root error-reporting interface early. Error boundaries can remain later, but the runtime must never leave a half-mounted region or skip cleanup because generated code, an event, an effect, or a disposer throws.

## What can remain deferred

The following choices do not block the first honest proof:

- nominal versus structural typed slots;
- final reusable state-composition syntax;
- context integration with `Microsoft.Extensions.DependencyInjection`;
- convenience APIs for async resources;
- frame priorities and advanced batching;
- CSS modules, broad media/platform conditions, transitions, and animations;
- first-class component values;
- production hot reload;
- full Native AOT support;
- alternate renderers and non-desktop platforms.

Hot reload deserves stable generated component and state-member IDs now, but Avalonia’s own public work should not be treated as a dependency for Lucent’s implementation.[21][22]

Native AOT likewise benefits from static generated metadata now and can become a later publish validation lane.[24][25]

## Recommended proof-of-concept sequence

1. **Frontend risk spike:** Parse one component with conservative C# islands, generate deterministic C#, and map three Roslyn diagnostics back to `.lui`.
2. **Runtime kernel:** Implement reactive cells, current component inputs, retained regions, ownership/disposal, and a deterministic scheduler without Avalonia.
3. **Avalonia projection:** Generate descriptors for `TextBlock`, `Button`, `StackPanel`, and `TextBox`, including value priority, events, child policy, and theme assumptions.
4. **Counter proof:** Show one direct assignment, no unrelated assignment, UI-thread dispatch, and cleanup after unmount.
5. **Form proof:** Show two-way text input, validation, focus/selection preservation, async completion after unmount, and error routing.
6. **Structural proof:** Show conditional lifetime plus keyed insert, delete, move, duplicate-key diagnostics, and no callbacks after disposal.
7. **Virtualization proof:** Render a large Lucent item template through `ItemsRepeater` or `ListBox` and verify recycling cleanup.[30]
8. **CSS proof:** Compile classes, a small pseudo-class set, typed tokens, and explicit precedence against local reactive values.
9. **Minimal LSP:** Reuse the stabilized frontend for diagnostics, symbols, completion, hover, and definition.

This sequence moves the minimal language server slightly later than the current roadmap, but does not demote it from the POC. Stabilizing the parser and semantic categories first prevents the LSP from becoming a second experiment in unsettled syntax.

## Tests that should become language contracts

- An opaque method call with no visible reactive source is diagnosed or documented as a snapshot.
- Replacing an observable intermediate object unsubscribes the old path and subscribes the new one.
- Two component instances with the same lexical state-member declaration never share state.
- Duplicate keys fail deterministically; moving a key preserves its state and control identity.
- Removing a branch disposes subscriptions and makes queued updates harmless.
- A background notification never writes an Avalonia property off-thread.
- A local Lucent value and a CSS pseudo-class resolve according to documented precedence.
- A `ContentControl` rejects multiple direct children with a Lucent diagnostic.
- A virtualized container does not retain the prior item’s classes, state, handlers, or context.
- A generated error points to the original `.lui` span rather than only `.g.cs`.
- Text input preserves focus, selection, undo, and validation behavior through state updates.

## Bottom line

Lucent does not need a different platform or a virtual DOM. It needs a smaller initial language and a more explicit runtime contract. If the project makes reactivity finite, retains a compact ownership tree, models Avalonia value priority, and treats control projection as a generated adapter seam, the core idea remains strong. If it keeps “normal C#,” “direct updates,” and “CSS-native” as broad promises without those rules, the difficult behavior will leak into every generated call site and the framework will become shallow in exactly the places where it needs locality.

## Sources

[1] https://docs.avaloniaui.net/docs/properties/value-precedence — Avalonia property value precedence
[2] https://docs.avaloniaui.net/docs/fundamentals/visual-and-logical-trees — Avalonia visual and logical trees
[3] https://docs.avaloniaui.net/docs/app-development/threading — Avalonia threading model
[4] https://docs.avaloniaui.net/docs/styling/style-selectors — Avalonia style selectors
[6] https://docs.avaloniaui.net/docs/styling/style-classes — Avalonia style classes
[7] https://docs.avaloniaui.net/docs/custom-controls/templated-controls — Avalonia templated controls
[9] https://docs.avaloniaui.net/docs/data-binding/compiled-bindings — Avalonia compiled bindings
[10] https://docs.avaloniaui.net/docs/data-binding/data-binding-syntax — Avalonia data binding syntax
[14] https://docs.avaloniaui.net/docs/fundamentals/coded-ui — Avalonia code-only UI
[17] https://v11.docs.avaloniaui.net/docs/guides/data-binding/binding-from-code — Avalonia binding from code
[21] https://github.com/AvaloniaUI/Avalonia/discussions/16997 — Avalonia Accelerate roadmap discussion: hot reload
[22] https://github.com/AvaloniaUI/Avalonia/issues/3266 — Avalonia XAML hot reload issue
[23] https://github.com/AvaloniaUI/Avalonia/blob/main/packages/Avalonia/AvaloniaBuildTasks.targets — Avalonia build tasks targets
[24] https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot — .NET Native AOT deployment overview
[25] https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trimming-concepts — .NET trim analysis concepts
[27] https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.sourcegeneratorcontext — Roslyn SourceGeneratorContext
[29] https://docs.avaloniaui.net/controls/data-display/contentcontrol — Avalonia ContentControl
[30] https://docs.avaloniaui.net/docs/how-to/itemscontrol-how-to — Avalonia ItemsControl and ItemsRepeater
[31] https://docs.avaloniaui.net/docs/how-to/textbox-how-to — Avalonia TextBox guidance
[32] https://docs.avaloniaui.net/docs/data-binding/binding-validation — Avalonia binding validation
