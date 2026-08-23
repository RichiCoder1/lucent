# Plan 005: Add scalable native collections and AvaloniaEdit

> **Executor instructions**: Complete Plans 003–004 first. Use Avalonia's free
> native collection/editor APIs directly. Do not retrofit virtualization into
> Lucent keyed regions or invent an item-renderer abstraction without a failing
> Workbench use case.
>
> **Drift check (run first)**:
> `git diff --stat 3b27cfe..HEAD -- examples/workbench tests/Lucent.Workbench.Tests src/Lucent.Compiler src/Lucent.Runtime Lucent.sln`
> Stop if Workbench's native interop shell is incomplete or if direct project
> package types no longer resolve through Plan 003's project semantic model.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: MEDIUM
- **Depends on**: `plans/003-component-composition.md`,
  `plans/004-desktop-application-interop.md`
- **Category**: direction / performance / interop
- **Planned at**: commit `3b27cfe`, 2026-08-16

## Why this matters

Workbench needs thousands of project/problem/quick-open rows and a credible
source editor. Avalonia's free native `ListBox` virtualizes item containers, and
AvaloniaEdit implements editing, selection, undo, IME, and scrolling. The
smallest honest Lucent proof is to configure those native controls through
ordinary component fields/inputs, not add a second recycler or mirror an editor
API.

## Current state and decision

- Plans 001–003 retain every keyed structural row as mounted controls. That is
  correct for small arbitrary regions and is not virtualization.
- Plan 004 makes attached properties, native KeyBindings, resources, commands,
  focus, and project-defined native controls usable from Workbench.
- The original Plan 005 TreeDataGrid spike failed with `AVLIC0001`: Avalonia
  Controls TreeDataGrid 12 is commercial and no license key is configured.
- Avalonia 12's free native `ListBox` provides virtualized containers, selection,
  focus, scrolling, and recycling. A flattened visible-node projection keeps
  hierarchy app-owned without adding a control-specific renderer.

**Decision:** use one app-owned `ObservableCollection<WorkspaceRow>` flattened
from the expanded workspace tree and one native `ListBox`. Indent rows with a
minimal native `FuncDataTemplate<WorkspaceRow>` because depth plus name are
needed. The ListBox owns containers, selection, focus, scrolling, and recycling.
Do not use TreeView, custom presenters, Lucent row components, or generic
item-renderer/recycler APIs. If a later screen requires a stateful Lucent
component as a recycled row, stop and design that concrete seam.

## Exact package and resource changes

In `examples/workbench/Lucent.Workbench.csproj`, add only:

```xml
<PackageReference Include="Avalonia.AvaloniaEdit" Version="12.0.0" />
```

Keep the existing Avalonia packages at 12.1.1. Do **not** add the commercial
TreeDataGrid package. AvaloniaEdit 12.0.0 declares an Avalonia 12 dependency;
do not downgrade the project or use `ICSharpCode.AvalonEdit`.

In Workbench `App.Initialize`, include the editor's shipped Fluent resource:

```csharp
Styles.Add(new StyleInclude(new Uri("avares://AvaloniaEdit/"))
{
    Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"),
});
```

`Avalonia.AvaloniaEdit` is the package ID; `AvaloniaEdit` is the control/resource
assembly and namespace. Workbench App and HeadlessTestApp both install
FluentTheme and this exact AvaloniaEdit StyleInclude. Do not copy themes or
translate them to Lucent CSS.

In `tests/Lucent.Workbench.Tests`, add `Avalonia.Headless` 12.1.1. Keep MSTest
and add `[assembly: DoNotParallelize]`. One `[AssemblyInitialize]` starts a
dedicated background UI thread; that thread calls
`AppBuilder.Configure<HeadlessTestApp>().UseHeadless(new
AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting()`, signals ready, then
runs `Dispatcher.UIThread.MainLoop(cancellationToken)`. `OnUiAsync` posts the
complete show/action/layout/close operation to that dispatcher, drains queued
jobs with a bounded timeout, and closes the Window in `finally`.
`[AssemblyCleanup]` cancels/wakes the loop and joins the thread with a timeout.
Never run UI work directly on MSTest worker threads. Plan 006 expands this
project; it must not create a second headless harness.

## Exact Workbench collection model

Add app-owned records/classes:

```csharp
internal sealed record WorkspaceNode(
    string Id,
    string Name,
    ObservableCollection<WorkspaceNode> Children);

internal sealed record WorkspaceRow(WorkspaceNode Node, int Depth);

internal sealed record ProblemItem(
    string Id,
    string File,
    int Line,
    string Message,
    ProblemSeverity Severity);

internal sealed record QuickOpenItem(string Id, string DisplayName, string Path);
```

- IDs are stable application identity, not container indexes.
- Use `ObservableCollection<T>` because Avalonia's native controls consume its
  collection notifications. Do not wrap it in Lucent state or mirror collection
  events in `Lucent.Runtime`.
- `WorkspaceSelectionController` owns the workspace roots, one visible
  `ObservableCollection<WorkspaceRow>`, expansion/rebuild, stable-ID validation,
  indexing, selection, and fallback. It exposes the visible projection to one
  native workspace `ListBox`.
- `WorkspaceSidebar` uses a minimal native `FuncDataTemplate<WorkspaceRow>` to
  render depth indentation and the node name. It does not mount a Lucent row
  component.
- `ProblemsPane` and quick-open use native `ListBox.ItemsSource` with
  `ObservableCollection<T>`. Keep their item display native: `DisplayMemberBinding`
  or a small C# `FuncDataTemplate<T>` only when multiple fields are required.
- Keep stock ListBox templates/presenters and place each inside a finite-height
  scrolling layout. Replacing presenters or nesting in an unbounded StackPanel
  invalidates the virtualization proof.

## Selection and focus contract

Create `WorkspaceSelectionController` in the Workbench application:

- Persist selected `WorkspaceNode.Id`, not row/container/index identity.
- Validate IDs are non-empty and unique over the current tree before assigning a
  projection; duplicate IDs are an application-model error.
- The native workspace ListBox binds `ItemsSource` to the visible projection and
  uses the controller's selected row/selection-changed seam. The controller may
  expose one `SelectedId` and `SelectById` operation, but must not become a
  generic selection framework.
- Incremental add/remove/move retains the selected model when it remains.
- Add a narrow `ResettableObservableCollection<T>.ReplaceAll` that replaces one
  root/child collection and raises one Reset after mutation. Before replacement,
  capture selected ID, rebuild the visible projection and ID index, then restore
  selection by ID or clear it if the ID vanished.
- When a selected node is removed, choose the next sibling at the removed index;
  otherwise the previous sibling; otherwise its parent; otherwise clear. Focus
  the workspace ListBox after applying that fallback.
- Containers, focus visuals, scrolling, and recycling remain Avalonia-owned.

Problems/quick-open use native flat ListBox selection APIs. Do not share one
generic selection framework; direct methods are cheaper and clearer.

## Exact virtualization evidence

Headless tests use a fixed 800x600 Window with each collection control in a
bounded `Grid` row, never an unbounded StackPanel. Populate 10,000 visible
workspace rows, 10,000 problems, and 10,000 quick-open items. Show the Window
and drain dispatcher/layout work. Then:

- each ListBox visual descendants contain at least one but fewer than 200
  realized `ListBoxItem` containers;
- call `ScrollIntoView(lastItem)` for each ListBox, drain, then scroll the first
  items back; every realized count stays below 200;
- inspect public visual descendants with `GetVisualDescendants()` and public
  `ListBoxItem` types. A realized container DataContext at each end must
  reference the expected first/last model, proving layout worked;
- insert/move/remove keeps workspace selection by ID; reset restores it by ID;
- architecture inspection confirms all data controls receive models/native
  templates only. Do not test private presenter fields.

The threshold is a regression ceiling for this fixed viewport, not a framework
benchmark. Log viewport, item count, and realized counts on failure. Do not test
private presenter fields.

## Exact AvaloniaEdit integration

- Replace Plan 004's placeholder editor `TextBox` in `DocumentPane.lui` with
  direct `AvaloniaEdit.TextEditor` syntax/import. Do not add a Lucent wrapper.
- Add `OpenDocument` application state with path, text, and dirty flag. The
  loaded editor reference is captured/cleared with paired Loaded/Unloaded
  handlers as in Plan 004.
- App constructs and retains one `DocumentSession` and passes it as a required
  WorkbenchApp/DocumentPane input. Loaded calls `session.Attach(editor)`;
  Unloaded calls `session.Detach(editor)` only for the currently attached editor.
  The session exposes idempotent synchronous `Detach`/`Dispose` so application
  shutdown need not wait for a visual-tree Unloaded event.
- `DocumentSession` owns an `_applyingModelText` guard. User `TextChanged`
  updates `OpenDocument.Text` and marks dirty only when the guard is false.
  Programmatic model-to-editor updates compare first; when replacement is
  necessary, set the guard, snapshot caret/selection/scroll state, call
  `editor.Document.Replace(0, editor.Document.TextLength, newText)`, clear
  `editor.Document.UndoStack` as a new external baseline, restore clamped state,
  then clear the guard in `finally`. The resulting TextChanged must not mark the
  model dirty or feed text back.
- Plan 005 adds root-owned Copy, Undo, Redo, and Select All commands and passes
  them to DocumentPane as Plan 004 command inputs. Their handlers call the
  version-verified direct `TextEditor.Focus()`, `Copy()`, `Undo()`, `Redo()`, and
  `SelectAll()` APIs; enablement reads corresponding `Can*` properties. Notify
  all four commands when the editor attaches/detaches, TextChanged fires,
  TextArea selection changes, an external replacement completes, or undo/redo
  executes. Search/find remains deferred.
- Keep one app-specific `DocumentSession` coordinator if needed for model/editor
  synchronization. It may mention AvaloniaEdit types; it must not expose a
  mirrored property/event surface.

## Commands

| Purpose | Command | Expected result |
| --- | --- | --- |
| Package restore | `dotnet restore Lucent.sln` | resolves AvaloniaEdit 12.0.0 and Headless 12.1.1; no commercial package |
| Baseline | `dotnet test Lucent.sln --no-restore` | Plans 001–004 baseline passes |
| Headless focus | `dotnet test tests/Lucent.Workbench.Tests/Lucent.Workbench.Tests.csproj --no-restore --filter "FullyQualifiedName~Virtualization|FullyQualifiedName~Editor"` | bounded realization/editor tests pass |
| Workbench smoke | `dotnet run --project examples/workbench/Lucent.Workbench.csproj -- --smoke-test data` | project/problem/editor flow exits 0 |
| Build | `dotnet build Lucent.sln --no-restore` | exit 0, no warnings |
| Full tests | `dotnet test Lucent.sln --no-build` | all tests pass |
| VS Code | `npm test` from `editors/vscode` | all tests pass |

## Scope

**Create**:

- `examples/workbench/WorkspaceModels.cs`
- `examples/workbench/ResettableObservableCollection.cs`
- `examples/workbench/WorkspaceSelectionController.cs`
- `examples/workbench/DocumentSession.cs`
- `tests/Lucent.Workbench.Tests/HeadlessTestApp.cs`
- `tests/Lucent.Workbench.Tests/HeadlessTestHarness.cs`
- `tests/Lucent.Workbench.Tests/VirtualizationTests.cs`
- `tests/Lucent.Workbench.Tests/EditorTests.cs`

**Modify**:

- `Lucent.sln`
- `examples/workbench/Lucent.Workbench.csproj`
- `examples/workbench/App.cs`
- `examples/workbench/Program.cs`
- `examples/workbench/WorkbenchApp.lui`
- `examples/workbench/WorkspaceSidebar.lui`
- `examples/workbench/DocumentPane.lui`
- `examples/workbench/ProblemsPane.lui`
- `examples/workbench/README.md`
- `tests/Lucent.Workbench.Tests/Lucent.Workbench.Tests.csproj`
- `plans/README.md` status only

**Out of scope**:

- Changes to `src/Lucent.Compiler` or `src/Lucent.Runtime`. Native package types
  should already work through Plans 003–004; a failure is a STOP condition.
- A Lucent data-grid/list/item-template/recycling API, component values, or one
  mounted component owner per data item.
- TreeView, custom presenters, custom virtualization, generic selection
  frameworks, custom editor, syntax highlighting service, language-server
  connection, filesystem workspace loading, document persistence, diffing, or
  tabs.
- Replacing native control templates/presenters, general performance framework,
  or promising a universal 200-container ceiling.

## Steps

### Step 1: Lock package/API compatibility with a C# spike

Record the original commercial package's `AVLIC0001` failure, remove that
package, and add the free AvaloniaEdit and headless references. Add a temporary
focused test that constructs native `ListBox`/`ListBoxItem` templates,
`ScrollIntoView`, and AvaloniaEdit `TextEditor` under Avalonia 12.1.1. Add
FluentTheme and the exact AvaloniaEdit StyleInclude to app and test app. Compile
the ListBox, data-template, scrolling, TextEditor, direct edit commands,
document replacement, and undo clearing before later steps; do not design
against remembered Avalonia 11 signatures.

**Verify**: restore/build succeeds with one version of each package and no
binding/type-load warnings.

### Step 2: Add the reusable headless Workbench harness

Initialize one headless App on the dedicated dispatcher thread with the exact
assembly setup/cleanup above, create/show an 800x600 Window through `OnUiAsync`,
and add helpers only for bounded dispatcher/layout draining and public visual
descendants. Every helper closes the Window in `finally`. Migrate existing
Workbench desktop-host tests onto this harness rather than leaving two
AppBuilder loops.

**Verify**: a sentinel Workbench window test shows, focuses one control, and
closes without a desktop or leaked component owner.

### Step 3: Integrate native ListBox data surfaces

Add typed models and the flattened workspace projection, use
`ObservableCollection` directly for normal changes and `ReplaceAll` only for
reset, and wire native ListBox selection by stable unique ID. Preserve stock
presenters and bounded layout. Keep the small existing structural keyed
example/tests unchanged.

**Verify**: collection and selection tests pass for add/remove/move/reset.

### Step 4: Prove bounded realization

Populate the fixed test window with 10,000 items in each of the three ListBoxes,
show/layout, count public `ListBoxItem` containers, scroll end/back, and assert
the evidence contract above. Fail if the last item cannot be reached or if
counts are zero.

**Verify**: `VirtualizationTests` passes repeatedly and reports counts on failure.

### Step 5: Replace the editor placeholder with AvaloniaEdit

Render TextEditor directly, synchronize one `OpenDocument`, preserve one
editor instance, and route focus/edit commands to its native API. Keep event
ownership in generated component handlers and app coordination in
`DocumentSession`.

**Verify**: headless test types text, asserts model/dirty update, performs
undo/redo, applies an external text replacement, and verifies control identity
plus clamped caret/selection.

### Step 6: Run the Workbench data smoke and full gates

The native smoke expands the flattened project tree, selects a file, opens its
placeholder text, edits it, navigates to one problem, and shuts down. It does not
access the real filesystem yet.

**Verify**: focused headless tests, native data smoke, full solution, and VS Code
tests pass.

## Test plan

- Pure model/controller tests own stable selection behavior.
- Headless tests own actual realization, scrolling, focus, text input, undo, and
  control identity and explicit session detach. They inspect public visual
  descendants, not internals.
- Existing Todo/keyed tests remain the structural-region contract.
- No compiler snapshots or fake virtualization implementation are added.

## Done criteria

- [x] Structural keyed regions remain unchanged and explicitly non-virtualized.
- [x] Workspace, problems, and quick-open ListBoxes keep realized containers
      below the fixed viewport ceiling for 10,000-item tests.
- [x] Workspace selection survives add/move/reset by application ID where the
      item remains and uses the documented fallback when it is removed.
- [x] AvaloniaEdit is rendered directly with its shipped theme.
- [x] Editing, undo/redo, dirty state, focus, caret/selection, and editor identity
      pass headless tests.
- [x] No Lucent item-renderer/recycler/editor abstraction is added.
- [x] Full build, tests, native smoke, and VS Code tests pass.

## STOP conditions

- Native ListBox/AvaloniaEdit types cannot bind as project controls after Plans
  003–004; report the exact semantic gap instead of adding wrappers.
- The chosen layout realizes all 10,000 items; first fix native layout/template
  configuration, not Lucent runtime code.
- Workbench needs a stateful Lucent component as a recycled row. Capture the
  concrete API/lifetime requirement and propose a separate narrow plan.
- Selection cannot be restored from stable model ID independently of a recycled
  container.
- AvaloniaEdit integration starts mirroring its public API or reconstructing the
  editor on text changes.

## Maintenance notes

Plan 006 expands the same headless project for accessibility/lifecycle flows.
If the pinned ListBox/AvaloniaEdit versions lack adequate native automation
peers, Plan 006 may add accessibility-only subclasses; those subclasses must add
peers only and must not mirror control APIs. Plan 010 may add actual
project/document I/O but must keep these native controls and stable selection
contracts.
