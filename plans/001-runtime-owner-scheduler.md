# Plan 001: Centralize component ownership and UI dispatch

> **Executor instructions**: Follow this plan in order. Run every verification
> gate before continuing. Update this plan's row in `plans/README.md` when done.
> Do not broaden the runtime interface to make later plans easier.
>
> **Drift check**: `git diff --stat 3b27cfe..HEAD -- src/Lucent.Compiler/CodeGeneration src/Lucent.Poc tests Lucent.sln`
> The plan was written against commit `3b27cfe` plus the active working-tree
> CSS/async/tooling changes. Compare the live excerpts below. Stop if mount,
> keyed-row disposal, computed cancellation, or dispatcher behavior has changed.

## Status

- **Priority**: P0
- **Effort**: M (one focused multi-file change, not a runtime rewrite)
- **Risk**: HIGH
- **Depends on**: none
- **Category**: direction / architecture
- **Planned at**: commit `3b27cfe`, 2026-08-16

## Why this matters

Lucent currently emits ownership, event cleanup, row cleanup, cancellation, and
dispatcher checks into every generated component. Components, branches, and
slots will multiply that code. Extract only the stable lifetime and dispatch
rules now, while leaving state storage, computed generations, and property
updates generated.

The target is one deep module: generated code learns a four-operation owner
interface while the runtime hides thread-safe disposal, cleanup ordering, child
ownership, and stale queued-commit suppression.

## Current state

- `src/Lucent.Compiler/CodeGeneration/GeneralCSharpEmitter.cs:67-205` emits an
  `IDisposable` class, mount guard, native event unsubscription, keyed-row
  cleanup, computed cancellation, and `_disposed` state.
- `GeneralCSharpEmitter.cs:231-296` performs `Dispatcher.UIThread.CheckAccess()`
  and `Post()` in every generated state setter.
- `GeneralCSharpEmitter.cs:304-389` posts computed success/failure commits and
  checks `_disposed` plus generation.
- `GeneralCSharpEmitter.cs:932-1150` gives each keyed row an ad hoc
  `List<Action>` cleanup implementation.
- There is no runtime project. Generated application code references Avalonia
  directly and is tested primarily through readable source assertions in
  `tests/Lucent.Compiler.Tests/GeneralCompilerTests.cs`.

Accepted constraints from the ADRs and architecture docs:

- A component instance is not an Avalonia control.
- Avalonia remains the only rendering and UI-thread platform.
- Disposal is deterministic and idempotent.
- No virtual DOM, service locator, generic runtime facade, or alternate backend
  seam is needed.

## Exact module interface

Create `src/Lucent.Runtime/` targeting `net9.0` and referencing Avalonia. Its
public surface for this plan is exactly these three types:

```csharp
namespace Lucent.Runtime;

public interface IUiDispatcher
{
    // Run inline when already on the UI thread; otherwise enqueue once.
    // Calls from one thread are observed in FIFO order.
    void Dispatch(Action action);
}

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public static AvaloniaUiDispatcher Instance { get; }
    public void Dispatch(Action action);
}

public sealed class ComponentOwner : IDisposable
{
    public ComponentOwner(IUiDispatcher dispatcher);
    public bool IsDisposed { get; }
    public CancellationToken CancellationToken { get; }
    public ComponentOwner CreateChild();
    public void OnDispose(Action cleanup);
    public void Dispatch(Action action);
    public void Dispose();
}
```

Do not add `IRuntime`, `IComponentOwner`, factories, priorities, async methods,
or public collection/region types.

### Required semantics

- `IUiDispatcher.Dispatch` is the only scheduler seam. Production dispatches
  inline on the UI thread and otherwise posts to `Dispatcher.UIThread`.
- `ComponentOwner.Dispatch` ignores calls made after disposal and wraps queued
  callbacks with a second disposal check. A background completion racing with
  disposal therefore cannot mutate controls.
- `OnDispose` registers cleanup in LIFO order. Registration after disposal is an
  `ObjectDisposedException`; silently accepting it would hide an ownership bug.
- `CreateChild` registers the child as parent-owned. Explicit child disposal is
  allowed and idempotent. Parent disposal disposes remaining children before
  earlier parent cleanup because of LIFO registration.
- The owner has one lifetime `CancellationTokenSource`. `Dispose` marks the
  owner disposed atomically, cancels first, then executes all cleanup even when
  one callback throws. After all callbacks run, throw one `AggregateException`
  containing cleanup failures. Cancellation callbacks that throw are included.
- Mount, owner/child creation, cleanup registration, and disposal are UI-thread
  lifecycle operations. `IsDisposed`, `CancellationToken`, and `Dispatch` must
  remain safe when read/called by background task completions.

The deterministic test adapter belongs in `tests/Lucent.Runtime.Tests`; it is
not production API. It should queue actions and expose `DrainOne()`/`DrainAll()`
to tests.

## Generated-code target shape

Generated classes remain internal and receive optional dispatcher injection:

```csharp
private readonly ComponentOwner _owner;
private bool _mounted;

internal CounterComponent(IUiDispatcher? dispatcher = null)
{
    _owner = new ComponentOwner(dispatcher ?? AvaloniaUiDispatcher.Instance);
}
```

Rules for the emitter:

1. Replace `_disposed` reads with `_owner.IsDisposed`.
2. Replace every `CheckAccess`/`Post` block with `_owner.Dispatch(() => ...);`.
   Keep the actual state mutation in a non-dispatching private core method so a
   call does not recursively dispatch forever.
3. Immediately after subscribing a native event, register its exact inverse
   with `_owner.OnDispose`.
4. Give every keyed row a child owner. Register row event cleanup on that owner;
   removing a key disposes its child owner. Delete row-local `List<Action>`.
5. Keep per-computed generation and replacement `CancellationTokenSource` in
   generated code for now. Link replacement cancellation to
   `_owner.CancellationToken`; register one owner cleanup callback for the
   current CTS field rather than one callback per refresh. Plan 006 may deepen
   async nodes later.
6. Generated `Dispose()` delegates to `_owner.Dispose()`. Keep the mount-once
   guard generated; mount count is not a general ownership concern.

### Consumer reference wiring

Generated code now has a compile-time dependency on `Lucent.Runtime`. Wire that
dependency for every current targets consumer, not only the POC:

- Add `LucentRuntimeProject` beside the compiler assembly properties in
  `build/Lucent.Compiler.props`, pointing at the local runtime project for this
  source-checkout phase.
- In `build/Lucent.Compiler.targets`, conditionally add that project as a normal
  `ProjectReference` when `LucentSource` is present and the project exists. This
  creates the build-graph edge and compile/copy-local reference; a bare
  `<Reference HintPath>` is insufficient when runtime outputs were deleted.
- Do not also add a direct POC runtime reference; the imported targets path must
  be the behavior under test for every current consumer.
- Include the resolved runtime target output in generation/build inputs only if
  needed for incremental correctness; do not hard-code a second assembly path
  as the compile reference.
- Add a temporary consumer integration test that imports the repo props/targets,
  contains one `.lui`, and runs `dotnet build` without manually referencing the
  runtime. Plan 008 will replace local hint paths with NuGet package assets.

## Commands

| Purpose | Command | Expected result |
| --- | --- | --- |
| Restore new projects | `dotnet restore Lucent.sln` | Runtime/test assets exist before no-restore gates |
| Baseline | `dotnet test Lucent.sln --no-restore` | 45 tests pass |
| Runtime tests | `dotnet test tests/Lucent.Runtime.Tests/Lucent.Runtime.Tests.csproj --no-restore` | all runtime tests pass |
| Compiler tests | `dotnet test tests/Lucent.Compiler.Tests/Lucent.Compiler.Tests.csproj --no-restore` | all compiler tests pass |
| MSBuild tests | `dotnet test tests/Lucent.Compiler.MSBuild.Tests/Lucent.Compiler.MSBuild.Tests.csproj --no-restore` | generated consumer compiles with runtime reference |
| Build | `dotnet build Lucent.sln --no-restore` | exit 0, no warnings |
| Full tests | `dotnet test Lucent.sln --no-build` | all tests pass |

## Scope

**Create**:

- `src/Lucent.Runtime/Lucent.Runtime.csproj`
- `src/Lucent.Runtime/IUiDispatcher.cs`
- `src/Lucent.Runtime/AvaloniaUiDispatcher.cs`
- `src/Lucent.Runtime/ComponentOwner.cs`
- `tests/Lucent.Runtime.Tests/Lucent.Runtime.Tests.csproj`
- `tests/Lucent.Runtime.Tests/ComponentOwnerTests.cs`
- `tests/Lucent.Runtime.Tests/UiDispatcherContractTests.cs`

**Modify**:

- `Lucent.sln`
- `build/Lucent.Compiler.props`
- `build/Lucent.Compiler.targets`
- `src/Lucent.Compiler/CodeGeneration/GeneralCSharpEmitter.cs`
- `src/Lucent.Poc/Lucent.Poc.csproj`
- `src/Lucent.Poc/MainWindow.cs`
- `src/Lucent.Poc/Program.cs`
- `tests/Lucent.Compiler.Tests/Lucent.Compiler.Tests.csproj`
- `tests/Lucent.Compiler.Tests/GeneralCompilerTests.cs`
- `tests/Lucent.Compiler.MSBuild.Tests/CompileLucentTests.cs`
- Existing generated snapshot only if the normal verify command requires it.

**Out of scope**:

- Parser, syntax, binder, styling, LSP, component composition, conditional UI,
  effect syntax, DI, batching, scheduler priorities, or a reactive library.
- Moving state values or property assignments into the runtime.
- Changing the public Lucent source language.

## Steps

### Step 1: Capture the current generated contract

Before adding the runtime, add assertions around all behavior being replaced:

- root event cleanup;
- keyed-row event cleanup;
- off-thread state dispatch;
- computed success/error stale-generation guards;
- no post-disposal control update;
- mount-once and idempotent disposal.

Use the existing generated-source test style. Do not add broad snapshots for the
new runtime internals.

**Verify**: compiler tests pass before production changes.

### Step 2: Add runtime projects and dispatcher contract

Add both projects to the solution. Implement the one-method dispatcher and run
the same contract against `AvaloniaUiDispatcher` where possible and the
deterministic adapter. Assert null-action validation.

**Verify**: dispatcher contract tests pass.

### Step 3: Implement `ComponentOwner`

Use a lock or equivalent atomic state transition to protect disposal state and
cleanup registration. Do not invoke user cleanup while holding the lock. Capture
the cleanup stack, mark disposed, then cancel/run cleanup. Test:

- LIFO order;
- child before parent cleanup;
- explicit child dispose followed by parent dispose;
- registration after dispose;
- dispatch before/after disposal;
- disposal between queue and drain;
- all callbacks run when several throw;
- one throwing cancellation-token callback plus throwing ordinary cleanups all
  run and appear in the final aggregate;
- concurrent dispatch/dispose stress loop without control mutation after drain.

**Verify**: runtime tests pass repeatedly (at least 20 iterations for the race
test inside one test process; do not shell-loop the suite).

### Step 4: Migrate root generated ownership

Inject/create the owner, migrate state dispatch and root events, then delegate
`Dispose`. Preserve source mappings and generated class visibility.

**Verify**: compiler tests pass and generated code contains no direct
`Dispatcher.UIThread` reference.

### Step 5: Migrate keyed-row ownership

Replace `disposeActions` with one child owner per entry. An entry's `Dispose`
must dispose the child owner exactly once. Reordering a retained key must not
replace its owner.

**Verify**: Todo keyed identity tests pass and generated code contains no
`List<Action> disposeActions`.

### Step 6: Route computed commits through the owner

Keep generation checks and per-refresh cancellation generated. Route both
success and failure commits through `_owner.Dispatch`, link refresh cancellation
to owner cancellation, and retain the generation check inside the dispatched
callback.

**Verify**: rapid replacement, late completion, and disposal tests pass.

### Step 7: Run the native smoke

The active working tree mounts Package Pulse in `MainWindow` while the existing
smoke still asserts Todo's tree. First split the smoke harness into explicit
Todo and Package Pulse cases so it never depends on whichever sample
`MainWindow` happens to mount. Keep the two race concerns at the correct test
seams:

- In deterministic runtime tests, queue through the test dispatcher, dispose the
  owner, drain the queue, and prove the callback did not run.
- In the Package Pulse native smoke, let the real fixed-delay search complete on
  its background continuation, wait with a bounded `DispatcherTimer`/UI-turn
  loop until the committed status/results are visible, then close cleanly. Do
  not block the UI thread or attempt a timing race between close and commit.

This native gate proves the production Avalonia adapter's off-thread path. The
deterministic runtime gate proves post-disposal suppression.

**Verify**:

```powershell
dotnet run --project src/Lucent.Poc/Lucent.Poc.csproj -- --smoke-test package-pulse
```

Expected: a component-specific `SMOKE:` sequence completes with exit code 0.

### Step 8: Prove targets consumers receive the runtime

Extend the MSBuild integration tests with a temporary SDK project that imports
the repo's Lucent props/targets, includes one source, and invokes `dotnet build`.
Delete `src/Lucent.Runtime/bin` and `obj` before the fixture build (after other
parallel tests are no longer using them), or direct the fixture to isolated
base output paths. Do not add a manual runtime reference in the fixture. Assert
the project-reference edge rebuilt the runtime, generated code compiled, and
the copied output contains `Lucent.Runtime.dll` where normal copy-local behavior
applies.

**Verify**: the focused MSBuild test passes.

## Test plan

- Runtime tests use observable callback order/counters only, not private fields.
- Compiler tests assert that generated callers use the runtime interface and
  retain source-mapped application expressions.
- POC smoke is the production-adapter test; deterministic runtime tests are the
  race and ordering test surface.
- Do not retain old emitter tests whose only assertion is direct dispatcher or
  `disposeActions` implementation text after the replacement tests pass.

## Done criteria

- [ ] The runtime public surface is exactly the three types above.
- [ ] Generated code contains no direct `Dispatcher.UIThread` call.
- [ ] Generated code contains no row-local cleanup list.
- [ ] Root and row native events clean up through owners.
- [ ] Queued commits are harmless after owner disposal.
- [ ] Cleanup is LIFO, exhaustive, and reports aggregated failures.
- [ ] A fresh targets consumer compiles generated runtime references.
- [ ] Generated behavior and current examples remain unchanged.
- [ ] Solution build, full tests, and native smoke pass.
- [ ] No files outside the listed scope are modified except `plans/README.md`
      status bookkeeping.

## STOP conditions

- Correct behavior requires exposing controls, component IR, state storage, or
  styling from the runtime interface.
- The implementation needs another scheduler abstraction or global mutable
  dispatcher.
- `ComponentOwner.Dispose` would need to run arbitrary cleanup under a lock.
- Per-computed cancellation cannot remain generated without changing semantics;
  report the exact conflict instead of inventing a generic async runtime.
- The POC needs a second ownership path outside `ComponentOwner`.

## Maintenance notes

Plan 002 may add one concrete conditional-region type behind this owner. Plans
003 and 005 must reuse child owners. Reviewers should scrutinize race behavior,
cleanup exception handling, retained keyed-row owners, and accidental runtime
surface growth.
