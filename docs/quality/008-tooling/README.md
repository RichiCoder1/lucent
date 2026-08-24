# Plan 008 tooling benchmark

`baseline.json` is captured with `LUCENT_DISABLE_BASE_CACHE=1` and
`LUCENT_DISABLE_INCREMENTAL_REBIND=1`; `final.json` uses the bounded shared
project-semantic cache and unchanged-signature rebind. Each artifact records
500 warm completions and 500 edit-plus-completion samples with the same fixture,
UTF-16 completion position, document-version schedule, required completion
labels, and completion-label fingerprint.

The runner uses an interactive in-process duplex JSON-RPC harness. It writes one
sample, waits for that exact response ID, then writes the next sample: there is
no request prequeue. Edit samples start their timer before `didChange`, send the
following completion, and stop after its parsed response. Server allocation is
measured with process-wide `GC.GetTotalAllocatedBytes` around each handled
request, rather than the invalid thread-local counter across `await`; it is
therefore conservative for any harness allocation occurring while a request is
handled. The runner records peak/retained managed memory and active project
generations.

Artifacts identify both HEAD and the measured dirty source. `worktreeSha256` is
SHA-256 over HEAD, the SHA-256 of `git diff --binary HEAD` for the measured
compiler/MSBuild/LSP/test/benchmark paths, and each sorted relevant untracked
path plus its file hash. The exact algorithm and untracked manifest are embedded
in each JSON artifact. Documentation, plans, research, subagent artifacts, and
other concurrent work are deliberately excluded; the measured paths are listed
in the embedded algorithm text. Artifacts are written before budget enforcement
so the intentionally failing baseline is still inspectable.

On the recorded Windows/.NET 9 reference machine, the final p95 was 0.2039 ms
for warm completion and 52.3567 ms for edit-plus-completion. The corresponding
p95 allocations were 17,056 and 9,110,688 bytes. These are below the Plan 008
100/150 ms and 2/32 MiB budgets; one active project generation is below the
two-generation bound. Re-run both files whenever the measured source identity
changes.

With both caches disabled, the same sequential workload recorded a 452.7499 ms
edit p95 and 105,771,136-byte edit p95 allocation, so `baseline.json` exits
nonzero after evidence is written as intended.

The runner is a release-only evidence gate, not a CI timing assertion. CI should
run deterministic language-server protocol and cache tests instead.
