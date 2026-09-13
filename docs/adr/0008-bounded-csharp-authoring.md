# Bounded C# authoring over retained recipes

Status: accepted direction for [#265](https://github.com/RichiCoder1/lucent/issues/265),
September 13, 2026. Concrete signatures are subject to the executable
[#266 feasibility gate](https://github.com/RichiCoder1/lucent/issues/266), currently
in progress. This record does not assert that downstream APIs are delivered.

`.lui` remains the primary authoring language. Concise C# authoring uses the same
`ComponentRecipe.Defer(string, Func<ReactiveScope, ComponentRecipe>)` lifetime:
setup occurs per mount, owns one stable root, and rolls back with the existing
mount transaction. There is no rerender engine or ambient current owner.

## Capability and conversion boundary

The gate tests one immutable `AuthorRecipe<TCapabilities>` over the erased
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
`Name(string)`, `Name(Func<string>)`, `Description(string)` and
`Description(Func<string>)`. `.End` returns `AuthorRecipe<T>`. Its terminal
conversions are direct rather than chained through another user conversion.
The target receives the last fixed value or live reader for each metadata field;
replacing one form clears the previous form.

The wrapper converts directly to both `ComponentRecipe` and `ContentRecipe`.
The immutable `.Aria` group also supports both terminal conversions, while
`.End` returns the original capability-bearing wrapper. Naming and deferred
forwarding retain contributions. User-defined conversions do not supply delegate
return covariance: a factory returning the wrapper needs an explicit lambda
when passed to a `Func<..., ComponentRecipe>`. The gate inventories these sites
before #269 atomically migrates stock factories and their consumers.

Style contributions reach the declared target before its first presentation,
inner contributions before outer ones, preserving existing last-writer and
control-authority rules. Semantic author metadata merges with the behavior-owned
base through one path. It cannot replace roles, actions, values, input policies
or password protection. Live behavior updates preserve author overrides; removing
an override reveals the latest base declaration. These runtime extensions belong
to #269 and #270, beyond the gate's proof component.

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
therefore that shape is deliberately excluded. This gate does not add generated
overloads to the production `StyleFluency` catalog.

## Build-time descriptor and initialization

The shared descriptor lives in the existing compiler assembly and is derived
from actual Roslyn property/component symbols. Its separately named fields are:
author name, symbol and value type, supported target capabilities, supported
input forms, aliases, and explicit style/semantic target metadata. Motion
eligibility and paint invalidation remain separate facts. The gate recognizes
the exact wrapper identity and closed marker return types alongside existing
exact `ComponentRecipe` returns; it does not migrate the stock catalog.

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

Later optional state generation implements explicit partial properties; it does
not rewrite fields or expressions. A direct generated factory runs inside the
deferred owner callback, creates cells in ordinal property-name order, attaches
once, then calls a synchronous typed initialization hook before authored code.
Unattached, off-thread, disposed and double-attachment access fail. Constant or
defaultable values and explicitly named static typed initializers are supported;
initialization failures use existing reverse-order rollback. Context helpers
forward to the real owner and its async/resource primitives.

## Delivery boundary

#266 must preserve a runnable positive/negative proof, concrete signature record,
package-metadata consumer and NativeAOT evidence before dependent work proceeds.
It leaves production stock factory returns intact. #267 adds the owned context;
#268 adds shared metadata/generated styles; #269 performs the atomic factory
migration; #270 adds persistent grouped semantics; #271 adds partial state.
Core/Browser adoption and bounded drawing/Gauge follow under #272–275.

The proof starts from delivered component source `a18d662`, after the editor
reference and desktop interaction implementation, rather than the older design
worktree. The pinned SDK/compiler,
results and final signature deviations will be recorded with gate completion.

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
| Compiler, generator, SDK and LSP fixtures | Component discovery must recognize wrappers from source and metadata together; generated public factories still return the erased recipe until the atomic migration. |

Existing lambda bodies already returning `ComponentRecipe` need no workaround.
The source inventory is a migration checklist, not evidence that every later
factory-return change is binary compatible; the change is deliberately pre-1.0.
