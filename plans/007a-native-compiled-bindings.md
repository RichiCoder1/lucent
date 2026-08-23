# Plan 007a: Use Avalonia compiled bindings at native seams

> **Executor instructions**: Keep Lucent's Roslyn-bound dependency graph and
> generated direct assignments as the default. Add Avalonia `CompiledBinding`
> only when the author explicitly requests native binding semantics. Do not
> make `State<T>` implement `INotifyPropertyChanged`, add a view-model adapter,
> or run the same property through both reactive systems.
>
> **Drift check**: `git diff --stat 46e9503..HEAD -- src/Lucent.Compiler
> src/Lucent.Runtime tests examples docs plans`
>
> The ListBox recycling STOP condition fired and was resolved by explicit user
> approval to ship bindings without root recycling. Still stop if coded
> `CompiledBinding.Create` cannot bind a native `AvaloniaProperty` without
> reflection.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: MEDIUM
- **Depends on**: Plans 005, 006, 007; commit `ce7167f`
- **Category**: language / native interop / performance
- **Status**: DONE
- **Planned at**: commit `46e9503`, 2026-08-17

## Why this matters

Lucent's compiler already does more than a binding engine for component-owned
state: it binds arbitrary C# expressions, derives precise reactive reads,
schedules source-specific invalidation, retains structural regions, and owns
async cancellation and failures. Replacing that path with Avalonia bindings
would add a second scheduler without removing Lucent machinery.

Native controls have a different seam. Avalonia binding already owns
`DataContext` changes, `INotifyPropertyChanged`, target-property default binding
modes, validation, and recycled template controls. Lucent should use that
behavior where it is the contract instead of rebuilding it.

The immediate proof is Plan 005's virtualized lists. First-class item templates
currently emit `FuncDataTemplate<T>(..., supportsRecycling: false)` because
their direct item expressions would become stale when Avalonia reuses a root.
A compiled item path can follow inherited `DataContext` without a Lucent
component owner. The implementation spike found that Avalonia 12.1.1
`ListBox` does not actually pass existing roots through `Build(data, existing)`.

## Decision

Add one explicit property-value form:

```csharp
Text: binding(item.Message);
```

`binding(...)` is contextual only when it is the complete value of a native
property member. It is not a general C# helper and does not change ordinary
Lucent expressions.

- In an item template, the first path segment must be the typed item local.
  Lower the remaining path against inherited `DataContext`; do not capture the
  current item as an explicit source.
- Outside an item template, the first path segment must identify one stable
  project object: a readonly ordinary component field or component parameter. Pass
  that object as the explicit compiled-binding source.
- Use the native target property's default `BindingMode`. This gives controls
  such as `TextBox.Text` their native two-way behavior without a Lucent-specific
  mode table.
- Keep plain property values unchanged. They remain direct assignments with
  Lucent dependency analysis and invalidation.
- Keep raw Avalonia `Binding`/`CompiledBinding` C# as the escape hatch for
  converters, explicit modes, relative sources, element names, priorities,
  delays, fallback values, and dynamic paths.

Do not add optional arguments to `binding(...)` in this plan.

**Approved spike adjustment (2026-08-17):** ship compiled bindings but keep
generated `FuncDataTemplate<T>` instances non-recycling. The public recycling
API can update an explicitly supplied root, but the native `ListBox` does not
supply one, including when a realized item is replaced. Do not claim or emulate
root reuse with a custom presenter or owner.

## Supported path subset

The compiler accepts only path shapes supported by Avalonia's coded compiled
binding API and proven by the Step 1 spike:

- property access: `item.Message`;
- nested property access: `item.Node.Name`;
- indexers with compile-time-valid arguments: `item.Names[0]`;
- a type cast followed by a path;
- logical NOT over a Boolean path;
- Avalonia-property access if the public coded API spike proves it useful.

Method calls, interpolation, arithmetic, conditional access, null coalescing,
object creation, arbitrary operators, and Lucent `State`, `Computed`, or
component-input reads are not native binding paths. Authors keep those as
ordinary Lucent expressions:

```csharp
Text: $"{item.File}:{item.Line} {item.Message}";
```

Unsupported `binding(...)` expressions receive a source-mapped diagnostic that
suggests a plain expression or raw Avalonia binding. Never silently fall back;
native binding is observable through update and validation behavior.

## Item-template contract

All first-class item templates retain the existing non-recycling generated
shape. Explicit item bindings still use inherited `DataContext`, notifications,
validation, and null clearing.

### Deferred recyclable native template

Do not emit `supportsRecycling: true` in Avalonia 12.1.1. Reconsider only when a
native item control observably passes existing roots. The intended safety
classification remains:

- every item-dependent native property is an accepted `binding(item.Path)`;
- remaining values are independent of the item and Lucent reactive sources;
- the existing native-only, exactly-one-root, no-event, no-structural-region,
  no-Lucent-component restrictions still hold.

The template builder creates one native tree and attaches compiled bindings to
the relevant `AvaloniaProperty` fields. Avalonia may return the existing root
and update its `DataContext`; bindings must refresh every target without
rebuilding controls.

### Direct non-recycling template

Retain the current `supportsRecycling: false` lowering and null-clear guard for
all templates. Mixed templates may still contain explicit native bindings.

The distinction between an explicit native binding and an ordinary Lucent
expression is semantic and deterministic. Do not infer that an arbitrary C#
expression is equivalent to a binding path.

## Native property and source contract

- Resolve the CLR property and its public static Avalonia property identifier
  (`StyledProperty<T>` or `DirectProperty<...>`) through Roslyn metadata. Search
  the target type hierarchy exactly as native property resolution already does.
- Diagnose `binding(...)` when the target is only a CLR property and has no
  bindable Avalonia property identifier.
- Validate source and target conversion through Roslyn before emission.
- Surface Avalonia's default-mode writeability and validation behavior
  normally; do not duplicate registration metadata or mirror errors into
  Lucent state. Roslyn still validates that the path can be read and converted
  to the native CLR property type.
- For a readonly ordinary-field source, its object identity is stable for the mounted
  component. Dispose the binding with `ComponentOwner`.
- For a component-parameter source, retain the binding disposable and replace
  it when `UpdateInputs` changes that source. Dispose the old binding before
  installing the new one; root disposal clears the current binding exactly
  once.
- `State<T>.Value`, `Computed<T>.Value`, and expressions derived from them stay
  on Lucent's direct invalidation path. No notification adapter is permitted.

## Generated-code shape

The intended coded-Avalonia shape is:

```csharp
text.Bind(
    TextBlock.TextProperty,
    CompiledBinding.Create<ProblemItem, string>(item => item.Message));
```

For a stable explicit source outside a template:

```csharp
textBox.Bind(
    TextBox.TextProperty,
    CompiledBinding.Create<SettingsModel, string>(model => model.Name,
        source: settings));
```

Use fully qualified generated names. Store only the `IDisposable` handles that
need component-owned replacement or cleanup. Template bindings are owned by
their native controls and must not create one `ComponentOwner` per row.

Generated output remains deterministic and receives readable snapshots for the
compiled item-template and explicit-source cases.

## Tooling and diagnostics

- Reuse the existing C# property-value island and spans; add no parser syntax.
- Binder identifies the exact invocation form contextually only after resolving
  a native property.
- Completion and hover inside the path use the same typed item/project semantic
  context as ordinary C# islands.
- Go-to-definition resolves the source model member.
- Hover on `binding` explains whether the target uses inherited `DataContext`
  or an explicit source and reports the native target's default binding mode.
- Completion does not suggest converters, modes, or other deferred arguments.
- Diagnostics distinguish unsupported path shape, non-bindable native target,
  incompatible value type, forbidden Lucent reactive source, and delayed slot
  ownership. Native default-mode write errors remain native binding errors
  because Avalonia's registration metadata is not duplicated in the compiler.

## Scope

**Create**:

- `tests/Lucent.Compiler.Tests/Snapshots/CompiledItemTemplate.g.cs.snap`
- `tests/Lucent.Compiler.Tests/Snapshots/CompiledNativeBinding.g.cs.snap`

**Modify**:

- `src/Lucent.Compiler/CodeGeneration/BoundComponentModel.cs`
- `src/Lucent.Compiler/CodeGeneration/GeneralBinder.cs`
- `src/Lucent.Compiler/CodeGeneration/GeneralCSharpEmitter.cs`
- `src/Lucent.Compiler/Semantics/CSharpIslandBinder.cs`
- `src/Lucent.Compiler/Semantics/NativeSymbolResolver.cs`
- `src/Lucent.Compiler/EditorIntelligence.cs`
- focused compiler, MSBuild, language-server, and Workbench tests
- `examples/workbench/WorkspaceSidebar.lui`
- `examples/workbench/ProblemsPane.lui`
- `docs/LANGUAGE.md`
- `docs/ARCHITECTURE.md`
- `docs/DECISIONS.md`
- `plans/README.md` status only

**Out of scope**:

- Replacing Lucent dependency analysis, direct assignments, `State<T>`,
  `Computed<T>`, `ComponentOwner`, or `IUiDispatcher`.
- Making Lucent components recyclable item roots.
- A view-model base class, `INotifyPropertyChanged` adapter, binding registry,
  converter framework, reflection path parser, or runtime binding abstraction.
- Binding mode/converter/relative-source/element-name syntax.
- Automatically converting ordinary expressions to native bindings.
- Custom virtualization, presenters, containers, or DataContext ownership.

## Steps

### 1. Lock the Avalonia 12.1.1 coded-binding contract

Add focused Avalonia.Headless fixtures using `CompiledBinding.Create` in code.
Prove inherited `DataContext`, nested-property notification, default two-way
`TextBox.Text`, null clearing, validation propagation, direct
`IRecyclingDataTemplate.Build(data, existing)` behavior, and actual constrained
`ListBox` behavior. Record which expression-tree shapes succeed and the exact
target-property API.

**Verify**: an explicitly supplied root keeps identity while its displayed item
changes; `INotifyPropertyChanged` updates the visible target; editing a TextBox
updates a writable source through the target's default mode. Record that the
native ListBox replaces roots rather than fabricating a recycling claim.

### 2. Parse and bind explicit native paths

Recognize exact property-value `binding(expression)` from the existing C# island,
retain its source spans, validate the bounded expression shape with Roslyn,
resolve the path root and result type, and resolve the target Avalonia property
identifier. Keep the parser and ordinary property-expression path untouched.

**Verify**: focused diagnostics cover every accepted shape and each rejection;
a project-defined native control and model work without special-case tables.

### 3. Lower compiled item templates

Emit compiled paths with inherited DataContext and retain current
non-recycling lowering for every item template. Do not create row owners or a
custom recycling presenter.

Convert only the Workbench rows whose output is naturally expressible as native
paths. `WorkspaceSidebar` currently computes indentation and `ProblemsPane`
formats several fields; keep them non-recycling unless the example model gains
an honest display property for application reasons. Do not add display-only
properties solely to satisfy this plan.

**Verify**: headless tests observe the public API's explicit-root behavior,
native ListBox replacement, correct values at both ends of a 10,000-item list,
bounded realization, notification updates, null clear, and no row owners.

### 4. Lower explicit-source native bindings

Support stable ordinary-member and component-parameter roots outside item
templates. Use native default modes, retain/dispose handles through the existing
owner, and replace parameter-rooted bindings during input updates.

Use one Workbench setting/input model only if it already implements the native
notification contract. Otherwise prove this slice with a focused SDK consumer;
do not introduce a view-model layer merely for dogfood.

**Verify**: one-way notification, default two-way editing, parameter-source
replacement, validation, and disposal tests pass without Lucent invalidation
for the bound property.

### 5. Complete tooling, documentation, and gates

Add compiler snapshots, project/MSBuild consumer builds, protocol completion /
hover / definition / diagnostic coverage, and update language/architecture /
decision documentation. Record the distinction between Lucent expressions and
native binding paths prominently.

**Verify**: warning-free solution build; all .NET and VS Code tests; Workbench
virtualization/reliability flows; Workbench native smoke; markdown links; stable
snapshots; `git diff --check`; independent Standards and Spec reviews.

## Done criteria

- [x] Ordinary Lucent property expressions retain direct generated assignment
      and source-specific invalidation behavior.
- [x] `binding(item.Path)` lowers to native compiled binding without reflection.
- [x] Item templates remain non-recycling; the headless harness records the
      Avalonia 12.1.1 limitation without a custom workaround.
- [x] Explicitly reused compiled-binding roots update displayed values, nested
      notifications, and null clears through inherited `DataContext`.
- [x] Explicit-source native bindings honor the target's default mode, replace
      parameter sources, propagate native validation, and dispose exactly once.
- [x] Unsupported path shapes and non-bindable targets receive source-mapped
      diagnostics rather than silent fallback.
- [x] Completion, hover, and definition use project/item types through the
      shared semantic model.
- [x] No Lucent runtime type, notification adapter, row owner, wrapper control,
      registry, reflection path, or new dependency is added.
- [x] Generated snapshots, SDK consumer, full tests, smokes, docs, and both
      independent reviews pass.

## STOP conditions

- ~~Avalonia does not update compiled bindings when a ListBox changes a recycled
  template root's inherited `DataContext`.~~ Fired; approved resolution is
  non-recycling item templates.
- Safe recycling requires a Lucent owner per row, custom container, presenter,
  DataContext interception, or manual subscription graph.
- `CompiledBinding.Create` cannot express the documented path subset through a
  public stable Avalonia 12.1.1 API.
- The compiler cannot resolve the target `AvaloniaProperty` or source path
  semantically without reflection or a control/property name table.
- Default two-way behavior requires adapting Lucent State/Computed into
  `INotifyPropertyChanged` or duplicating writes across both reactive systems.
- Mixed direct/native bindings require changing existing Lucent expression
  semantics or silently choosing a different fallback.

## Deferred follow-ups

Add mode, converter, relative-source, element-name, delay, and fallback syntax
only after Workbench or a package consumer has a concrete case that raw Avalonia
binding cannot express clearly. Reconsider recyclable Lucent row components
only when Avalonia exposes a deterministic per-realization ownership seam.
