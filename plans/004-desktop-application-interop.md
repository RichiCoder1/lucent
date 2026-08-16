# Plan 004: Prove the native desktop application shell

> **Executor instructions**: Implement only framework work forced by the
> Workbench flows below. Update the index when all gates pass.
>
> **Drift check**: `git diff --stat 3b27cfe..HEAD -- src examples tests docs/adr`
> Stop if component composition is not executable.

## Status

- **Priority**: P1
- **Effort**: L
- **Risk**: MEDIUM
- **Depends on**: plan 003
- **Category**: direction / interop
- **Planned at**: commit `3b27cfe`, 2026-08-16

## Why this matters

Developer tools are keyboard-first desktop applications. Commands, focus,
menus, dialogs, storage pickers, windows, clipboard, resources, and attached
properties are table stakes. Avalonia already implements them; Lucent's job is
to make the native interfaces usable without new language subsystems.

## Current state

- Native controls, CLR properties, events, and content metadata resolve through
  Roslyn (`GeneralBinder.cs:94-228`).
- The POC explicitly lacks attached properties, templates, resources,
  namescopes, advanced routed events, and richer value conversion
  (`docs/poc/0004-native-controls-todo.md:138-166`).
- `MainWindow.cs` imperatively mounts one generated component; there is no
  author-facing top-level/window service seam.

Official Avalonia references:

- Commands and key bindings:
  <https://docs.avaloniaui.net/docs/input-interaction/mouse-and-keyboard-shortcuts>
- Focus: <https://docs.avaloniaui.net/docs/input-interaction/focus>
- Menus: <https://docs.avaloniaui.net/controls/menus/menu>
- Dialogs/storage: <https://docs.avaloniaui.net/docs/how-to/dialogs-how-to>

## Scope

**In scope**:

- Project and bind attached Avalonia properties such as grid placement and
  keyboard navigation.
- Allow ordinary C# command objects and native `KeyBinding`/`MenuItem.Command`
  use from component members/inputs.
- Provide a narrow top-level adapter passed from the host for owned dialogs,
  storage pickers, clipboard, and opening secondary windows.
- Prove focus request/restore and command enablement in Workbench.
- Add only target-typed value conversions needed by these flows.

**Out of scope**:

- A Lucent command bus, global shortcut manager, router, navigation framework,
  DI container, window manager, or cross-platform abstraction over Avalonia.
- Full template/control-theme authoring. Use C# custom controls or existing
  Avalonia resources when the current language cannot express a template.

## Steps

### 1. Define Workbench command flows

Implement commands for Open Workspace, Close Workspace, Quick Open, Show
Command Palette, Focus Sidebar, Focus Editor, and Toggle Problems. One command
object must drive menu, shortcut, palette item, and enabled state.

**Verify**: unit tests prove invocation and enablement are shared rather than
duplicated per UI entry point.

### 2. Complete attached-property and resource projection

Add symbol-aware lowering for attached properties needed by Grid and keyboard
navigation. Provide an explicit way to attach existing Avalonia resources and
styles without inventing CSS equivalents.

**Verify**: project-semantic tests use real Avalonia attached properties and a
project-defined attached property.

### 3. Add the top-level adapter

Pass one host-owned object into the root component for storage picker, clipboard,
owned dialog, and secondary-window operations. Keep platform services out of
global static state and make the adapter fakeable in tests.

**Verify**: tests run each flow with a fake adapter and no desktop process.

### 4. Implement focus behavior

Use native focus APIs. On closing the palette/dialog, restore focus to the prior
control; Tab order and keyboard navigation must remain native Avalonia behavior.

**Verify**: an Avalonia headless or temporary smoke test exercises keyboard-only
open, invoke, close, and focus restoration.

### 5. Extend Workbench

Add a menu bar, command palette, open-folder picker, placeholder workspace tree,
split panes, problems toggle, settings dialog, and persisted window geometry
interface (persistence implementation may remain in Plan 006).

**Verify**: all flows are keyboard reachable and no command behavior is copied
between menu, shortcut, and palette.

## Done criteria

- [ ] Workbench can be operated without a pointer for its primary flows.
- [ ] Commands use native `ICommand`/Avalonia command sources.
- [ ] Focus restoration is covered by interaction tests.
- [ ] Dialog, storage, clipboard, and window operations are host-owned and
      fakeable.
- [ ] Attached properties bind through Roslyn symbols.

## STOP conditions

- A proposed feature duplicates an Avalonia command/focus/window capability.
- Platform operations require a global service locator.
- The adapter grows unrelated application business logic.
- Supporting a control requires one-for-one Lucent wrapper properties.

## Maintenance notes

Qt, Flutter, SwiftUI, and Avalonia all separate semantic commands from their
menu/shortcut presentation. Preserve that separation in Workbench and in tests.
