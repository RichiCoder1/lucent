# Application roots, routing and component companions

Status: owner-approved and refined after Claude Code Fable High adversarial review,
2026-09-14. A0 passed; the owner resumed A1–A7 implementation on 2026-09-20.
The [review and dispositions](application-routing-component-authoring-fable-review.md)
record what changed during design. The [A0 acceptance record](application-routing-component-authoring-a0.md)
now supplies executed compiler, editor and package evidence. Dependent runtime and consumer
migrations are underway. The selected pipeline combines early authored projection, one foreign-generator
preparation pass and semantic signature refinement with exact final-output comparison on
Roslyn 5.9.0 and SDK 10.0.401. Unsupported generator graphs fail without convergence.

## Intended outcome

Make a component recipe/factory the normal application root, expose application lifecycle
configuration on the builder, and remove redundant application-setup adapters. Let .lui
express a routed UI with generated default component mappings and typed customization.
Allow substantial component behavior in .lui and provide an optional, deliberate partial
code-behind contract when splitting a component improves readability.

Owner's north star: a simple application can be authored entirely in .lui except for its
entry point, for now. Treat the entry point as small bootstrap/wiring, not a place to move
all unsupported application logic. A companion .lui.cs file is optional organization;
it must not be mandatory for basic state, properties, handlers, helper types, routes or
application behavior. The final proof must include an app with only bootstrap C# and
no hand-authored C# state, lifecycle-adapter or route-registry helper file.

The owner's example is a design sketch, not a claim about current syntax. Its incidental
unbound identifiers and attribute punctuation are not design decisions. In particular,
the owner has clarified that a recipe/factory root is acceptable and suggested
`.Run(ComponentBrowserApp.Create)` rather than requiring runtime type activation.

## Design baseline evidence

Read-only inspection of main at `f3e4b784661ae96f04c0f9a48ab5b367a94681be` supplied the
September 14 design baseline below. These observations describe that historical checkout,
including adapters subsequently removed during implementation. Current progress and
verification belong in the [execution record](application-routing-component-authoring-execution.md).

### Application root and lifecycle

- Core already supports Run(ComponentRecipe) and Run(IApplicationLifecycle), at
  src/Lucent.Core/Application.cs:150. The recipe convenience uses a no-op lifecycle.
- ComponentBrowserLifecycle constructs the picker/launcher after a session exists and
  returns ComponentBrowserStructure.Create; its remaining lifecycle methods are no-ops
  (apps/Lucent.ComponentBrowser/ComponentBrowserLifecycle.cs:5).
- ComponentBrowserStructure adds surface/fill presentation and mounts the application
  .lui component with theme/services. Removing the adapter must preserve that real
  presentation behavior (ComponentBrowserStructure.cs:5).
- The optional Hosting adapter exposes a separate host builder/root factory/close
  preparation surface (src/Lucent.Hosting/HostedApplication.cs:25). Consolidating author
  setup must retain its existing application service scope and async disposal.
- Build currently requires explicit platform selection; UseWindows selects the adapter
  and window settings, not picker/launcher registrations. The example's omission of that
  setup does not by itself request automatic platform selection (Application.cs:98;
  Lucent.Platform.Windows/WindowsApplicationBuilderExtensions.cs:9).
- Theme already flows through the session and mount environment. Explicit theme props
  can often disappear without moving ownership of application theme (ApplicationSession.cs:101,
  :265; MountContext.cs:40).
- The current fluent recipe wrapper is AuthorRecipe<TCapability>, a readonly value type
  with an implicit conversion to ComponentRecipe; Component is a static factory class,
  not a component-instance base class. AuthorAria<TCapability> also converts to recipes.
  There is no existing Component<T> or common conversion interface to assume
  (AuthoringRecipes.cs:124, :328; Component.cs:4).
- Wrapper-returning method groups do not convert to Func<ComponentRecipe> merely because
  wrapper values convert. Existing tests reject that shape with CS0407 and accept an
  explicit conversion lambda (AuthoringOverloadTests.cs:95). The accepted Create method
  group therefore needs an erased generated factory return or typed factory overloads;
  no runtime activation/reflection is needed.
- Builder callbacks currently use single replacement slots. Ordered additive lifecycle
  callbacks would be a new policy. Synchronous component cleanup separately unwinds in
  reverse registration order and attempts every cleanup (ComponentContext.cs:67;
  ReactiveScope.cs:218).
- Platform transport and the owner context are ready before startup. Hosting starts
  services, creates its application scope/binding, then invokes the root factory.
  Requirements resolve only when Core mounts the deferred root. Preserve that ordering
  and distinguish a services-ready hook from a successfully-mounted hook
  (WindowsBootstrap.cs:182, :248; ApplicationSession.cs:164;
  Lucent.Hosting/HostedApplication.cs:56).

### Routing

- Typed route records, generated codecs/definitions, URI factories, retained outlets,
  route context, guards, cancellation and redirects already exist. Route attributes
  currently accept ModuleType/Template/Id/Parent, with no component association
  (src/Lucent.Core/RouteGenerationAttributes.cs:31).
- Generated modules expose Module, definitions and typed reference factories, not a
  combined Routes value with component mappings (Lucent.Lui.Generator/RouteGenerator.cs:701).
- Component Browser manually pairs table/descriptors with a component factory and guard.
  Its .lui embeds an outlet recipe expression; RouteOutlet.Create is not currently a
  registered .lui component tag (apps/Lucent.ComponentBrowser/ComponentBrowserRoutes.cs:9;
  ComponentDetail.lui:173).
- Routed .lui already reads typed RouteContext<T> and its Parameters. Route values need
  not become implicitly copied component props (ComponentExample.lui:3).
- NavigationSession owns one immutable authoritative table, bounded journal and active
  root outlet. Outlets borrow the nearest session; child outlets consume the parent's
  cursor. Shell navigation controls need access to the same session outside the outlet
  (NavigationSession.cs:23; Navigation/RouteOutlet.cs:32).
- Async preparation already provides Allow/Stay/Redirect/Fail. The synchronous route
  component factory is a separate seam and runs for newly mounted levels. Retained
  identity uses definition identity plus owned captures; changing a resolver closure
  does not automatically rebuild retained levels (RouteOutletContracts.cs:6;
  RouteOutlet.cs:592, :653, :705).
- Live table registration/replacement is not supported. It is distinct from guards and
  choosing a component for a matched route; it would require current-route and journal
  semantics of its own (Navigation/RouteTable.cs:220; NavigationSession.cs:23).
- Every route-record constructor parameter currently appears exactly once in the path
  or query template. A fixed /examples/tables route and an Id constructor parameter
  would need either a capture/query or a parameterless record (RouteGenerator.cs:523).

### Component identity and companion files

- .lui currently generates static Components.Name(...) factories. It does not generate
  a public CLR type named for each component. Run(Type) and the sample's typeof-based
  component association therefore need a new identity contract or another spelling
  (LuiCompiler.cs:3130, :3185).
- Generated component state is a private sealed, non-partial helper with a generated
  name. The containing static Components class is partial. A conventional public
  component partial is a new compiler surface, not only a file-naming change
  (LuiCompiler.cs:3167, :3300).
- ComboBoxExample.lui creates a mount-local ComboBoxExampleModel. Its 59-line C# helper
  holds owner-managed signals and ordinary methods. Suggest returns ValueTask.FromResult;
  its presence does not establish a need for async work outside .lui
  (apps/Lucent.ComponentBrowser/Examples/ComboBoxExample.lui:15;
  ComboBoxExample.cs:8, :34).
- .lui accepts fields and methods, but rejects properties, constructors and nested types.
  The helper's exact class shape cannot be pasted into the file, while equivalent inline
  fields/methods are plausible. Prove concrete unsupported behavior before attributing
  any example split to a language requirement (LuiParser.cs:712).
- Ordinary async Task/ValueTask methods are accepted by the parser and emitted as
  authored methods. The explicit await prohibition concerns synchronous Setup, not
  all component methods. This source finding was not accompanied by a new executable
  async-method probe (LuiParser.cs:718, :901; LuiCompiler.cs:3392).
- C# already supports [ComponentState] sealed partial classes with explicit [State]
  partial get/set properties and synchronous Initialize(ComponentContext). The state
  generator creates owner-attached signals before invoking the hook, but it does not
  currently combine that state with .lui's generated helper
  (Lucent.Lui.Generator/ComponentStateGenerator.cs:20, :273;
  tests/Lucent.Lui.Generator.Tests/ComponentStateGeneratorTests.cs:32).
- Existing state rules differ: .lui constant initializers infer writable state, other
  unmarked expressions infer derived state, [Once] creates a writable initial copy, and
  readonly captures a read-only initial value. C# [State] properties are explicitly
  writable. Sharing a class name alone cannot establish a safe move-between-files rule
  (LuiCompiler.cs:1011).
- Nullable injection currently fails the required-service shape checks. Optional service
  borrowing is an explicit policy extension, not an existing consequence of a question
  mark (LuiCompiler.Requirements.cs:60).

### Existing ownership boundaries

The current application session remains the owner of startup, negotiated close and
terminal cleanup. Preserve owner-thread root creation, one-shot execution, retryable
coalesced close preparation and fatal-error behavior. Stop lifecycle/services, dispose
composition, then dispose the lifecycle scope/host; attempt independent cleanup and
preserve failures (ApplicationSession.cs:397; ADR 0003).

Component state belongs to a mount. A factory produces a reusable recipe; creating the
recipe is distinct from initializing its mount's state. Borrowed services remain owned
by their provider, and [Owned] remains synchronous component cleanup unless a separate
decision explicitly changes it. Accepted application writes outlive a departing route.
Routing preserves prepare/stage/publish/retire, typed closed mappings and retained route
identity, with no automatic route/component DI scopes (ADRs 0005, 0008 and 0009).

These are current contracts to preserve while simplifying the author-facing model.
Any proposed change to them must be surfaced as a decision, not hidden in syntax sugar.

## Interview decisions

### Round one: accepted

1. Root input is a recipe or typed factory. The owner explicitly accepts
   `.Run(ComponentBrowserApp.Create)` as the desired shape. Lifecycle methods belong
   on the application builder; no mandatory runtime Type activation is requested.
   The named Create entry point is a target design, not an API that already exists.
2. Routing exposes two separate typed customization seams: navigation decisions and
   destination rendering. Generated component mappings provide the default rendering.
   This does not itself approve live route-table mutation or remount-on-every-prop-change.

### Round two: accepted

3. Component companion: one named partial component shared by .lui and optional .lui.cs,
   with a generated Create factory and distinct state per mount. No companion file is
   required. The owner selected this over a factory plus separate partial state model.
4. Navigation owner: a Router parent owns/provides the session and RouterOutlet consumes
   it. Advanced callers can supply an existing session; that session is borrowed rather
   than silently transferred. Shell controls share navigation outside the outlet.
5. Optional injection: nullable service declarations borrow an available service or
   receive null. Non-nullable injection remains required. Optional resolution must not
   hide provider construction failures or transfer ownership. This extends ADR 0009's
   required-only restriction, not its application-service lifetime boundary.

### Round three: state and dynamic behavior

6. Accepted: keep concise inferred state declarations in .lui and explicit [State]
   partial properties in ordinary C#. Both portions access one mounted state instance.
   Ordinary C# fields remain ordinary C# fields. Moving a declaration between files must
   preserve its writable/derived/snapshot semantics explicitly; sharing a type does not
   authorize source rewriting that changes C# field semantics.
7. Accepted: one framework-invoked imperative setup hook, implemented in either file,
   after owner/requirements are ready and before UI creation. No authored component
   constructors. Async work remains explicit and cancellable. Defining the hook in both
   portions must not accidentally execute initialization twice.
8. Accepted: react to destination-selection changes at the same URI. Retain state while
   the resolved component type/key is unchanged; replace the destination when that
   identity changes, without adding a history entry. This extends current mount-only
   route-factory evaluation and requires explicit implementation/retention proof.
9. Accepted: a fixed validated route table with dynamic guards/rendering. Live route
   pattern additions/removals and their active-route/history policy are outside this work.

Owner addition: .lui-only simple applications, with only the entry point remaining in C#,
are the acceptance target. This requires addressing the remaining supporting-declaration
and route-authoring gaps, not only giving existing C# helpers a companion filename.

The shared partial declaration contains each state/member definition once. Code in either
file can use members contributed by the other; do not create a second companion state
object or force component authors to pass the same state back into their own markup.
The compiler and C# state generator need a shared semantic plan for owner attachment,
reactive backing, requirement resolution and diagnostics. This is a new integration over
the existing retained runtime, not an already available partial association.

### Round four: accepted

10. Ordinary C# declarations belong alongside components in .lui: route records, attributed
    route modules, models and helper types. Reuse C# syntax and the existing typed route
    model; do not introduce a second route declaration language. Supporting types have
    ordinary C# semantics, including ordinary constructors, rather than component state
    inference or mount ownership.
11. Navigation guards run for navigation only. Reactive destination replacement at the
    same URI is an ordinary UI change and does not run guards. Applications must express
    approval-sensitive changes through navigation; a rendering hook is not an authorization
    boundary or a promise to preserve a departing destination's local state.
12. Lifecycle callbacks compose additively. Startup runs in registration order; stop and
    cleanup unwind in reverse. Any close-preparation callback may decline a close. A later
    registration must not silently replace an earlier callback.

### Round five: accepted

13. Allow support-only .lui files, retaining at most one component
    per file. Types may be colocated with UI or organized into their own files. Additional
    namespace forms, top-level executable statements and a new entry-point model are not
    part of this extension. Owner refinement: do not encourage support-only .lui files
    unnecessarily when an ordinary .cs file provides materially equivalent organization
    and behavior. The goal is capability and choice, not a .lui file-count target.
14. Ordinary types declared in .lui participate in
    external .NET source generation from the first release, including AOT JSON models.
    Restricting initial support to Lucent's generators would retain a reason to require
    C# files and does not satisfy the accepted compatibility target.

Owner's subsequent toolchain decision: newer libraries or .NET versions, including .NET 11,
are allowed where useful or necessary. Prefer a simpler supported generation architecture
over preserving an old compiler-library pin through extra build machinery. Select and
document concrete versions in A0; this permission removes a design constraint and does not
itself perform an upgrade in this design workspace.

## Scope of ordinary C# declarations

This is a bounded language expansion with meaningful compiler/tooling work. The parser
change is the smaller part; project-wide binding and generator visibility are the main
cost. Reuse Roslyn parsing for classes, records, structs, interfaces and enums, including
their normal members, attributes and type parameters. They live in the authored namespace,
outside generated component containers. The initial proposal preserves the current .lui
namespace/import conventions and one-component limit. Component-only restrictions such
as inferred state and the ban on authored instance constructors do not apply to helpers.

Current main already stores ordered top-level nodes, but only knows component/style nodes;
it requires a component and emits only component-bearing documents. Its shared component
index produces signature-only stubs and binds them before component bodies. Merely copying
types into final output leaves references unresolved during those earlier steps. Introduce
separate authored-declaration and component-signature projections, with cross-file partial
binding, duplicate diagnostics and dependency-aware invalidation. A declaration edit or
removal must invalidate affected consumers without stale generated output. Evidence:
LuiSyntax.cs:114; LuiParser.cs:131, :2224; LuiCompiler.cs:98, :3098;
compiler/LuiProjectContext.cs:50, :90, :206, :380.

LUI enters generation through AdditionalTextsProvider, whereas the route and C# state
generators use SyntaxProvider. Ordinary final source output is not a mechanism for passing
declarations between these generators in the same pass. Lucent needs a shared declaration
pipeline; route and state APIs used by .lui must be represented before body binding.
Do not run a generator repeatedly until it happens to converge. Evidence:
LuiGenerator.cs:93; RouteGenerator.cs:62; ComponentStateGenerator.cs:59.

Roslyn now documents an experimental RegisterPreCompilationSourceOutput phase. It accepts
non-compilation inputs and makes emitted sources visible to later generator phases,
which is a promising fit for projecting authored types from .lui. It cannot depend on
semantic compilation or syntax-provider results. This is an option to evaluate, not a
feature assumed available in Lucent's pinned toolchain. Sources consulted 2026-09-14:
[Roslyn feature design](https://github.com/dotnet/roslyn/blob/main/docs/features/pre-compilation-source-outputs.md),
[API proposal and experimental status](https://github.com/dotnet/roslyn/issues/83089),
and [API reference](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.incrementalgeneratorinitializationcontext.registerprecompilationsourceoutput?view=roslyn-dotnet-5.9.0).
The reference identifies a 5.9.0 package and prerelease caveat; neither that URL nor a
package upgrade proves compatibility with the compiler hosting the analyzer. Main pins
Roslyn 5.0.0 and SDK 10.0.401. Read-only local assembly metadata inspection found that the
5.0.0 package lacks this API while the installed 10.0.401 SDK's compiler assembly is
5.9.0.0 and contains it. Thus the build host offers a potential path, but the generator's
compile-time API, language-server workspace and tests remain on the older library pin.
This is presence evidence, not an executed compatibility proof. A0 must choose and verify
the supported build/editor/compiler matrix and document changed package/SDK minimums.

Microsoft's [.NET 11 download page](https://dotnet.microsoft.com/en-us/download/dotnet/11.0)
lists RC1, SDK 11.0.100-rc.1, as a go-live release dated September 8, 2026. Its
[support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
lists RC1 support through October 13, 2026. These were verified on September 14; refresh the
release pin when executing A0. The owner's approval allows this candidate baseline. A
go-live SDK does not change the experimental status documented for a particular Roslyn
API. Prefer evaluating aligned newer Roslyn libraries and the pre-compilation phase first;
keep the SDK projection route below as a fallback. An SDK/compiler upgrade and a net11.0
runtime target are separate decisions: change the application target when the chosen
capabilities require or materially benefit from it, not just because the build SDK changes.

For the existing toolchain, evaluate an SDK step that projects authored declarations into
normal Compile inputs before C# generation. The asset SDK already generates a .g.cs file
and includes it as Compile input (Sdk/Assets.targets:92). Reuse the build integration
pattern, not the asset parser. Design-time builds and the language server must use the
same projection contract; exactly one pipeline owns each declaration's final emission.
Neither approach may require moving AOT model annotations into hand-authored C# while
claiming that ordinary .lui types are fully supported. Version changes are permitted for
the eventual work; no compiler upgrade or build-step implementation has been made here.

There are two distinct interoperability proofs: an external generator must see an authored
.lui model, and .lui code must be able to use the resulting generated API. Early declaration
projection addresses the first; it does not automatically make ordinary generator outputs
available to LUI's semantic binding in the same pass. A0 must also demonstrate an annotated
JSON context and a component handler using its generated members. Options must account
for when ordinary C# can defer validation to final compilation and when Lucent needs
symbols earlier to classify state, bind tags or lower expressions. Lucent-owned route/state
generation can share a semantic model, but that alone does not prove external-generator
compatibility. Do not accept only a C# caller proof and label the complete path solved.

The editor already projects generated documents and maps copied C# tokens to their source.
The formatter already uses Roslyn member syntax, recursive C# layout and lexical preservation
checks. Extend those shared paths with top-level declaration boundaries rather than adding
an unrelated formatter or editor parser. Evidence: language-server/LuiProjectContext.cs:1334,
:1929; LuiDocumentLayout.cs:39; LuiCSharpLayout.cs:36; LuiSourceComparison.cs:22.

For cold-workspace coverage, reuse the asset SDK's existing workspace-probe approach
(tests/Lucent.Lui.Sdk.Fixtures/AssetWorkspaceProbe/Program.cs:13). LUI's editor currently
removes additional .lui documents and adds manual projections to avoid double generation
(language-server/LuiProjectContext.cs:2635). Any new early projection must replace stale
disk declarations with unsaved editor content, avoid publishing duplicate types, and
remove declarations when inputs disappear. Cover cold build/workspace, unsaved changes,
cross-language rename and file deletion with no prior generated artifacts.

## Proposed authoring shape

The following is target syntax, not currently compiling sample code. The essential names
are ComponentName.Create, Router, RouterOutlet and the existing route attributes. Other
new API spellings remain subject to the bounded compiler proof.

```csharp
// Application.lui
namespace Example;

[LucentRouteModule(RouteFallbackPolicy.Reject)]
public static partial class AppRoutes { }

[LucentRoute(typeof(AppRoutes), "/", Component = typeof(HomePage))]
public readonly record struct HomeRoute;

public record Person(string Name);

public component Application() {
    <Router routes={AppRoutes.Routes} initial="/">
        <RouterOutlet />
    </Router>
}
```

```csharp
// HomePage.lui
namespace Example;

public component HomePage() {
    readonly Person person = new("Alex Example");

    <Text>{person.Name}</Text>
}
```

```csharp
// Program.cs: bootstrap/platform wiring, with normal namespace imports.
LucentApplication.CreateBuilder()
    .UseWindows()
    .Build()
    .Run(Application.Create);
```

Generated Component metadata binds a named component's Create factory statically; it does
not activate a runtime Type. An automatically mapped destination must be callable without
required explicit props. Typed route values remain available through RouteContext<T>;
services use inject and shared application state uses context. Destinations requiring
additional props use the explicit rendering hook, with clear diagnostics when no usable
default mapping exists. No name-based copying from route-record members into props.

Optional companions use the same namespace and partial type, with no separate state object:

```csharp
// Counter.lui.cs: proposed companion form, optional.
namespace Example;

public sealed partial class Counter {
    [State(0)] public partial int Count { get; set; }

    void Increment() => Count++;
}
```

Counter.lui supplies the component and can read Count/call Increment directly. The compiler
supplies the partial component identity and factory. This is not an instruction to apply
[ComponentState] to a second helper class. Factory creation stays deferred; every mount
creates its own state and resolves its own borrowed requirements.

## Contracts to prove before migration

### Shared state and initialization

The LUI component pipeline is the sole emitter of a component's constructor, reactive
property implementations, setup bridge and Create implementation. It consumes companion
[State] declarations directly through the shared semantic model. The separate C# state
generator remains for standalone [ComponentState] types; applying [ComponentState] to a
component is an authored diagnostic. Never let two generators emit competing constructors
or initialize separate state objects for the same component.

Framework-managed initialization has a deterministic order: attach the mount owner and
resolve requirements; initialize companion [State] cells in ordinal property-name order;
evaluate .lui declarations in existing source order; invoke setup once; create UI. Companion
state initializers retain the existing constant/static ComponentContext factory contract
and cannot read this component's not-yet-initialized instance state. .lui initializers may
read initialized companion state. Shared imperative initialization belongs in setup.
Detect premature/cyclic reads rather than claiming that all possible cycles disappear.

Ordinary companion C# field initializers retain normal CLR construction timing, before
the generated constructor body. They are outside the managed-state ordering promise and
must not require already-attached services/state. Do not weave or reorder them. The deferred
mount transaction owns registered resources and rolls them back if managed initialization
or setup fails; ordinary unregistered resources do not acquire hidden cleanup semantics.

Use one generated declaring hook, partial void Setup(ComponentContext context), with an
implementation contributed by either the .lui Setup block or the C# companion. Existing
Setup()/Setup(owner) source forms lower through a bridge preserving their owner alias;
they do not create a second hook or lifecycle. This uses the existing ComponentContext
facade for the new companion contract rather than requiring authors to use ReactiveScope.
Missing setup is valid. Duplicate implementations, inconsistent accessibility, duplicate
members and generated-name collisions report at authored locations. Internal generated
members are reserved implementation details, not supported companion API.

Companion association is by namespace/type identity, not file suffix; .lui.cs is an
organizational convention. The language server's existing synthetic generated.lui.cs
documents must not be mistaken for authored companions.

Writable, derived and snapshot declarations retain their meanings when accessed across
files. C# fields stay ordinary fields. Moving state into a companion may require a [State]
property or an explicit initializer/setup representation; promise semantic equivalence,
not arbitrary text movement. Normal component methods, properties and async handlers
should be representable in .lui. Async handlers use the existing cancellation and owner
posting rules; setup does not become implicit async initialization. Add positive and
negative fixtures for the actual ComboBox helper behavior rather than assuming all C#
constructs are supported because Roslyn can parse them.

### Factory identity and migration

Publish early named-component identity declarations along with ordinary supporting types,
so typeof(HomePage) is a real symbol when route metadata binds. A0 should prove a declaring
extended partial factory, for example public static partial ComponentRecipe Create(),
paired with exactly one later implementation. It must not publish both an ordinary stub
body and a duplicate final method. Preserve parameter, accessibility and capability metadata
in the declaration projection; missing/conflicting implementations remain final errors.

Bind tags to a resolved factory symbol and emit a qualified call. Named component tags
target Type.Create(...); existing static [LucentComponent] factories remain supported for
stock Core and C# authoring. Do not emit an unqualified Name(...) that can be shadowed by
the new named class, or silently prefer a type because it shares a tag's spelling. Diagnose
real candidate ambiguity. Migrate each project's generated-factory callers atomically;
keep both factory shapes as intentional authoring contracts, not duplicate registrations
for one component. Source-map and rename identities follow the resolved authored symbol.

Create returns ComponentRecipe at the public factory boundary, preserving its proven
method-group conversion. A direct zero-argument root method group requires an actual
zero-parameter signature; parameterized roots can use an explicit lambda. Default route
mapping validates a statically callable no-prop factory, including any deliberate default
argument adaptation, and diagnoses required props rather than inventing values. No runtime
Type activation is introduced.

### Declarative navigation

Router creates and provides a session, or borrows an explicitly supplied session. Supplying
both conflicting session/table inputs is a diagnostic or clear construction error. Session
creation and initial navigation happen once per Router mount. Replacing a table requires an
explicit owner remount/new session; do not invent live registration through a reactive prop.
Nested outlets consume the current route cursor and retain existing single-root-participant
rules. Shell controls can read the session above the outlet.

Generated Routes is one stable immutable bundle of its table, descriptors and default
mapping, preserving the session/outlet's exact table identity requirement. A stock Core
RouterOutlet selects root versus nested behavior using its internal cursor lookup. This
does not add author-facing optional context or create another navigation session. Invalid
initial locations follow the existing explicit reject/fallback policy and mount rollback.

Navigation preparation preserves cancellation, latest-intent handling and existing
prepare/stage/publish/retire behavior. Loading remains navigation pending state, not an
implicit async rendering factory. Destination selection is synchronous and tracks relevant
reactive reads. Its result carries stable component identity and an optional key; delegate
allocation is not identity. Unchanged selection preserves local state. A changed identity
replaces only the affected destination subtree without journal mutation or navigation
guards. A typed selection value carries component identity, key and explicit wrapper
identity. Reject raw anonymous recipe results at the authoring boundary where possible;
never guess identity from a delegate or recipe allocation. A missing key differs from
an explicit key, and key equality must be stable for the mounted selection.

Preserve ordinary construction-time prop semantics. Selecting the same component with a
new recipe object must not imply a new general-purpose reconciliation engine. Values that
need live updates use existing readers/context; use a changed key to request replacement.
Same-URI replacement stages only while the navigation session is idle. During preparation,
staging or publication, mark the affected level dirty and coalesce selection changes without
using navigation's single staged slot. Re-evaluate the latest selection after the session
returns to idle following success, veto, failure or cancellation. Disposed/replaced levels
discard pending work; newly mounted levels evaluate fresh. Preserve root-only interaction
coordination while supporting replacement below a retained parent/nested cursor.

Stage replacement before retiring the committed subtree and validate session/selection
generation immediately before publication. A failed stage never publishes partial UI or
silently drops the previous view; existing failure policy determines subsequent recovery
or termination. Track selection reads separately from recipe construction so construction
side effects do not become selector dependencies. Schedule invalidation through the existing
bounded reactive drain and fail/report a feedback loop instead of recursively replacing to
convergence. Test a mount that writes a selector dependency, nested disposal, and a pending
selection change followed by navigation veto as well as successful navigation.

### Builder lifecycle and optional services

Configuration remains inert. Startup establishes the platform owner context and services,
then invokes ordered startup callbacks, creates the root recipe once, applies registered
root contributions/service binding and mounts it. A
successfully-mounted notification, if exposed, must be distinct from services-ready startup.
Keep platform selection explicit and Core independent from optional Hosting/container APIs.
The optional Hosting adapter configures the same builder lifecycle rather than requiring
a second author-facing root/lifecycle object.

Root decoration is a framework phase, not another required authored root adapter. Hosting
attaches its lifecycle-owned binding through the existing Attach contract. Core-only
startup can explicitly contribute typed root context and one closed service source for
inject; typed context provision does not secretly register an injectable service. Source
ownership is explicit and duplicate root service bindings fail clearly. This gives the
Windows picker/launcher a services-ready registration point while preserving application
scope, root presentation and eventual source revocation. No general service locator or
automatic component/route DI scope enters Core.

Proposed builder hook names are OnStart, OnPrepareClose, OnStop and OnDispose, each returning
the builder and appending a callback. OnStart establishes application behavior without
returning a root recipe; Run owns that separate input. OnPrepareClose returns the existing
typed close decision. Async phases use explicit cancellation and ValueTask contracts.
OnStop runs before composition disposal; OnDispose is terminal cleanup after composition,
while the application scope remains available until its final disposal. Exact public
delegate/context signatures must preserve the current lifecycle's available capabilities
without exposing a general service locator. A static helper declared in .lui may supply
these callbacks, so Program.cs only wires them and the chosen root factory.

Close preparation uses an ordered snapshot and can stop at the first veto, but every
invoked callback must have a way to undo temporary quiescence. Supply an attempt-scoped
declined callback registration: preparation registers restoration when it suspends write
admission; on a veto, invoke these callbacks in reverse order before returning false.
The vetoing participant's restoration is included. Running every prepare callback and
aggregating booleans alone would not restore admission. This notification does not itself
replay/cancel writes or decide service-specific recovery.

Preserve coalesced close requests and retry after restoration. Escaping preparation or
restoration failures retain ADR 0003's terminal policy. Fatal cancellation bypasses ordinary
decline recovery; stale completion cannot reopen services during terminal cleanup. Accepted
writes remain service-owned. Cancellation or failure is never treated as approval to close.

Startup tracks cleanup registrations as resources are acquired, including inside a callback
that later throws. Each startup participant can register its paired stop/disposal cleanup;
later unentered participants have none to run. Independently registered builder terminal
callbacks are explicitly unconditional and receive startup outcome information, rather than
being implicitly paired by list position. Cleanup registration order governs reverse unwind
within each phase, and failures do not suppress independent cleanup. Preserve the phase
boundaries: stop lifecycle/services, dispose UI, then dispose application scope/host.
No notification claims the root mounted or a startup callback completed when it did not.

Nullable injection adds optional lookup to the service-provider boundary; missing services
yield null while construction exceptions remain failures. Required injection still fails
clearly. Preview and service-free tests can mount components lacking optional services;
design mode must not silently waive required services or take ownership of borrowed ones.

Carry optionality in requirement metadata and through source lookup, service binding and
the no-binding mount environment. No binding and an absent optional service both yield null;
a stopped/revoked binding, wrong-owner access or provider failure remains an error. An
optional-source adapter must distinguish absence explicitly and must not implement lookup
by catching arbitrary exceptions from required Resolve. Preserve diagnostics and the source's
closed-type/ownership contract when evolving IComponentServiceSource.

## A0: decisive compiler and ownership proof

The review's disposition is ready for A0 only. Its proposed identity stubs, single emitter
and qualified support for both factory shapes are adopted above. The early projection
must contain helper types and component identities/member signatures, not just route
records. Bodies/initializers whose typing depends on generated members are an explicit
part of the proof, not a tolerated semantic blind spot.

Evaluate a compatible pre-compilation projection first. If it cannot supply generated
symbols at LUI's required semantic stage, evaluate a bounded preparatory generator pass:

1. Build the authored declaration/stub compilation from current C# and .lui inputs. Run
   non-LUI generators once with LUI lowering disabled. This is a generator-driver/preparation
   pass, not a successful assembly emission; required partial implementations intentionally
   arrive later. Do not accept a deliberately failed C# build as the prepass implementation.
2. Feed captured generated trees into LUI's binding compilation only. Do not add them as
   duplicate final Compile inputs or change the compilation visible to ordinary generators.
   The final generation/compilation emits each type/member once and must have no suppressed
   final errors. Own projected declarations and final implementations explicitly.
3. Compare the preparatory and final non-LUI generated outputs by generator identity,
   hint name and deterministic source content. A mismatch fails with a diagnostic and
   minimal reproduction; it never schedules another pass. Track all source, additional-file,
   option, analyzer-version and reference inputs in cache identity. A graph requiring
   cyclic/body-dependent generation beyond these stages is a diagnosed unsupported graph,
   not silent stale binding. This is a feasibility contract to test, not a proven engine.

The bound is one preparatory and one final non-LUI generation pass, with no generator
recursively invoking itself and no iterate-until-stable driver. The editor uses the same
logical projection and pass boundaries against unsaved buffers. Canceled results cannot
replace a newer document version. Measure generation and diagnostic latency for cold
startup and repeated declaration/body edits, cache reuse and invalidation; prevent a full
uncancelable generator run from blocking every keystroke. Document the selected analyzer
execution/trust boundary using the existing project tooling policy.

The review's alternative of tolerant expression binding with an approved degradation list
is not accepted as a release solution: the owner requested first-class interoperability.
Moving annotations into .cs, requiring save/rebuild, skipping state classification or
accepting false editor diagnostics fails A0. Ordinary final C# validation remains useful,
but it cannot replace symbols that Lucent needs for state/tag/requirement semantics.

Use two explicit fixture variants so the optional companion does not contradict the
bootstrap-only C# acceptance requirement:

- All-.lui variant: model and JsonSerializable context, route module/record and component
  association, component state initializer plus handler using generated JSON members,
  generated route API use, and a Program.cs that passes the generated Create method group.
  No hand-authored C# bridge, state/registry/helper file or handwritten generated API.
- Companion variant: equivalent component behavior split into .lui plus a C# partial with
  [State] and a method used by markup. Verify two independent mounts, cross-file access,
  setup exactly once, initializer failure cleanup and diagnostics for duplicate setup or
  [ComponentState] misuse. Also keep a stock static-factory tag beside the named component.

A0 passes only when a clean build succeeds in one invocation without prior generated
artifacts; the real external generator is consumed in both directions, including state
classification; unsaved edits bind generated APIs correctly; removing a supporting file
removes its symbols with only expected authored diagnostics; duplicate types are absent;
the companion/method-group proofs pass; and a packaged SDK consumer publishes and executes
the routed fixture under NativeAOT. If the bounded pass is selected, matching-output
assertions and deliberate mismatch diagnostics are part of the fixture.

Record exact versions, commands/exits, positive discovery, generated-output ownership,
intermediate-diagnostic classification, cache keys and cold/warm editor measurements.
The local API-presence metadata observation and Fable's source-only review are not substitute
evidence. A1-A7 remain blocked on failure. Try a bounded alternative within the accepted
model where useful; return to the owner only if a concrete minimal failure requires a
product-scope change. A0 success refines the technical implementation contracts and
unblocks the existing sequence without another broad design interview.

## Proposed work sequence

The authorized handoff slices are now tracked by [parent #301](https://github.com/RichiCoder1/lucent/issues/301)
and [A0–A7 #302–309](application-routing-component-authoring-execution.md). A0 is complete and A1–A7 execution resumed on 2026-09-20;
downstream slices retain their native prerequisites. The
[handoff](../../advisor-plans/application-routing-component-authoring-handoff.md) records the
approved scope and review dispositions. The [A0 record](application-routing-component-authoring-a0.md)
links final executed evidence and the historical experiments that informed selection.

| Slice | Deliverable and decisive evidence | Depends on |
| --- | --- | --- |
| A0 | Prove authored-declaration visibility, companion identity and method-group root on supported compiler/editor hosts; external generators consume .lui types and .lui code uses generated APIs. Record chosen pipeline and cost. | Review resolved; first actionable slice |
| A1 | Shared document/declaration projection, ordinary C# types, support-only files, cross-file binding and correct invalidation. | A0 |
| A2 | Named partial components, Create, shared state/requirements, properties and setup diagnostics; two independent mounts plus equivalent inline/companion behavior. | A1 |
| A3 | Builder root factory and ordered lifecycle hooks, unified optional Hosting integration; startup/close/failure/disposal tests preserve existing contracts. | A0; A2 for generated-root proof |
| A4 | Route component mappings and Router/RouterOutlet authoring, including route declarations entirely in .lui and shell navigation access. | A1, A2 |
| A5 | Reactive destination identity and separate navigation preparation hooks; nested retention, same-URI replacement, failure and stale-work proofs. | A4 |
| A6 | Cross-language editor navigation/rename/diagnostics, source maps, formatter and lint compatibility. Integrate each earlier slice as it lands. | A1 through A5 |
| A7 | Migrate Component Browser and a small all-.lui app; remove proven redundant adapters, document bootstrap and companion patterns; package-consumer NativeAOT execution. | A2 through A6 |

The declaration pipeline and named-state integration are the largest compiler risks;
reactive outlet replacement is the principal runtime risk. Builder convenience and tag
adapters are smaller once those foundations are established. Top-level types share much
of A1/A2's required projection work, but external generator interoperability can enlarge
A0/A1 materially. No elapsed-time estimate is claimed before that focused proof.

The final acceptance app must contain models, route definitions, component state, event
handlers and application behavior in .lui, with only platform/service/bootstrap wiring in
Program.cs. The companion example is an additional organizational option. NativeAOT proof
must publish and execute a package consumer; source inspection and generated-code snapshots
alone are insufficient. Choose focused tests by affected behavior; the design-only work
recorded here does not require application builds or runtime checks.

[ADR 0011](../adr/0011-named-components-and-declarative-application-composition.md) records
the accepted identity and ownership direction. Q1 through Q14 are answered and the owner
has confirmed shared understanding. Independent review is complete and its dispositions
are incorporated. A0 resolves compiler feasibility before broad implementation; this
review does not claim the feature or its compiler approach has already been proven.
