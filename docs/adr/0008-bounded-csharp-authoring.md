# Bounded C# authoring over retained recipes

Status: accepted and implemented direction for
[#265](https://github.com/RichiCoder1/lucent/issues/265), September 13, 2026. The
executable [#266 feasibility gate](https://github.com/RichiCoder1/lucent/issues/266)
is preserved as historical evidence for the concrete signatures below. The
current source implements the bounded authoring runtime and its first stock
adoption. Package-only C# and `.lui` consumers execute in managed and NativeAOT
modes; the handoff records exact verification and publication status.

`.lui` remains the primary authoring language. Concise C# authoring uses the same
`ComponentRecipe.Defer(string, Func<ReactiveScope, ComponentRecipe>)` lifetime:
setup occurs per mount, owns one stable root, and rolls back with the existing
mount transaction. There is no rerender engine or ambient current owner.

## Capability and conversion boundary

The implementation uses one immutable `AuthorRecipe<TCapabilities>` over the erased
`ComponentRecipe`, with a closed set of styled, accessible and combined marker
types. Contributions do not add generic nesting or mounted elements. A default
or otherwise invalid wrapper fails before mounting. Factories opt into known
targets through their return type; components do not acquire styling or semantic
targets by searching their descendants.

The gate's concrete construction seam is a nongeneric static facade:

```csharp
AuthorRecipeTarget<T> AuthorRecipe.Target<T>(
    Action<CompositionContext, Element, AuthorRecipeValues> apply);
AuthorRecipe<T> AuthorRecipe.Create<T>(string kind, AuthorRecipeTarget<T> target);
AuthorRecipe<T> AuthorRecipe.Defer<T>(string kind, AuthorRecipeTarget<T> target,
    Func<ReactiveScope, AuthorRecipe<T>> build);
```

The subsequent [context and navigation decision](0009-context-injection-and-navigation.md)
renames this gate's `CompositionContext` mounting parameter to `MountContext`.
The separate scope-owned `ComponentContext` authoring interface keeps its name.

`T` is one of `StyledCapability`, `AccessibleCapability` or
`StyledAccessibleCapability`. Their `AuthorCapability` base is closed to
consumer inheritance. Construction with the base marker is rejected. Creation
owns an initially empty root: it does not accept an arbitrary erased recipe
whose existing presentation or semantic owner could conflict with the target.
The target performs presentation, attaches its behavior and mounts any content.
Deferred forwarding must preserve the same target at every hop; conflicting
targets fail inside the existing mount transaction.

The wrapper provides `Named(string)`, `Kind`, `IsValid`, `Recipe` and direct
conversions. C# 14 extension members expose `.Style(Style)` only for styled
markers and `.Aria` only for accessible markers. `AuthorAria<T>` offers
`Name(string)`, `Name(Func<string>)`, `Description(string)`,
`Description(Func<string>)` and `Metadata(Func<AriaMetadata?>)`. Grouped metadata
can override name and description together; returning null removes that grouped
contribution and reveals the current earlier author or behavior value. `.End`
returns `AuthorRecipe<T>`. Its terminal
conversions are direct rather than chained through another user conversion.
The retained target receives one ordered metadata reader. Fixed, live and grouped
contributions retain authoring order, with the last active writer winning each
field independently.

The package consumer exposed a required public custom-behavior seam:
`BehaviorContext.BindSemantics(Func<SemanticDeclaration>)`. Like `SetSemantics`,
it is registered during attachment by the semantic-owning behavior. Its reader
is reactive and owned by that behavior's scope, so disposal releases it. Raw
effect registration and semantic mutation remain internal. Persistent author/base
metadata merging is implemented on the declared retained target: behavior updates
preserve author overrides, and removing an override reveals the latest behavior
declaration.

The wrapper converts directly to both `ComponentRecipe` and `ContentRecipe`.
The immutable `.Aria` group also supports both terminal conversions, while
`.End` returns the original capability-bearing wrapper. Naming and deferred
forwarding retain contributions. User-defined conversions do not supply delegate
return covariance: a factory returning the wrapper needs an explicit lambda
when passed to a `Func<..., ComponentRecipe>`. The gate inventoried these sites,
and #269 adapted the stock factories and their consumers atomically.

Style contributions reach the declared target before its first presentation,
inner contributions before outer ones, preserving existing last-writer and
control-authority rules. Semantic author metadata merges with the behavior-owned
base through one path. It cannot replace roles, actions, values, input policies
or password protection. Live behavior updates preserve author overrides; removing
an override reveals the latest base declaration.

## Same-name input families

Concrete supported properties expose value, `Func<T>` and, where declared,
`Token<T>` overloads with the same name and declaring type. Values are snapshots,
readers remain live, and tokens preserve their identity. Only proven concrete
scalar value overloads receive `OverloadResolutionPriority(1)`; broad object,
callback and competing-conversion shapes are excluded.

`Name(null)` remains invalid, as do typed null readers and tokens. Semantic names
do not gain theme-token overloads. Nullable `Width(null)` assigns null rather
than removing an assignment. Nonnullable `Spacing(default)` selects `0f`, while
bare null cannot silently select zero. The compiler matrix covers reference,
nullable value, enum and struct values, lambdas, method groups, typed nulls,
default literals and unsupported broad-object/callback cases.

The executable matrix uses proof overloads backed by real `Style.Set`,
`Style.Bind`, properties and tokens. Each supported family mounts fixed, lambda,
method-group, token and default inputs, then changes the signal/theme to check
snapshot, live-reader and token behavior. A separate negative case demonstrates
that assigning priority to `object` steals a lambda from its reader overload;
therefore that shape is deliberately excluded. The gate did not add production
overloads; the integrated generator now emits the supported `StyleFluency`
catalog from the shared descriptor.

## Build-time descriptor and initialization

The shared descriptor lives in the existing compiler assembly and is derived
from actual Roslyn property/component symbols. Its separately named fields are:
author name, symbol and value type, supported target capabilities, supported
input forms, aliases, and explicit style/semantic target metadata. Motion
eligibility and paint invalidation remain separate facts. The compiler recognizes
the exact wrapper identity and closed marker return types alongside existing
exact `ComponentRecipe` returns. Generated author-property metadata and the
migrated stock catalog use the same descriptor boundary.

The generator consumes this descriptor through its current analyzer dependency.
No compiler, reflection-discovery service, Windows or renderer dependency enters
Core. Same-assembly generation and separate compiled/package metadata must both
resolve the proof component. Architecture expected-surface checks remain
independent of generated production metadata.

The embedded Roslyn package family must support C# 14 extension-member metadata.
The gate reproduced `CS1061` when Roslyn 4.14 consumed `.Aria` from the SDK-built
Core assembly. It updates the compiler, generator and editor package family
together to Roslyn 5.0.0, including the SDK's packaged notices. Core retains no
runtime Roslyn dependency. This raises the analyzer/compiler host requirement
to a C# 14-capable host; the repository already pins .NET SDK 10.0.401. Tooling
tests and a package-only consumer are required alongside the runtime proof.

Component state generation implements explicit partial properties; it does
not rewrite fields or expressions. A direct generated factory runs inside the
deferred owner callback, creates cells in ordinal property-name order, attaches
once, then calls a synchronous typed initialization hook before authored code.
Unattached, off-thread, disposed and double-attachment access fail. Constant or
defaultable values and explicitly named static typed initializers are supported;
initialization failures use existing reverse-order rollback. Context helpers
forward to the real owner and its async/resource primitives.

## Implementation boundary

#266 preserves the runnable positive/negative proof, concrete signature record,
package-metadata consumer and historical NativeAOT evidence. The current source
also implements the owned `ComponentContext`, generated author-property metadata
and style fluency, the stock-factory migration, persistent grouped semantics,
generated partial component state, shared label binding, bounded retained Drawing
and Gauge adoption.

`.lui` remains the primary stock-control authoring direction. C# authoring adds a
typed facade over the same retained recipes and does not introduce rerendering,
ambient owners, reflection discovery or a parallel widget hierarchy. Public stock
factories advertise author capabilities only when their declared retained root is
the actual target. `Slider`, `ListBox` and `VirtualizedList` are style-only because
their control semantics live on descendants; moving those semantics solely to
offer `.Aria` would change their accessibility geometry and ownership.

Generated component state is limited to top-level, non-generic, sealed partial
classes without authored instance constructors. Explicit partial properties use
constant/default values or a named static typed initializer. Cells are created in
deterministic property-name order on the existing deferred owner, followed by one
synchronous partial initialization hook. Async initialization is rejected;
unattached, off-thread and disposed access fails through the existing reactive
guards, and initialization failure uses the mount transaction rollback.

The proof starts from delivered component source `a18d662`, after the editor
reference and desktop interaction implementation, rather than the older design
worktree. The toolchain, executable results and final signature decisions are
recorded below.

### Feasibility evidence

The final isolated candidate passes Core **510/510**, Compiler **66/66**,
Generator **17/17**, and editor **27/27**, with warning-clean builds and Core
architecture positive/negative checks. The overload matrix executes all five
supported families as values, lambdas, method groups, tokens and defaults,
including typed-null rejection. The SDK is 10.0.401; the embedded Roslyn family
is 5.0.0.

After integration with the desktop closeout, Core **512/512** and Windows
**127/127** pass with warning-clean builds and the architecture checks retained.
The combined run is recorded in `artifacts/authoring-integrated-managed.log`.

Core and SDK packages `0.3.0-dev.gate266.2` from candidate `44f2d5d` were restored
into a fresh cache. A separately packaged proof library and its package-only
consumer compile and execute generated `.lui` components in managed and
win-x64 NativeAOT modes. They verify first-presentation width, authored semantics,
live metadata, stable composition/element identity, and invalidation of the old
semantic generation. No runtime Roslyn files are present. Native executable
SHA-256: `50F129797E7173598A7601A1EF2F72F2511DECBB50EF4ABD6573D242BE69EF4B`.
These local gate package identities are not claims of GitHub package publication.

Logs are under `artifacts/authoring266-worktree/artifacts/` in the main checkout:
`authoring-bind-core-full.log`, `authoring-compiler-generator-full-final.log`,
`authoring-editor-full.log`, and `authoring-package-verified.log`. The gate itself
left stock factories unchanged; the later integrated implementation supersedes
that historical source boundary. Current verification is recorded in the handoff
rather than inferred from these gate-only logs.

### Factory-return migration inventory

The initial source inventory identifies the following erased recipe contracts.
Keep them erased where they are lifetime/content infrastructure; adapt callers
with explicit conversion lambdas when migrating capability-bearing factories.

| Consumer | Migration concern |
| --- | --- |
| `Recipes.cs`: `Defer`, conditional and keyed factories | Direct expression returns convert; method groups returning wrappers do not satisfy erased delegates. |
| `Components/Lists/ChoiceItem.cs`: `Content` | User and stock item factories must remain accepted through explicit lambdas. |
| `Components/Navigation/NavigationTypes.cs`: `TabItem.Content` | Tab content factories have the same delegate boundary. |
| `Components/Navigation/NavigationComponents.cs` and `Components/Scrolling/ScrollingComponents.cs` | Keyed/tab/virtual row callbacks retain erased recipes without losing mount identity. |
| `ContextMenus.cs`, `Behavior.cs`, `InputRouter.Menus.cs` | Popup/menu callbacks remain erased; capability metadata must not alter popup ownership. |
| `apps/Lucent.ComponentBrowser/ComponentBrowserLifecycle.cs` and application lifecycle tests | `ValueTask<ComponentRecipe>` is invariant. Inferred generic factory results may need an explicit `ComponentRecipe` type argument or cast. |
| Compiler, generator, SDK and LSP fixtures | The migration made component discovery recognize wrappers from source and metadata together while preserving erased infrastructure boundaries. |

Existing lambda bodies already returning `ComponentRecipe` need no workaround.
The source inventory is a migration checklist, not evidence that every later
factory-return change is binary compatible; the change is deliberately pre-1.0.
