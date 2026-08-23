# Plan 008 evidence record

Plan 008 is **DONE**. Independent Standards and Spec reviews passed after the
final cancellation-framing and benchmark-attestation remediations.

## Baseline and final benchmark

- Fixture and workload: `tools/Lucent.LanguageServer.Benchmarks`.
- Baseline: `docs/quality/008-tooling/baseline.json` (cache disabled solely for
  comparison).
- Final: `docs/quality/008-tooling/final.json`.
- Both runs use 500 sequential, non-overlapping warm completions and 500
  edit/completion cycles measured from client send through the matching parsed
  response.
- Final p95: 0.1805 ms warm / 51.9855 ms edit-plus-completion; p95 allocation:
  17,056 / 8,441,112 bytes; one active project generation; peak heap
  84,887,336 bytes from a 33,740,296-byte post-warm baseline; retained heap
  41,960,920 bytes.
- Both artifacts identify the measured dirty source as worktree SHA-256
  `6951F81DEF34059531F998DD6D68229F5B5F131907E844E8308DFA846E36A515`.

## Implemented seams

- A bounded eight-entry shared Roslyn base cache uses project/context contents,
  global usings, references, and source bytes as its identity.
- Design-time MSBuild consumes an atomic generated-file manifest rather than a
  wildcard, so renamed/deleted Lucent output cannot remain a `Compile` input.
- Formatting is deterministic and idempotent; generated mappings are hash-gated
  and refuse stale generated content.
- Watched project/C# changes rebuild open project generations before the next
  completion; completion performs no project or filesystem refresh.
- The serial request processor has a concurrent cancellation reader. Responses
  publish as one non-request-cancellable JSON-RPC frame after a cancellation
  check, with queued and active cancellation regressions.
- Native-C# quality fixtures cover available XML documentation, clean display
  names without `global::`, nullable/generic/deprecated metadata, hover,
  completion, definitions, malformed input, overlays, triggers, and UTF-16.
- CSS selector/resource navigation is project-scoped and overlay-aware; Lucent
  intentionally provides no CSS class completion inside `Class:` values.

## Final gates

- Warning-free solution build.
- 303 .NET tests passed: runtime 33, analyzers 11, Workbench 45, language server
  39, compiler 158, and MSBuild 17.
- VS Code server preparation and 6 extension tests passed.
- Workbench smoke and Package Pulse POC smoke passed.
- `git diff --check` passed.

The separate default Todo POC smoke still reports its pre-existing
`todo-failed-second-row-shape` assertion. It is not a Plan 008 tooling gate and
is not represented as passing here.
