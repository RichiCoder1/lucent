# Plan 006: Complete lifecycle, errors, accessibility, and UI testing

> **Executor instructions**: Treat this as the quality gate for dogfood, not a
> feature grab bag. Update the index only when observable contracts pass.
>
> **Drift check**: `git diff --stat 3b27cfe..HEAD -- src examples tests docs`

## Status

- **Priority**: P1
- **Effort**: L
- **Risk**: MEDIUM
- **Depends on**: plans 001–005
- **Category**: direction / correctness / tests
- **Planned at**: commit `3b27cfe`, 2026-08-16

## Why this matters

Real applications spend much of their time loading, refreshing, failing,
persisting, subscribing, and shutting down. They also need keyboard and screen
reader semantics that survive custom composition. Make those behaviors owner
contracts and headless interaction tests before packaging Lucent.

## Current state

- `Computed<T>` cancels replaced work and suppresses stale generations, but the
  emitter owns the implementation and exposes `IsPending`/`ErrorMessage` fields
  (`GeneralCSharpEmitter.cs:304-389`).
- `docs/DECISIONS.md` defers reusable state, effect cleanup, DI integration,
  structural error/loading handling, and scheduling details.
- Existing POC smoke coverage imperatively inspects a real window; there is no
  Avalonia headless test project or automation/accessibility contract.

References:

- Avalonia accessibility:
  <https://docs.avaloniaui.net/docs/app-development/accessibility>
- Avalonia headless testing:
  <https://v11.docs.avaloniaui.net/docs/concepts/headless/>

## Scope

**In scope**:

- Move async computation ownership into the runtime owner established by Plan
  001 while preserving stale-while-refreshing semantics.
- Add structural loading and error boundaries on conditional-region machinery.
- Add one owned subscription/effect mechanism with cleanup-before-rerun and
  cleanup-on-dispose; prefer ordinary owned helper objects where enough.
- Define constructor/input injection for application services; interoperate with
  `Microsoft.Extensions.DependencyInjection` without replacing it.
- Add `INotifyPropertyChanged`, `INotifyCollectionChanged`, and relevant Avalonia
  observable adapters only where Workbench uses them.
- Persist Workbench settings/recent workspaces atomically through an application
  repository interface.
- Add automation metadata forwarding and Avalonia headless user-flow tests.

**Out of scope**:

- Hook ordering, a custom DI container, global reactive stores, optimistic
  mutation framework, telemetry platform, SSR, or multiple scheduler priorities.
- Replacing Avalonia automation peers or testing native file-picker UI.

## Steps

### 1. Unify async ownership

Replace generated cancellation/generation boilerplate with an owner-bound async
node. Keep one graph for sync and async derived values. Cancellation remains
cooperative, so generation and owner checks stay mandatory.

**Verify**: tests cover rapid replacement, ignored late success/error, owner
disposal, stale value, first load, and refresh.

### 2. Add structural loading and error handling

Implement boundaries as owned structural regions. A first unresolved read shows
loading content; refresh retains committed content; an unhandled fault reaches
the nearest error boundary and one root reporter.

**Verify**: deterministic tests cover nested boundaries and cleanup after fault.

### 3. Add the minimum lifecycle interface

Support owned subscriptions and effects with explicit phase, cleanup-before-
rerun, cleanup-on-dispose, and error routing. If an ordinary helper object owned
by `ComponentOwner` handles a case, do not add syntax.

**Verify**: subscription counts return to zero after branch/component disposal.

### 4. Add application data adapters

Use real Workbench models to implement property/collection notification adapters
and atomic settings persistence. Keep adapters at the interop seam; do not make
Lucent state implement legacy notification interfaces.

**Verify**: external model updates invalidate only dependent UI and settings
survive a write-interruption simulation without corruption.

### 5. Establish accessibility contracts

Forward `AutomationProperties`, preserve native control peers, require accessible
names for custom interactive controls, and verify visible focus for keyboard
flows. Add stable automation IDs only where tests or assistive tooling need them.

**Verify**: automated checks find names/roles for Workbench's primary controls.

### 6. Add headless user-flow tests

Create a dedicated Avalonia headless project. Cover open workspace through a
fake top-level adapter, quick open, command palette, focus restoration, list
selection, file edit, problem navigation, loading/error recovery, settings, and
clean shutdown.

**Verify**: tests run without a visible desktop and pass in CI-ready mode.

## Done criteria

- [ ] Async, effects, subscriptions, and boundaries share owner lifetime.
- [ ] One root error reporter sees otherwise unhandled errors.
- [ ] Observable adapters are incremental and disposed deterministically.
- [ ] Workbench settings writes are atomic and tested.
- [ ] Primary Workbench flows are keyboard accessible and headless-tested.
- [ ] Native automation peers remain authoritative.

## STOP conditions

- The lifecycle interface depends on invocation order like React hooks.
- A second async resource model appears beside computed nodes.
- DI or persistence becomes global runtime state.
- Tests need production backdoors that application code can call.

## Maintenance notes

This plan turns framework claims into user-observable contracts. Review failure
and disposal paths more carefully than happy-path syntax.
