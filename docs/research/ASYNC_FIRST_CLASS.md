# Async as a first-class Lucent capability

**Review date:** 2026-08-15
**Question:** Can Lucent reproduce Solid 2.0's async-first reactive design on C# and Avalonia?

> **Implementation checkpoint:** Package Pulse now proves an owned `Computed<T>`
> returning `Task<T>`, cancellation, generation-based stale-result suppression,
> stale content, dispatcher commits, adjacent compiled CSS, pseudo-classes, and
> native Avalonia transitions. Structural loading/error boundaries and
> dependency-specific invalidation remain future work.

## Decision summary

Yes. C# and Avalonia provide the required execution machinery, but Avalonia does not provide Solid's semantics as a complete feature.

- C# supplies `Task<T>`, `ValueTask<T>`, `IAsyncEnumerable<T>`, `async`/`await`, `CancellationToken`, and flowing `ExecutionContext`.
- Avalonia supplies a single UI dispatcher, a synchronization context, and basic task-result binding.
- Lucent must supply the reactive graph semantics: ownership, dependency tracking, cancellation, stale-result suppression, loading/error boundaries, stale-while-revalidating behavior, and scheduling.

This fits Lucent's existing compiler-first direction. The compiler already identifies reactive `State<T>.Value` reads and lowers them to generated updates; an async computation can become another graph node rather than a second visual tree or a separate view-model convention.

## What Solid 2.0 is doing

Solid 2.0 allows ordinary computations such as memos and derived stores to return a `Promise` or `AsyncIterable`. Consumers still use their normal accessor. An unresolved read marks that branch as not ready, and a structural `Loading` boundary decides where fallback UI appears. This replaces a parallel `createResource` model.[1]

The important semantics are broader than accepting a promise:

1. **One graph for sync and async values.** Async is a capability of an ordinary derived computation, not a separate resource category.[1]
2. **Structural initial loading.** `Loading` handles a branch that cannot yet produce content.[1]
3. **Stale while revalidating.** Once content has committed, it normally remains visible while a changed dependency is being answered. `isPending` exposes that in-flight change for subtle updating UI.[1]
4. **One error path.** Async failures propagate through the graph to an error boundary rather than requiring callers to inspect a resource-specific error property.[1]
5. **Owned lifetime.** Reactive roots are owned by their parent by default, so disposal and cleanup follow UI ownership unless explicitly detached.[2]
6. **Built-in scheduling.** Updates batch by microtask, effects split dependency computation from side-effect application, and transitions are runtime scheduling behavior rather than explicit wrappers.[1][3]

## Mapping to Lucent

| Solid concept | C#/Avalonia mechanism | Lucent responsibility |
| --- | --- | --- |
| Promise-returning computation | `Task<T>` / `ValueTask<T>` | Treat task-returning derivations as graph nodes and expose settled `T` to render computations. |
| Async iterable | `IAsyncEnumerable<T>` | Optional later stream node; cancel enumeration with owner lifetime. |
| Owner | Component or structural-region instance | Own computation, cancellation source, subscriptions, and cleanup. |
| `Loading` | Native Lucent structural region projected to Avalonia controls | Track unresolved reads and atomically reveal fallback/content. |
| `isPending` | Runtime query over graph state | Distinguish initial not-ready state from replacement work in flight. |
| Error boundary | `Exception` from faulted task | Route through one owned Lucent error mechanism on the UI scheduler. |
| Stale result suppression | Generation/version check plus cancellation | Ignore an older task that finishes after a newer dependency version. |
| UI commit | `Dispatcher.UIThread` | Marshal property and structural mutations to Avalonia's UI thread. |

Avalonia's `Task` binding (`{Binding Profile^}`) proves that task completion can feed UI and provides a fallback-value convention. Refresh requires replacing the task and raising property change.[5] That is useful interop, but it does not provide graph-wide ownership, cancellation, stale transitions, structural loading/error boundaries, or compiler-derived dependencies. Lucent should not build its native model on task binding.

## C# constraints

C# supports the runtime model but cannot transparently unwrap `Task<T>` as `T`. Lucent therefore needs either a small language rule or a compiler-visible computation type. It should not use `.Result`, `.Wait()`, or synchronous blocking; those undermine UI responsiveness and can deadlock when a continuation needs the UI context.[6]

`await` captures the current synchronization context by default, and Avalonia installs a dispatcher-backed synchronization context. Lucent should still make commit scheduling explicit: computation may resume anywhere, but all control access and graph commits go through the scheduler. Avalonia documents `Dispatcher.UIThread.Post` for fire-and-forget work and `InvokeAsync` for awaitable UI work.[4]

`CancellationToken` is cooperative, so cancellation alone cannot guarantee correctness. Every computation also needs a monotonically increasing generation. Completion commits only when its owner is alive and its generation is still current.

## Recommended Lucent design

### One computation kind

Do not add `Resource<T>`, `AsyncResource<T>`, and separate loading flags. Add one compiler/runtime concept for a derived computation whose implementation may produce either `T` or an awaitable `T`.

Illustrative syntax only:

```csharp
computed User user = async cancellationToken =>
    await users.GetAsync(userId.Value, cancellationToken);

Fragment Render() =>
    Loading(fallback: ProgressRing()) {
        Text(user.Name);
    };
```

The syntax is not the decision. The semantic contract is:

```text
tracked inputs
    -> computation returning T or awaitable T
    -> not-ready | ready(T) | refreshing(stale T) | faulted(error)
    -> dependent property/structural computations
```

The compiler can lower this to an internal node; callers should not manage task replacement, `PropertyChanged`, or dispatcher posts.

### Owned async node

The minimum internal node needs:

- its component/region owner;
- tracked upstream dependencies;
- the last committed value, if any;
- current generation and cancellation source;
- current status and error;
- dependent computations;
- scheduler access.

On dependency change:

1. increment generation;
2. cancel the previous token;
3. start the new computation without blocking the UI thread;
4. retain the previous committed value if one exists;
5. on completion, enqueue a UI-thread commit;
6. commit only if owner and generation are still current;
7. invalidate only dependents of that node.

This is a deep runtime module: generated code needs only create/read/invalidate/dispose operations, while race handling and lifecycle stay local.

### Boundaries, not flags

Use structural regions for first-load and errors:

```csharp
Errored(fallback: error => ErrorPanel(error)) {
    Loading(fallback: UserSkeleton()) {
        UserCard(user);
    }
}
```

A pending query can be added later for stale-refresh affordances. Do not expose `IsLoading`, `Error`, and `Data` on every node; that recreates the parallel resource model Solid is removing.

### Scheduler policy

Lucent already identifies an explicit scheduler seam as required. The first policy can stay small:

- one FIFO dispatcher queue;
- coalesce repeated invalidation of the same computation in one turn;
- compute before apply;
- apply Avalonia changes only on the UI thread;
- ignore work for disposed owners;
- preserve stale committed UI until replacement work settles;
- provide `FlushAsync()` only for tests and imperative focus/layout boundaries.

Do not implement priorities, optimistic mutation, SSR concepts, or multiple transition classes in the first slice.

## Smallest credible proof

Add one async profile/search example after the component/conditional lifetime work exists:

1. A `State<int>` or query state drives an async derived value.
2. Initial load displays one fallback region.
3. Changing the input keeps the old content visible and exposes pending state.
4. Two rapid changes prove that the older completion cannot overwrite the newer result.
5. Removing the owning region cancels work and produces no later control update.
6. A fault reaches one error boundary.
7. A deterministic scheduler test covers the same sequence without real timing.

This proves the architecture without first building a general effects system or a broad async API.

## Fit with the current repository

The direction is compatible, but the implementation is not ready for it yet:

- `GeneralCSharpEmitter` currently rewrites `State<T>.Value` lexically and calls one broad `UpdateBindings()` method.
- Generated state setters already marshal through `Dispatcher.UIThread`, which is the correct outer constraint.
- The current IR contains state, controls, properties, events, and keyed loops, but no computation, owner, conditional boundary, scheduler, or error node.
- Existing design notes already require cancellation, stale-result suppression, deterministic cleanup, error routing, and an explicit scheduler seam before async sugar is exposed.

The next architectural step should therefore be the scheduler/owner module and a symbol-bound reactive computation IR—not public async syntax. Once that seam works for synchronous derived values, allowing its compute function to return `Task<T>` is a contained extension.

## Conclusion

C# and Avalonia do allow Lucent to reproduce the useful parts of Solid 2.0's async-first model. The compiler advantage is substantial: Lucent can derive dependencies, generate cancellation and generation checks, and commit directly to existing Avalonia controls. The hard part is not `await`; it is giving pending work precise ownership and reveal semantics.

The minimal path is one derived-computation model, one owner/scheduler implementation, and structural loading/error boundaries. Avalonia task binding remains an interop feature, not the foundation.

## Sources

[1] Solid 2.0 async-data RFC: https://github.com/solidjs/solid/blob/next/documentation/solid-2.0/05-async-data.md
[2] Solid 2.0 signals, ownership, and context RFC: https://github.com/solidjs/solid/blob/next/documentation/solid-2.0/02-signals-derived-ownership.md
[3] Solid 2.0 reactivity, batching, and effects RFC: https://github.com/solidjs/solid/blob/next/documentation/solid-2.0/01-reactivity-batching-effects.md
[4] Avalonia threading documentation: https://docs.avaloniaui.net/docs/app-development/threading
[5] Avalonia task-result binding: https://docs.avaloniaui.net/docs/data-binding/how-to-bind-to-a-task-result
[6] .NET guidance on synchronous wrappers for async methods: https://learn.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/synchronous-wrappers-for-asynchronous-methods
[7] .NET task cancellation: https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/task-cancellation
