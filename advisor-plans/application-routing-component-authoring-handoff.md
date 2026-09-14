# Application, routing and LUI authoring handoff

Status: ready for dispatch, 2026-09-14. The requested Claude Code Fable High review
completed successfully as claude-fable-5-1; findings have source-checked dispositions.
The owner approved refinement followed by handoff. Start A0; A1-A7 require its pass.

## Adopt the design deliberately

Source design checkout: D:/.codex/worktrees/1b0a/lucent. Implementation checkout:
D:/src/richicoder1/lucent. The design checkout's runtime is older. Do not merge or copy
its runtime tree or replace the complete glossary/credits files.

Adopt these documents, reconciling any destination changes:

- docs/plans/application-routing-component-authoring.md
- docs/adr/0011-named-components-and-declarative-application-composition.md
- docs/plans/application-routing-component-authoring-fable-review.md
- This handoff.

Merge only relevant CONTEXT.md terms (Component recipe, Component declaration, Component
companion, Router and Route outlet) and CREDITS.md's Application and LUI authoring research
section. Preserve unrelated source, glossary, credits, architecture and existing queue.
Research baseline: f3e4b784661ae96f04c0f9a48ab5b367a94681be. Refresh source facts before edits.

## Authorization and sequence

The owner approved all fourteen interview decisions, accepted the consolidated model and
permits newer Roslyn libraries/.NET versions, including .NET 11, where useful or necessary.
Do not reopen those product choices. Fix engineering gaps consistently with them and
record concrete choices. If a proof shows an accepted capability requires a materially
different product/runtime model, report the minimal failing case and the specific decision;
do not silently reduce the goal.

Queue this after already accepted implementation work. At handoff preparation,
Implementation reports formatter/linter work F01-F06 completed, issues #295-300 closed and
release 0.3.0-dev.79.1 published; recheck its current queue before execution. These are
task-reported completion facts, not new independent runtime verification by this review.

Use the repository's existing GitHub/Project 4 execution workflow. First check for matching
issues; create a parent and A0-A7 execution slices only if absent, with native dependencies
and a link from Road to 1.0 #223. Ticket bodies must summarize acceptance and verification
without relying on local-only links. Keep A0 as the first actionable item; downstream
slices remain gated by its concrete result. Do not upload complete local review transcripts.

## Decisive A0 outputs

Produce a small, reproducible feasibility fixture and a decision report before broad source
migration. Record exact SDK, Roslyn/compiler/workspace/generator versions, dependencies,
commands, exit codes, positive fixture counts, failure diagnostics and runtime evidence.
Do not hide compiler or AOT warnings or call source inspection an executed proof.

| Proof | Required observation |
| --- | --- |
| Ordinary declarations | Model, attributed route record/module and helper type declared in .lui bind in the same file, another .lui file and ordinary C#. Support-only files are supported without prescribing their use. |
| External generation | A real external JSON generator sees an annotated .lui model/context, and an actual .lui handler uses its generated API successfully. No hand-authored C# bridge hides the interoperability gap. |
| Lucent generation | A .lui component consumes route/state APIs generated from authored .lui declarations in the same build. No unbounded generator rerun or guessed final output. |
| Component identity | Generated named partial and companion form one state object per mount. A zero-prop Create method group satisfies the proposed root factory API without reflection or accidental conversion assumptions. |
| Initialization | Explicit owner/requirement/state/setup phases, cross-file access, two independent mounts and rollback after a failed initializer. Output identifies which semantics remain for A2, rather than claiming full state implementation from a stub. |
| Editor parity | Cold workspace with no generated disk artifacts, unsaved edit, rename and deletion; diagnostics and navigation resolve to authored source with no duplicate/stale declarations. |
| Packaged consumer | The chosen compiler/generation path works through the packaged SDK; NativeAOT publication and execution cover the actual accepted generator dependency closure. |

Use the refined plan's A0 section as the decisive gate. It splits the all-.lui application
from the optional-companion fixture so their C# file requirements do not conflict. The
JSON proof includes a component state initializer requiring generated symbol typing,
not just a handler that could defer validation to final C# compilation.

First evaluate aligned newer Roslyn libraries and RegisterPreCompilationSourceOutput.
The installed SDK compiler has this API, while the current 5.0.0 package pin does not.
Its experimental status and build/editor host requirements need actual verification.
Early .lui-to-Compile projection is a fallback to evaluate, not a second automatically
maintained path. Separate an SDK/compiler upgrade from raising application runtime targets.

Crucially, making declarations visible to external generators does not by itself make
their ordinary generated outputs visible during LUI's own semantic pass. The selected
design must state which C# binding can defer to final compilation, which Lucent binding
needs earlier symbols, how bundled generation shares those symbols and what the cold and
unsaved-editor paths do. Reject a pass claim that only demonstrates a C# caller.

A0 completion requires a selected generation/initialization architecture, proof artifacts,
resolved reviewer blockers and updated implementation contracts. If a technical approach
fails, try a bounded alternative consistent with the accepted model; do not manufacture
success or keep adding parallel pipelines without a documented reason. Continue independent
work only where it cannot commit consumers to the failed approach.

If an extra generation stage is needed, evaluate one bounded preparatory generator-driver
pass plus the final pass, with generated trees added to LUI binding only and output-match
checks. Never rely on a deliberately failing preliminary assembly build, duplicate final
types, generator recursion or iterate-until-stable behavior. Reduced interoperability,
save-before-bind and false editor diagnostics are not accepted fallback outcomes.

## Review refinements that must survive adoption

- One LUI component emitter implements both files; standalone [ComponentState] remains
  separate. Companion managed cells initialize before LUI source-ordered declarations,
  then a single ComponentContext setup bridge. Ordinary C# field timing is unchanged.
- Publish component identity/factory declarations early and qualify resolved tag calls.
  Support named Create factories and existing static factories without duplicate tag
  candidates or type-name shadowing. Associate companions by type, not filename.
- Serialize same-URI replacement behind navigation and re-evaluate after any return to
  idle, including veto and cancellation. Preserve typed destination/wrapper identity,
  nested rollback, root interaction ownership and the existing bounded reactive drain.
- Add post-factory root binding/context decoration. Distinguish typed context from
  injectable service registration and preserve one owned/borrowed service-binding model.
- Close veto runs reverse attempt-scoped decline restoration before returning false.
  Preserve terminal failure/fatal-cancellation rules and service-owned accepted writes.
  Track cleanup when startup resources are acquired, including partial startup failure.
- Optional injection handles no binding and missing service as null, while provider errors,
  stopped/revoked binding and wrong-owner access remain failures.

The review document preserves Fable's findings and lists refinements/rejections explicitly.
Do not adopt the raw review's degraded-binding suggestion or its contradictory single
fixture claiming both a C# companion and no C# beyond Program.cs.

## Contracts every slice preserves

- .Run(ComponentName.Create) accepts a deferred recipe factory. Builder lifecycle hooks
  compose; startup order is forward, terminal stop/cleanup order reverse, close may veto.
  Preserve platform/services readiness, owner-thread creation and existing disposal phases.
- .lui and optional .lui.cs share a named partial component with one setup hook and no
  authored component instance constructor. Inferred .lui state and explicit C# [State]
  preserve writable/derived/snapshot distinctions. Ordinary C# fields remain ordinary.
- Nullable inject means optional borrowed service, with missing -> null and provider
  construction failures surfaced. Required service and ownership rules remain intact.
- Router owns a created session or borrows a supplied one. RouterOutlet consumes it;
  shell controls share the session. Typed routes have fixed validated patterns and
  generated component defaults; arbitrary prop needs use explicit render customization.
- Guards prepare navigation only. Reactive component type/key changes at the same URI
  replace the affected destination without history or guards. Same identity retains state.
  Preserve nested retention, transaction/rollback and current-generation publication.
- Support ordinary C# declarations in .lui, including external generation. Pure-helper
  .cs files remain a normal organizational choice. The acceptance app demonstrates
  .lui-only application behavior with bootstrap-only C#; it is a capability proof, not a
  policy forcing all application code into .lui.

## Integration and completion

Follow A0-A7 dependencies from the plan. Integrate source maps, editor and formatter work
as syntax lands; do not wait until migration to discover that new declarations cannot be
edited. Migrate current Component Browser examples and a small application only after
the selected pipeline and ownership behavior are proven. Preserve its real root styling,
window behavior, service registration and navigation state while removing adapters.

Use docs/TESTING.md and docs/agents/verification.md to select focused compiler/generator,
LSP/extension, Core/navigation/Hosting and package-consumer tests. Finish with the required
published NativeAOT application/close/failure checks on the affected path. Desktop checks
must respect any active focus/input constraints. Record validation in execution issues;
do not infer full release certification from a focused feature pass.

Report adopted docs, parent/child issue links, the selected A0 architecture and evidence,
subsequent slice status, remaining risks, and current source/package identifiers. Completing
the handoff means accepting and starting the work; it is not a claim that the feature is
already implemented.
