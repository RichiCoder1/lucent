# M5 challenge B: optimistic status mutation

Add an Open/Close action to the selected issue inspector. The Issue Browser source contract—not Lucent Core—classifies outcomes as Saved, Rejected(reason), or TransientFailure(reason). Rejection rolls back and displays its reason. Transient failure retains the optimistic status, marks the issue Not synced, and offers manual Retry. Browsing, filtering, selection, and unrelated mutations remain interactive.

The newest mutation wins per issue, while different issues may save concurrently. Filtering, virtualization departure, and selection changes do not cancel saves because saves belong to application issue state; application disposal cancels all outstanding saves. The ordinary deterministic local source must cover success, rejection, and fail-once transient behavior without framework test hooks. Stale completions must never commit. Automatic retry, resilience libraries, generalized mutation policy, and malicious-callback hardening are excluded.

