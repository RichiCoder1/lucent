# Plan 003: Make Lucent components genuinely composable

> **Executor instructions**: Complete Plans 001 and 002 first. Follow this plan
> in order, run every verification gate, and update this plan's row in
> `plans/README.md`. Do not broaden the language beyond the exact subset below.
>
> **Drift check (run first)**:
> `git diff --stat 3b27cfe..HEAD -- build src/Lucent.Compiler src/Lucent.Compiler.MSBuild src/Lucent.LanguageServer src/Lucent.Runtime tests examples/counter examples/workbench Lucent.sln`
> The plan was written against commit `3b27cfe` plus the active working-tree
> compiler/tooling/example changes and the contracts specified by Plans 001–002.
> Stop if those plans are incomplete, `ComponentOwner` differs materially from
> Plan 001, or bound C# islands/dependency IDs differ materially from Plan 002.

## Status

- **Priority**: P0
- **Effort**: L
- **Risk**: HIGH
- **Depends on**: `plans/001-runtime-owner-scheduler.md`,
  `plans/002-reactivity-conditional-regions.md`
- **Category**: direction / language architecture
- **Planned at**: commit `3b27cfe`, 2026-08-16

## Why this matters

Lucent currently compiles each `.lui` independently and resolves every rendered
name as an Avalonia control. Consequently, the checked-in counter screen already
writes `Counter {}` in `MainWindow.lui`, but it still needs `Counter.cs` as a
native wrapper around `CounterComponent`. Parameters are parsed and discarded,
slots are only documentation, and generated components return one `Control`.

This plan makes `Counter {}` a real cross-file Lucent component invocation. It
adds only the project index, current inputs, fixed root fragments, child-owner
lowering, and delayed single-site slots required for that behavior. Components
remain compiler-owned logical instances—not Avalonia controls and not a VDOM.

## Current state

- `src/Lucent.Compiler/Parsing/Parser.cs:103-124` reads the component parameter
  clause into `parameters` and discards it.
- `Parser.cs:126-190` recognizes only `State<T>`, `Computed<T>`, and one
  `Fragment Render()` method; opaque members are diagnosed.
- `Parser.cs:221-260` parses a rendered identifier followed immediately by a
  brace. There is no argument list, fragment literal, slot supply, or yield.
- `src/Lucent.Compiler/Syntax/SyntaxNodes.cs:18-34` stores no parameter or slot
  declarations. `UiElementSyntax` has no invocation arguments.
- `src/Lucent.Compiler/CodeGeneration/GeneralBinder.cs:19-31` diagnoses every
  component after the first in a file, then binds only `syntax.Component`.
  One component per file remains the rule in this plan.
- `GeneralBinder.cs:94-105` sends every rendered name to
  `NativeSymbolResolver.ResolveControl`; no component-symbol path exists.
- `src/Lucent.Compiler/CodeGeneration/BoundComponentModel.cs:5-92` models one
  native root and has no parameters, ordinary members, fragments, component
  calls, or slots.
- `src/Lucent.Compiler/CodeGeneration/GeneralCSharpEmitter.cs:67-155` emits one
  parameterless class whose `Mount()` returns one `Control`.
- `src/Lucent.Compiler.MSBuild/CompileLucent.cs:67-119` constructs one project
  context but calls `LucentCompiler.Compile` independently for each source.
- `src/Lucent.LanguageServer/ProjectContextLoader.cs:216-285` asks MSBuild for
  references and C# `Compile` items, but not `LucentSource` items.
- `examples/counter/MainWindow.lui` already contains `Counter {}`. Today it
  resolves to the native adapter in `examples/counter/Counter.cs`; deleting that
  adapter makes the missing composition support visible.

Decisions that constrain the implementation:

- ADR 0003: components are class-shaped logical instances, have current
  read-only parameters, and are not Avalonia controls.
- ADR 0002: every component has implicit `children`; named slot supply is
  `slot name { ... }`; each slot has at most one syntactic yield site.
- ADR 0004: native controls and Lucent components share authoring syntax but
  retain separate semantic symbols and child-placement rules.
- Plans 001–002: child lifetime uses `ComponentOwner`; expression binding uses
  one symbol-backed C# batch per component; structural work reuses the existing
  owner/conditional machinery.

## Exact language subset

### Files and names

- Exactly one component declaration is allowed per `.lui` file. Keep the
  existing diagnostic for additional declarations in the same file.
- Every source in one MSBuild/LSP project participates in one component index
  before any component body binds. Source order must not affect resolution.
- A component's identity is `(namespace, name)`. Duplicate fully-qualified
  names are errors reported at both declarations.
- Before emission, resolve every generated `<Name>Component` type name against
  the shared Roslyn project compilation. An authored C# type with the same
  namespace/name is a Lucent error at the component declaration and at the C#
  declaration location; do not let this escape as a downstream duplicate-type
  compiler error.
- Unqualified renderable names search the current namespace and imported
  namespaces. Qualified names are allowed. If a visible component and native
  control are both viable, report an ambiguity instead of silently choosing.
- There are no component overloads or generic components in this plan.
- Build a directed component-invocation graph after all bodies bind. Reject
  direct and indirect cycles at the invocation edge that closes the cycle.

### Declarations and ordinary members

Support these two component forms:

```csharp
component Counter(int initial = 0)
{
    private readonly State<int> count = new(initial);

    Fragment Render() => StackPanel { /* ... */ };

    private void Increment() => count.Update(value => value + 1);
}

component Badge(string text) => TextBlock { Text: text; };
```

Rules:

- Parse the declaration parameter clause with Roslyn. Allow ordinary value
  parameters, named arguments, and compile-time optional defaults. Reject
  `ref`, `out`, `in`, `params`, attributes, and duplicate names with mapped
  diagnostics.
- Parameters are reactive, current, read-only inputs. They are in scope in
  state/computed initializers, ordinary methods, render islands, and event
  lambdas. A child input update does not reinitialize instance fields or state.
- Block-bodied components contain exactly one parameterless instance
  `Fragment Render()` method. The current block-return form and expression body
  are accepted. Expression-bodied components are stateless shorthand and may
  not declare slots or ordinary members.
- Parse component members through Roslyn rather than adding another regex.
  Initially preserve:
  - one-variable field declarations with an explicit type and initializer;
  - `const` or `static` fields as ordinary C#;
  - instance fields with `private` accessibility and optional `readonly`;
  - private instance/static methods with block or expression bodies, including
    generic and `async` methods when Roslyn accepts them.
- Keep `State<T>` and `Computed<T>` as compiler-recognized field kinds. Move
  instance field initialization into the generated constructor in source order,
  after input fields are assigned. This intentionally permits the documented
  `new(initial)` state initializer while preserving initialize-once behavior.
- Reject properties, events, constructors, destructors, operators, nested
  types, multiple variables in one field declaration, uninitialized instance
  fields, and non-private instance members. Do not silently drop any member.
- Reserve authored names `Mount`, `UpdateInputs`, and `Dispose`, plus the
  case-sensitive `__lucent_` prefix used for every other generated field/helper.
  Reject a parameter, slot, or ordinary member that collides with this contract,
  with a diagnostic at its declaration. Diagnose collisions among parameters,
  slots, and ordinary members through the shared synthetic class; never defer
  them to generated C#.
- Include supported member declarations in the same Plan 002 synthetic
  component probe as render islands. Use symbol-backed lowering inside ordinary
  method bodies so `state.Value`/`state.Update` work there without spelling
  rewrites.
- Summarize reactive reads for component-local methods by Roslyn method symbol.
  Compute the transitive union through calls to other component-local methods
  (a fixed-point handles recursive call graphs), and attach that summary when a
  render computation calls the method. External/project method bodies remain
  opaque snapshots as specified by Plan 002. This keeps
  `Text: FormatTitle()` current when `FormatTitle()` reads an input without
  guessing through arbitrary external code.
- Diagnose a component-local method used by a render computation when its
  reachable body contains a recognized state mutation. Render computations may
  call pure helpers, but must not hide an observable mutation behind reevaluation.

### Invocation and input binding

Extend a parsed renderable with an optional argument list:

```csharp
Counter {}
Counter(initial: 10) {}
UserCard(user, compact: true) {}
```

- Braces remain mandatory. For a component they contain implicit children and
  named slot supplies; they are not property assignments.
- Preserve each argument's expression, optional name, and absolute span. Bind
  it with Plan 002's `CSharpIslandBinder` using the matched parameter type as
  `ExpectedType`.
- Apply normal positional-before-named ordering, duplicate/unknown argument,
  missing-required-argument, optional-default, implicit-conversion, and nullability
  diagnostics. There is one signature, so do not implement overload resolution.
- Arguments on a native control are rejected in this plan; native properties,
  content metadata, and events keep their existing syntax.
- Add component parameters as `BoundReactiveSourceKind.Parameter`. An argument
  target in the parent depends on the union of its bound argument dependencies.
  A retained child receives `UpdateInputs(...)`; it is not reconstructed.
- Generated `UpdateInputs` compares with `EqualityComparer<T>.Default`, assigns
  every changed input first, then invokes parameter invalidations in declaration
  order. Repeated target calls when several inputs change are acceptable in this
  plan; do not add batching.

## Exact project compilation shape

Add a batch entry point while preserving the current one-source API for focused
tests and callers:

```csharp
public sealed record LucentSourceInput(
    string SourcePath,
    string SourceText,
    string? StylePath = null,
    string? StyleText = null);

public sealed record LucentSourceCompilation(
    string SourcePath,
    CompilationResult Result);

public sealed record LucentProjectCompilationResult(
    IReadOnlyList<LucentSourceCompilation> Sources)
{
    public bool Succeeded { get; }
}

public static LucentProjectCompilationResult CompileProject(
    IReadOnlyList<LucentSourceInput> sources,
    LucentProjectContext? projectContext = null);
```

The names may move into existing result files, but the behavior is fixed:

1. Normalize source paths with the platform path comparer. Diagnose every
   duplicate physical source input before deduplicating compilation work; the
   project is already failed at that point.
2. Parse every source and collect declarations, parameters, slots, usings, and
   parser diagnostics.
3. Create one base `ProjectSemanticCompilation` from the project references/C#
   sources supplied by Plan 002. Reuse that base for every component; derive one
   `ComponentSemanticAnalysis`, synthetic probe tree, and semantic model per
   component as Plan 002 requires. Do not reload metadata/C# sources per
   component or merge different component scopes into one probe.
4. Build one immutable `ComponentIndex` from all syntactically usable sources.
5. Bind every component against that index, then detect component cycles.
6. Emit every source only when the project has no error diagnostics. Return
   per-source diagnostics/symbols either way.

`LucentCompiler.Compile(...)` delegates to `CompileProject` with one source and
returns that source's result. It can resolve only components supplied in that
batch; it must never read sibling files implicitly.

`CompileLucent.Execute()` must read all `.lui` and adjacent styles first, invoke
`CompileProject` once, log every diagnostic, and preserve its existing
all-or-nothing write behavior. Keep one generated file per source and the
existing duplicate-output-path diagnostic. Do not partially write output when a
sibling component fails.

Output paths are an MSBuild-adapter concern and are not present on
`LucentSourceInput`. Before calling `CompileProject`, `CompileLucent` must
preflight all normalized physical source paths and `GetOutputPath` results,
report duplicate source/output diagnostics, and stop. In
`build/Lucent.Compiler.targets`, remove the evaluation-time predicted
`<Compile Include="@(LucentGenerated)">`. Add only the task's successful
`@(_LucentGeneratedFiles)` outputs to `Compile` and `FileWrites` inside the
target after `CompileLucent` returns. Run generation on each build for this
source-checkout phase; `WriteIfChanged` prevents timestamp churn. Add a
`ponytail:` comment naming an output manifest/accurate target-output inference
as the upgrade if generation time becomes measurable. This prevents duplicate
predicted C# items and keeps failed batches out of `CoreCompile`.

## Exact component index and bound model

Create `src/Lucent.Compiler/Semantics/ComponentIndex.cs`. It owns only static
project symbols and resolution; runtime instance state does not belong here.

At minimum, each `ComponentSymbol` records:

```csharp
internal sealed record ComponentSymbol(
    string NamespaceName,
    string Name,
    string GeneratedTypeName,
    SourceSpan DeclarationSpan,
    string SourcePath,
    IReadOnlyList<ComponentParameterSymbol> Parameters,
    IReadOnlyList<ComponentSlotSymbol> Slots);
```

Use distinct bound node kinds rather than pretending a component is a native
control:

- `BoundNativeControl`
- `BoundComponentInvocation`
- `BoundFragment`
- `BoundSlotSupply`
- the Plan 002 conditional and keyed-region nodes generalized to carry
  fragments where this plan explicitly permits them

`BoundComponentInvocation` stores the resolved `ComponentSymbol`, fully mapped
effective argument islands (including omitted defaults), slot factories, stable
lexical site ID, and output cardinality. Component symbols and parameters/slots
must also surface as distinct `LucentSemanticSymbolKind` values.

Do not expose `ComponentIndex` as a second language model. Compiler emission,
editor intelligence, and LSP navigation consume the same instance/results.

## Exact fragment contract

Add one runtime value type:

```csharp
namespace Lucent.Runtime;

public readonly struct Fragment
{
    public static Fragment Empty { get; }
    public int Count { get; }
    public Control this[int index] { get; }
    public IReadOnlyList<Control> Roots { get; }
    public static Fragment From(params Control[] roots);
    public static Fragment Concat(params Fragment[] fragments);
}
```

Required behavior:

- `default(Fragment)` equals the empty fragment.
- `From` rejects null arrays, null controls, and duplicate control references;
  it takes an immutable snapshot.
- `Concat` preserves order, ignores empty fragments, and rejects a duplicate
  control appearing through two inputs.
- Store roots behind a non-array read-only wrapper such as
  `Array.AsReadOnly(copiedRoots)`. `Roots` must not return a backing array that
  callers can cast and mutate; use the same non-array empty wrapper for
  `default(Fragment)`.
- This is a mounted-control tuple only. It has no keys, diffing, element types,
  render callbacks, subscriptions, or mutable child collection.

Add explicit fragment syntax:

```csharp
Fragment Render() => Fragment {
    TextBlock { Text: "one"; }
    TextBlock { Text: "two"; }
};
```

The current single-root return is shorthand for a one-root fragment;
`Fragment {}` is empty. A top-level fragment in this plan contains a fixed
sequence of native controls and/or component sites. Top-level `if`, `foreach`,
and `yield` are diagnosed: dynamic structural regions must remain inside a
native content host. This keeps a mounted component's root tuple stable and
avoids inventing a root-outlet protocol.

Native projection rules:

- Collection content flattens fragments in lexical order.
- Scalar native content accepts a fragment whose maximum cardinality is zero or
  one; a statically possible second root is a compile error at the invocation.
- A component's call-site cardinality is computed recursively from its fixed
  root sites and actual supplied slots. Component cycles are already errors.
- Do not insert layout controls, content presenters, invisible anchors, or
  component-as-control wrappers.

Generalize Plan 002's `ConditionalRegion` from `Control` to `Fragment`:

```csharp
public sealed class ConditionalRegion : IDisposable
{
    public ConditionalRegion(ComponentOwner owner, Action<Fragment> setRoots);
    public int? ActiveBranch { get; }
    public void Show(int branch, Func<ComponentOwner, Fragment> mount);
    public void Clear();
    public void Dispose();
}
```

Keep all Plan 002 mount/publication/rollback semantics. The generated adapter
projects zero/one/many roots into the already-dedicated native content route.
For collection routes, clearing and re-adding retained controls is acceptable
until profiling proves indexed moves are needed; add a `ponytail:` comment at
that generated helper. Do not add a generic fragment host or virtual tree.

Generalize keyed entries from one `Control Root` to one `Fragment Roots`.
Existing keys retain the child component instance, owner, controls, and state;
native order is the flattened fragment order. A changed row value calls child
`UpdateInputs` rather than replacing it.

## Exact generated component contract

For a component with parameters and slots, emit two constructor paths and one
stable mounted fragment:

```csharp
internal CardComponent(
    string? title = null,
    IUiDispatcher? __lucent_dispatcher = null)
{
    __lucent_owner = new ComponentOwner(
        __lucent_dispatcher ?? AvaloniaUiDispatcher.Instance);
    __lucent_inputTitle = title;
    // initialize authored instance fields once, in declaration order
}

internal CardComponent(
    ComponentOwner __lucent_ownerArgument,
    string? title,
    Func<ComponentOwner, Fragment> __lucent_childrenFactory,
    Func<ComponentOwner, Fragment> __lucent_actionsFactory)
{
    __lucent_owner = __lucent_ownerArgument;
    __lucent_inputTitle = title;
    __lucent_children = __lucent_childrenFactory;
    __lucent_actions = __lucent_actionsFactory;
    // initialize authored instance fields once, in declaration order
}

internal Fragment Mount();
internal void UpdateInputs(string? title);
public void Dispose() => __lucent_owner.Dispose();
```

Rules:

- The top-level constructor exposes authored parameters followed by optional
  `__lucent_dispatcher`. Every non-authored constructor/helper parameter uses
  the reserved prefix, so it cannot collide with a component parameter. The
  top-level path supplies empty slot delegates.
- The nested constructor receives a parent-created child owner, all effective
  argument values, then one `Func<ComponentOwner, Fragment>` per slot in stable
  declaration order (`children` first). Use this stdlib delegate; do not add a
  runtime slot interface/class.
- A static invocation site creates one child owner and one concrete generated
  child instance, mounts it once, and stores its `Fragment`. Owner cleanup
  clears the parent fields. Parent disposal reaches the child through the owner
  hierarchy.
- Conditional invocation sites create the child inside the branch owner. Keyed
  invocation sites create it inside the keyed-entry owner. Removing the branch
  or key disposes child-before-parent resources exactly once.
- Input changes call the concrete child's `UpdateInputs`; no reflection,
  dictionary property bag, runtime component registry, or common component
  interface is introduced.
- Emit each authored input as a private mutable backing field plus an
  authored-facing get-only property using the source parameter name, for
  example
  `private string? __lucent_inputTitle; private string? title => __lucent_inputTitle;`.
  `UpdateInputs` writes only the backing field. The synthetic probe exposes the
  same get-only property, so assignment and `ref`/`out` use in methods or event
  lambdas produce mapped diagnostics rather than mutating current inputs.
- Name all implementation-only fields and helpers with the reserved
  `__lucent_` prefix. Keep only `Mount`, `UpdateInputs`, and `Dispose` as fixed
  generated contract names; signature binding has already rejected authored
  collisions with them. Rename Plan 001/002 emitter fields such as `_owner` and
  region/control helpers into this prefix during the migration; do not leave a
  second unreserved generated-name family.
- Component CSS stays source-owned. For a non-empty fragment, emit equivalent
  fresh compiled style instances into every native root's `Styles` collection
  so each projected logical subtree receives the component stylesheet. Do not
  reuse one mutable `Style` instance across roots. Diagnose component CSS on an
  empty fragment; do not invent an invisible style host.

## Exact slot contract

Syntax:

```csharp
component Card(string? title = null)
{
    slot actions;

    Fragment Render() => StackPanel {
        TextBlock { Text: title; }
        yield children;
        StackPanel { yield actions; }
    };
}

Card(title: "Profile") {
    TextBlock { Text: user.Name; }

    slot actions {
        Button { Content: "Save"; Click: () => Save(); }
    }
}
```

Rules:

- `children` is an implicit optional slot on every block-bodied component and
  cannot be redeclared. `slot name;` declares an optional, untyped named slot.
- Component invocation body renderables supply `children`. `slot name { ... }`
  supplies a named slot. A named supply is not part of `children`.
- Unknown, duplicate, or repeated supplies are errors. Omitted slots use a
  shared generated empty delegate. Required, typed, and repeatable slots remain
  out of scope.
- Each declared slot has at most one syntactic `yield` in the callee. A slot may
  be omitted or never yielded. Reject undeclared yields, duplicate yields, and
  yields inside keyed loops.
- A yield inside a Plan 002 conditional is allowed only when that conditional
  still owns a dedicated native content route. Generalize its branch root to a
  fragment; do not add mixed conditional siblings in this plan.
- Supplied content is a delayed generated factory. It captures caller inputs,
  state, ordinary lexical values, and keyed-row locals; the callee invokes it at
  the yield site with a child owner created there. Do not create controls at the
  call site before the callee yields.
- In a keyed invocation, capture the retained `LoopValue<T>` cell, not the
  iteration variable's one-time value. Reintroduce the row local from
  `loopValue.Value` inside the slot factory on every invocation. A retained row
  whose value changes must therefore render the latest value if the callee later
  unmounts and remounts the yield.
- The supplied subtree's reactive targets remain in the caller's generated
  invalidation graph and are guarded while unmounted. The yield owner registers
  event cleanup and field clearing. Sequential remount after a conditional
  toggle is allowed; simultaneous double-mount is an internal error.
- Context syntax is not implemented here. The owner passed at the yield site is
  the future context seam; do not add a service container or context dictionary.

## Commands

| Purpose | Command | Expected result |
| --- | --- | --- |
| Baseline | `dotnet test Lucent.sln --no-restore` | Plans 001–002 baseline passes |
| Compiler | `dotnet test tests/Lucent.Compiler.Tests/Lucent.Compiler.Tests.csproj --no-restore` | all compiler tests pass |
| Runtime | `dotnet test tests/Lucent.Runtime.Tests/Lucent.Runtime.Tests.csproj --no-restore` | all runtime tests pass |
| MSBuild | `dotnet test tests/Lucent.Compiler.MSBuild.Tests/Lucent.Compiler.MSBuild.Tests.csproj --no-restore` | two-file consumer compiles |
| LSP | `dotnet test tests/Lucent.LanguageServer.Tests/Lucent.LanguageServer.Tests.csproj --no-restore` | cross-file tooling tests pass |
| Counter | `dotnet build examples/counter/Counter.csproj --no-restore` | builds without `Counter.cs` adapter |
| Existing hosts | `dotnet build examples/todo/Todo.csproj --no-restore && dotnet build examples/package-pulse/PackagePulse.csproj --no-restore && dotnet build src/Lucent.Poc/Lucent.Poc.csproj --no-restore` | all one-root hosts compile with `Fragment Mount()` |
| Workbench | `dotnet run --project examples/workbench/Lucent.Workbench.csproj -- --smoke-test` | exits 0 after component switch/close |
| Build | `dotnet build Lucent.sln --no-restore` | exit 0, no warnings |
| Full tests | `dotnet test Lucent.sln --no-build` | all tests pass |
| VS Code | `npm test` from `editors/vscode` | 5 or more tests pass |

## Scope

**Create**:

- `src/Lucent.Compiler/LucentSourceInput.cs`
- `src/Lucent.Compiler/LucentProjectCompilationResult.cs`
- `src/Lucent.Compiler/Semantics/ComponentIndex.cs`
- `src/Lucent.Runtime/Fragment.cs`
- `tests/Lucent.Compiler.Tests/ComponentCompositionTests.cs`
- `tests/Lucent.Runtime.Tests/FragmentTests.cs`
- `examples/workbench/Lucent.Workbench.csproj`
- `examples/workbench/Program.cs`
- `examples/workbench/App.cs`
- `examples/workbench/WorkbenchApp.lui`
- `examples/workbench/WorkspaceSidebar.lui`
- `examples/workbench/DocumentPane.lui`
- `examples/workbench/ProblemsPane.lui`
- `examples/workbench/README.md`

**Modify**:

- `Lucent.sln`
- `src/Lucent.Compiler/LucentProjectContext.cs`
- `src/Lucent.Compiler/CompilationResult.cs`
- `src/Lucent.Compiler/LucentCompiler.cs`
- `src/Lucent.Compiler/LucentSemanticSymbol.cs`
- `src/Lucent.Compiler/LucentCompletionItem.cs` only for a component/parameter/slot kind
- `src/Lucent.Compiler/Syntax/SyntaxNodes.cs`
- `src/Lucent.Compiler/Parsing/Parser.cs`
- `src/Lucent.Compiler/Semantics/ProjectSemanticCompilation.cs`
- `src/Lucent.Compiler/Semantics/CSharpIslandBinder.cs`
- `src/Lucent.Compiler/Semantics/NativeSymbolResolver.cs`
- `src/Lucent.Compiler/CodeGeneration/BoundComponentModel.cs`
- `src/Lucent.Compiler/CodeGeneration/GeneralBinder.cs`
- `src/Lucent.Compiler/CodeGeneration/GeneralCSharpEmitter.cs`
- `src/Lucent.Runtime/ConditionalRegion.cs`
- `src/Lucent.Compiler.MSBuild/CompileLucent.cs`
- `build/Lucent.Compiler.targets` — replace predicted generated compile items
  with successful task outputs as specified above
- `src/Lucent.Compiler/EditorIntelligence.cs`
- `src/Lucent.LanguageServer/ProjectContextLoader.cs`
- `src/Lucent.LanguageServer/LanguageServer.cs`
- `src/Lucent.Poc/MainWindow.cs`
- `src/Lucent.Poc/Program.cs` only if its smoke directly consumes `Mount()`
- `tests/Lucent.Compiler.Tests/GeneralCompilerTests.cs`
- `tests/Lucent.Compiler.Tests/ProjectSemanticBindingTests.cs`
- `tests/Lucent.Runtime.Tests/ConditionalRegionTests.cs`
- `tests/Lucent.Runtime.Tests/UiDispatcherContractTests.cs` — add Fragment to
  the exact exported runtime-type contract
- `tests/Lucent.Compiler.MSBuild.Tests/CompileLucentTests.cs`
- `tests/Lucent.LanguageServer.Tests/LanguageServerProtocolTests.cs`
- `examples/counter/MainWindow.lui`
- `examples/counter/App.cs`
- `examples/counter/Counter.csproj` only if an explicit compile item requires removal
- `examples/counter/README.md`
- `examples/todo/App.cs`
- `examples/package-pulse/App.cs`
- `docs/LANGUAGE.md`
- `docs/TOOLING.md`
- `plans/README.md` status only

**Delete**:

- `examples/counter/Counter.cs` — the native adapter is replaced by direct
  `Counter {}` component invocation.
- `examples/todo/Todo.cs` — direct `Todo {}` component invocation replaces the
  ambiguous same-named native adapter.
- `examples/package-pulse/PackagePulse.cs` — direct `PackagePulse {}` component
  invocation replaces the ambiguous same-named native adapter.

**Out of scope**:

- Context declarations/access, dependency injection, effects, reusable hooks,
  first-class component values, runtime component lookup, dynamic loading, or
  reflection-based invocation.
- Required/typed/repeatable slots, slot values outside declarative content, or
  more than one simultaneous yield of a slot.
- Component inheritance, interfaces, overloads, generic components, public
  component constructors, partial classes, properties, events, custom
  constructors, or arbitrary C# type members.
- Top-level dynamic fragments, mixed conditional siblings, unkeyed loops,
  component hot reload, style hosts for multi-root components, or optimized
  indexed collection moves.
- Making components inherit `Control`, inserting native layout wrappers, or
  building a virtual DOM.

## Steps

### Step 1: Characterize the missing composition contract

Add failing tests before production changes:

- two files in either input order resolve `MainWindow -> Counter`;
- current-namespace and imported-namespace lookup;
- duplicate names, ambiguous native/component names, missing names, and direct/
  indirect component cycles have source-spanned diagnostics;
- an authored C# `FooComponent` colliding with `component Foo()` is diagnosed
  before generated C# compilation;
- `Counter {}` produces a component symbol, not a native-control symbol;
- one file with two components remains rejected;
- the checked-in counter fails when the native `Counter.cs` adapter is absent.

Use `ComponentCompositionTests.cs` for new language behavior; leave native
projection characterization in `GeneralCompilerTests.cs`.

**Verify**: the new focused tests fail for the expected missing component-index
behavior while the pre-existing suite remains green.

### Step 2: Batch project sources and build the component index

Add `LucentSourceInput`, project results, and `CompileProject`. Parse all sources
before binding, create one base `ProjectSemanticCompilation`, derive one probe
tree/semantic model per component, then build/use the immutable `ComponentIndex`.
Preserve one-source API behavior by delegation. Change the MSBuild task from its
per-source compile loop to one batch call without changing all-or-nothing writes.

Add tests that reverse source order and that put the caller before the callee.
Run a temporary two-file SDK consumer through `dotnet build`, not only the task
class, so generated cross-file type references are compiled by C#.
Add SDK fixtures that (a) include the same physical `.lui` twice and (b) include
two distinct `*/Foo.lui` inputs that map to the current flat output name. Assert
the task reports duplicate source/output diagnostics before compilation and
adds no generated `Compile` items.

**Verify**: compiler and MSBuild focused tests pass; malformed sibling input
produces no newly written outputs.

### Step 3: Parse parameters, members, invocations, fragments, slots, and yields

Extend syntax nodes with exact spans for declaration parameters, invocation
arguments, supported ordinary members, slot declarations/supplies, yields, and
`UiFragmentSyntax`. Generalize Plan 002 condition branches and keyed bodies to
fragments, but retain the explicit restrictions in this plan.

Use Roslyn to parse C# parameter/member/argument shapes and translate diagnostics
to `.lui` spans. The Lucent parser still owns declarative delimiters and recovery.
Do not parse arguments by splitting on commas or members with regexes.

**Verify**: parser tests cover nested generic/default expressions, named
arguments, multiline methods, empty/one/two-root fragments, malformed supplies,
duplicate yields, and recovery into the next member.

### Step 4: Bind component signatures, C# members, and argument dependencies

Add parameter/slot symbols to the index. During component binding, resolve each
renderable as native, component, or ambiguity. Map arguments to one component
signature, supply omitted defaults, and submit argument/member islands in the
same Plan 002 `BindAll` batch with real expected types and symbols.

Extend reactive sources with parameters. Ensure state/computed initialization can
read constructor inputs while later `UpdateInputs` never reruns initializers.
Extend Plan 002's already-bound initializer islands with parameter symbols and
constructor-time lowering; do not convert them back to raw text or create a
second initializer binder.
Keep ordinary method calls opaque for dependency propagation, but symbol-lower
recognized state operations within the method body. Component-local methods are
the exception: compute their transitive reactive-read summaries and apply those
dependencies at render call sites; keep external methods opaque.

**Verify**: binder tests cover positional/named/default arguments, conversions,
nullability, missing/unknown/duplicate arguments, parameter shadowing, parameter-
dependent properties/computed factories, initialize-once state, and an ordinary
`Increment()` method that mutates state. Add `Text: FormatTitle()` where the
helper reads an input and prove a retained child's `UpdateInputs` refreshes the
text; diagnose attempts to assign/pass an input by `ref` or hide state mutation
behind a render helper.
Add declaration-span tests for a parameter named `Mount`, an authored
`Dispose()` method, and a member beginning `__lucent_`.

### Step 5: Implement and test mounted `Fragment`

Add `Fragment` and its small runtime test file. Generalize `ConditionalRegion`
to fragments while retaining every Plan 002 rollback and exactly-once ownership
test. Change keyed entry roots to fragments and flatten in source/key order.

Do not add a fragment interface, builder, node hierarchy, observable collection,
or host abstraction. Use arrays and existing collection APIs.

**Verify**: runtime tests cover default/empty, ordered concatenation, immutable
input snapshot, null/duplicate rejection, conditional rollback with zero/two
roots, owner disposal, and inability to cast `Roots` to a mutable backing array.

### Step 6: Emit component instances, current inputs, and fragment projection

Change generated `Mount()` to return `Fragment`. Emit the two constructor paths,
initialize instance fields once, and add `UpdateInputs`. At each invocation,
create the child through the correct lexical/branch/key owner and store its
concrete instance plus mounted roots.

This return-type change affects every authored host, not only Counter. Update
all current hosts to require the expected one-root cardinality and native type:
`src/Lucent.Poc/MainWindow.cs`, `examples/todo/App.cs`, and
`examples/package-pulse/App.cs`. Delete the Todo and PackagePulse native adapter
classes and keep their existing `.lui` call sites as direct component
invocations. Update `src/Lucent.Poc/Program.cs`
only where its smoke assertions consume `Mount()` directly. Do not use
`fragment.Roots.First()`; throw a clear host-boundary error unless `Count == 1`
and root `[0]` has the expected `Window`/`Control` type.

Project fragments directly into native scalar/collection routes. Diagnose known
scalar over-cardinality. Preserve keyed child instances and call
`UpdateInputs` when a retained row value changes.

**Verify**: generated-code tests show no native wrapper, reflection, component
registry, VDOM, or remount on input changes. Runtime integration tests prove
child-before-parent disposal and state preservation after parent input updates
and keyed movement. POC, Todo, and Package Pulse build with the fragment-returning
contract before proceeding to slots.

### Step 7: Emit delayed slots at their yield owners

Generate one factory delegate for each supplied slot. The callee invokes the
factory only at its yield site with a newly created yield owner. Mount supplied
controls there, register event cleanup/field clearing there, and keep reactive
targets in the caller invalidation methods guarded by mount state.

Cover omitted slots, unused supplied slots (factory never called), ordinary
children, named slots, conditional unmount/remount, caller input/state capture,
keyed-row local capture, and disposal. A slot factory mounted twice concurrently
must throw rather than alias controls.

The keyed-row regression must update a retained row value, unmount the callee's
conditional yield, remount it, and assert the slot renders the new row value.

**Verify**: focused tests observe construction only at yield, latest caller values
on remount, and exactly-once cleanup. No control is constructed for an omitted or
currently unyielded slot.

### Step 8: Remove the counter adapter and prove the user-facing example

Delete `examples/counter/Counter.cs`. Keep `MainWindow.lui`'s existing
`Counter {}` unchanged except for syntax adjustments required by this plan.
Update `App.cs` for `Fragment Mount()` and assert exactly one `Window` root.
Update the README to state that invocation is direct.

**Verify**:

```powershell
dotnet build examples/counter/Counter.csproj --no-restore
```

Expected: exit 0; generated `MainWindowComponent` constructs
`CounterComponent`, and no authored/native `class Counter : ContentControl`
exists.

### Step 9: Bring editor and LSP project snapshots to parity

Have `ProjectContextLoader` request `LucentSource` items from MSBuild in addition
to references/C# sources. Build a project source snapshot from disk, overlaying
every open document's unsaved text by normalized path. Give each project
snapshot a generation derived from the normalized source paths plus all open
buffer versions/text. On `didOpen`, `didChange`, or `didClose`, invalidate every
cached analysis in that project. Hover, definition, completion, diagnostics, and
document symbols must all query/rebuild against the same current generation;
none may prefer an older per-document analysis. Pass that snapshot to the same
parser/index/binder used by builds.

After a generation change, rebuild and publish diagnostics (including empty
diagnostic arrays that clear old errors) for every open document in the affected
project, not only the changed URI. Serialize/cancel this project-wide analysis
through the language server's existing request cancellation path so an older
generation cannot publish after a newer one.

Add completion for visible components, component arguments, and named slots;
hover for component/parameter/slot symbols; definition to another `.lui`; and
document symbols for declarations. Do not reconstruct a component table in the
language server.

**Verify**: protocol tests open two files, prime the caller caches, change the
callee without saving, and observe updated completion, hover, and definition
from the caller. Repeat after closing the dirty callee to prove disk fallback.
Create then fix a callee signature/name error and assert caller diagnostics are
published and subsequently cleared without editing the caller. Source order and
disk/open-buffer precedence tests pass.

### Step 10: Start the multi-file Workbench dogfood shell

Create `examples/workbench` with `WorkbenchApp`, `WorkspaceSidebar`,
`DocumentPane`, and `ProblemsPane` as separate `.lui` components. Exercise:

- positional/named/default inputs;
- one implicit `children` slot and one named slot;
- one conditional component branch;
- one stateful child whose state survives a parent input update;
- static placeholder workspace/problem data only.

The smoke mode mounts the window through `Fragment`, asserts the expected tree,
switches one conditional pane, closes the window, drains one UI turn, and exits
without an unhandled exception. Exactly-once removed-child cleanup belongs in
the deterministic compiler/runtime integration tests from Steps 6–7; do not add
a production instrumentation hook only for this smoke. Do not add commands,
persistence, a custom editor, virtualization, DI, or real filesystem access.

**Verify**: the Workbench smoke command exits 0, then the full solution and VS
Code tests pass.

### Step 11: Update language/tooling documentation and index status

Update `docs/LANGUAGE.md` from working syntax to the exact executable subset:
fixed top-level fragments, current inputs, supported member forms, and optional
single-site slots. Update `docs/TOOLING.md` with cross-file component navigation.
Do not claim context, typed slots, or arbitrary C# members.

**Verify**: `git diff --check`; all Markdown links resolve; only the Plan 003 row
changes status in `plans/README.md`.

## Test plan

- `ComponentCompositionTests.cs` is the semantic truth for project indexing,
  parameters, component calls, fragments, slots, cycles, ownership, and source
  spans. Assert behavior and small generated contracts, not whole snapshots.
- `FragmentTests.cs` tests only runtime tuple invariants. Conditional ownership
  stays in `ConditionalRegionTests.cs`.
- MSBuild tests must compile a fresh two-file SDK project and prove one bad source
  prevents all output replacement.
- LSP protocol tests must use two `.lui` documents and unsaved-buffer overlay;
  compiler-only symbol tests are insufficient for cross-file navigation.
- Counter is the minimal direct-invocation acceptance test. Workbench is the
  product-shaped parameter/slot/conditional/disposal smoke.
- Keep Plan 002's dependency/shadowing tests green after adding parameter sources
  and ordinary members.

## Done criteria

- [x] `Counter {}` resolves to the `.lui` component with `Counter.cs` deleted.
- [x] All project `.lui` declarations are indexed before body binding; reversing
      input order produces identical successful output.
- [x] One malformed source prevents partial MSBuild output replacement.
- [x] Duplicate physical sources/output paths and generated C# type collisions
      are diagnosed before emission.
- [x] Authored declarations cannot collide with generated contract names or the
      reserved implementation prefix.
- [x] Parameters bind with C# types/defaults/named arguments and update retained
      children without rerunning state initializers.
- [x] Authored input identifiers are read-only; component-local helper method
      dependencies keep render targets current.
- [x] Supported ordinary methods work; every unsupported member gets a mapped
      diagnostic rather than being dropped.
- [x] Component and native-control symbols are distinct; ambiguities and cycles
      are diagnosed at exact source spans.
- [x] `Fragment` supports fixed zero/one/many roots without wrappers or a VDOM.
- [x] Every pre-existing generated-component host validates and consumes the
      new fragment return contract; Todo, Package Pulse, and POC builds pass.
- [x] Static, conditional, and keyed component sites use child owners and dispose
      child-before-parent exactly once.
- [x] Slots are delayed, caller-capturing, optional, single-site, and owned at the
      yield location.
- [x] Compiler and LSP consume the same component index/semantic results.
- [x] Cross-file completion, hover, definition, and unsaved-buffer overlay work.
- [x] A sibling `.lui` change republishes/clears diagnostics for every affected
      open document from one current project generation.
- [x] Counter builds and Workbench's native smoke passes.
- [x] Full solution, runtime/compiler/MSBuild/LSP, and VS Code tests pass.
- [x] No files outside scope are modified except generated/ignored build output and the approved diagnostic/snapshot test exception.

## STOP conditions

Stop and report; do not improvise if:

- Plans 001–002 are not complete or their owner/dependency contracts changed.
- Cross-file binding would require compiling sources in discovery order or
  reading sibling files from `LucentCompiler.Compile` implicitly.
- A component must inherit `Avalonia.Controls.Control` or a hidden native wrapper
  is required for identity, styling, or child placement.
- Correct component calls require a runtime registry, reflection, dynamic
  loading, or a common component interface.
- A top-level structural change requires a root outlet. Keep top-level fragments
  fixed and report the motivating source instead of adding one.
- Slot content must be materialized before `yield`, mounted simultaneously at
  two sites, or given a second ownership path.
- Scalar content cannot be validated against fragment cardinality before native
  mutation.
- Editor/LSP support would need a second parser, component index, or scope model.
- A per-document LSP cache can return symbols from an older project snapshot
  after any sibling `.lui` open/change/close event.
- Supporting the Workbench shell requires context, typed/repeatable slots,
  mixed structural siblings, routing, persistence, virtualization, or real file
  I/O. Use static data and the accepted narrow subset.

## Maintenance notes

Plan 004 builds the desktop shell on these concrete component instances. Plan
005 must reuse keyed child fragments and owners for virtualization rather than
adding another component lifecycle. Context may later extend the slot-factory
arguments, but must preserve caller lexical capture and yield-site ownership.

Reviewers should scrutinize project-wide all-or-nothing diagnostics, input
initialize-once behavior, fragment cardinality, slot construction timing,
unsaved LSP overlays, and accidental wrapper/registry/VDOM abstractions.
