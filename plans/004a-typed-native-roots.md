# Plan 004a: Expose typed native roots for host interop

> **Executor instructions**: Keep `Mount(): Fragment` unchanged. Add only the
> typed interop proven by Workbench's settings and generated-preview windows.

## Status

- **Priority**: P1
- **Effort**: S
- **Risk**: LOW
- **Depends on**: Plans 003 and 004
- **Category**: interop / DX

## Why this matters

Lucent can already author a component rooted in native `Window`, but C# hosts
receive only an untyped `Fragment`. Workbench therefore has two handwritten
`Window` adapters whose main purpose is mounting one Lucent pane and disposing
it on close. A statically typed single-root interface removes that ceremony
without creating a Lucent windowing subsystem.

Research: [Native window composition](../docs/research/NATIVE_WINDOW_COMPOSITION.md).

## Contract

For a component whose rendered fragment has exactly one direct native root,
generate an additional method with the exact native type:

```csharp
public global::Avalonia.Controls.Window MountRoot();
```

Rules:

- `MountRoot()` calls the existing one-shot `Mount()` path and returns that
  mounted root. It does not construct, show, own, close, or dispose a window.
- `Mount(): Fragment` remains unchanged and is still used by component
  composition.
- Generate the method only when static cardinality is exactly one and the root
  is a direct resolved native control. Zero/many roots, structural roots, and
  component-indirect roots receive no approximate method.
- The exact return type participates in generated source and C# design-time
  tooling. No common component interface, reflection, registry, VDOM, native
  wrapper, or runtime helper is added.
- Normal one-shot mount and disposed-owner errors remain unchanged.

## Workbench proof

- Replace `SettingsDialog.cs` with `SettingsDialog.lui`, rooted in native
  `Window` and containing `SettingsPane`.
- Replace `GeneratedPreviewWindow.cs` with `GeneratedPreviewWindow.lui`, rooted
  in native `Window` and containing `GeneratedPreviewPane`.
- Modal settings code holds the generated component in `using`, awaits native
  `ShowDialog`, and therefore disposes after close or failure.
- Modeless preview code retains its generated component until native `Closed`;
  `Closing` cancellation does not dispose it, and a synchronous show failure
  disposes it immediately.
- Continue passing explicit native owners through `IWorkbenchDesktopHost`.

## Steps

### Step 1: Characterize the generated interface

Add failing generated-code and SDK-consumer tests for one direct `Window` root,
one ordinary control root, and zero/many/conditional/component-indirect roots.

### Step 2: Emit typed single-root mounting

Reuse the existing bound root/cardinality result and mounted control field. Do
not independently infer types in the emitter.

### Step 3: Remove Workbench adapters

Move native window declarations into `.lui`, simplify settings and preview
presentation, and retain explicit native ownership and deterministic component
disposal.

### Step 4: Verify lifecycle and tooling

Test modal success/failure, non-modal `Closed`, cancelled `Closing`, show
failure, exact-once disposal, generated-type C# design-time visibility, and
completion/hover for the typed method.

## Scope

Expected compiler changes are limited to the bound root contract, generated C#
emitter, focused compiler/MSBuild/LSP tests, Workbench window sources, and the
language/tooling documentation. `Lucent.Runtime` should not change.

## Done criteria

- [ ] Direct single native roots expose an exact typed `MountRoot()` method.
- [ ] Existing `Mount(): Fragment` output and composition remain unchanged.
- [ ] Unsupported root shapes do not receive an approximate typed method.
- [ ] Workbench has no pane-only handwritten `Window` adapters.
- [ ] Modal and non-modal paths preserve native owners/results and dispose the
      logical component exactly once at the correct lifetime point.
- [ ] Full build, tests, Workbench headless/smoke gates, and VS Code tests pass.

## STOP conditions

- Static root type requires runtime discovery, reflection, or a registry.
- Typed mounting changes the existing fragment composition interface.
- Removing an adapter requires Lucent to duplicate Avalonia window policy.
- Modeless lifetime cannot be retained without a global owner/window registry.
