# Typed navigation

Navigation owns the opened route and its bounded history. Application services
still own loading, accepted writes and persistent editor sessions. A selected
list item, keyboard focus and the opened route can differ.

For the `.lui`-first application path, use generated `AppRoutes.Bundle`, `<Router>` and
`<RouterOutlet>`. The [application authoring guide](APPLICATION-AUTHORING.md#declarative-routing)
covers default component mappings, shared shell navigation and reactive destination
selection. The APIs below also support applications that compose navigation explicitly.
Both paths share the same matching, session and transactional outlet runtime. Explicit
composition builds a `RouteBundle` from current generated module descriptors; it does
not require a separate navigation engine or a different route-context contract.

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

Build one `RouteBundle` from the generated modules and destination mapping. The bundle
creates and retains the exact `RouteTable` and `RouteDescriptorSet` pairing. Its table
is the matching authority. A reference from a different pattern instance is rejected
even if its text looks equivalent. Locations are bounded and canonicalized; malformed
input, unmatched routes and ambiguous tables are rejected.

## Own and place the session

```lui
public component Browser() {
    readonly NavigationSession navigation = new(owner, BrowserRouting.Table,
        BrowserRoutes.Issues().Location);
    readonly NavigationInteraction interaction = new(owner, navigation);

    <NavigationBoundary interaction={interaction} label="Issue navigation">
        {BrowserRouting.Router(navigation, interaction)}
    </NavigationBoundary>
}
```

Both constructors are already scope-owned; do not add `[Owned]`. An application model
can own the session and pass it to the Router at the feature boundary. Do not inject a
navigation session from DI: placement determines its authority.

The app's `BrowserRouting.Bundle` is created once with
`RouteBundle.Create([BrowserRoutes.Module], destinationFactory)`. The factory maps
each known route level to a stable `RouteDestination`. `BrowserRouting.Router` places
that bundle with `Components.Router([Components.RouterOutlet(options)], Bundle,
session: navigation)`. Passing `session` borrows the application's existing session;
disposing the Router does not dispose it. Descendant components declare exact requirements:

```csharp
internal static class BrowserRouting
{
    internal static RouteBundle Bundle { get; } = RouteBundle.Create(
        [BrowserRoutes.Module],
        static level => level.Id.Value switch
        {
            "issues" => new(typeof(IssuesRoute), Components.IssuesPage()),
            "issue" => new(typeof(IssueRoute), Components.IssuePage()),
            _ => throw new InvalidOperationException("Unknown browser route."),
        }
    );

    internal static RouteTable Table => Bundle.Table;

    internal static ComponentRecipe Router(
        NavigationSession navigation,
        NavigationInteraction interaction
    ) => Lucent.Core.Components.Router(
        [
            Lucent.Core.Components.RouterOutlet(
                options: new RouteOutletOptions(interaction: interaction)
            ),
        ],
        Bundle,
        session: navigation
    );
}
```

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

A nested route shell mounts `Components.RouterOutlet()`. It consumes the nearest
Router's private child cursor, bundle and session rather than creating a second
session participant. Each session permits one active root outlet. Declaring a child
route without mounting a child outlet fails instead of silently flattening the branch.

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

## Opt-in startup location restoration

`NavigationRestoration` captures one committed canonical location in a bounded,
versioned UTF-8 envelope. The application owns the file, storage scheduling and
workspace identity. Create the session without `initialLocation`, mount its root
outlet, and call `Restore` from the application's `OnMounted` startup path:

```csharp
var navigation = new NavigationSession(owner, BrowserRouting.Table);
var restoration = new NavigationRestoration(
    BrowserRouting.Table,
    "browser-workspace-routes-v1",
    BrowserRoutes.Issues(),
    static match => match.DefinitionId.Value is "issues" or "issue"
);

// Once the root outlet is mounted; empty bytes mean no saved snapshot.
NavigationRestorePlan plan = restoration.Decode(savedUtf8.Span);
NavigationOperation startup = navigation.Restore(plan);
```

Use a scope key that distinguishes the application, workspace/account and route
contract revision. Scope and definition keys are nonblank and limited to 128 UTF-8
bytes. The pure predicate explicitly admits persistable routes; exclude routes
containing credentials or private values that must not survive restart. The typed
fallback must belong to the exact table passed to the restoration policy. Guards
remain responsible for resource availability and authorization.

Version 1 location payloads have this fixed shape:

```json
{"schema":"lucent.navigation","version":1,"scope":"browser-workspace-routes-v1","mode":"location","active":{"definition":"issue","location":"/issues/42"}}
```

The default and hard payload limit is 256 KiB; applications can configure a smaller
limit. Decoding rejects invalid UTF-8, malformed or truncated JSON, duplicate or
unknown fields, trailing data, comments, trailing commas, unknown versions/modes,
scope mismatches and values over their bounds. JSON depth is bounded at eight.
Locations must already use Core's canonical text and satisfy the current table's
limits. Both their matched definition ID and application admission policy must
still agree with the snapshot. Results expose finite reason codes, and their
diagnostic strings omit route values, scope keys and payload contents.

`Decode` does not mutate a session. Even an absent or rejected snapshot returns a
plan carrying the configured fallback. `Restore(plan)` requires an empty, idle
session with its root attached and permits only one accepted startup restore.
Wrong-table, populated or pending sessions return `InvalidRestorationState` without
superseding their current work. The policy is checked again at replay admission.
The selected route runs ordinary Enter preparation with `NavigationOrigin.Restoration`
before staging and publication. A guard rejection tries the fallback once through
that same path; a fallback rejection leaves the session empty. Redirects use the
existing redirect bound and publish one fresh entry. Supersession, cancellation,
disposal and terminal failures never launch an obsolete fallback. A cold activation
that has already started or committed therefore takes precedence over restoration.

`restoration.Capture(navigation)` reads the committed route, including while a later
asynchronous preparation is pending. It rejects capture during staging, publication,
retirement or outlet replacement. Schedule ordinary captures after retirement;
`RegisterCommitted` runs inside publication, so its handler must schedule capture
after the transaction returns to the application's owner loop. For an
`ApplicationSession`, use its synchronization context captured by `OnMounted`.
A `ReactiveScope.Post` alone is insufficient: the reactive batch can drain scope
posts while the session is still retiring. `Ready` carries the UTF-8 bytes.
`NoSnapshot` means the active route is absent or disallowed: clear any previously
stored navigation snapshot. Other finite failures carry no partial output.

Use atomic file replacement and write generations so an older asynchronous write
cannot overwrite a newer capture. Capture before normal close disposes the outlet.
Persistence errors must remain separate from accepted document writes and close
negotiation. Resuming a location creates fresh route interaction state, using the
existing heading/useful-target focus and authored viewport defaults after layout.

### Bounded journal and interaction state

Opt in to retained history by appending `options` to the restoration constructor:

```csharp
options: new NavigationRestorationOptions(
    NavigationRestorationMode.Journal,
    maximumEntries: 32,
    stateCodecs: NavigationRestorationStateCodecs.Interaction
)
```

The default remains location-only with no registered state codecs. Journal mode can
also decode an older location snapshot. It captures at most the configured entry
count and session capacity, with a hard limit of 64. The contiguous capture window
includes the active entry, prefers preceding history and then fills from forward
history. Disallowed entries inside that window are omitted in order. Snapshot-local
keys identify entries in the envelope; import allocates fresh runtime entry IDs.

Version 1 journal envelopes replace `active` with `activeKey` and `entries`:

```json
{"schema":"lucent.navigation","version":1,"scope":"browser-workspace-routes-v1","mode":"journal","activeKey":2,"entries":[{"key":1,"definition":"issues","location":"/issues"},{"key":2,"definition":"issue","location":"/issues/42"}]}
```

Malformed structure, duplicate keys, an invalid active reference or an exceeded bound
rejects the snapshot. Invalid inactive routes are dropped; an invalid active route
selects fallback. `DroppedEntries` and `DroppedStates` expose finite decode counts.
Replay rechecks policy, and a journal larger than the destination capacity selects
fallback. Only the active route mounts and runs startup guards. The entire journal,
active route and imported state publish together. Back/Forward runs normal guards.
A redirect or fallback discards imported history and state and commits one fresh entry.

The explicit `Interaction` codec persists only authored focus IDs and viewport offsets
from the root outlet's existing `NavigationInteraction` owner. Capture reads current
active targets freshly and uses retained state for inactive entries. Each encoded
state is limited to 4 KiB and 16 viewport positions. Target IDs are nonblank and
bounded to 128 UTF-8 bytes; offsets must be finite and nonnegative. Unknown codecs or
versions and invalid bounded state discard that entry's state, preserving its route.
An oversized state rejects the whole payload. With no registered codec or interaction
owner, the route history remains usable without interaction state.

Imported viewport offsets remain pending until scene installation can clamp them to
measured extents; even very large finite offsets never enter projected coordinates
first. Capture preserves a still-owned pending imported position and focus request
without changing live geometry, so closing before the first layout does not replace
restorable state with defaults. Once measured, capture uses the actual clamped offset.
Targets mounted by generated conditional content receive one deferred reconciliation
pass. A newer explicit application scroll write, including zero, wins during that
gap; targets still absent after the pass are discarded.
New application scroll writes, newer navigation, target removal, and interaction
disposal expire pending restoration. Navigation-owned focus requests use conditional
generation cancellation so a newer application focus request survives cleanup.
Imported state follows existing focus reconciliation. Missing
targets use the normal authored fallback; later navigation supersedes pending
reconciliation. Model/codec tests do not establish physical focus behavior. Real
interaction checks remain a separate proof boundary, as do application storage and
package-only NativeAOT replay. Page trees, services, editor undo, credentials and
arbitrary application objects are never serialized.

### Issue Browser storage example

Issue Browser enables persistence only with `--restore-navigation` (which can be
combined with `--native-menus`). Ordinary launch and the default hosted fixture
never read or create navigation files. The opt-in path is
`%LOCALAPPDATA%/Lucent/IssueBrowser/navigation-v1.json`, with the fixed offline
fixture scope `issue-browser-fixture-routes-v1`, at most 32 journal entries and
the explicit interaction codec. Search filters, list selection, issue status
mutations and credentials are outside this snapshot.

The application creates an empty session, binds its persistence owner before the
root outlet mounts, and replays through public `OnMounted` after issue loading
settles. Missing, invalid or unavailable storage selects `/issues`; a current
resource guard rejects unavailable issue numbers and applies the same safe
fallback. A newer navigation while storage is pending remains authoritative.

The application posts capture after committed navigation and captures current
interaction state once more before normal close. Its storage owner stages a
bounded file beside the destination, flushes it, then checks the latest accepted
generation at atomic replacement. It retains one in-flight and one coalesced
pending snapshot. Clearing a snapshot uses the same generation fence. Accepted
writes drain during close; a declined close restores navigation admission and
capture. Write errors report finite diagnostic categories, preserve the previous
file, and do not cancel independently accepted issue status operations. Generation
ordering belongs to one application instance; this storage example does not
coordinate concurrent processes.

The implementation lives in
[IssueBrowserNavigationPersistence](../apps/Lucent.IssueBrowser/IssueBrowserNavigationPersistence.cs)
and [IssueBrowserNavigationStorage](../apps/Lucent.IssueBrowser/IssueBrowserNavigationStorage.cs).
Core remains independent of filesystem storage.

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
Hosting service boundary. OS activation, persisted journal history, route transitions
and multiple windows are separate capabilities.
