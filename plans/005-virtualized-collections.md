# Plan 005: Add scalable native collections and AvaloniaEdit

> **Executor instructions**: Complete Plans 003–004 first. Use Avalonia's
> collection/editor APIs directly. Do not retrofit virtualization into Lucent
> keyed regions or invent an item-renderer abstraction without a failing
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
source editor. Avalonia already virtualizes TreeDataGrid/ListBox containers, and
AvaloniaEdit already implements editing, selection, undo, IME, and scrolling.
The smallest honest Lucent proof is to configure those native controls through
ordinary component fields/inputs—not add a second recycler or mirror an editor
API.

## Current state and decision

- Plans 001–003 retain every keyed structural row as mounted controls. That is
  correct for small arbitrary regions and is not virtualization.
- Plan 004 makes attached properties, native KeyBindings, resources, commands,
  focus, and project-defined native controls usable from Workbench.
- Avalonia 12 TreeDataGrid virtualizes rows through its rows presenter and has a
  separate selection model. Reset events cannot preserve selection without an
  application key restore.
- AvaloniaEdit 12 is a native Avalonia control package. It must be used directly,
  not confused with WPF AvalonEdit.

**Decision:** no Lucent compiler/runtime item-renderer interface is needed for
the first Workbench. TreeDataGrid typed columns, native `ItemsSource`, and native
templates cover the actual flows. If a later screen requires a stateful Lucent
component as a recycled row, stop and design that concrete seam then.

## Exact package and resource changes

In `examples/workbench/Lucent.Workbench.csproj`, add:

```xml
<PackageReference Include="Avalonia.Controls.TreeDataGrid" Version="12.1.1" />
<PackageReference Include="Avalonia.AvaloniaEdit" Version="12.0.0" />
```

Keep the existing Avalonia packages at 12.1.1. AvaloniaEdit 12.0.0 declares an
Avalonia 12 dependency; do not downgrade the project or use
`ICSharpCode.AvalonEdit`.

In Workbench `App.Initialize`, include the editor's shipped Fluent resource:

```csharp
Styles.Add(new StyleInclude(new Uri("avares://Avalonia.Controls.TreeDataGrid/"))
{
    Source = new Uri("avares://Avalonia.Controls.TreeDataGrid/Themes/Fluent.xaml"),
});
Styles.Add(new StyleInclude(new Uri("avares://AvaloniaEdit/"))
{
    Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"),
});
```

`Avalonia.AvaloniaEdit` is the verified public package ID; `AvaloniaEdit` is the
control/resource assembly and namespace. Workbench App and HeadlessTestApp both
install FluentTheme and these exact TreeDataGrid/AvaloniaEdit StyleIncludes so
stock presenters materialize. Do not copy themes or translate them to Lucent CSS.

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

internal sealed record ProblemItem(
    string Id,
    string File,
    int Line,
    string Message,
    ProblemSeverity Severity);

internal sealed record QuickOpenItem(
    string Id,
    string DisplayName,
    string Path);
```

- IDs are stable application identity, not container indexes.
- Use `ObservableCollection<T>` because Avalonia's native controls consume its
  collection notifications. Do not wrap it in Lucent state or mirror collection
  events in `Lucent.Runtime`.
- `WorkspaceSidebar` owns a native `TreeDataGrid`. Build one
  `HierarchicalTreeDataGridSource<WorkspaceNode>` in an ordinary component field
  using the package's typed hierarchical expander/text columns and child
  selector. Bind its `Source` directly.
- `ProblemsPane` and quick-open use native `ListBox.ItemsSource` with
  `ObservableCollection<T>`. Keep their item display native: `DisplayMemberBinding`
  or a small C# `FuncDataTemplate<T>` only when multiple fields are required.
- Keep stock TreeDataGrid/ListBox templates/presenters and place each inside a
  finite-height scrolling layout. Replacing presenters or nesting in an
  unbounded StackPanel invalidates the virtualization proof.

## Selection and focus contract

Create `WorkspaceSelectionController` in the Workbench application:

- Persist selected `WorkspaceNode.Id`, not row/container/index identity.
- Validate IDs are non-empty and unique over the current tree before assigning
  a source; duplicate IDs are an application-model error.
- Use one `HierarchicalTreeDataGridSource<WorkspaceNode>` and assign
  `source.Selection = new TreeDataGridRowSelectionModel<WorkspaceNode>(source)`;
  use `source.RowSelection` and `IndexPath` for all selection operations.
- Incremental add/remove/move retains the selected model when it remains.
- Add a narrow `ResettableObservableCollection<T>.ReplaceAll` that replaces one
  root/child collection and raises one Reset after mutation. Before replacement,
  capture selected ID. Rebuild an ID-to-`IndexPath` map, drain source/layout
  processing, then clear/select the new path. Clear selection if the ID vanished.
- When a selected node is removed, choose the next sibling at the removed index;
  otherwise the previous sibling; otherwise its parent; otherwise clear. Focus
  the TreeDataGrid after applying that fallback.
- Containers, focus visuals, scrolling, and recycling remain Avalonia-owned.

Problems/quick-open use the same ID principle but native flat selection APIs.
Do not share one generic selection framework; three small controllers or direct
methods are cheaper and clearer.

## Exact virtualization evidence

Headless tests use a fixed 800×600 Window with the collection control in a
bounded `Grid` row—never an unbounded StackPanel—and 10,000 flat rows plus a
project tree whose expanded visible population exceeds 10,000. Construct
`HierarchicalTreeDataGridSource`, typed columns, and
`TreeDataGridRowSelectionModel` exactly as the Step 1 spike compiled. Call
`source.ExpandAll()`, show the Window, and drain dispatcher/layout work. Then:

- visual descendants contain at least one but fewer than 200 realized
  `TreeDataGridRow` instances;
- the problems/quick-open `ListBox` realizes at least one but fewer than 200
  `ListBoxItem` containers;
- call `treeDataGrid.BringRowIntoView(lastIndexPath, null)` and
  `listBox.ScrollIntoView(lastItem)`, drain, then bring/scroll the first items
  back; every realized count stays below 200;
- inspect public visual descendants with `GetVisualDescendants()` and public
  `TreeDataGridRow`/`ListBoxItem` types. A realized row/container DataContext at
  each end must reference the expected first/last model, proving layout worked;
- insert/move/remove keeps selection by ID; reset restores it manually;
- architecture inspection confirms the data controls receive models/native
  templates only; do not claim private owner-count instrumentation from visual
  traversal.

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
  `SelectAll()` APIs; enablement reads the corresponding `Can*` properties.
  If AvaloniaEdit 12 has no `CanSelectAll`, use
  `editor.Document.TextLength > 0` explicitly. Notify all four commands when the
  editor attaches/detaches, TextChanged fires, TextArea selection changes, an
  external replacement completes, or undo/redo executes.
  Search/find remains deferred rather than adding an editor feature here.
- Keep one app-specific `DocumentSession` coordinator if needed for model/editor
  synchronization. It may mention AvaloniaEdit types; it must not expose a
  mirrored property/event surface.

## Commands

| Purpose | Command | Expected result |
| --- | --- | --- |
| Package restore | `dotnet restore Lucent.sln` | resolves TreeDataGrid 12.1.1, AvaloniaEdit 12.0.0, and Headless 12.1.1 |
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

- Changes to `src/Lucent.Compiler` or `src/Lucent.Runtime`. Direct package types
  should already work through Plans 003–004; a failure is a STOP condition.
- A Lucent data-grid/list/item-template/recycling API, component values, or one
  mounted component owner per data item.
- Custom editor, syntax highlighting service, language-server connection,
  filesystem workspace loading, document persistence, diffing, or tabs.
- Replacing native control templates/presenters, general performance framework,
  or promising a universal 200-container ceiling.

## Steps

### Step 1: Lock package/API compatibility with a C# spike

Add package references and a temporary focused test that constructs the exact
TreeDataGrid source/columns and AvaloniaEdit TextEditor under Avalonia 12.1.1.
Add FluentTheme and both exact StyleIncludes above to app and test app. Compile
`HierarchicalTreeDataGridSource`, expander/text columns,
`TreeDataGridRowSelectionModel`, `ExpandAll`, `BringRowIntoView`, TreeDataGrid,
TextEditor, direct edit commands, document replacement, and undo clearing before
later steps; do not design against remembered Avalonia 11 signatures.

**Verify**: restore/build succeeds with one version of each package and no
binding/type-load warnings.

### Step 2: Add the reusable headless Workbench harness

Initialize one headless App on the dedicated dispatcher thread with the exact
assembly setup/cleanup above, create/show an 800×600 Window through `OnUiAsync`,
and add helpers only for bounded dispatcher/layout draining and public visual
descendants. Every helper closes the Window in `finally`. Reuse Plan 004's fake
desktop host.

**Verify**: a sentinel Workbench window test shows, focuses one control, and
closes without a desktop or leaked component owner.

### Step 3: Integrate native TreeDataGrid and ListBox data surfaces

Add typed models/source/columns, use ObservableCollection directly for normal
changes and `ReplaceAll` only for reset, and wire the exact selection model by
stable unique ID/IndexPath. Preserve stock presenters and bounded layout. Keep
the small existing structural keyed example/tests unchanged.

**Verify**: collection and selection tests pass for add/remove/move/reset.

### Step 4: Prove bounded realization

Populate the fixed test window with 10,000 items, show/layout, count public row
containers, scroll end/back, and assert the evidence contract above. Fail if the
last row cannot be reached or if counts are zero.

**Verify**: `VirtualizationTests` passes repeatedly and reports counts on failure.

### Step 5: Replace the editor placeholder with AvaloniaEdit

Render TextEditor directly, synchronize one `OpenDocument`, preserve one editor
instance, and route focus/edit commands to its native API. Keep event ownership
in generated component handlers and app coordination in `DocumentSession`.

**Verify**: headless test types text, asserts model/dirty update, performs undo/
redo, applies an external text replacement, and verifies control identity plus
clamped caret/selection.

### Step 6: Run the Workbench data smoke and full gates

The native smoke expands the project tree, selects a file, opens its placeholder
text, edits it, navigates to one problem, and shuts down. It does not access the
real filesystem yet.

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

- [ ] Structural keyed regions remain unchanged and explicitly non-virtualized.
- [ ] TreeDataGrid and ListBox keep realized containers below the fixed viewport
      ceiling for 10,000-item tests.
- [ ] Selection survives add/move/reset by application ID where the item exists.
- [ ] AvaloniaEdit is rendered directly with its shipped theme.
- [ ] Editing, undo/redo, dirty state, focus, caret/selection, and editor identity
      pass headless tests.
- [ ] No Lucent item-renderer/recycler/editor abstraction is added.
- [ ] Full build, tests, native smoke, and VS Code tests pass.

## STOP conditions

- TreeDataGrid/AvaloniaEdit types cannot bind as native project controls after
  Plans 003–004; report the exact semantic gap instead of adding wrappers.
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
If pinned TreeDataGrid/AvaloniaEdit versions lack adequate native automation
peers, Plan 006 may replace the direct control types with accessibility-only
subclasses; those subclasses must add peers only and must not mirror control APIs.
Plan 008 may add actual project/document I/O but must keep these native controls
and stable selection contracts.
