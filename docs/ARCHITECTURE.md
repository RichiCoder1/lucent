# Compiler and runtime architecture

Lucent should provide simple component semantics to authors and specialized execution to the machine. The compiler does most of the analysis; the runtime owns only the state and coordination that must remain dynamic.

## Compilation pipeline

The proof-of-concept architecture is a dedicated Lucent frontend that generates C# and delegates ordinary .NET compilation to Roslyn:

```text
                         .lui
                           |
                     Lucent parser
                           |
                      syntax tree
                           |
                   semantic analysis
             / C# types / UI tree / reactivity
                           |
                       Lucent IR
                           |
                     generated C#
                           |
                         Roslyn
                           |
                      .NET assembly
                           |
                     Lucent runtime
                           |
                        Avalonia
```

The compiler and language server must share the parser, syntax tree, semantic model, diagnostics, and symbols. A Roslyn fork is too expensive for the initial proof of concept, while treating `.lui` as ordinary `.cs` would make false promises about syntax and tooling.

The first intermediate representation should model only what the initial renderer needs: components, controls, properties, events, children, slots, context, conditionals, loops, expressions, and reactive dependencies. It should not be generalized around hypothetical alternate renderers.

## Rendering model

Lucent does not use a generic runtime virtual DOM by default. Given:

```csharp
Text {
    text: $"Hello {user.Name}";
}
```

the frontend can associate `user.Name` with the target Avalonia property. A change should update that existing property directly rather than rerendering and diffing an unrelated tree.

The author-facing contract is nevertheless a declarative `Fragment Render()` method. Semantically, it describes the complete output for the component's current inputs, state, slots, and context, and its invocation count is not observable. The compiler may lower that method into creation, property, event, and structural computations instead of calling it as a conventional virtual method at runtime.

Conceptually, generated code separates creation and static assignment from reactive work:

```text
CREATE Column
CREATE Text
CREATE Button

STATIC
  Button.Text = "Sign out"

REACTIVE
  user.Name -> Text.Text

EVENT
  Button.Click -> signOut
```

Conditional and repeated regions are structural dependencies. A changed condition may create or remove a subtree; a changed property should not invalidate that larger region unless it depends on the same value.

## Identity and lifetime

Component implementation lookup and persistent instance identity are separate concerns.

- Static component sites and state-member declarations can use lexical identity within their owner.
- A keyed repeated component uses the component call site plus its key.
- State, subscriptions, effects, and context access belong to the resulting logical instance.
- Removing a logical instance disposes its owned resources.

For a keyed loop, the implementation must support insertion, deletion, movement, state preservation, and minimal control churn. Unkeyed dynamic identity rules should stay narrow until real examples justify a broader model.

## Runtime responsibilities

`Computed<T>` execution is represented by one owner-bound `OwnedComputed<T>`
per declaration. It owns cancellation, generation checks, stale-result
suppression, pending/error state, and dispatcher commits. An explicit
`try (source) { ... } catch (Exception error) { ... }` UI boundary reuses
`ConditionalRegion` for its failure branch; otherwise a source failure reaches
the component owner's root reporter exactly once.

The runtime may own:

- component instances and state-member storage;
- context scopes;
- subscriptions and cleanup;
- effect scheduling;
- invalidation and batching;
- keyed structural regions;
- Avalonia dispatcher integration;
- development instrumentation.

It should not create a second complete visual tree for generic diffing, another dependency-property system, another rendering or windowing layer, or reflection-heavy hot paths. Runtime CSS parsing should also be avoided where the compiler has the required information. Application-owned persistence, document coordination, and native accessibility peers remain ordinary Workbench services rather than Lucent runtime features.

## Avalonia boundary

Avalonia remains responsible for windows, rendering, input, accessibility, text and IME, clipboard, drag and drop, menus, graphics, automation, and application lifetime.

Lucent projects need a deliberate escape path through the abstraction:

```text
Lucent component
    -> projected Avalonia control
    -> custom or third-party Avalonia control
    -> platform-specific integration
```

Controls and components share composition syntax, but components should not be forced into heavyweight Avalonia control instances. The projection layer must map Lucent properties, events, children, styles, and lifetimes onto Avalonia without hiding the underlying control when an application needs it.

## Existing .NET observable types

`INotifyPropertyChanged`, `INotifyCollectionChanged`, `ObservableCollection<T>`, relevant Avalonia observables, and potentially `IObservable<T>` are adapter surfaces rather than Lucent's native reactive model.

The priority order is:

1. Compiler-visible Lucent reactivity.
2. Efficient Avalonia integration.
3. Automatic or low-friction .NET observable interop.
4. Slower fallback mechanisms only where necessary.

At an explicitly authored native property seam, `binding(source.Path)` lowers
to Avalonia's public coded `CompiledBinding` API. Avalonia then owns
`DataContext` changes, property notifications, validation, and default binding
modes. Ordinary Lucent expressions still lower to direct assignments and
compiler-derived invalidation. The two schedulers do
not drive the same property, and Lucent-native state does not implement legacy
notification interfaces merely for compatibility.

## Performance constraints

The proof of concept needs evidence for the architecture, not just a parser demo. Measure or inspect at least:

- whether a state change updates an existing Avalonia property directly;
- the allocations and bookkeeping for a small component instance;
- keyed insertion, movement, and removal behavior;
- subscription creation and disposal;
- startup work introduced by generated code and the runtime;
- whether static CSS work remains out of the runtime hot path.

Scheduling, batching, and priority semantics are deferred, but the first implementation should make those policies explicit enough to replace later.
