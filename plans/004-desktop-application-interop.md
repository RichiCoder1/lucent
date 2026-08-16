# Plan 004: Prove the native desktop application shell

> **Executor instructions**: Complete Plan 003 first. Implement only interop
> forced by the Workbench flows below. Run every gate and update the index.
>
> **Drift check (run first)**:
> `git diff --stat 3b27cfe..HEAD -- src/Lucent.Compiler src/Lucent.Runtime src/Lucent.LanguageServer tests examples/workbench docs Lucent.sln`
> Stop if component invocation/current inputs/fragments are not executable or if
> Workbench does not exist as the four-component shell from Plan 003.

## Status

- **Priority**: P1
- **Effort**: L
- **Risk**: MEDIUM
- **Depends on**: `plans/003-component-composition.md`
- **Category**: direction / interop
- **Planned at**: commit `3b27cfe`, 2026-08-16

## Why this matters

Developer tools need commands, shortcuts, focus, menus, dialogs, storage,
clipboard, windows, resources, and grid/navigation properties. Avalonia already
owns those capabilities. Lucent needs two small projection improvements—attached
properties and mount-only native collections—then Workbench should use ordinary
C# and real Avalonia APIs rather than acquire parallel framework services.

## Current state

- Plan 003 supplies cross-file components, current inputs, ordinary methods,
  fragments, slots, and one project semantic model.
- `NativeSymbolResolver` currently resolves instance properties/events/content;
  `GeneralBinder` rejects read-only properties and has no attached-property path.
- Native child syntax currently assumes `Control`; `Window.KeyBindings` is a
  get-only native collection of `KeyBinding`, not a visual child route.
- Workbench after Plan 003 is a static multi-component shell. No framework
  command, focus, platform-service, or windowing abstraction exists.
- Avalonia 12 exposes `KeyBinding`, `ICommand` command sources, native focus
  management, `TopLevel.StorageProvider`, `TopLevel.Clipboard`, owned
  `Window.ShowDialog`, and attached properties. Keep those APIs visible.

## Exact attached-property contract

Add one syntax extension inside a native control:

```csharp
TextBlock {
    Grid.Row: 1;
    Grid.Column: 0;
    KeyboardNavigation.TabIndex: 2;
    AutomationProperties.Name: "Problems";
}
```

Rules:

- A member name containing `.` is an attached-property candidate. Split on the
  final dot; resolve the owner type through the same namespace/import/project
  compilation as native controls. Add a parser production used only before a
  member colon: `qualified-member-name := identifier ('.' identifier)*`, and
  preserve the complete name and segment spans. Dots inside the value remain C#.
- Resolve exactly one accessible, static, non-generic, `void Set<Member>(target,
  value)` method whose first argument accepts the rendered native control and
  whose second argument is the value type. For every supported framework or
  project-defined attached property, require a public static `<Member>Property`
  field whose type derives from `AvaloniaProperty`.
- Bind the value through Plan 002's `CSharpIslandBinder` with the setter's second
  parameter type as `ExpectedType`. Lower directly to the resolved static setter
  using its fully-qualified type; do not use reflection or property-name tables.
- Overload ambiguity, inaccessible setter, incompatible target control, missing
  property field, or assignment conversion failure is a mapped Lucent error.
- Add `NativeAttachedProperty` to `LucentSemanticSymbolKind`. The selected
  setter is the definition/hover target; display/documentation also names the
  validated property field. Add a member-header completion path for `Owner.`
  that works on incomplete syntax and filters setter candidates by conversion
  from the current rendered control. Do not route it through ordinary instance
  property completion or `FindValueMember`.
- Attached values participate in the same static/reactive target methods and
  dependency IDs as instance properties.

## Exact mount-only native collection contract

Support a get-only native collection property only for mount-time C# collection
expressions:

```csharp
Window {
    KeyBindings: [
        new KeyBinding {
            Gesture = new KeyGesture(Key.O, KeyModifiers.Control),
            Command = openWorkspace,
        },
        new KeyBinding {
            Gesture = new KeyGesture(Key.P, KeyModifiers.Control),
            Command = quickOpen,
        },
    ];
}
```

Rules:

- The resolved property must have a public getter, no public setter, and exactly
  one accessible public instance, non-generic, one-parameter `Add(T)` method on
  its return type. Diagnose zero or multiple viable methods.
- The value must parse as Roslyn `CollectionExpressionSyntax` containing only
  `ExpressionElementSyntax`. Reject spread elements with mapped element spans.
  Preserve every expression element's absolute `.lui` span, bind it as a
  separate island with expected type `T`, and emit
  `control.Property.Add(value)` in source order during mount.
- Collection elements must have no reactive dependencies. Diagnose a reactive
  read rather than inventing replacement/diff semantics.
- This feature does not make arbitrary non-Control objects renderable and does
  not change native content metadata. It exists for `KeyBindings` and similarly
  shaped mount-only framework collections proven by Workbench.
- Reject explicit assignment to every other get-only property as before.

## Commands and focus: application code, not runtime API

Create these Workbench-owned types; do not place them in `Lucent.Runtime`:

```csharp
internal sealed class DelegateCommand : ICommand
{
    public DelegateCommand(Action execute, Func<bool>? canExecute = null);
    public bool CanExecute(object? parameter);
    public void Execute(object? parameter);
    public event EventHandler? CanExecuteChanged;
    public void NotifyCanExecuteChanged();
}

internal interface IWorkbenchDesktopHost
{
    Task<IReadOnlyList<IStorageFolder>> PickWorkspaceAsync(Window owner);
    Task SetClipboardTextAsync(TopLevel owner, string text);
    Task<TResult> ShowDialogAsync<TResult>(Window owner, Window dialog);
    void ShowOwnedWindow(Window owner, Window child);
}
```

This is an application boundary, not a Lucent abstraction. The production
implementation calls the owner's real Avalonia 12 storage provider, clipboard,
dialog, and window APIs. The fake records calls/results without a desktop.
Specifically use `owner.StorageProvider.OpenFolderPickerAsync(...)`,
`owner.Clipboard.SetTextAsync(text)`, `dialog.ShowDialog<TResult>(owner)`, and
`child.Show(owner)`. These APIs are not falsely described as cancellable.
Workbench checks its component-lifetime token before starting and after awaiting
them, observes eventual task faults, and suppresses continuation after lifetime
cancellation.

One `DelegateCommand` instance must drive every presentation of one action.
Create Open Workspace, Close Workspace, Quick Open, Show Command Palette, Focus
Sidebar, Focus Editor, Toggle Problems, Settings, and Copy Diagnostic commands
as ordinary Workbench component fields initialized once. Menu items,
`KeyBinding.Command`, buttons, and palette entries receive those same instances.
State/input changes call `NotifyCanExecuteChanged`; do not duplicate predicates.
`WorkbenchApp` declares `IWorkbenchDesktopHost desktopHost` and
`CancellationToken lifetime` as required component inputs. `App.cs` owns the
token source, passes both inputs, cancels it in the same `Window.Closed` handler
that disposes the component, and then disposes the token source. Child components
receive root-created `ICommand` inputs; they never construct duplicate commands.

Use existing language features to capture focusable controls: an authored
private field is assigned from a native `Loaded` event's typed `sender`; its
paired `Unloaded` handler clears the field only when it still references that
sender. Generated owner disposal unsubscribes both handlers. Do not add `ref`,
namescope, or focus syntax. Use a native `TextBox` as the editor focus target in
this plan; Plan 005 replaces it with AvaloniaEdit. Before opening
the palette/settings overlay, store the current focused `InputElement`; on close,
call its native `Focus()` if still attached/focusable, otherwise focus the
editor fallback. Configure modal overlay tab containment through
`KeyboardNavigation.TabNavigation: KeyboardNavigationMode.Cycle`. Tab order and
arrow navigation otherwise remain native.

## Resources and values

- Keep application resources in `examples/workbench/App.cs`: add `FluentTheme`
  and explicit `StyleInclude`/resource instances through Avalonia APIs. Do not
  add Lucent resource syntax or duplicate theme loading.
- Use explicit C# constructors for `KeyGesture`, `GridLength`, icons, brushes,
  and other framework values. Add no target-type convenience conversion unless
  one required Workbench expression cannot be written clearly in ordinary C#.
- Settings and command-palette dialogs are in-window component/conditional
  overlays where practical. The Settings command also opens one concrete
  `SettingsDialog` native `Window` through `ShowDialogAsync`; generated-preview
  opens one concrete `GeneratedPreviewWindow` through `ShowOwnedWindow`. These
  two app-owned C# adapters mount/dispose Lucent pane components and exist only
  to prove native owned-window lifetime, not to wrap Window generally.

## Commands

| Purpose | Command | Expected result |
| --- | --- | --- |
| Restore new test project | `dotnet restore Lucent.sln` | Workbench test assets exist before no-restore gates |
| Baseline | `dotnet test Lucent.sln --no-restore` | Plans 001–003 baseline passes |
| Compiler | `dotnet test tests/Lucent.Compiler.Tests/Lucent.Compiler.Tests.csproj --no-restore` | attached/collection projection tests pass |
| LSP | `dotnet test tests/Lucent.LanguageServer.Tests/Lucent.LanguageServer.Tests.csproj --no-restore` | attached-property tooling tests pass |
| Workbench tests | `dotnet test tests/Lucent.Workbench.Tests/Lucent.Workbench.Tests.csproj --no-restore` | command/host tests pass |
| Workbench smoke | `dotnet run --project examples/workbench/Lucent.Workbench.csproj -- --smoke-test shell` | keyboard shell flow exits 0 |
| Build | `dotnet build Lucent.sln --no-restore` | exit 0, no warnings |
| Full tests | `dotnet test Lucent.sln --no-build` | all tests pass |
| VS Code | `npm test` from `editors/vscode` | all tests pass |

## Scope

**Create**:

- `examples/workbench/DelegateCommand.cs`
- `examples/workbench/IWorkbenchDesktopHost.cs`
- `examples/workbench/AvaloniaWorkbenchDesktopHost.cs`
- `examples/workbench/SettingsDialog.cs`
- `examples/workbench/GeneratedPreviewWindow.cs`
- `examples/workbench/SettingsPane.lui`
- `examples/workbench/GeneratedPreviewPane.lui`
- `examples/workbench/Properties/AssemblyInfo.cs`
- `tests/Lucent.Workbench.Tests/Lucent.Workbench.Tests.csproj`
- `tests/Lucent.Workbench.Tests/CommandTests.cs`
- `tests/Lucent.Workbench.Tests/DesktopHostTests.cs`

**Modify**:

- `Lucent.sln`
- `src/Lucent.Compiler/Syntax/SyntaxNodes.cs`
- `src/Lucent.Compiler/Parsing/Parser.cs`
- `src/Lucent.Compiler/Semantics/NativeSymbolResolver.cs`
- `src/Lucent.Compiler/Semantics/CSharpIslandBinder.cs`
- `src/Lucent.Compiler/CodeGeneration/BoundComponentModel.cs`
- `src/Lucent.Compiler/CodeGeneration/GeneralBinder.cs`
- `src/Lucent.Compiler/CodeGeneration/GeneralCSharpEmitter.cs`
- `src/Lucent.Compiler/EditorIntelligence.cs`
- `src/Lucent.Compiler/LucentSemanticSymbol.cs`
- `src/Lucent.Compiler/LucentCompletionItem.cs`
- `src/Lucent.LanguageServer/LanguageServer.cs` only for protocol shaping
- `tests/Lucent.Compiler.Tests/ComponentCompositionTests.cs`
- `tests/Lucent.Compiler.Tests/ProjectSemanticBindingTests.cs`
- `tests/Lucent.LanguageServer.Tests/LanguageServerProtocolTests.cs`
- `examples/workbench/Lucent.Workbench.csproj`
- `examples/workbench/App.cs`
- `examples/workbench/Program.cs`
- `examples/workbench/WorkbenchApp.lui`
- `examples/workbench/WorkspaceSidebar.lui`
- `examples/workbench/DocumentPane.lui`
- `examples/workbench/ProblemsPane.lui`
- `examples/workbench/README.md`
- `docs/LANGUAGE.md`
- `docs/TOOLING.md`
- `plans/README.md` status only

**Out of scope**:

- Lucent command, focus, dialog, clipboard, storage, resource, navigation,
  windowing, or DI subsystems.
- Arbitrary object renderables, mutable collection-property diffing, templates,
  control themes, namescopes, routed-event options, or broad value converters.
- Real filesystem project loading, persistence, virtualization, AvaloniaEdit,
  accessibility auditing, or general headless UI infrastructure.

## Steps

### Step 1: Characterize attached and collection projection

Add failing parser/binder/emitter/tooling tests for `Grid.Row`,
`KeyboardNavigation.TabNavigation`, `AutomationProperties.Name`, a
project-defined attached property, and mount-only `Window.KeyBindings`.
Include wrong target, overload ambiguity, reactive collection element, missing
setter/property field, incomplete `Grid.` completion, spread collection element,
ambiguous `Add`, and ordinary read-only-property failures.

**Verify**: focused tests fail only for the new projection behavior.

### Step 2: Implement symbol-backed attached properties

Extend member-name parsing without changing ordinary C# island delimiters.
Resolve applicable setter/property symbols through the shared project semantic
base, bind the expected value type, emit direct static setter calls, and expose
the symbols to editor/LSP queries.

**Verify**: compiler and LSP tests pass; generated source contains resolved
`SetRow`/`SetTabNavigation` calls and no reflection/string lookup.

### Step 3: Implement mount-only native collections

Classify eligible getter/Add shapes, parse collection elements with Roslyn,
bind each expected `T`, and emit mount-time `Add`. Keep these nodes out of the
reactive target graph.

**Verify**: generated `Window.KeyBindings` compiles in a temporary SDK consumer;
reactive elements receive a mapped diagnostic.

### Step 4: Add Workbench commands and fakeable desktop host

Implement `DelegateCommand`, the app-specific host interface, production native
adapter, and fakes. Add `InternalsVisibleTo("Lucent.Workbench.Tests")` plus a
project reference from the tests. Pass desktop host/lifetime into WorkbenchApp
and root command instances down as component inputs. Wire those instances into
menu, shortcuts, palette, and buttons. Use explicit Window/TopLevel owners for
every platform operation.

**Verify**: pure tests prove shared identity, one enablement predicate, call
arguments, cancellation behavior, and no global platform state.

### Step 5: Implement keyboard/focus shell behavior

Capture sidebar/editor controls through paired native Loaded/Unloaded handlers.
Implement
palette/settings overlays, prior-focus capture, native focus restoration,
modal tab cycling, Escape close, and fallback focus. Add attached automation
names/IDs now so Plan 006 can audit them without changing structure.

**Verify**: Workbench shell smoke invokes the configured `KeyBinding.Command`,
asserts it is the same command instance used by the matching menu item, opens
and closes the palette, verifies `InputElement.IsFocused` restoration, toggles
problems, and closes cleanly. Defer physical key-routing automation to Plan 006.

### Step 6: Verify all desktop operations

Drive open-folder, clipboard, the concrete Settings dialog, and the concrete
generated-preview secondary window through the fake host. Assert pre-cancel
skips calls, cancellation after a call suppresses continuation, and native task
faults are observed. In one native smoke, invoke only non-interactive paths; do
not automate the OS picker.

**Verify**: Workbench tests, smoke, full solution, and VS Code tests pass.

## Test plan

- Compiler tests own symbol resolution, expected types, diagnostics, lowering,
  and source mapping.
- LSP tests own attached-property completion/hover/definition.
- Workbench unit tests own commands and platform-adapter calls without desktop
  UI. The native smoke owns focus and command routing until Plan 006.
- Do not snapshot whole generated files or test OS picker UI.

## Done criteria

- [x] Attached properties bind through Roslyn symbols and direct setter calls.
- [x] Mount-only native collections cover KeyBindings without general object UI.
- [x] One native `ICommand` instance drives each action everywhere it appears.
- [x] Focus capture/restoration and modal tab cycling use Avalonia APIs.
- [x] Dialog/storage/clipboard/window calls are owner-explicit and fakeable.
- [x] Workbench primary shell flows are keyboard reachable.
- [x] No Lucent runtime/platform-service abstraction is added.
- [x] Full build, tests, Workbench smoke, and VS Code tests pass.

## STOP conditions

- An attached property cannot be resolved from setter/property symbols.
- KeyBindings require making arbitrary non-Control objects renderable rather
  than using the bounded getter/Add projection.
- A proposed feature duplicates Avalonia command/focus/window/resource behavior.
- Platform operations require global state or an adapter gains business logic.
- Focus requires new Lucent syntax; use loaded sender capture or report why it
  cannot meet the Workbench flow.
- A control requires one-for-one Lucent wrapper properties.

## Maintenance notes

Plan 005 consumes AvaloniaEdit and native virtualized ListBox controls through
this native symbol and resource path, not wrappers. Its originally planned
TreeDataGrid dependency was replaced after Avalonia 12 required a commercial
license. Plan 006 moves shell smoke assertions to headless tests and audits the
attached automation metadata.
