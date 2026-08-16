# Plan 002: Bind reactive dependencies and add conditional regions

> **Executor instructions**: Complete Plan 001 first. Follow this plan in order,
> run every gate, and update `plans/README.md`. Keep the initial conditional
> grammar deliberately narrow; Plan 003 owns general fragments and composition.
>
> **Drift check**: `git diff --stat 3b27cfe..HEAD -- src/Lucent.Compiler src/Lucent.Runtime tests/Lucent.Compiler.Tests tests/Lucent.Runtime.Tests examples/package-pulse`
> Stop if Plan 001's `ComponentOwner`/`IUiDispatcher` interface differs from the
> contract in `plans/001-runtime-owner-scheduler.md`.

## Status

- **Priority**: P0
- **Effort**: L
- **Risk**: HIGH
- **Depends on**: `plans/001-runtime-owner-scheduler.md`
- **Category**: direction / architecture
- **Planned at**: commit `3b27cfe`, 2026-08-16

## Why this matters

Any state change currently calls one broad `UpdateBindings()`, every keyed
region, and every computed refresh. The emitter also decides whether an
expression is reactive by matching identifier text. That cannot survive local
shadowing, ordinary members, or component inputs.

This plan makes the binder own two facts: which compiler-recognized reactive
sources an expression reads, and which generated computation each source must
invalidate. It also uses that graph for the first structural branch. No generic
signal runtime or render-tree diff is introduced.

## Current state

- `GeneralCSharpEmitter.cs:739-803` emits one `UpdateBindings()` for all reactive
  properties/content and then updates every keyed region.
- `GeneralCSharpEmitter.cs:1395-1461` reparses expression text and rewrites
  `identifier.Value`/`identifier.Update` by name. It cannot distinguish a state
  member from a shadowing local with the same spelling.
- `GeneralCSharpEmitter.cs:261-270` refreshes all computed values after any state
  update.
- `NativeSymbolResolver.cs:599-656` already owns the project Roslyn compilation,
  making it the right dependency for a focused C#-island binder.
- `Parser.cs:260-340` permits one returned native root with properties, nested
  controls, and keyed loops, but no conditional member.
- `BoundComponentModel.cs:5-92` stores raw expression text and has no dependency
  identity or conditional region.

## Reactive contract

The initial contract is intentionally finite:

1. `State<T>.Value` is a reactive read of that state source.
2. `Computed<T>.Value`, `.IsPending`, and `.ErrorMessage` are reads of one
   computed source. They may share one invalidation identity in this plan;
   facet-specific invalidation is not required.
3. `State<T>.Update(...)` is a mutation, not a read. Lambda/body reads inside its
   argument are analyzed normally.
4. Reads remain reactive through ordinary C# expression structure, including
   interpolation, conditional expressions, LINQ, and method arguments.
5. Dependencies hidden inside an opaque method body are snapshots unless the
   method receives/reads a visible reactive value in the island. The compiler
   does not guess hidden dependencies and does not diagnose every method call.
6. A local, lambda parameter, or keyed-loop variable shadowing a state name is
   not a reactive source.
7. Dependencies are deduplicated and stored in deterministic source declaration
   order.
8. Computed-factory dependencies form a directed graph. Self-dependencies and
   cycles are compile errors with source-spanned diagnostics; they must never
   reach generated refresh methods.

## Exact compiler module shape

First extract `ProjectSemanticCompilation`, an internal module that creates and
owns the one Roslyn `CSharpCompilation` plus import context used by native-symbol
resolution, island binding, and editor intelligence. `GeneralBinder` constructs
it once per component compilation and passes it to both `NativeSymbolResolver`
and the island binder. Do not duplicate reference/source loading.

In this one-source plan, that component compilation is also the whole Lucent
compilation. Plan 003 may lift ownership to a project batch: it must reuse one
base reference/C#-source compilation, derive exactly one synthetic probe tree
and semantic model per component, and keep native resolution, island binding,
and editor queries on that shared base. It must not collapse different component
scopes into one probe or reload project references per component.

Then add one internal semantic module,
`src/Lucent.Compiler/Semantics/CSharpIslandBinder.cs`. It binds a complete batch
for one component. `GeneralBinder` must first resolve a draft native/component
tree and collect every island request, then bind the batch, then finalize the
immutable bound model. It must not bind islands during recursive tree discovery.

The batch interface is:

```csharp
internal sealed class CSharpIslandBinder
{
    IReadOnlyDictionary<int, BoundCSharpIsland> BindAll(
        IReadOnlyList<CSharpIslandRequest> requests);
}

internal sealed record CSharpIslandRequest(
    int Id,
    string Text,
    SourceSpan Span,
    CSharpIslandKind Kind,
    CSharpIslandRole Role,
    ITypeSymbol? ExpectedType,
    IReadOnlyList<BoundLocal> Locals);

internal sealed record BoundCSharpIsland(
    string SourceText,
    string LoweredText,
    SourceSpan Span,
    CSharpIslandKind Kind,
    IReadOnlyList<int> Dependencies,
    IReadOnlyList<BoundReactiveRead> ReactiveReads);

internal sealed record BoundReactiveRead(
    int SourceId,
    SourceSpan Span);
```

The interface requirements are not illustrative:

- Bind all islands for one component against one synthetic Roslyn syntax tree
  and semantic model, not one compilation per property.
- The synthetic tree contains one probe class, synthetic state/computed wrapper
  fields, one wrapper method per island, and the event/loop locals that are in
  scope for that island. Keep a map from each wrapper's synthetic span back to
  its absolute `.lui` span for diagnostics and semantic queries.
- Each request supplies semantic context, not just text. Native property/content
  assignments use the resolved target type; event lambdas use the resolved
  delegate type; conditions use `bool`; computed factories use
  `Func<CancellationToken, Task<T>>`; statement bodies use `void`; untyped loop
  source/key probes use `var`/`object?` as appropriate. This preserves target-
  typed `new()`, collection expressions, lambdas, and conversions.
- Give each state/computed source a synthetic field symbol with its declared
  value type and the compiler-recognized `Value`/`Update` surface.
- Give event parameters and keyed-loop variables real local/parameter symbols.
- Identify dependencies by Roslyn symbol equality, never identifier spelling.
- Retain every ordered reactive read occurrence and its absolute `.lui` span in
  `ReactiveReads`. Derive deduplicated `Dependencies` in source declaration
  order from those occurrences; do not discard occurrence spans after binding.
- Lower recognized member accesses with a `CSharpSyntaxRewriter` using bound
  symbols. Preserve trivia and produce source-mapped generated expressions.
- Return Roslyn diagnostics translated to absolute `.lui` spans through the
  existing diagnostic path.
- Preserve the resolved property/content target type on its draft/final bound
  member so editor queries and later lowering do not have to resolve it again.
- Bind state initializer expressions and computed initial-value expressions as
  `BoundCSharpIsland` with expected type `T` in the same component batch. They
  receive mapped diagnostics and symbol-backed lowering, but their dependency
  IDs do not schedule invalidation: these expressions run once when the
  component instance initializes. The computed factory remains the reactive
  expression that controls refresh.

Add one reusable internal `ComponentSemanticAnalysis` result used by both
compilation and editor queries. It owns the parsed syntax, diagnostics, shared
`ProjectSemanticCompilation`, bound island/scope results, and semantic symbols
for one source/component. `LucentCompiler.Compile`, `GetCompletions`, and
`GetExpressionSymbol` must all enter through this analysis service rather than
letting `EditorIntelligence` parse and construct a resolver independently. The
public `CompilationResult` may project the existing fields from this internal
result; do not expose Roslyn objects publicly.

Add these minimal IR concepts:

```csharp
internal enum BoundReactiveSourceKind { State, Computed }

internal sealed record BoundReactiveSource(
    int Id,
    string Name,
    BoundReactiveSourceKind Kind,
    string ValueTypeName,
    SourceSpan Span);
```

Replace raw expression/statement strings in state initializers, computed initial
values/factories, properties, content, loop source/key, and event bodies with
`BoundCSharpIsland` where lowering or dependency analysis applies. Initializer
dependencies are one-time reads, and event-body dependencies need not drive
invalidation, but the same symbol-backed lowering must prevent shadowing bugs.
Do not create a general graph class. Existing bound members plus dependency IDs
are enough.

## Exact invalidation shape

Generate one private invalidation method per reactive source, not a runtime
observer graph:

```csharp
private void InvalidateQuery()
{
    UpdateBinding1();
    UpdateRegion1();
    RefreshPackages();
}

private void InvalidatePackages()
{
    UpdateBinding2();
    UpdateRegion2();
}
```

Rules:

- One generated update method owns one reactive property/content target or
  structural region. Static assignments remain in mount.
- Initial mount calls every update method exactly once, then starts computed
  work in declaration order.
- A state setter calls only its source invalidation method after equality says
  the value changed.
- Starting a computed refresh invalidates its computed source after setting
  pending/error state. Success and failure invalidate it again after committing.
- A computed factory is refreshed only by the sources listed in its bound
  dependency set.
- Before emission, run depth-first cycle detection over computed-factory edges.
  Use `ReactiveReads` to report each self/cyclic back-edge at the corresponding
  factory read occurrence and do not emit source. Add no runtime cycle guard for
  statically known sources.
- Direct method calls are acceptable for now. Do not add batching, bit masks,
  queues, priorities, or a public reactive runtime until measurements require
  them.

## Exact conditional subset

Add this render-member grammar:

```csharp
StackPanel {
    if (condition) {
        Border { /* one native root */ }
    } else {
        TextBlock { /* one native root */ }
    }
}
```

Initial restrictions, all enforced with source-spanned diagnostics:

- A conditional is a `UiMemberSyntax` inside a native control.
- The host must have a compatible native content route.
- Like the current keyed-loop subset, the host dedicates its child content to
  one conditional region; it cannot mix that conditional with ordinary children
  or a keyed loop in this plan.
- Each branch contains exactly one native control root. `else` is optional and
  means no rendered control when false.
- Nested conditionals, conditionals inside keyed rows, multiple siblings,
  component roots, and multi-root fragments wait for Plan 003.
- The condition is a bound expression; only its dependencies invalidate the
  region.
- Branches may contain static nested native controls and reactive
  property/content expressions. They may not contain keyed loops or another
  conditional in this plan. Generated branch-control fields are nullable;
  branch target methods update them only while mounted and branch-owner cleanup
  clears them.
- Branch IDs are deterministic preorder integers within the component. They are
  not absolute paths, line numbers, or raw source offsets.

Add one concrete runtime type to `Lucent.Runtime`:

```csharp
public sealed class ConditionalRegion : IDisposable
{
    public ConditionalRegion(ComponentOwner owner, Action<Control?> setRoot);
    public int? ActiveBranch { get; }
    public void Show(int branch, Func<ComponentOwner, Control> mount);
    public void Clear();
    public void Dispose();
}
```

Required semantics:

- Same active branch is a no-op; property computations inside that branch are
  updated separately by their own dependency methods, guarded by mounted branch
  fields.
- Switching creates a new child owner and mounts the new root before publishing
  it. After `setRoot(newRoot)` succeeds, dispose the old branch owner.
- Mount and publication failures are distinct. If `mount(newOwner)` fails,
  dispose the new owner exactly once and rethrow; do not call `setRoot` and do
  not touch the old root/owner.
- Only after `setRoot(newRoot)` throws, call `setRoot(oldRoot)` to restore the
  previous content, dispose the new owner exactly once, and rethrow the
  publication failure (aggregated with new-owner cleanup failure when needed).
  If restoration also fails, dispose both owners, clear active state, and throw
  one aggregate containing publication, restoration, and cleanup failures.
  Test adapters that mutate before throwing.
- `Clear()` publishes `null`, then disposes the old branch owner.
- `Dispose()` disposes the active branch without calling `setRoot`; parent tree
  teardown owns native detachment. The constructor registers this disposal with
  `owner.OnDispose`; parent disposal leaves `ActiveBranch == null` and never
  calls `setRoot`.
- Calls are UI-thread lifecycle operations. Background work reaches them only
  through the owning component's dispatcher.

The generated `setRoot` adapter handles the resolved native route directly:
assign scalar `Content`/`Child`, or clear/add the dedicated collection route.
The runtime does not reflect over controls or content metadata.

## Commands

| Purpose | Command | Expected result |
| --- | --- | --- |
| Baseline | `dotnet test Lucent.sln --no-restore` | Plan 001 baseline plus runtime tests pass |
| Compiler tests | `dotnet test tests/Lucent.Compiler.Tests/Lucent.Compiler.Tests.csproj --no-restore` | all pass |
| Runtime tests | `dotnet test tests/Lucent.Runtime.Tests/Lucent.Runtime.Tests.csproj --no-restore` | all pass |
| Build | `dotnet build Lucent.sln --no-restore` | exit 0, no warnings |
| Full tests | `dotnet test Lucent.sln --no-build` | all pass |

## Scope

**Create or modify only**:

- `src/Lucent.Compiler/Semantics/CSharpIslandBinder.cs` (create)
- `src/Lucent.Compiler/Semantics/ProjectSemanticCompilation.cs` (create)
- `src/Lucent.Compiler/Semantics/NativeSymbolResolver.cs`
- `src/Lucent.Compiler/LucentCompiler.cs`
- `src/Lucent.Compiler/CompilationResult.cs`
- `src/Lucent.Compiler/Syntax/SyntaxNodes.cs`
- `src/Lucent.Compiler/Parsing/Parser.cs`
- `src/Lucent.Compiler/CodeGeneration/BoundComponentModel.cs`
- `src/Lucent.Compiler/CodeGeneration/GeneralBinder.cs`
- `src/Lucent.Compiler/CodeGeneration/GeneralCSharpEmitter.cs`
- `src/Lucent.Runtime/ConditionalRegion.cs` (create)
- `src/Lucent.Compiler/EditorIntelligence.cs`
- `src/Lucent.LanguageServer/LanguageServer.cs` only if shared semantic results
  require protocol plumbing
- `tests/Lucent.Compiler.Tests/GeneralCompilerTests.cs`
- `tests/Lucent.Compiler.Tests/ProjectSemanticBindingTests.cs` if symbol-backed
  shadowing requires project context
- `tests/Lucent.Runtime.Tests/ConditionalRegionTests.cs` (create)
- `tests/Lucent.LanguageServer.Tests/LanguageServerProtocolTests.cs`
- `examples/package-pulse/PackagePulse.lui`
- `src/Lucent.Poc/Program.cs`
- `docs/poc/0005-async-css-package-pulse.md`
- Generated snapshot only if the normal verify command requires it

**Out of scope**:

- Component invocation, parameters, slots, context, ordinary member bodies,
  nested/mixed structural regions, repeatable fragments, or multiple rendered
  roots.
- Runtime observer graphs, public signals, batching, scheduler priorities,
  facet-specific computed dependencies, effects, or optimistic updates.
- Inspecting arbitrary method bodies to discover hidden dependencies.

## Steps

### Step 1: Characterize current over-invalidation and shadowing

Add failing tests that prove the intended difference:

- two independent states update separate properties;
- only one state refreshes a computed factory;
- self-dependent and two-node cyclic computed factories are rejected;
- a loop source updates only for its dependency;
- a retained row whose content reads an independent state refreshes when that
  state changes, and a reactive key change reconciles the containing region;
- a lambda/local shadowing a state name is not rewritten;
- interpolation and LINQ with visible state reads are reactive;
- `State.Update` is lowered but not counted as a read;
- computed pending/value/error reads all depend on the computed source.

**Verify**: the new targeted/shadowing assertions fail against the lexical
emitter while existing tests remain green.

### Step 2: Build one semantic model per component

Extract `ProjectSemanticCompilation`, migrate `NativeSymbolResolver` to consume
it, then split `GeneralBinder` into explicit draft collection and finalization
phases. Draft collection resolves native controls/properties/events/content,
records target/delegate/condition context, assigns stable request IDs, and
collects all island requests. `BindAll` adds one synthetic probe tree and one
semantic model, then finalization replaces request IDs with
`BoundCSharpIsland`. Make symbol-backed lowering the only rewriting path.

Build `ComponentSemanticAnalysis` around that pipeline and route compile,
completion, and symbol lookup through it. `EditorIntelligence` may format
completion/hover responses, but it must not create another parser, resolver, or
project compilation.

**Verify**: binder-level tests assert exact dependency IDs, lowered text, and
absolute diagnostic spans for all Step 1 cases. Native symbol tests remain
unchanged. Add target-typed `new()`, collection-expression, event-lambda, and
non-boolean-condition cases, plus typed state/computed initial values with
mapped failures and symbol-backed state reads. Remove `IsReactive` and
`RewriteStateReferences` only after their callers have migrated.

### Step 3: Emit one method per computation target

Split broad `UpdateBindings()` into deterministic target methods and source
invalidation methods. Use stable preorder numbering for generated method names.
For each keyed region, define its dependencies as the deterministic union of
the loop source, key, and every row property/content expression; any of those
sources routes to `UpdateRegion`. Keep source `#line` mapping around application
expressions.

**Verify**: generated tests prove a state invalidation method does not mention
unrelated target methods or computed refresh methods, while retained row
bindings and reactive keys do update from independent sources.

### Step 4: Parse and bind the conditional subset

Add `UiIfSyntax` with condition span, true root, optional false root, and total
span. Reuse the parser's existing delimiter-aware C# island scanning. Enforce
the dedicated-host and one-root restrictions in `GeneralBinder`, not by parser
accident.

**Verify**: parser/binder tests cover true-only, true/false, malformed condition,
mixed children, nested conditional, keyed-row conditional, and incompatible
host diagnostics.

### Step 5: Implement `ConditionalRegion`

Add interface-level tests for show-same, switch, clear, dispose, mount failure,
publish failure before mutation, publish failure after mutation with successful
restoration, restoration failure, child cleanup ordering, and parent owner
disposal. Observe roots, `ActiveBranch`, and callbacks; do not inspect private
fields.

The mount-failure test must assert that publication was never attempted, the old
branch remains active, and the new owner was disposed once. Publish-failure
tests assert restoration call order and exactly-once cleanup.

**Verify**: runtime tests pass.

### Step 6: Emit conditional mounting and updates

Generate one field per region plus nullable fields for branch controls, and one
update method bound only to condition dependencies. Mount branch controls inside
the child owner; register branch event cleanup and field clearing there. Reactive
target methods check the relevant branch field before assignment. Immediately
after branch fields are assigned and the root is published, run every target
method in that branch once so dependencies that changed while it was unmounted
cannot leave default values. The generated route adapter performs exact native
content assignment.

**Verify**: generated code has no generic control diff, reflection, or source-
offset identity. Switching branches disposes old event handlers. A false branch
whose independent title state changes mounts later with the latest title.

### Step 7: Convert Package Pulse's structural states

Use separate dedicated native hosts and the new conditional subset for the
initial-loading, failure, and empty-result messages. Keep the keyed result list
outside conditional branches so stale content remains mounted during refresh;
keyed loops inside branches remain out of scope. Do not add a second resource
type.

Extend Plan 001's component-specific Package Pulse smoke in
`src/Lucent.Poc/Program.cs`: drive normal, empty, failing, and rapid replacement
queries through the native controls; observe initial loading, success, empty,
failure, retained stale results during refresh, latest-generation commit, and
clean shutdown. Use bounded UI-turn waits, not sleeps or blocked UI-thread races.

**Verify**: compiler tests plus the native POC smoke observe first load, success,
empty result, failure, stale refresh, and clean shutdown.

### Step 8: Keep editor semantics on the shared path

Update `EditorIntelligence` to traverse `UiIfSyntax` and consume the same island
scope/symbol results from `ComponentSemanticAnalysis` for hover and completion
rather than independently parsing or guessing shadowing. Expose only the bounded
semantic queries the editor needs. Add LSP
tests for completion/hover inside both branches and a shadowed local context.

**Verify**: language-server protocol tests pass and no second conditional scope
walker reconstructs dependency meaning.

## Test plan

- Binder tests are the dependency/lowering truth; emitter tests verify routing,
  not Roslyn internals.
- Runtime tests cover branch ownership transactionality and cleanup.
- Generated-code tests keep deterministic names and source mappings readable.
- POC smoke proves the production dispatcher/owner/native-content integration.
- Delete tests tied only to removed broad `UpdateBindings()` or lexical rewrite
  implementation once replacement behavior is covered.

## Done criteria

- [ ] `IsReactive` and `RewriteStateReferences` no longer exist.
- [ ] Every reactive target and computed factory stores bound dependency IDs.
- [ ] Every reactive read occurrence retains its absolute source span.
- [ ] Shadowing cannot cause reactive rewrite or invalidation.
- [ ] Self/cyclic computed dependencies are rejected before emission.
- [ ] Each source invalidates only its dependent target methods.
- [ ] Keyed-region dependencies include source, key, and row bindings.
- [ ] Conditional syntax obeys the dedicated-host, one-root subset.
- [ ] Conditional branch ownership is transactional and deterministic.
- [ ] Newly mounted branch targets initialize from current source values.
- [ ] Editor hover/completion traverses conditionals through shared semantics.
- [ ] Package Pulse uses structural initial/empty/error states and stale refresh.
- [ ] Full build, tests, and native smoke pass.
- [ ] No files outside scope are modified except `plans/README.md` status.

## STOP conditions

- One Roslyn compilation/semantic model is created per expression in steady
  compilation; redesign around one component probe instead.
- Recursive draft discovery attempts to call island binding before all request
  IDs and semantic contexts are collected.
- Correct dependency identification falls back to identifier text.
- Conditional implementation requires a generic virtual tree or runtime
  reflection over native content properties.
- Supporting mixed/multi-root branches is necessary for Package Pulse; stop and
  propose the smallest Plan 003 prerequisite rather than expanding this plan.
- The generated source cannot preserve `.lui` diagnostic/source mapping through
  symbol-backed lowering.
- Native resolver, compiler binder, and editor would require separate Roslyn
  project compilations or different scope rules.

## Maintenance notes

Plan 003 must reuse `BoundCSharpIsland`, source IDs, component owners, and stable
region numbering. It may generalize branch roots to fragments, but should not
replace the dependency binder or add a second structural lifetime mechanism.
