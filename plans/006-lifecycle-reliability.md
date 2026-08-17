# Plan 006: Close lifecycle, failure, accessibility, and UI-test gaps

> **Executor instructions**: Complete Plans 001–005 first. This is the dogfood
> reliability gate, not a general reactive-framework project. Reuse the owner,
> conditional regions, native controls, and single Workbench headless harness.
>
> **Drift check (run first)**:
> `git diff --stat 3b27cfe..HEAD -- src/Lucent.Compiler src/Lucent.Runtime examples/workbench tests/Lucent.Workbench.Tests tests/Lucent.Runtime.Tests docs`
> Stop if generated computed fields no longer match Plan 002, fragments/owners no
> longer match Plans 001/003, or Plan 005's headless harness is absent.

## Status

- **Priority**: P1
- **Effort**: L
- **Risk**: HIGH
- **Depends on**: Plans 001–005
- **Category**: correctness / accessibility / tests
- **Planned at**: commit `3b27cfe`, 2026-08-16

## Why this matters

Workbench can now compose native desktop controls, but release confidence still
depends on deterministic async ownership, structural failures, durable settings,
accessible semantics, and actual keyboard flows. Centralize only the repeated
async mechanism and prove user-visible contracts in the existing headless app.

## Starting contract from earlier plans

- Plan 001's `ComponentOwner` owns cancellation, child cleanup, UI dispatch,
  LIFO disposal, and aggregate cleanup failures.
- Plan 002 emits cancellation/generation/value/pending/error fields per
  `Computed<T>` source. `.Value`, `.IsPending`, and `.ErrorMessage` share one
  reactive source identity; Package Pulse uses conditionals for status.
- Plan 003 changes all mounted roots/regions to `Fragment` and keeps generated
  component owners private.
- Plan 004 uses native ICommand/focus/platform APIs and adds attached automation
  properties. Workbench App owns a lifetime token and root component disposal.
- Plan 005 uses native observable collections and AvaloniaEdit and creates the
  one dedicated-thread `tests/Lucent.Workbench.Tests` headless harness.

This plan intentionally adds **no** general effect API, hook ordering, DI
container, `INotifyCollectionChanged` adapter, or Avalonia observable bridge:
Workbench has no unmet use case for them. Generated native event handlers are
already owner-unsubscribed; ListBox controls consume ObservableCollection
directly; DocumentSession owns its explicit editor events. Add a lifecycle
primitive later only when an application cannot express its cleanup through
those existing routes.

## Runtime API evolution

### Root error reporting

Evolve (do not replace) Plan 001's owner surface:

```csharp
public sealed class ComponentOwner : IDisposable
{
    public ComponentOwner(IUiDispatcher dispatcher);
    public ComponentOwner(
        IUiDispatcher dispatcher,
        Action<Exception>? reportUnhandled);

    // Existing members remain unchanged.
    public ComponentOwner CreateChild();
    public void ReportUnhandled(Exception error);
}
```

- Keep the one-argument constructor as a real metadata overload delegating to
  the two-argument form; an optional parameter alone is not binary-compatible.
- Child owners inherit the same root reporter; no child reporter or service
  locator is added.
- `ReportUnhandled(null)` throws `ArgumentNullException`. Before disposal it
  invokes the root reporter synchronously on the UI lifecycle call. If omitted,
  it rethrows the exception with preserved dispatch information. Reporter
  exceptions propagate; the owner must not recursively report them.
- Calls after disposal are ignored because stale async/event paths must not
  resurrect a dead application.
- Plan 001 disposal still executes every cleanup and throws one aggregate.
  Workbench App retains the root `Action<Exception>` separately, passes it into
  the generated root, and invokes that retained delegate—not the now-disposed
  owner—for a disposal aggregate or a save fault observed after shutdown.
- Generated root constructors add `Action<Exception>? __lucent_reportUnhandled`
  after the optional dispatcher. WorkbenchApp also declares required authored
  `Action<Exception> errorReporter` input. App passes the same retained delegate
  as that input and as `__lucent_reportUnhandled`; authored command fields and
  late task observers use the input because reserved generated names are not
  author-visible. Nested component constructors still receive a parent-created
  owner and cannot replace the reporter. SettingsDialog and
  GeneratedPreviewWindow pass the retained reporter to their independently
  mounted generated roots.
- Generated native event delegates catch application exceptions and call their
  owner reporter. Workbench `DelegateCommand` receives the same reporter and
  catches command-body exceptions because KeyBinding can invoke it without a
  generated event wrapper.

### One owner-bound async computed node

Add exactly one runtime type:

```csharp
public sealed class OwnedComputed<T> : IDisposable
{
    public OwnedComputed(
        ComponentOwner owner,
        T initialValue,
        Func<CancellationToken, Task<T>> factory,
        Action invalidate,
        Action<Exception>? reportUnhandled = null);

    public T Value { get; }
    public bool HasCommittedValue { get; }
    public bool IsPending { get; }
    public Exception? Error { get; }
    public string? ErrorMessage { get; }
    public void Refresh();
    public void Dispose();
}
```

Semantics:

1. Construction stores the placeholder `initialValue`, starts no work, and
   registers one idempotent disposal with the owner. `HasCommittedValue` is
   false even though `Value` can return the typed placeholder.
2. `Refresh` is a UI-lifecycle operation. It increments one generation,
   cancels/disposes the previous linked CTS, clears `Error`, sets `IsPending`,
   installs the replacement CTS, and invokes/captures the factory task with a
   token linked to the owner token **before** invalidating pending UI. It then
   calls `invalidate` once. `Refresh` after disposal is a no-op. Synchronous
   factory throws become an already-captured normal failure before invalidation.
3. Completion reaches state only through `owner.Dispatch`. Generation, token,
   and owner-disposed checks suppress replaced/disposed success and failure.
4. Success stores Value, sets `HasCommittedValue`, clears pending/error, and
   invalidates once. Failure preserves the last value/commit flag, stores the
   original Exception, clears pending, invalidates once, then calls the optional
   unhandled reporter. State is fully committed before either callback. Only
   `OperationCanceledException` observed while the
   captured linked token is cancelled is cooperative cancellation; another
   OperationCanceledException is a failure.
5. Refresh therefore shows loading before the first commit, while a refresh
   keeps stale committed content. A failure is inspectable without converting
   exceptions to strings; `ErrorMessage` remains a compatibility projection.
6. Dispose is idempotent, cancels/disposes the current CTS, and suppresses all
   later commits.

Implementation must separate factory execution from completion: catch only a
synchronous factory throw or the awaited factory task failure. Never place the
UI commit, `invalidate`, or reporter call inside that catch, because inline
dispatch could otherwise catch and report a reporter/fallback exception twice.
OwnedComputed catches an `invalidate` failure and sends that distinct update/
mount error once to `owner.ReportUnhandled`; the generated callback does not
also report it. Pending invalidation happens only after the replacement factory
task/failure is captured, so even a throwing reporter cannot strand pending state
without running work. On completion, commit state first, attempt invalidation,
then attempt the optional source-failure reporter. If both errors require
reporting, attempt each exactly once; collect and throw one AggregateException
of reporter-thrown exceptions only after both attempts. Never treat a reporter
exception as a source failure.
Replacement cancellation also completes cleanup deterministically: capture any
exception from `oldCts.Cancel()`, always dispose that CTS in `finally`, install
and invoke the new generation, then report the captured cancellation-callback
failure once through `owner.ReportUnhandled`. A reporter throw may propagate only
after the replacement is valid and running. Add the same exhaustive cleanup
test shape as Plan 001; a throwing cancellation callback must not strand refresh
halfway or leak the old CTS.

Delete equivalent generated CTS/generation/pending/error boilerplate. Keep
Plan 002's compile-time dependency graph and generated targeted invalidation;
do **not** add a runtime sync graph, observer graph, batching, or resource type.

## Exact structural async failure syntax

Add one bounded UI-region form inside a native content host:

```csharp
try (packages) {
    ContentControl {
        if (!packages.HasCommittedValue) {
            ProgressBar { AutomationProperties.Name: "Loading packages"; }
        } else {
            PackageResults(items: packages.Value) {}
        }
    }
}
catch (Exception error) {
    ErrorPane(message: error.Message, retry: retryCommand) {}
}
```

Rules:

- The parenthesized expression must resolve to exactly one declared
  `Computed<T>` identifier. Catch is required, has no filter/finally, must name
  `System.Exception`, and introduces one catch local through the existing
  C#-island local-symbol path.
- Add `UiAsyncBoundarySyntax` and `BoundAsyncBoundary`; retain exact source,
  source-identifier, catch-type/name, content, fallback, and total spans.
- Reuse Plan 003's fragment-capable `ConditionalRegion`: source `Error == null`
  selects content, non-null selects fallback. All mount/publication rollback,
  branch child ownership, current-input, and target-refresh semantics stay the
  same. Do not add `ErrorBoundaryRegion`.
- The source itself is the region dependency. Refresh clears Error and remounts
  content; failure mounts fallback; retry calls that source's generated Refresh.
- A `.Value` UI read must be lexically inside the content branch of
  `try (thatSource)`. Diagnose unguarded or differently guarded value reads at
  the occurrence span. Status reads may occur outside the boundary.
- If a source has at least one valid boundary, its `OwnedComputed` receives no
  unhandled callback and every boundary renders that source's failure. A source
  with no boundary receives `owner.ReportUnhandled`. This prevents duplicate
  reporting while keeping otherwise-unhandled work visible.
- Nested boundaries are lexical and source-specific: an inner source failure
  switches its inner region; an outer source failure disposes the whole inner
  branch. Errors thrown while mounting/updating fallback content or by native
  events are not recaptured by that same boundary and reach the root reporter.
- Plan 002's dedicated-host rule still applies inside each branch. The outer
  async boundary owns its parent's publication route; every nested `if`/keyed
  dynamic region must be inside its own native ContentControl/collection host as
  shown. One structural region may not borrow another region's publication target.

Loading needs no second syntax: `HasCommittedValue` plus the existing Plan 002
conditional contract distinguishes first load from stale refresh. Preserve
`.IsPending` for an optional refresh indicator.

### Computed probe/lowering evolution

Extend Plan 002's synthetic `Computed<T>` surface and symbol-backed lowering to
exactly `Value : T`, `HasCommittedValue : bool`, `IsPending : bool`,
`Error : System.Exception?`, `ErrorMessage : string?`, and `Refresh() : void`.
All five property facets share the existing computed source identity;
`Refresh()` is a mutation and not a read. Lower each to the matching
`OwnedComputed<T>` member. Retry is ordinary authored code, for example
`private void RetryPackages() => packages.Refresh();`, wrapped by the existing
root-owned DelegateCommand and bound in the same component probe.

For this plan, reject every `Computed<T>.Value` read in an ordinary component
method that is transitively summarized into a UI/render computation. Permit a UI
Value read only in a directly bound render island whose syntax ancestor is the
matching boundary content. Computed factory/initializer and other non-UI
computation islands remain valid and retain Plan 002's computed dependency/cycle
analysis. This deliberately avoids pretending to propagate lexical boundary
context through render-reachable helper summaries; status facets and Refresh
remain valid in ordinary methods.

## Application services and settings

Dependency injection is already component input injection: `App.cs` constructs
or resolves services (including from `Microsoft.Extensions.DependencyInjection`
if a consumer chooses it) and passes explicit interfaces to WorkbenchApp. Add no
DI package or runtime integration merely to demonstrate compatibility.

Add Workbench-owned persistence:

```csharp
internal sealed record WorkbenchSettings(
    string? RecentWorkspace,
    double SidebarWidth,
    bool ProblemsVisible);

internal interface ISettingsRepository
{
    Task<WorkbenchSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(WorkbenchSettings value, CancellationToken cancellationToken);
}
```

`JsonFileSettingsRepository` has an internal constructor
`(string path, Action<Exception> reportRecoverableLoadError, Action<string,
string>? commitForTests = null)`, uses `System.Text.Json`, and owns a per-instance
`SemaphoreSlim`. The public application path supplies the reporter and default
commit; only the friend test assembly can inject the commit. Both load and save
take the semaphore and create the destination directory when needed. Serialize
before touching the destination; create a unique temp
file in the destination directory with `FileMode.CreateNew`/`FileShare.None`,
flush it with `FlushAsync` then `Flush(flushToDisk: true)`, call
`cancellationToken.ThrowIfCancellationRequested()` immediately before commit,
then use same-directory `File.Replace(temp, destination, null)` when the
destination exists or `File.Move(temp, destination)` when it does not. On any
failure, delete the temp best-effort and leave the previous destination intact.

Missing settings return defaults. Invalid JSON invokes
`reportRecoverableLoadError` at most once per repository instance and returns
defaults without deleting/overwriting the corrupt file. Clamp sidebar width to
the Workbench-supported range on load. Save cancellation before commit leaves
the old file; after successful rename it is considered committed. Tests may use
an internal test-only commit delegate through existing InternalsVisibleTo to
inject a failure between flush and rename; no public production backdoor exists.

App loads before constructing WorkbenchApp, passes settings/repository as inputs,
and starts saves from explicit state-change methods. Concretely, App constructs
and retains `SettingsSaveCoordinator`, passes that same instance as a required
WorkbenchApp input, and owns its public `Task Tail`; the generated component does
not expose private members to App. WorkbenchApp's required authored inputs in
this plan are its existing desktop host/lifetime plus `errorReporter`, initial
settings, the save coordinator, App-owned `DocumentSession`, and `IProblemLoader`.

The first Window Closing disables commands, cancels component lifetime, and
calls the App-owned DocumentSession's synchronous idempotent Detach/Dispose,
then immediately disposes the root component so its ComponentOwner token cancels all
OwnedComputed work before any save wait. App then bounded-awaits the independently
owned save tail. On timeout, attach exactly one continuation that observes
eventual task fault and invokes App's separately retained reporter; never use the
disposed owner. No global settings singleton is added.

## Accessibility contract

Use Plan 004 attached properties and native peers. Primary controls have stable
IDs and accessible names:

| Target | AutomationId | Name | Peer/control type | Shown tree | Focus expectation |
| --- | --- | --- | --- | --- | --- |
| root Window | `Workbench.Window` | `Lucent Workbench` | native Window / `Window` | shell | contains current focus |
| `AccessibleWorkspaceListBox` | `Workspace.Tree` | `Workspace` | app peer / `Tree` | shell | Focus Sidebar lands here |
| `AccessibleTextEditor` | `Document.Editor` | `Editor` | app peer + `IValueProvider` / `Edit` | shell | Focus Editor lands here |
| native ListBox | `Problems.List` | `Problems` | native ListBox peer / `List` | problems shown | problem navigation lands here |
| native ContentControl overlay | `CommandPalette.Dialog` | `Command palette` | native peer + ControlTypeOverride / `Window` | palette open | first palette result; Tab cycles within |
| settings Window | `Settings.Dialog` | `Settings` | native Window peer / `Window` | settings shown | first settings field |

- Native text/content supplies a name where adequate; otherwise set
  `AutomationProperties.Name`. Do not assign IDs to every virtualized row.
- Version inspection shows the flattened workspace ListBox and AvaloniaEdit
  12.0.0 do not expose adequate semantic peers. Add two accessibility-only
  Workbench subclasses: `AccessibleWorkspaceListBox : ListBox` returns a
  `ControlAutomationPeer` reporting `AutomationControlType.Tree`;
  `AccessibleTextEditor : TextEditor` returns a peer reporting
  `AutomationControlType.Edit` and implementing `IValueProvider` from the real
  Text/IsReadOnly state. It raises the Value property-change event on TextChanged.
  These subclasses add no mirrored UI properties or behavior. Keep native
  ListBox, Button, MenuItem, ContentControl, and Window peers.
- Add a test-only accessibility audit that walks realized public controls and
  verifies every focusable primary control has a non-empty native/content or
  AutomationProperties name and a native automation peer/role. Unrealized rows
  are not expected to have peers.
- Version-verify the public automation-peer creation/access API in a compile
  spike. Show shell, palette, and settings separately and assert that each of the
  six table IDs exists exactly once in its relevant tree, equals the expected
  `AutomationProperties.AutomationId`, has the expected non-empty name, and has
  a native peer with a non-empty role/control type. A generic audit alone is not
  sufficient.
- Keep FluentTheme focus visuals; Workbench styles may not suppress them. The
  tests prove keyboard focus reaches each primary target; the native smoke
  records a manual focus-visual check rather than pixel-testing a theme.

## Headless user-flow contract

Extend Plan 005's `tests/Lucent.Workbench.Tests` project and dedicated UI thread;
do not create another project, AppBuilder setup, or dispatcher loop. Use
Avalonia.Headless input helpers (version-verified in a compile spike) to send
physical key presses to the shown Window. Do not invoke ICommand directly in
the final routing tests.

Cover these deterministic flows with Plan 004's fake desktop host and bounded
dispatcher drains:

1. Ctrl+O invokes the shared Open Workspace command and fake picker; cancelling
   the fake result changes no workspace.
2. Ctrl+P opens quick-open, arrows change selection, Enter opens the item, and
   Escape restores editor focus.
3. The command palette traps Tab/Shift+Tab, invokes Toggle Problems, closes, and
   restores prior focus.
4. Project-tree keyboard selection opens one document; editor typing, copy,
   undo, redo, and problem navigation retain the same editor control.
5. Add one bounded Workbench use case: `IProblemLoader.LoadAsync(workspace,
   token)` feeds a `Computed<IReadOnlyList<ProblemItem>>` in WorkbenchApp. The
   production `PlaceholderProblemLoader` returns existing placeholder problems;
   a controllable fake owns TaskCompletionSources. Opening a workspace starts
   first load; Ctrl+Shift+R invokes the root Refresh Problems command. First load
   shows loading, refresh keeps stale results, failure switches the explicit
   catch branch, keyboard-activating Retry recovers, and nested owners clean up.
   Plan 009 replaces only the placeholder loader implementation.
6. Settings loads, changes, saves, reopens, and survives injected pre-rename
   failure with the old file intact.
7. App constructs and retains `SettingsSaveCoordinator`, passes it as a required
   WorkbenchApp input, and awaits its public serialized `_saveTail`; every requested
   save appends to that tail and one observer owns its faults. The first Window
   Closing is cancelled, disables commands, cancels lifetime/work, immediately
   detaches/disposes the App-owned DocumentSession, disposes the root component,
   then bounded-awaits
   the app-owned tail while the still-shown Window keeps the dispatcher alive,
   attaches one retained-reporter continuation on timeout, reports any disposal
   aggregate once, marks shutdown complete, then posts a second Close which is
   allowed. Root disposal removes
   editor/native event subscriptions, disposes every branch/component owner,
   and leaves no queued mutation that changes controls after close.

Tests may expose counters only on fakes/test dispatchers. They must not call
private generated methods, mutate private controls, or add production-only test
hooks.

## Commands

| Purpose | Command | Expected result |
| --- | --- | --- |
| Restore | `dotnet restore Lucent.sln` | existing and Plan 005 packages restore |
| Baseline | `dotnet test Lucent.sln --no-restore` | Plans 001–005 baseline passes |
| Runtime | `dotnet test tests/Lucent.Runtime.Tests/Lucent.Runtime.Tests.csproj --no-restore` | OwnedComputed/owner tests pass |
| Compiler | `dotnet test tests/Lucent.Compiler.Tests/Lucent.Compiler.Tests.csproj --no-restore` | boundary/lowering tests pass |
| LSP | `dotnet test tests/Lucent.LanguageServer.Tests/Lucent.LanguageServer.Tests.csproj --no-restore` | boundary/catch-local tooling passes |
| Package Pulse | `dotnet run --project src/Lucent.Poc/Lucent.Poc.csproj --no-restore -- --smoke-test package-pulse` | loading/failure/retry/stale smoke exits 0 |
| Workbench | `dotnet test tests/Lucent.Workbench.Tests/Lucent.Workbench.Tests.csproj --no-restore` | headless flows/settings/a11y pass |
| Native smoke | `dotnet run --project examples/workbench/Lucent.Workbench.csproj -- --smoke-test reliability` | native focus/lifecycle check exits 0 |
| Build | `dotnet build Lucent.sln --no-restore` | exit 0, no warnings |
| Full tests | `dotnet test Lucent.sln --no-build` | all tests pass |
| VS Code | `npm test` from `editors/vscode` | all tests pass |

## Scope

**Create**:

- `src/Lucent.Runtime/OwnedComputed.cs`
- `tests/Lucent.Runtime.Tests/OwnedComputedTests.cs`
- `examples/workbench/WorkbenchSettings.cs`
- `examples/workbench/ISettingsRepository.cs`
- `examples/workbench/JsonFileSettingsRepository.cs`
- `examples/workbench/SettingsSaveCoordinator.cs`
- `examples/workbench/IProblemLoader.cs`
- `examples/workbench/PlaceholderProblemLoader.cs`
- `examples/workbench/AccessibleWorkspaceListBox.cs`
- `examples/workbench/AccessibleTextEditor.cs`
- `examples/workbench/ThrowingEvent.lui`
- `tests/Lucent.Workbench.Tests/SettingsRepositoryTests.cs`
- `tests/Lucent.Workbench.Tests/AccessibilityTests.cs`
- `tests/Lucent.Workbench.Tests/EventReportingTests.cs`
- `tests/Lucent.Workbench.Tests/UserFlowTests.cs`

**Modify**:

- `src/Lucent.Runtime/ComponentOwner.cs`
- `src/Lucent.Compiler/Syntax/SyntaxNodes.cs`
- `src/Lucent.Compiler/Parsing/Parser.cs`
- `src/Lucent.Compiler/Semantics/CSharpIslandBinder.cs`
- `src/Lucent.Compiler/CodeGeneration/BoundComponentModel.cs`
- `src/Lucent.Compiler/CodeGeneration/GeneralBinder.cs`
- `src/Lucent.Compiler/CodeGeneration/GeneralCSharpEmitter.cs`
- `src/Lucent.Compiler/EditorIntelligence.cs`
- `src/Lucent.LanguageServer/LanguageServer.cs` only for protocol shaping
- `tests/Lucent.Runtime.Tests/ComponentOwnerTests.cs`
- `tests/Lucent.Runtime.Tests/UiDispatcherContractTests.cs` — add OwnedComputed
  to the exact exported runtime-type contract
- `tests/Lucent.Compiler.Tests/GeneralCompilerTests.cs`
- `tests/Lucent.Compiler.Tests/ComponentCompositionTests.cs`
- `tests/Lucent.LanguageServer.Tests/LanguageServerProtocolTests.cs`
- `examples/package-pulse/PackagePulse.lui`
- `examples/workbench/App.cs`
- `examples/workbench/Program.cs`
- `examples/workbench/DelegateCommand.cs`
- `examples/workbench/DocumentSession.cs`
- `examples/workbench/WorkbenchApp.lui`
- `examples/workbench/WorkspaceSidebar.lui`
- `examples/workbench/DocumentPane.lui`
- `examples/workbench/ProblemsPane.lui`
- `examples/workbench/SettingsPane.lui`
- `examples/workbench/SettingsDialog.lui`
- `examples/workbench/GeneratedPreviewWindow.lui`
- `examples/workbench/README.md`
- `tests/Lucent.Workbench.Tests/HeadlessTestApp.cs`
- `tests/Lucent.Workbench.Tests/HeadlessTestHarness.cs`
- `tests/Lucent.Workbench.Tests/Lucent.Workbench.Tests.csproj`
- `src/Lucent.Poc/Program.cs`
- `docs/LANGUAGE.md`
- `docs/ARCHITECTURE.md`
- `docs/TOOLING.md`
- `docs/DECISIONS.md`
- `plans/README.md` status only

**Out of scope**:

- General effects/subscription syntax, runtime sync observer graph, collection
  adapter, state store, hooks, priorities, optimistic mutation, or new resource
  model.
- Custom DI container/service locator, adding Microsoft.Extensions.DependencyInjection,
  global services/settings, cloud sync, migration framework, or secrets storage.
- Custom automation peers beyond the two accessibility-only third-party-control
  peers above, OS picker automation, screenshot/pixel assertions, custom editor,
  telemetry, or packaging (Plan 009).

## Steps

### Step 1: Characterize and implement OwnedComputed

Write runtime tests first for construction, first refresh, synchronous throw,
success, stale refresh, rapid replacement, late success/error, cancellation,
foreign OperationCanceledException, throwing replacement cancellation callback,
owner disposal, refresh-after-dispose,
idempotent dispose, invalidate counts/failures, reporter throw without recursive
reporting, and optional unhandled reporting. Implement
the runtime type without a second scheduler/graph.

**Verify**: runtime tests pass with deterministic dispatcher/task sources; no
sleep or real Avalonia dispatcher is used.

### Step 2: Migrate generated computed fields and add boundaries

Add parser/binder tests for exact syntax/spans, invalid source/type/catch/filter/
finally forms, catch-local binding, guarded/unguarded Value reads, fragments,
Value-in-helper rejection, all five reactive facets, Refresh mutation, nested
different-source boundaries, and diagnostics. Replace generated async
boilerplate with OwnedComputed and reuse ConditionalRegion lowering. Preserve
Plan 002 dependency IDs/cycle checks and source mapping.

Migrate Package Pulse to outer `try(source)` plus inner first-load/empty/result
conditionals, each under its own dedicated native host. Its refresh keeps
committed results; failure renders catch; Retry clears the error and recovers.

**Verify**: compiler/runtime/LSP tests and Package Pulse smoke pass; generated
source contains one OwnedComputed per declaration and no generated CTS/generation
fields.

### Step 3: Route otherwise-unhandled errors once

Evolve ComponentOwner, generated root constructors/events, DelegateCommand, and
Workbench App reporter. Test child inheritance, omitted reporter rethrow,
after-dispose suppression, reporter throw, event/command errors, cleanup
aggregate forwarding through App's retained delegate, invalidation/fallback
failures, and handled-vs-unhandled computed failures.

**Verify**: every injected failure is observed exactly once at boundary or root;
fallback failure reaches root rather than recursively remounting fallback.

### Step 4: Add explicit services and atomic settings

Construct repository/reporter in App and pass explicit component inputs. Add the
JSON repository and tests for missing/valid/invalid/clamped data, cancellation,
concurrent saves, no temp leftovers, and injected failure after durable temp
flush but before rename. Assert once-per-instance invalid-data reporting and the
timed-out-save continuation. The old destination bytes must remain exact.

Add `IProblemLoader`, placeholder/fake implementations, the Workbench computed
source, and root Refresh Problems/Retry commands here. Keep the loader bounded
to placeholder problems; Plan 009 may replace its implementation, not its async
lifetime/error contract.

**Verify**: repository tests pass on the supported CI OSes; Workbench settings
round-trip through close/reopen without global state.

### Step 5: Audit native accessibility

Add the stable IDs/names, the two narrow third-party-control peers, audit helper,
peer/role checks, and keyboard focus assertions. Compile-spike the public peer/
IValueProvider APIs and assert the exact six ID/name/peer-role/focus tuples in
their shown trees. Ensure app styles do not remove Fluent focus visuals. Do not inspect
or force creation of unrealized virtualized rows.

**Verify**: AccessibilityTests passes for the shown shell, palette, settings,
tree, editor, and problems list.

### Step 6: Convert smoke claims into headless user flows

Compile-spike the pinned Avalonia.Headless key-input API, then implement all seven
flows above on Plan 005's UI thread/harness. Use fake native dialogs/storage and
temporary filesystem settings only. Close every Window in `finally`.

**Verify**: Workbench tests pass repeatedly without visible desktop, hangs, or
post-close mutations; then run native reliability smoke and all gates.

## Done criteria

- [x] Generated computed work uses one owner-bound node and preserves first-load,
      stale-refresh, replacement, failure, and disposal semantics.
- [x] Explicit async boundaries own their source failure branches; all other
      failures reach one root reporter exactly once.
- [x] No general effects, DI, observable, or second resource framework is added.
- [x] Settings commits preserve the old file on pre-rename failure.
- [x] Primary controls retain native peers, stable IDs/names, and keyboard focus.
- [x] All seven Workbench flows pass in the single headless harness.
- [x] Full build, tests, native smoke, and VS Code tests pass.

## STOP conditions

- Async migration changes Plan 002 dependency identities/cycle diagnostics or
  creates another sync observer graph.
- A boundary needs a virtual DOM, hidden host, or exception swallowing.
- A proposed lifecycle API depends on call order like hooks or has no current
  Workbench caller.
- DI/persistence becomes Lucent global state or a test requires a public
  production backdoor.
- Headless tests create another AppBuilder/dispatcher loop, invoke commands
  directly instead of routing keys, or automate native OS pickers.
- Accessibility work replaces a native peer or materializes all virtual rows.

## Maintenance notes

Plan 009 consumes these deterministic shutdown/settings/headless gates for
packaging. Observable-model/effect APIs remain deferred until a concrete app
cannot use native collection binding, generated event ownership, or explicit
application coordination.
