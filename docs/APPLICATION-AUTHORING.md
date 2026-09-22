# Application and component authoring

Use `.lui` for component state, behavior, models, and routes. Keep `Program.cs` for
platform and service wiring. Named components are enabled with
`<LucentLuiNamedComponents>true</LucentLuiNamedComponents>` in a project using
`Lucent.Lui.Sdk`. The supported toolchain is SDK 10.0.401 and Roslyn 5.9.

Run the maintained [inline sample](../apps/Lucent.AuthoringSample/README.md) with
`dotnet run --project apps/Lucent.AuthoringSample`, or its companion variant with
`dotnet run --project apps/Lucent.AuthoringSample.Companion`. Component Browser uses the
same generated-root and routing APIs in a larger application.

## Bootstrap and lifecycle

```csharp
return LucentApplication.CreateBuilder()
    .UseWindows()
    .SetTitle("My application")
    .Build()
    .Run(Application.Create);
```

The generated `Create` method returns a `ComponentRecipe`. Passing its method group
defers recipe creation until application startup has completed on the UI owner thread.
`Build(Application.Create).Run()` is equivalent. Passing `Application.Create()` creates
the recipe immediately, although component instances and state still belong to mounts.

Builder hooks are additive. `OnStart` runs in registration order before the root factory.
`ConfigureRoot` runs after the factory and before mounting; use it for root presentation
or recipe decoration. `OnMounted` runs only after the root successfully mounts. Startup
and mounted notifications therefore describe different points in the lifetime.

Use the start callback's `ProvideRootContext<T>` to provide a borrowed capability, and
register acquired-resource cleanup with its `OnStop` or `OnDispose` methods immediately
after acquisition. Stop callbacks run in reverse registration order before composition
disposal; disposal callbacks run in reverse order afterwards. Every cleanup is attempted,
and failures are aggregated. Cleanup receives the startup outcome, including whether
services became ready, the factory ran, and the root mounted.

`OnPrepareClose` callbacks run in registration order until one declines. Register
attempt-specific recovery with `ApplicationCloseContext.OnDeclined`; a decline invokes
those restorations in reverse order. Cancellation and exceptions retain the application's
failure semantics. Do not treat an exception as a user declining to close.

`Lucent.Hosting` adds `UseHosting` to the same builder. It starts Microsoft Generic Host,
creates the application service scope, attaches one service binding, and owns async scope
and host cleanup. Component code borrows declared services; it does not dispose them.
The lower-level `IApplicationLifecycle` entry point remains available for existing hosts.

## Named components and optional companions

```lui
namespace Example;

public record CounterLabel(string Text);

public component Counter(CounterLabel label) {
    int count = 0;
    string description = label.Text + ": " + count;

    void Increment() { count++; }

    <Column>
        <Text>{description}</Text>
        <Button onInvoke={Increment}>Increment</Button>
    </Column>
}
```

This declares `Example.Counter.Create(CounterLabel)`. A `<Counter label={...} />` tag
binds the named factory. Stock `[LucentComponent]` static factories remain valid tags.
There is one component declaration per file; ordinary classes, records, structs,
interfaces, enums, delegates, nested types, and generic supporting types can precede it.
A support-only `.lui` file needs no component. Supporting partial types can span `.lui`
and `.cs` files. Generic component declarations remain unsupported.

Each mount creates one sealed partial component instance. An optional companion matches
its namespace and type name; its filename is a convention, not an identity mechanism.
For example, move a counter's state and handler into `Counter.lui.cs`:

```csharp
public sealed partial class Counter
{
    [State(0)]
    public partial int Count { get; set; }

    private void Increment() => Count++;

    partial void Setup(ComponentContext context)
    {
        // Register external resources explicitly through the component context.
    }
}
```

The matching markup can read `Count` and invoke `Increment`. Do not keep duplicate
members in both files. C# companion fields and properties use ordinary C# semantics;
reactive companion properties require `[State]`. `.lui` state inference remains unchanged:
constant initializers create writable state, nonconstant expressions create derived values,
`[Once]` creates writable state initialized once, and `readonly` captures a snapshot.

Requirements resolve before component-local reactive initialization. Companion state cells
initialize before `.lui` state, followed by one setup invocation. Ordinary C# field
initializers retain normal construction timing. Put requirement-dependent work in state
initializers or setup. Authored component constructors and `[ComponentState]` on the same
named component are rejected. Implement setup either in `.lui` or as the companion's
`partial void Setup(ComponentContext)`; implementing both is an error. Failed initialization
unwinds resources already registered with the owner.

`context T value;` borrows the nearest exact typed context. `inject T service;` requires
an application service; `inject T? service;` permits absence. Nullable injection does not
hide activation failures, revoked bindings, or ownership errors. Custom service sources
must implement `IOptionalComponentServiceSource` to distinguish absence from failure.
The Hosting adapter supplies that capability using its scoped service provider.

`context ThemeContext theme;` reads the theme supplied by the framework for that mount,
including an explicitly themed subtree. It does not create a theme or override the
application's appearance. `ThemeContext` remains framework-controlled and cannot be
replaced with `Context.Provide`.

Inside `.lui`, `owner` continues to mean the mount's `ReactiveScope`, including in
`Setup(owner)`. The companion setup hook instead receives a `ComponentContext`, which
provides typed mount, theme, ownership, and cleanup operations.

Keep platform/bootstrap wiring in `Program.cs`. Ordinary `.cs` remains useful for shared
non-UI libraries, service implementations, and larger algorithms that benefit from normal
C# file organization. Choose a companion when splitting one component improves readability;
it is not required for state, handlers, supporting models or generated JSON APIs.

## Declarative routing

Route declarations may live in a support-only `.lui` file:

```csharp
[LucentRouteModule(RouteFallbackPolicy.Reject)]
public static partial class AppRoutes { }

[LucentRoute(typeof(AppRoutes), "/", Id = "home", Component = typeof(Home))]
public readonly record struct HomeRoute();

[LucentRoute(typeof(AppRoutes), "/items/{id}", Id = "item", Component = typeof(Item))]
public readonly record struct ItemRoute(int Id);
```

`AppRoutes.Bundle` contains the generated fixed table, its descriptors, and the type-backed
default destination mappings. No separate table/descriptor pairing or factory switch is
needed. Mapped components must expose the supported parameterless `Create` factory;
receive route data through `context RouteContext<ItemRoute> route;` rather than copied props.

```lui
public component Application() {
    <Router routes={AppRoutes.Bundle} initial="/">
        <ApplicationShell />
    </Router>
}

// In a separate file:
public component ApplicationShell() {
    context NavigationSession navigation;

    <Column>
        <Button onInvoke={() => navigation.Navigate(AppRoutes.Item(42))}>Open item</Button>
        <RouterOutlet />
    </Column>
}
```

`Router` normally owns its navigation session. Supply `session` only to borrow an existing
session backed by the bundle's exact table; do not also supply `initial`. The shell and
outlets see that same session. A nested `RouterOutlet` consumes the next matched level.
Declare parent relationships with the route attribute's `Parent` property.

The root outlet accepts `RouteOutletOptions`. Its asynchronous `prepare` callback controls
navigation admission. Its synchronous `resolve` callback selects a `RouteDestination` for
each route level and tracks reactive reads. `RouteDestinationRequest.GetContext<T>()`
provides typed route data; `Default` is the generated destination. Keep policy on the root
outlet: nested outlets participate in its transaction and reject independent options.
The typed parameters describe the requested match. During staged navigation, live context
values such as `ActiveEntry` still describe the committed route until publication succeeds.

During an idle session, changing a resolver dependency retains the current destination when
component type and author key are unchanged; a changed identity replaces only the affected
branch. This does not navigate, run navigation guards, or create history entries. Selection
waits while navigation is pending and reevaluates on return to idle, including veto and
cancellation. Resolver or staging failures must leave the previously committed branch intact.
During idle replacement, navigation requested from a resolver or mount callback takes precedence: the candidate
is discarded before navigation preparation starts. Selection is checked again after
mounting, including lazy derived dependencies. Initial mounting uses this same boundary.
Publication and retirement are synchronous owner phases; reentering navigation there
is a terminal programming error, just as during ordinary navigation publication.
Ordinary failed selection remains retryable after rollback. If that failed callback also
queued navigation, the session terminates with the original error and cancels the intent;
it cannot publish new content while the failure is unwinding.

## Build and editor ownership

The SDK projects ordinary declarations and component signatures, runs foreign generators
once for binding, then refines and lowers components. Final foreign-generator output must
match preparation exactly. This is a bounded pipeline, with no retry-until-stable generation.
Generated C# is build-owned; never copy it into the application as source.

The language server uses the same inputs, including unsaved `.lui`, companion and
applicable `.editorconfig` text. Named project dependencies are prepared from the current
editor graph before their consumers; previously built emitters do not supply stale symbols.
Hover, completion, signature help, definitions, references, rename, diagnostics and symbols
map back to authored locations. Support-only files participate in the same project graph.
Changing or removing a declaration invalidates dependent views and generated APIs. Stale
or canceled work cannot replace a newer editor snapshot.

Companion `[State]` properties follow the standalone state generator's supported shape.
Unsupported property or accessor modifiers, including `required` and a private setter,
report `LUI2053` at the companion declaration instead of a generated partial-member error.

See [LUI formatting](LUI-FORMATTING.md) for formatting and safe fixes, and
[the execution record](plans/application-routing-component-authoring-execution.md) for
implementation and verification status.
