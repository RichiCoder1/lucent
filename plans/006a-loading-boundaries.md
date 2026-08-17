# Plan 006a: Add explicit loading clauses to async boundaries

> **Executor instructions**: Extend Plan 006's single-source async boundary.
> Reuse `OwnedComputed<T>` and `ConditionalRegion`; do not add automatic subtree
> discovery, a runtime status graph, a second boundary type, or Solid-style
> reveal coordination.
>
> **Drift check**: `git diff --stat 6b98897..HEAD -- src/Lucent.Compiler src/Lucent.Runtime tests examples docs`

## Status

- **Priority**: P1
- **Effort**: S
- **Risk**: MEDIUM
- **Depends on**: Plan 006
- **Category**: language / async UX / tooling
- **Status**: TODO

## Why this matters

Plan 006 deliberately made loading UI explicit through
`HasCommittedValue`, but the common first-load shape is noisy and obscures the
useful contract: initial unresolved work replaces content, while later refreshes
keep committed content visible. Lucent already owns every required state and
transactional branch lifetime. A loading clause can expose that behavior without
adding another async abstraction.

Solid v2's loading boundary is the product reference for first-load fallback,
stale content during later updates, and separate loading/error handling:
<https://v2.solidjs.com/concepts/boundaries>. Lucent keeps its source explicit
instead of discovering pending reads dynamically.

## Current contract

- `OwnedComputed<T>` exposes `Value`, `HasCommittedValue`, `IsPending`, `Error`,
  and `Refresh()` with replacement cancellation and stale-generation suppression.
- `try (source) { ... } catch (Exception error) { ... }` owns exactly one
  declared `Computed<T>` and lowers to `ConditionalRegion`.
- `ConditionalRegion.Show(int, ...)` already supports more than two branch IDs;
  its publication rollback and branch-owner disposal need no runtime change.
- Today authors place an `if (!source.HasCommittedValue)` inside the content
  branch or render the source's placeholder value.

## Exact syntax

Add one optional contextual `loading` clause between the content and required
catch clauses:

```csharp
try (packages) {
    PackageResults(items: packages.Value) {}
}
loading {
    ProgressBar { AutomationProperties.Name: "Loading packages"; }
}
catch (Exception error) {
    ErrorPane(message: error.Message, retry: retryCommand) {}
}
```

Rules:

- `loading` is contextual only after an async-boundary content branch. A native
  control or component named `Loading` remains valid elsewhere.
- At most one loading clause is allowed. It must precede the required catch;
  duplicates and `loading` after catch receive syntax diagnostics at the clause.
- `ParseAsyncBoundary` consumes immediately adjacent duplicate loading clauses
  before catch and trailing loading clauses after catch. The first valid
  pre-catch clause wins; every discarded clause is diagnosed at its contextual
  keyword, and recovery leaves the next non-loading render member untouched.
- The clause introduces no local and accepts the same fragment/cardinality
  subset as the content and catch branches. Empty loading content is allowed and
  clears the boundary route during first load.
- Existing `try/catch` source remains valid and keeps its current behavior when
  no loading clause is present.
- The source remains exactly one declared `Computed<T>` identifier. Do not infer
  sources from subtree reads or accept arbitrary tasks/expressions.

## State and branch contract

When a loading clause exists, select exactly one branch in this precedence:

| Source state | Active branch |
| --- | --- |
| `Error is not null` | catch |
| `Error is null && !HasCommittedValue` | loading |
| `Error is null && HasCommittedValue` | content |

`IsPending` does not choose the loading branch. After the first commit, refresh
keeps content and its last committed `Value` mounted; authored content may use
`IsPending` for an updating indicator. Before any first refresh has started,
`HasCommittedValue == false` still selects loading because content is not ready.

The transitions are therefore:

- first refresh: loading → content or catch;
- retry before any successful commit: catch → loading → content or catch;
- refresh after a successful commit: content remains content while pending;
- refresh failure after a commit: content → catch, retaining the stale value;
- retry after a committed failure: catch → content immediately while refresh is
  pending, then content or catch on completion.

The source-error reporting contract remains unchanged. A source with this
boundary reports through catch rather than the root reporter. Exceptions from
mounting, publishing, updating, or disposing loading/content/catch fragments are
not source failures and follow the existing call-site route:

- during initial `Mount()`, the failure propagates to the caller and is not also
  reported;
- during an `OwnedComputed<T>` invalidation, the failure propagates through the
  generated invalidator and `OwnedComputed<T>` reports it to the root once;
- during an authored event/state update, the failure propagates to the existing
  generated event/root route once.

Do not add reporting inside `ConditionalRegion` or the boundary emitter; that
would duplicate the outer owner of each operation.

## Binding and lowering contract

- Extend `UiAsyncBoundarySyntax` with an optional loading branch and retain exact
  clause/branch spans. Keep parser recovery at the next catch or render member.
- Add a dedicated bound async-boundary node containing the source identity and
  all present branches. Do not encode the three-state selector as nested authored
  conditionals or duplicate source resolution in the emitter.
- Replace `ComputedFailureReporter`'s current condition-text recognition with
  structural traversal of the dedicated boundary's computed source identity.
  Nested boundaries count; an unbounded computed does not. This is required so
  source failure renders catch without also reaching the root reporter.
- Bind `packages.Value` as guarded only in the content branch. Loading and catch
  may read status facets or call `Refresh()`, but a `Value` read there receives
  the existing guarded-read diagnostic. Render-reachable helper restrictions
  from Plan 006 remain unchanged.
- Emit one existing `ConditionalRegion` with fixed IDs `loading = 0`,
  `content = 1`, and `catch = 2`. Use one publication route; do not insert a native host,
  wrapper, or nested region merely to obtain three branches.
- Preserve `ConditionalRegion` mount/publication rollback, old-branch disposal,
  fragment ordering, current-input behavior, and owner cleanup without changing
  `Lucent.Runtime`.
- All facets retain the source's existing dependency ID. One source invalidation
  reevaluates the selector once; add no observer graph or new scheduler path.
- Editor intelligence and LSP traversal use the same parsed/bound branches.
  Completion and hover expose the catch local only in catch and ordinary source
  symbols in all branches.

## Workbench proof

Convert the Workbench problems boundary to use the clause. First load shows an
authored loading fragment. A later Ctrl+Shift+R refresh keeps the existing
`ProblemsPane` mounted with `loadedProblems.IsPending == true`; failure switches
to the existing retry branch. Do not add another computed source, command, or
test harness.

## Scope

**In scope**:

- Async-boundary parser, syntax, bound model, binder, emitter, editor traversal,
  diagnostics, and source mapping.
- Focused compiler and protocol tests plus the existing Workbench async flow.
- Language documentation and roadmap/review records.

**Out of scope**:

- Automatic pending-read discovery, multiple sources per boundary, arbitrary
  `Task` values, catch filters/finally, or general synchronous error boundaries.
- Solid's `on` reset key, `Reveal`, sequential/together coordination, nested
  boundary registration, or custom boundary primitives.
- Clearing committed values on refresh, replacing stale-content semantics, or
  changing `OwnedComputed<T>`.
- A runtime loading/error status graph, new public runtime type, hidden native
  host, virtual tree, effects, subscriptions, or batching.

## Steps

### 1. Characterize syntax and state selection

Add failing parser/binder/emitter tests for optional loading, no-clause
compatibility, duplicate/out-of-order clauses, contextual `Loading` names,
branch cardinality, branch-local scopes, and `Value` reads in each branch.
Parser recovery cases retain a following sibling render member.

**Verify**: failures identify only the missing loading-clause contract.

### 2. Parse and bind the loading branch

Retain exact spans, create the dedicated bound boundary, and enforce the
single-source and branch-specific guard rules through the shared semantic path.

**Verify**: diagnostics, completion, hover, and source spans agree in compiler
and protocol tests.

### 3. Lower three states through one existing region

Emit the fixed `0/1/2` branch IDs and the precedence table above. Reuse the
existing fragment factories and transactional `ConditionalRegion.Show` path.

**Verify**: generated-code tests show one region, no new runtime type/host, and
the exact loading/content/catch selector order. Compiler/runtime integration
tests inject loading/content/catch mount, publication, binding-update, and
old-branch-disposal failures: initial mount rethrows without reporting, while
computed/event invalidations take their existing root route exactly once.

### 4. Prove lifecycle transitions in Workbench

Convert the existing problems boundary and extend its current headless flow to
assert first-load loading, first success, stale-content refresh, failure, retry
before/after a commit, synchronous factory throw, rapid replacement cancellation,
focusable retry, and exact branch cleanup. Use the existing controllable loader
and harness. Add generated/runtime coverage proving a bounded computed suppresses
its source reporter while an unbounded computed still reports.

**Verify**: focused compiler, LSP, Workbench, full solution, and VS Code tests
pass; the native reliability smoke remains green.

### 5. Document the bounded feature

Update `docs/LANGUAGE.md` with the syntax, precedence table, stale-refresh rule,
and explicit differences from automatic Solid-style boundaries. Record the
adversarial review and final disposition in `plans/REVIEW-006A.md`.

**Verify**: `git diff --check`, documentation checks, plan/review links, and the
fresh final blocker/high review gate pass.

## Done criteria

- [ ] Existing async boundaries compile unchanged without a loading clause.
- [ ] First unresolved work selects loading; committed refresh keeps content.
- [ ] Error always selects catch before loading/content consideration.
- [ ] Retry transitions match the pre-commit and post-commit contracts.
- [ ] One `ConditionalRegion` owns all branches with transactional rollback and
      exact cleanup; `Lucent.Runtime` has no new public type.
- [ ] Source-reporter suppression uses bound source identity, not generated
      condition text, and bounded/unbounded sources report exactly as intended.
- [ ] Initial branch failure rethrows without duplicate reporting; later
      invalidation failures follow their existing root route once.
- [ ] `Value` is guarded only in the matching content branch, with mapped
      diagnostics elsewhere.
- [ ] Compiler, LSP, and Workbench tests cover syntax, state, tooling, ownership,
      and source mapping.
- [ ] Documentation does not claim automatic source discovery, reset keys, or
      reveal coordination.

## STOP conditions

- Correct semantics require clearing the last committed value or changing
  `OwnedComputed<T>` refresh ordering.
- Three branches require a hidden native host, nested publication route, second
  lifecycle mechanism, or runtime observer graph.
- The compiler cannot preserve branch-specific guarded-read and catch-local
  scopes through the shared semantic analysis.
- Workbench requires multiple computed sources, reset keys, or coordinated
  reveal to prove the loading clause.
