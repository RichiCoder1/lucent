# C# authoring

`.lui` remains Lucent's primary composition language. C# factories and generated
`.lui` components share the same recipes, retained elements, reactive graph,
semantics and lifetime. The C# surface makes explicit setup and library components
less repetitive; it does not rerun a component body after every state change.

## Owned setup

```csharp
ComponentRecipe Counter() => Component.Define("counter", ui =>
{
    var count = ui.State(0);
    return Components.Button(() => $"Count: {count.Value}", () => count.Value++)
        .Height(40)
        .Aria.Description("Increment this counter")
        .End;
});
```

Each mount runs setup once inside the existing deferred mount transaction. Its
context offers `State`, `Computed`, `Observe`, `Resource`, `Own`, `OnDispose` and
`Post`. These operations use the retained component's owner; there is no ambient
context or second scope. State belongs to the UI owner thread. `Post` accepts
cross-thread work and returns a cancellation handle; disposing the component
also cancels queued callbacks and owned work.

`Resource` forwards Lucent's latest-generation `AsyncValue<T>` behavior, including
source-driven loading, cancellation and optional stale values. The stale-value
overloads require the final `name` argument (which may be `null`) so a string
stale value cannot accidentally bind as a diagnostic name. Accepted application
work should remain owned by the application when it must outlive a component.

An explicit diagnostic name is optional for each allocation. Fallback names use
`component.operation-ordinal`; named allocations also consume the single
per-mount ordinal. Nested setup, failure rollback and reverse-order disposal
follow the existing `ComponentRecipe.Defer` contract.

Future owner-context work (#207) can extend this explicit per-mount facade over
the same retained owner. This phase adds no service locator, ambient context,
dependency-injection container or navigation lifetime.

## Snapshot values, live readers and tokens

```csharp
Components.Text(model.Title);        // Snapshot at recipe construction.
Components.Text(() => model.Title);  // Tracked when the mounted control reads it.

Style.Empty.Spacing(8);
Style.Empty.Spacing(() => density.Value);
Style.Empty.Spacing(MyTokens.Spacing);
```

Same-name helpers are generated from the actual authorable property descriptors.
Reader and token assignments retain their identity and lifetime. Existing aliases
remain available. Only supported concrete value types receive overload priority;
this is not a blanket rule for `object` or callback-valued properties. For a
nullable dimension, `Width(null)` assigns a null dimension; it does not remove
the assignment. `Spacing(default)` selects the zero value. Typed null readers
and tokens are invalid.

Recipe chains forward these same style assignments to the existing target before
its first presentation. They add no retained element. Component defaults,
author order, variants and control-authoritative values keep their usual
precedence. Inner same-root contributions precede outer contributions.

## Capabilities and accessibility

Opted-in stock factories return `AuthorRecipe<StyledCapability>` or
`AuthorRecipe<StyledAccessibleCapability>`. `.Style(...)` and generated property
helpers are available only on styled recipes. `.Aria` is available only on
accessible recipes; `.End` leaves the group and preserves the original marker.
Recipes and terminal Aria groups convert directly to both `ComponentRecipe` and
`ContentRecipe`, including component content collections.

```csharp
Components.Button(() => model.Caption, model.Save)
    .Padding(12)
    .Aria.Name("Save document")
    .Description(() => model.SaveHelp)
    .End;
```

Names and descriptions are author metadata over the control's current semantic
declaration. They do not replace its role, actions, range, selection,
relationships or password protection. Every behavior update reapplies active
author metadata. An ordered `.Aria.Metadata(() => optionalMetadata)` contribution
may return `null` to reveal earlier contributions and the latest control base.
`Name(null)`, blank names and blank descriptions remain invalid.

The basic Button and Gauge factories also accept an explicit `aria` reader, so
their `.lui` consumers use the same merge path:

```lui
<Button aria={() => new AriaMetadata(name: "Save document")} onInvoke={model.Save}>
    {model.Caption}
</Button>
```

Capabilities are deliberately explicit. Layout-root composites such as Tabs,
Disclosure and Slider expose style on their root, without guessing which descendant
should receive a name. ListBox and VirtualizedList also expose root styling; their
list semantics belong to a retained region inside the viewport. Arbitrary erased recipes cannot acquire capabilities by searching
their children. Existing generated composites and popup factories retain their
declared return types unless they explicitly opt in.

## Optional generated state

```csharp
[ComponentState]
public sealed partial class CounterState
{
    [State] public partial int Count { get; set; }
    [State("Untitled")] public partial string Title { get; set; }
}

ComponentRecipe Counter() => Component.Define<CounterState>("counter", (ui, state) =>
    Components.Button(() => $"Count: {state.Count}", () => state.Count++));
```

The generator implements explicit partial properties with owner-backed cells.
It does not rewrite ordinary fields, properties or arbitrary C# expressions.
Each mount creates a fresh instance, initializes cells in ordinal property-name
order and attaches all of them before the optional synchronous
`partial void Initialize(ComponentContext context)` hook runs.

Use `[State(Initializer = nameof(CreateValue))]` for a static initializer returning
the exact property type and taking one `ComponentContext`. Nonnullable reference
properties need an explicit initializer. Construction uses a direct generated
static interface factory, without reflection. Unattached, disposed and off-thread
state access fails. Async initialization belongs in an explicit `Resource`.
The first generator supports top-level, nongeneric, sealed partial classes with
no explicit instance constructor. State properties must be partial instance
get/set properties without accessor modifiers, `init`, or `required`. The
initialization hook must be synchronous; an `async void` hook is rejected.

Advanced library code can still use the retained `ComponentRecipe` and explicit
`AuthorRecipe.Target<T>` APIs. A custom target owns its presentation and semantic
behavior. `BehaviorContext.BindSemantics` provides an owned live declaration;
custom mappings consume `AuthorRecipeValues.MetadataReader` when forwarding
ordered accessibility contributions.

## Source migration

Opted-in stock factory return types changed before release. Ordinary calls,
content collections and explicit recipe assignments still convert. C# delegate
return covariance does not apply user-defined conversions: replace a method
group passed as `Func<..., ComponentRecipe>` with an explicit lambda.

```csharp
Func<string, ComponentRecipe> make = value => Components.Text(value);
```

Update exact return-type assertions and explicit delegate declarations alongside
the factory change. `.lui` component discovery recognizes the exact closed Core
capability markers from source and referenced package metadata; a similarly named
consumer type is not a component capability. Core has no runtime compiler or
renderer dependency. The generator and editor require the repository's C# 14
toolchain (SDK 10.0.401 and Roslyn 5.0).

Keep the language server and Core/SDK authoring version aligned: property
discovery reads the attributed metadata introduced in this phase. When working
from repository project references, build the default Debug configuration once
before opening the editor project so MSBuild can load its generator analyzers.
Package consumers receive those analyzer binaries during SDK restore. Editor
snapshots preserve the C# state/style generators and original document-version
metadata while generating `.lui` projections separately.

## Portable drawing and Gauge

`DrawingDescriptor` records a bounded immutable command list in local coordinates.
`Components.Drawing` mounts it on one retained root. Commands include lines,
arcs, rectangles, rounded rectangles, ellipses and paths, with scoped clipping and
transforms. Live values are read while recording on the owner, never during Skia
replay. Invalid geometry, excessive commands or unbalanced scopes fail while
recording. This is a small portable drawing surface, not an immediate-mode canvas
or a chart engine.

The initial recording limits are 4,096 operations, 16,384 referenced path verbs,
32 nested clip/transform scopes, and an absolute geometry/transform component
limit of 1,000,000. Images, text shaping, custom renderer callbacks and arbitrary
scene nodes are outside this recorder; compose normal Lucent children instead.
Retained scenes hold independent drawing leases and can finish replay after the
component is disposed without reading its former state.

```csharp
Components.Gauge("Storage used", () => model.Percent, new GaugeOptions(unit: "%"))
    .Aria.Description("Read-only storage usage")
    .End;
```

Gauge uses a six-DIP ring in a 96-DIP preferred box, stock theme brushes and
ordinary value/unit text. When a monochrome palette gives the track and accent
the same brush, the track narrows to two DIP while the value arc stays six DIP,
preserving a visible distinction without introducing a custom color. Style
controls its layout. Narrow boxes ellipsize the visible label and clip drawing
to the box while keeping the complete semantic value. Valid values expose a
read-only ProgressBar range; null shows an empty indicator, and nonfinite or
out-of-range inputs show `Unavailable` without publishing an invalid range.
It schedules no idle animation. The Component Browser feedback example and
package consumers exercise the same component through `.lui` and C#.

## Cost and verification

`tools/Measure-Authoring.ps1` measures a forced Core rebuild and an unchanged
incremental build, including project references and analyzers but excluding
restore. On the September 13 implementation candidate, SDK 10.0.401 Release
builds took 21.448 seconds and 0.976 seconds respectively, with warm dependency
caches. These are observations on the development machine, not isolated generator
timings or performance thresholds.

The six attributed property groups contain 54 properties. Generation supplies
165 Style methods and 330 capability-specific recipe methods, extending the
existing StyleFluency type and adding one helper type. Three handwritten Padding
conveniences remain. The measurement script records source identity, dirty state,
toolchain, commands and counts under `artifacts/authoring-build-measurements.json`.

`tools/Test-AuthoringPackages.ps1` characterizes raw deferred, context-backed and
generated-state counters in both managed and NativeAOT execution. Each path warms
32 mounts, then measures allocations and elapsed time for 256 complete
mount/drain/dispose cycles while asserting one retained root and no child roots.
Drawing contracts separately verify that idle frames and equality-suppressed
updates do not rerecord commands. Package verification in CI executes this proof
alongside the maintained headless package consumer.

The first package-only observation used Core/SDK
`0.3.0-dev.authoring265.1` from `0a2ae12`:

| Counter construction | Managed bytes per cycle | NativeAOT bytes per cycle |
| --- | ---: | ---: |
| Raw deferred recipe | 70,657 | 72,606 |
| ComponentContext | 70,665 | 72,614 |
| Generated partial state | 70,689 | 72,638 |

These totals include the same Button, graph work and disposal, not just state
construction. The facade adds eight observed bytes and generated state adds
32 bytes over that baseline. The corresponding 256-cycle elapsed times were
19.389/17.113/15.871 ms managed and 10.833/9.714/7.911 ms NativeAOT. Warmup,
tiering and measurement order make these timings unsuitable for ranking the
APIs. Rerun the maintained script for another environment; the values are not
acceptance thresholds.
