# Typed navigation

Navigation owns the opened route and its bounded history. Application services
still own loading, accepted writes and persistent editor sessions. A selected
list item, keyboard focus and the opened route can differ.

For the `.lui`-first application path, use generated `AppRoutes.Bundle`, `<Router>` and
`<RouterOutlet>`. The [application authoring guide](APPLICATION-AUTHORING.md#declarative-routing)
covers default component mappings, shared shell navigation and reactive destination
selection. The APIs below also support applications that compose navigation explicitly.
Both paths share the same matching, session and transactional outlet runtime. Explicit
outlets consume current generated module descriptors too; choosing generated mappings
does not require a separate navigation engine or a different route-context contract.

## Declare routes

Route declarations are generator input. They produce typed references, codecs
and closed `RouteContext<T>` providers without runtime discovery or reflection.

```csharp
[LucentRouteModule(RouteFallbackPolicy.Reject)]
public static partial class BrowserRoutes { }

[LucentRoute(typeof(BrowserRoutes), "/issues", Id = "issues")]
public readonly record struct IssuesRoute();

[LucentRoute(typeof(BrowserRoutes), "/issues/{number}", Id = "issue")]
public readonly record struct IssueRoute(int Number);
```

`BrowserRoutes.Issue(42)` returns a `RouteReference`. Capture types come from the
record parameters; do not write `{number:int}`. Zero-capture records still need
`()`. A route can declare `Parent = typeof(WorkspaceRoute)` when its component
is nested under a persistent route shell. Parentage is explicit, not inferred
from common path text.

Build one `RouteTable` from the generated module patterns and one
`RouteDescriptorSet` from that same table and modules. The table is the matching
authority. A reference from a different pattern instance is rejected even if
its text looks equivalent. Locations are bounded and canonicalized; malformed
input, unmatched routes and ambiguous tables are rejected.

## Own and provide the session

```lui
public component Browser() {
    readonly NavigationSession navigation = new(owner, BrowserRouting.Table,
        BrowserRoutes.Issues().Location);
    readonly NavigationInteraction interaction = new(owner, navigation);

    <Provide value={navigation}>
        <NavigationBoundary interaction={interaction} label="Issue navigation">
            {BrowserRouting.Outlet(interaction)}
        </NavigationBoundary>
    </Provide>
}
```

Both constructors are already scope-owned; do not add `[Owned]`. Alternatively,
an application model can own the session and provide it at the feature boundary.
Do not inject a navigation session from DI: placement determines its authority.

The app's `BrowserRouting.Outlet` calls `RouteOutlet.Create(descriptors,
levelFactory, options: new RouteOutletOptions(interaction: interaction))`.
The factory maps each known route definition to its component recipe. Descendant
components declare exact requirements:

```lui
public component IssueDetails() {
    context RouteContext<IssueRoute> route;
    context NavigationSession navigation;
    <Column>
        <Text>{"Issue #" + route.Parameters.Number}</Text>
        <Button onInvoke={() => navigation.Back()}>Back</Button>
    </Column>
}
```

A nested route shell mounts `RouteOutlet.CreateChild` with the same descriptors
and level factory. This consumes its private child cursor, not a second session
participant. Each session permits one active root outlet. Declaring a child route
without mounting a child outlet fails instead of silently flattening the branch.

Matching prefixes retain their elements, component state and resolved service
references. Changed suffixes mount again. Typed `Parameters` stay fixed for a
retained level; its live entry and child state follow publication. Breakpoint
participation and layout do not initiate navigation.

## Prepare, publish and retire

`Navigate`, `Back` and `Forward` all use the same transaction path. The default
typed navigation action is Push; Replace updates the current history entry.
Back traverses actual history. A command such as “All issues” navigates to the
collection and is a separate action.

Set the `Prepare` callback on `RouteOutletOptions` (for example,
`new RouteOutletOptions(prepare: handler)`) for asynchronous preparation before activation.
The callback receives the route level, current/target snapshots, history action,
origin and Leave/Enter phase. It returns `Allow`, `Stay`, `Redirect` or `Fail`.
Leave preparation walks leaf to root; Enter walks root to leaf. Preparation does
not mount hidden components or resolve their services.

A newer intent supersedes an older pending intent. Observe its cancellation
token for obsolete reads or prompts. Accepted saves must keep their independent
application lifetime; cancellation is not permission to abandon accepted writes.
Stay and expected preparation failures preserve the committed route and journal.
An unmatched location is rejected before preparation. Unexpected staging (including
component-mount), publication or retirement failures terminate the session; the
operation reports a terminal failure.

Use `navigation.RegisterCommitted(owner, callback)` to apply application state
that follows every successful navigation, including Back and Forward. The
synchronous callback runs after Current and Journal are assigned, inside the
publication batch, before effects and old-branch retirement. It must not perform
fallible asynchronous work. Perform that work during preparation; an exception
from a committed callback is terminal.

## Focus, scroll and commands

`NavigationBoundary` provides its `NavigationInteraction` to descendants, labels
the semantic region, announces successful navigation and routes Alt+Left through
the session's Back command. Existing editor and modal command scopes retain
precedence. Menu commands use the popup's inherited environment and the same
session; opening a row menu need not select or open that row.

Register stable authored targets with `NavigationTarget`, passing the same
`FocusTarget` used by its real control and optionally a `ViewportState`. The
wrapper does not introduce another focusable control. Active target IDs must be
unique within the committed branch.

Fresh Push uses target viewport defaults. Replace transfers available target
state. Back and Forward restore captured entry state, falling back to a route
heading or useful control when the prior target is gone. Focus in a persistent
parent is preserved. Retention is bounded by the navigation journal; retiring
an entry does not turn it into a hidden retained application tree.

See [Issue Browser](../apps/Lucent.IssueBrowser/IssueBrowser.lui) for a collection
outside the detail outlet and [application ownership](APPLICATIONS.md) for the
Hosting service boundary. OS activation, persisted history, route transitions
and multiple windows are separate capabilities.
