# Plan 005: Add scalable collections and third-party control integration

> **Executor instructions**: Do not retrofit virtualization into structural
> keyed loops. Implement and test a separate collection interface.
>
> **Drift check**: `git diff --stat 3b27cfe..HEAD -- src/Lucent.Compiler src/Lucent.Runtime examples tests`

## Status

- **Priority**: P1
- **Effort**: L
- **Risk**: HIGH
- **Depends on**: plans 003 and 004
- **Category**: direction / performance / interop
- **Planned at**: commit `3b27cfe`, 2026-08-16

## Why this matters

Project trees, problem lists, search results, and command palettes can contain
thousands of rows. Lucent's structural keyed loop intentionally realizes native
controls and is appropriate for small arbitrary regions. A real developer tool
also needs Avalonia's recycled, virtualized collection controls and one credible
third-party control integration.

## Current state

- `GeneralBinder.cs:187-225` restricts keyed loops to one collection host and one
  native row root.
- `GeneralCSharpEmitter.cs:932-1150` manages a dictionary of realized controls
  and native child order; it has no recycling contract.
- `docs/DESIGN_REVIEW.md:221-230` explicitly separates keyed structural regions
  from `ListBox`/`ItemsRepeater` virtualization.
- Workbench needs both a large tree/list and source viewing/editing.

Official Avalonia guidance notes that `ListBox` and `ItemsRepeater` virtualize
only under appropriate layout constraints:
<https://docs.avaloniaui.net/docs/app-development/performance>.

## Scope

**In scope**:

- Preserve current keyed loops for small structural UI.
- Design one narrow item-renderer/template seam for native virtualized controls.
- Correctly reset item input, state, classes, context, event handlers, selection,
  and focus when a container is recycled.
- Integrate Avalonia's TreeDataGrid (or the smallest suitable native tree/list)
  for Workbench's project tree/problems.
- Integrate AvaloniaEdit as the third-party control proof without wrapping its
  complete interface.
- Measure realized row count and update breadth on a representative dataset.

**Out of scope**:

- Making arbitrary structural loops virtualized.
- A Lucent data-grid framework, custom text editor, infinite-scroll protocol,
  or general component-value syntax unless the chosen native control requires a
  narrowly specified renderer value.

## Steps

### 1. Characterize structural loops

Add a regression test proving small keyed regions preserve identity and do not
claim virtualization. Document the ceiling in generated/runtime tests.

**Verify**: existing Todo identity tests remain green.

### 2. Spike the native collection seam

Use a C# proof first: feed 10,000 project/problem items into the selected native
control, observe recycling callbacks, selection, focus, and updates. Record the
minimum interface Lucent-generated item content must satisfy.

**Verify**: the spike demonstrates bounded realized controls while scrolling.

### 3. Implement only the proven renderer seam

Bind the minimum item input and owner reset semantics. Keep virtualization,
selection, and scrolling in Avalonia. Generated content may create a fragment
for a realized item but must dispose/reset it on recycle.

**Verify**: tests prove no prior item state, class, event, or context leaks into a
recycled container.

### 4. Integrate Workbench data surfaces

Use the native virtualized control for project tree, problems, and quick-open
results. Preserve selected identity across incremental model updates.

**Verify**: a 10,000-item headless test keeps realized controls bounded and
selection stable after insert/move/remove.

### 5. Add AvaloniaEdit

Reference AvaloniaEdit directly. Pass document text/model through its real
interface, forward focus and commands, and keep editor-specific code in one
adapter module. Do not mirror its properties in Lucent.

**Verify**: Workbench opens a `.lui` file, edits text, and receives a change
notification through the native control.

## Done criteria

- [ ] Structural loops and virtualized collections have distinct interfaces.
- [ ] Recycling cleanup has deterministic tests.
- [ ] Workbench handles 10,000 rows without realizing 10,000 controls.
- [ ] Selection and focus survive model updates where identity survives.
- [ ] AvaloniaEdit is used directly with one narrow adapter.

## STOP conditions

- The design requires every list item to remain mounted off-screen.
- Virtualization logic appears in the Lucent runtime instead of Avalonia.
- The AvaloniaEdit adapter starts reproducing the editor's public surface.
- Stable selection cannot be defined independently from container identity.

## Maintenance notes

Review performance with measurements, not generated-code aesthetics. The key
contracts are bounded realization and complete recycle cleanup.
