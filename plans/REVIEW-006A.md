# Adversarial review disposition for Plan 006a

Reviewed by fresh isolated read-only reviewer processes with no repository
write authority.

## Initial review

Verdict: **FAIL**.

- Source-error suppression depended on generated condition-text matching and
  would regress after introducing a dedicated bound boundary.
- Initial mount and later invalidation error routes were conflated, risking
  duplicate reporting.
- Duplicate/trailing loading-clause recovery was not executable enough.
- Transition and lifecycle tests omitted pre-commit retry, synchronous failure,
  rapid replacement, exact reporting, and branch cleanup.

The plan now requires structural source-identity traversal, distinct existing
call-site error routes, exact parser consumption/recovery, fixed branch IDs, and
the missing compiler/runtime/headless evidence.

## Second review

Verdict: **FAIL** on the review-record deliverable only. The reviewer confirmed
the revised syntax, selector, reporting, lowering, guard, tooling, transition,
and test contracts, but the roadmap linked this file before it existed. The
plan now names this record in Step 5 and includes it in final link/review gates.

## Implementation acceptance

Implemented in the active Plan 006a slice. The generated boundary uses one
`ConditionalRegion` with branch IDs `0` loading, `1` content, and `2` catch;
existing two-branch boundaries retain their generated shape. `OwnedComputed<T>`
and the runtime public surface are unchanged. Source failure suppression now
checks `BoundAsyncBoundary.SourceId`, not generated condition text.

Executable evidence includes compiler branch/guard/recovery tests, protocol
traversal with loading content, Workbench physical loading/stale/failure/retry
flow, Package Pulse compatibility, and warning-free solution gates. The
Workbench loading fragment is authored in `WorkbenchApp.lui`; no second test
harness or native host was added.

## Implementation review

Verdict: **FAIL**, then remediated. Fresh Standards and Spec reviews found that
nested structural members could be accepted but omitted, and that loading
source-map/lifecycle evidence was weaker than the checked completion claims.
The binder now rejects nested structural regions consistently across all async
branches. Focused tests cover exact loading diagnostic positions, all three
branch IDs, transactional rollback/cleanup, computed invalidation reporting,
and the authored Workbench first-load/stale/error/retry transitions.

## Final review

Verdict: **PASS**. Fresh independent Standards and Spec reviews found no
blocker, high, or medium issue after the final traversal and executable
reporting fixes. They confirmed async-branch slot-yield traversal, bound source
identity, fixed branch IDs, generated initial-mount and bounded/unbounded
reporting behavior, transactional cleanup, loading source maps/tooling, and the
Workbench transition/cancellation flow.

The intentionally bounded surface still
rejects automatic source discovery, multiple sources, arbitrary tasks, reset
keys, reveal coordination, nested structural regions, and `Value` reads outside
the content branch. Avalonia GUI 200% scaling remains a separate approved
deferral from Plan 007 and is unrelated to this plan.
