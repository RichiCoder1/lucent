> Independent adversarial review by Claude Code Fable High, 2026-09-14. The CLI request used `--model fable --effort high`; its successful result reports `claude-fable-5-1`. Source baseline: `f3e4b784661ae96f04c0f9a48ab5b367a94681be`.
>
> This was a read-only source/design review, not an executed compiler or runtime proof. The review is reproduced below, except its self-estimated token-count paragraph was removed. Reviewer recommendations are not all adopted verbatim; the disposition section appended after the review is authoritative together with the refined plan.

# Adversarial review: application roots, routing and component companions

**Disposition: ready for A0 only, with explicit gates.** Nothing here requires revisiting Q1 to Q14. Three items are blockers to broad implementation, and each resolves through a technical clarification proposed below. A0 as written is not yet decisive; its fixture and pass/fail outputs are made concrete in the last section.

## Findings by priority

### 1. External generator interoperability

**Both directions already fail for Lucent's own generators, not only external ones.** LUI binding runs against the raw `CompilationProvider`, which excludes every generator's output, at `src/Lucent.Lui.Generator/LuiGenerator.cs:115-150`. The route and state generators read attributes only from C# syntax, at `RouteGenerator.cs:62-75`. So a `.lui` body cannot reference the generated `AppRoutes.Routes`, and a route record declared in `.lui` is invisible to the route generator. The JSON context case in plan section "Scope of ordinary C# declarations" is the same defect, so a proof on Lucent generators alone is meaningful but insufficient, as the plan already says.

**The sketch's `Component = typeof(HomePage)` is a third instance of the same defect.** HomePage would be LUI output, so in the route generator's semantic model that typed constant is an error type and no mapping can be emitted. This is fixed for free if the projection A0 builds includes component identity stubs, not only helper types.

- Refinement: emit stubs as C# 9 extended partial methods so the stub and the later implementation are two parts of one member rather than duplicates.

```csharp
// projected before other generators run
public sealed partial class HomePage { public static partial ComponentRecipe Create(); }
```

- This replaces the current dedupe-by-signature scheme at `src/Lucent.Lui.Compiler/LuiProjectContext.cs:194-204`, which only works because the stub is dropped later.

**The pre-compilation phase cannot satisfy direction two on its own.** Plan lines 276-291 and 313-321 state this correctly. The phase takes no Compilation, so it can project `.lui` syntax but never sees a JSON generator's output. Tolerant late binding is not an acceptable primary answer either, because LUI needs symbols for state classification via constant evaluation at `LuiCompiler.cs:1011-1016`, `var` inference at `:1017-1025`, task and collection diagnostics at `:1026-1046`, requirement type checks at `LuiCompiler.Requirements.cs:52-105`, and tag resolution at `LuiCompiler.cs:1398-1426`.

- Refinement: A0 compares exactly two candidates and rejects a third by name.
- Candidate A, bounded two-pass compile. Pass one compiles C# plus the declared-type projection plus component stubs with LuiGenerator disabled and generated files emitted to a known path. Pass two is the normal compile, where LUI binding adds the pass-one generated trees as binding-only references. Generators run exactly twice.
- Candidate A invariant: non-LUI generator outputs must not depend on LUI bodies. Verify by hashing pass-one and final generator outputs. A mismatch is a diagnostic, never a retry.
- Candidate B, Roslyn pre-compilation phase for projection plus tolerant binding restricted to expression islands. It cannot classify state or infer `var` from generated members, so document the degradation list before choosing it.
- Rejected: any iterate-until-stable driver, per the owner's instruction.

**Unsaved editor content has one viable path for either candidate.** The language server already replaces `.lui` additional documents with its own projections at `src/Lucent.Lui.LanguageServer/LuiProjectContext.cs:2640-2645` and already holds analyzer references at `:2650`. It can therefore run external generators through a GeneratorDriver against the stub compilation built from unsaved buffers. A0 must measure per-keystroke cost of that run.

**Roslyn version is a consequence, not a prerequisite.** Candidate A needs no new Roslyn API and works on the pinned 5.0.0 family in `Directory.Packages.props:8-11`. Candidate B needs the 5.9 package family in the analyzer, tooling and language server, plus a host compiler that has it. My read-only check could not confirm the plan's claim that the installed SDK compiler contains the new API, because the search tool skips binaries. Treat that claim as unverified until A0 executes it. Decide the .NET 11 SDK move only if Candidate B wins.

### 2. Named partial component state

**Two generators would emit into one partial type.** The LUI compiler emits the constructor, state cells, methods and setup into a private nested class at `LuiCompiler.cs:3300-3419`. The state generator emits a private constructor, a static factory and a partial `Initialize` at `ComponentStateGenerator.cs:280-306`, and rejects any declared constructor at `:83`. Both into one class is a compile error.

- Clarification, consistent with Q3 and Q6: the LUI compiler is the sole emitter for a component type. It already receives the Compilation, so it can read companion `[State]` partial properties from C# trees and emit their implementations itself.
- `[ComponentState]` on a component type is a diagnostic. The static-abstract `IComponentState` path in `src/Lucent.Core/ComponentState.cs:26-31` stays for standalone state classes only.

**The single setup hook has two incompatible shapes today.** LUI lowers `Setup` to a method taking the mount owner scope at `LuiCompiler.cs:3631`. The C# state hook takes a component context. Q7 says one hook, either file.

- Clarification: the compiler emits a declaring `partial void Setup(ReactiveScope owner)` and the `.lui` block lowers to the implementing part. A companion may implement it instead. Two implementations produce CS0757, which must be re-reported at the authored span.

**Cross-file initialization order is undefined in the plan.** Companion `[State]` properties only allow constants or static typed initializers at `ComponentStateGenerator.cs:43`, so they cannot depend on LUI state.

- Refinement: create companion cells first in ordinal order, then LUI initializers in source order, then Setup, then UI. Cycles are impossible by construction. Document that LUI initializers may read companion state and not the reverse.

**Collisions and accessibility need authored-location diagnostics.** Mismatched partial accessibility gives CS0262. Duplicate members give CS0102. The generated `owner` local and `__luiState_` fields at `LuiCompiler.cs:3365-3377` and `:3510` become visible to companion code. A component type named like a stock tag shadows the built-in lookup at `:1417-1425`, because namespace types win over `using static` imports.

**Incremental migration is impossible under the current tag lowering.** Tags lower to a bare call with a static import at `LuiCompiler.cs:3107-3108`. Once a type with the same name exists in the namespace, that call is CS1955.

- Refinement: tag binding becomes permanently dual-shape. A type with a static `[LucentComponent] Create` lowers to `X.Create(...)`. A static method lowers to `X(...)`, which stock Core components keep. Migration within one project is atomic.

**Root method group and NativeAOT are fine if Create returns the erased recipe.** Wrapper-returning method groups fail, as `tests/Lucent.Lui.Compiler.Tests/AuthoringOverloadTests.cs:105` shows. Only parameterless components qualify as a root or default destination. Rollback is preserved as long as construction stays inside the deferred build, since `MountContext.Dispose` unwinds deferred scopes in reverse at `src/Lucent.Core/MountContext.cs:346-356`.

### 3. Route mapping and reactive destination replacement

**Reactive replacement collides with the single staged slot.** Each outlet has one `_staged` field used by navigation staging at `src/Lucent.Core/Navigation/RouteOutlet.cs:587` and `:625`, and only the root may stage at `:480`.

- Refinement: same-URI replacement runs only while the session phase is Idle. If a navigation is in flight, record the pending selection and re-evaluate after commit. A remounted level evaluates fresh, so the pending record is dropped.
- Reuse the local publication path already used by `Initialize` at `:389-411`, with a synthetic publication and the generation-zero revision bump at `:813`. Nested mounts can build their own stage. Interaction hooks stay root-only, as at `:336`.

**Destination identity is unspecified beyond "type or key".** The retained key is definition plus owned captures at `:705-708`. Add a destination identity of component Type plus optional key. Explicit wrapping must declare identity through a typed helper. A bare recipe from the rendering hook is rejected at mount time with a clear message.

**Selection re-entrancy needs a bound.** Mount code that writes a signal read by the selection produces a loop. Commit already runs inside a graph batch at `NavigationSession.cs:871`, so effects defer. Cap replacements per flush and enter the existing terminal policy on overflow. Do not iterate to convergence.

**Router and RouterOutlet fit the runtime with two clarifications.** A session per Router mount fits the component owner scope, and an unmatched initial location throws at `NavigationSession.cs:74-84`, which becomes an ordinary mount rollback. The outlet demands the session's exact table instance at `RouteOutlet.cs:340`, so the generated `Routes` value must be one static bundle of table, descriptors and default mapping.

- A single `RouterOutlet` tag must choose root or child. The cursor is provided per level at `:657`, but ADR 0009 forbids optional context for authors. Clarification: RouterOutlet is a Core stock component that uses an internal optional cursor lookup. No author-facing optional context is added.

**Default mapping validation belongs in pass one.** With component stubs projected, the route generator can require a parameterless Create and emit a diagnostic otherwise. Route context and live readers need no change; live updates already flow at `src/Lucent.Core/RouteDescriptors.cs:87-94`.

### 4. Builder lifecycle and optional services

**A root decoration phase is missing.** The builder has single slots only at `src/Lucent.Core/Application.cs:45-105`. Hosting is a whole lifecycle that owns the root factory and wraps the root at `src/Lucent.Hosting/HostedApplication.cs:56-75`. Component Browser constructs a picker and launcher and passes them as props at `apps/Lucent.ComponentBrowser/ComponentBrowserLifecycle.cs:7-13`, and those have no home in the proposed shape.

- Refinement: phases are OnStart in order, root factory once on the owner thread, root decoration, mount, optional OnMounted. Hosting attaches its binding as a decorator using `Attach` at `ComponentServices.cs:26-36`. Core-only apps get a `Provide<T>` on the start context for root-level context. No new scope or locator is introduced.

**Stop-at-first-veto contradicts ADR 0003.** Each preparation callback is expected to stop accepting writes. If the third callback vetoes, the first two never learn the close was declined. Run every callback and aggregate, or add an explicit declined notification. Coalescing and retry are preserved by the session at `ApplicationSession.cs:348`.

**Partial startup failure runs unpaired cleanup.** The session calls stop and dispose regardless of how far startup got, at `ApplicationSession.cs:406-429`. Either register paired participants under the hood or hand each stop callback a started flag.

**Phase boundaries match Hosting's needs.** OnStop before composition disposal fits `StopAccepting` at `HostedApplication.cs:92`, and OnDispose after fits `Revoke`, which requires a disposed composition at `ComponentServices.cs:57`.

**Nullable injection touches three seams.** The source interface only has a required resolve at `ComponentServices.cs:4-9`; add an optional counterpart with a default implementation. The binding throws on null at `:124`; optional must bypass that. No binding at all throws at `MountEnvironment.cs:151-154`; optional must yield null there too, or the service-free preview promise fails. The compiler rejects annotated types at `LuiCompiler.Requirements.cs:60`, and requirement metadata needs an optional flag.

### 5. Migration, tooling and acceptance

- **Discover companions by type, not filename.** The editor already uses "generated.lui.cs" as a synthetic document name at `LuiProjectContext.cs:500` and `:3134`. The `.lui.cs` suffix is a convention only.
- **Index freshness excludes declared types.** The generation hash covers component signatures only at `compiler/LuiProjectContext.cs:50-57`. Declared types must join it, and the component-required paths at `:92-93` and `LuiCompiler.cs:3134` must accept support-only files.
- **Formatter and source maps survive if `Mapped` spans are kept for the new top-level layout.** The declaration-order lint needs a rule for type declarations.
- **Acceptance must be measurable.** Cold build from clean succeeds in one invocation. Editor reports zero false diagnostics on an unsaved edit that adds a property and uses it through a generated API. Deleting a support file yields only the expected errors. The final app has no hand-authored state, adapter or registry file. A package consumer runs under NativeAOT.

## A0 gate and handoff

**Blockers to broad implementation, each resolved by the clarification above:** single emitter for component types, dual-shape tag lowering, and component identity stubs in the projection. Adopt these into the plan before A0 starts.

**A0 fixture.** One project containing: a `.lui` support file declaring a record and a `[JsonSerializable]` context; a component whose state initializer and handler use the generated JSON members; a route module and route record in `.lui` with a Component mapping; a Program.cs using the Create method group; a companion file with one `[State]` property and one method used from markup.

**A0 passes only if all hold.**

1. Cold `dotnet build` with no prior obj succeeds once, with no hand-authored C# beyond Program.cs.
2. The language server binds the unsaved fixture edit through the generated JSON member before save.
3. Deleting the support file produces exactly the expected errors and no stale type.
4. Two mounts of the companion component hold distinct state, and setup runs once per mount.
5. Pass-one and final generator output hashes match under Candidate A, or the degradation list is signed off under Candidate B.
6. A NativeAOT publish of a package consumer runs the routed fixture.

**A0 fails if** any step needs a second build, a save to disk, a convergence loop, or moving the JSON annotations into a `.cs` file. On failure, A1 through A7 do not start; the follow-up is a product decision on whether direction two may be deferred to final-compilation validation.

**On pass,** A0 records the chosen pipeline, version minimums and per-keystroke cost, and A1 proceeds with the three clarifications folded into ADR 0011.

## Disposition after review

The owner approved refinement and handoff in advance. The following technical dispositions
preserve Q1-Q14 and are incorporated into the
[refined plan](application-routing-component-authoring.md) and ADR 0011. Broad runtime
implementation remains gated by A0; no new compiler/runtime checks were executed during
this review. All finding groups have a disposition; unresolved feasibility is assigned to
explicit executable gates, not presented as proven.

| Finding | Disposition and required evidence |
| --- | --- |
| Generator input/output visibility and component identity | Adopt early helper/component identity declarations and the extended partial factory proof. A0 covers generated JSON use from a state initializer and handler, generated route mapping, cold build and unsaved editor state. |
| Two-pass candidate | Refine to one preparatory generator-driver pass and one final pass, with binding-only generated trees and single final emission. Do not rely on an intentionally failing precompile of incomplete partial methods. Compare output identities/content, diagnose mismatch, and prohibit recursion/convergence loops. This remains an implementation experiment. |
| Tolerant binding candidate | Do not adopt a reduced capability/degradation list. The approved first-class interoperability goal remains required; defer to final C# validation only where Lucent does not need symbols earlier. A0 must fail honestly if the actual required cases cannot bind. |
| Installed Roslyn API | Keep the earlier read-only local assembly-metadata observation separate from Fable's inability to search binaries. Neither is an executed compatibility proof. A0 verifies the real compiler, editor/workspace and generator host matrix; .NET 11/newer libraries are already permitted. |
| Duplicate state emitters | Adopt one LUI component emitter reading companion [State]. Diagnose [ComponentState] on a component; preserve the separate standalone state feature. |
| Setup hook shape | Adopt a single partial setup bridge, using ComponentContext for the new companion surface rather than the review's raw ReactiveScope parameter. Existing LUI Setup()/Setup(owner) aliases keep behavior through lowering. ComponentContext already wraps the same retained owner; this creates no extra owner. |
| Initialization ordering | Adopt companion managed cells first, then source-ordered LUI declarations, setup, UI. Clarify that ordinary C# field initializers keep normal CLR timing. Static companion initialization cannot read this component's uninitialized instance. Do not claim all cycles are impossible; diagnose premature/cyclic reads and prove failure cleanup. |
| Factory/tag migration and collisions | Adopt qualified symbol-based binding for named Create and existing static factories, single registration, authored diagnostics and atomic project migration. A0 keeps a stock tag beside a named component. Direct method-group roots require a compatible signature; explicit lambdas handle parameterized roots. |
| Navigation/replacement race | Adopt idle-only replacement and coalesced dirty selection, re-evaluated after every return to idle, including veto/failure/cancellation. Disposed levels drop pending work. Do not reuse private initialization publication code without proving nested transaction/interaction invariants. |
| Render identity and feedback | Adopt typed component/key/wrapper identity and separate selector tracking. Use the existing bounded reactive drain and explicit loop failure; do not invent a recursive convergence mechanism or arbitrary new global limit. |
| Stable route bundle and nested outlet | Adopt one stable Routes table/descriptors/mapping bundle and stock internal cursor detection, without author-facing optional context. |
| Root service/context decoration | Add the explicit post-factory/pre-mount phase. Hosting owns and attaches its source; Core-only startup can provide typed context and an explicit closed service source. Context is not implicitly inject registration. Preserve exactly one root binding and its ownership. |
| Veto and write-admission recovery | Keep ordered early veto but add attempt-scoped declined callbacks, unwound in reverse before false returns. The review's run-all/aggregate option alone cannot restore earlier participants. Preserve ADR 0003 terminal preparation/restoration failures, fatal-generation cancellation, and service-owned accepted writes. |
| Partial startup cleanup | Track cleanup on actual acquisition, including the failing startup callback. Distinguish paired participant cleanup from unconditional builder terminal callbacks; do not pair unrelated callback lists by position. |
| Optional services | Cover requirement metadata, provider, binding and absent-binding environment. Missing yields null; construction errors, stopped/revoked binding and wrong-owner access stay errors. Reject catch-all required-resolution adapters. |
| Companion/file/tooling identity | Associate by namespace/type rather than suffix; maintain authored source maps, declaration-aware invalidation, synthetic-document exclusion, rename/deletion and declaration-order lint behavior. |
| Contradictory fixture wording | Split the proof into an all-.lui app with bootstrap-only C# and an additional optional-companion variant. The companion fixture cannot simultaneously claim no companion C# exists. |

The final disposition remains **ready for A0 with explicit gates**. Implementation may
run that proof and continue through A1-A7 when it passes. A failing candidate does not
automatically reopen product questions: evaluate a bounded compatible alternative first,
and escalate only a demonstrated conflict with accepted scope. The handoff does not claim
that Fable reran or executed the refined design; it records review plus primary-agent
source-checked dispositions.
