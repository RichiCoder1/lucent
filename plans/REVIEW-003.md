# Adversarial review disposition for Plan 003

Reviewed on 2026-08-16 with fresh isolated Pi processes using
`openai-codex/gpt-5.6-terra`. Reviewers had read-only tools, no extensions,
skills, context files, session history, or repository write authority. The
managed `pi-subagents` scout could not start because the installed Codex
conversion extension invokes unsupported `node:v8.createHook` behavior in Bun;
the isolated reviewer path avoided that extension and did not modify files.

## Round 1

### 1. Plans 001–002 are not implemented

**Rejected as a Plan 003 defect.** This is true execution state, but Plan 003's
executor instructions, dependency metadata, drift check, and STOP conditions
already require both plans to be complete. Plan 003 was requested for future
execution and does not absorb prerequisite implementation.

### 2. Source deduplication could hide duplicate MSBuild inputs

**Accepted.** The plan now separates compiler-batch source identity from
MSBuild output identity. Duplicate physical sources and duplicate flat output
paths are preflight failures before compilation, with SDK-project fixtures.

### 3. Generated component types could collide with authored C# types

**Accepted.** `ComponentIndex` must check generated fully-qualified names
against the shared Roslyn project compilation and report both declaration
locations before emission.

### 4. Current inputs were writable through generated fields

**Accepted.** Authored identifiers now resolve to get-only properties backed by
reserved private fields. Assignment and `ref`/`out` use must produce mapped
diagnostics in the same synthetic semantic probe.

### 5. Component-local helper methods could hide input dependencies

**Accepted.** The plan now computes transitive reactive-read summaries for
component-local method symbols and attaches them at render call sites. External
methods remain opaque. Hidden state mutation in a render helper is diagnosed.

### 6. Keyed slot factories could capture stale row values

**Accepted.** Keyed factories capture the retained `LoopValue<T>` cell and
reintroduce the row local from its current value on every delayed invocation.
The required regression updates, unmounts, and remounts the same keyed row.

### 7. Unsaved sibling changes could leave caller LSP caches stale

**Accepted.** LSP analysis now has one project-snapshot generation derived from
all open buffers. Open/change/close invalidates every affected project document,
and all semantic requests use that generation.

### 8. `Fragment.Roots` could expose a mutable backing array

**Accepted.** The plan requires a copied non-array read-only wrapper, including
for the empty/default value, plus a cast/mutation regression test.

## Round 2

### 9. `CompileProject` could not diagnose task-specific output collisions

**Accepted.** Output preflight remains in `CompileLucent`; successful task
outputs are added dynamically to `Compile`/`FileWrites` after the task. Predicted
evaluation-time compile items are removed. `WriteIfChanged` bounds the deliberate
always-run generation tradeoff.

### 10. Authored members could collide with generated helpers

**Accepted.** `Mount`, `UpdateInputs`, and `Dispose` are reserved contract names;
all other generated members use the reserved `__lucent_` prefix. Parameter,
slot, ordinary-member, and prefix collisions receive declaration diagnostics.

### 11. Cross-file diagnostics were invalidated but not republished

**Accepted.** A project generation change must publish diagnostics or explicit
clears for every open project document, with cancellation preventing an older
generation from publishing afterward.

## Round 3

### 12. Contract examples still used unreserved generated field names

**Accepted.** Constructor examples and ownership/input/slot fields now use
`__lucent_*`. The migration explicitly renames inherited Plan 001/002 generated
fields so there is no second collision-prone naming family.

## Final verdict

A fourth fresh-process blocker/high-severity gate reviewed the corrected plan.
Verdict: `PASS`.

No optional feedback was carried into scope.
