# Responsive layouts

All dimensions are logical pixels before device scaling. The thresholds follow from minimum useful content widths; they are design inputs to issue #74, not frozen framework constants.

## Content-derived thresholds

| Arrangement | Width range | Derivation |
| --- | --- | --- |
| Wide | 1,060 and above | 24 outer + 184 navigation + 1 rule + 320 inbox + 1 rule + 24 editor inset + 482 editor content + 24 outer = 1,060 |
| Medium | 840–1,059 | 16 outer + 64 navigation rail + 1 rule + 300 inbox + 1 rule + 16 editor inset + 426 editor content + 16 outer = 840 |
| Compact | 480–839 | 24 left + 432 usable field/editor width + 24 right = 480; only one content pane is shown at a time |

The proposed minimum window size is **480 × 520**. The width keeps the capture field, compact back action, save status, and a readable editor line usable without horizontal scrolling. The height leaves room for the 48-pixel command bar, a 48-pixel compact header, and at least 424 pixels of list or editor content. Below that size, the Windows host should enforce the minimum rather than inventing another arrangement.

The layout chooses an arrangement from the shell's assigned content box, not the monitor, window backing pixels, or an unconstrained child's desired size. Crossing a threshold changes composition once; the child must not be able to change the constraint source and create an oscillating breakpoint.

## Shared regions

- **Capture bar:** a single-line field followed by a context-sensitive `Add` action. It accepts a URL or plain thought. It remains available in every arrangement.
- **Navigation:** Inbox and Archive destinations, item counts when known, and a capture shortcut hint. Search belongs with the inbox rather than global navigation.
- **Inbox header:** destination title, result count or loading state, and search field.
- **Item list:** fixed-height virtualized rows with two title lines, one metadata line, selection, focus, and save/error adornments.
- **Editor:** title, optional URL with `Open`, multiline note, last-edited/save status, and archive/restore action.

## Wide: navigation, list, and editor

```text
┌────────────────────────────────────────────────────────────────────────────┐
│ Capture a link or thought…                                      [ Add ]     │ 48
├───────────────┬─────────────────────────┬──────────────────────────────────┤
│ Inbox      18 │ Inbox · 18              │ Questions for the storage spike │
│ Archive      6│ [ Search saved items… ] │ https://example.test/…   [Open] │
│               │                         │                                  │
│               │ ▌SQLite is transactional│ Notes                            │
│               │  docs · edited 2m       │ ┌──────────────────────────────┐ │
│               │                         │ │ Plain-text editor             │ │
│               │  Questions for storage  │ │                              │ │
│               │  note · edited 14m      │ └──────────────────────────────┘ │
│               │                         │ Saved 10:42        [ Archive ]   │
└───────────────┴─────────────────────────┴──────────────────────────────────┘
    184 min             320 min                    482 min
```

Navigation and inbox are resizable only if the later layout evaluation proves a clear keyboard and persistence model. For the first slice they use fixed/content-bounded tracks and give remaining width to the editor. The editor body is the primary vertical scroll owner. The list owns its own vertical scroll viewport.

## Medium: rail, list, and editor

```text
┌───────────────────────────────────────────────────────────────┐
│ Capture a link or thought…                          [ Add ]    │
├─────┬──────────────────────┬──────────────────────────────────┤
│ In  │ Inbox · 18           │ Questions for the storage spike │
│ Ar  │ [ Search…          ] │ https://…                [Open] │
│     │                      │                                  │
│     │ ▌SQLite is transact… │ Notes                            │
│     │  docs · 2m           │ ┌──────────────────────────────┐ │
│     │                      │ │ Plain-text editor             │ │
│     │  Questions for stor… │ └──────────────────────────────┘ │
│     │  note · 14m          │ Saved              [ Archive ] │
└─────┴──────────────────────┴──────────────────────────────────┘
  64          300 min                    426 min
```

The navigation becomes a 64-pixel rail with accessible names and tooltips; text abbreviations in the sketch stand in for icons and are not the intended control labels. Counts move to accessible descriptions or small badges only if they fit without obscuring focus. Editor metadata becomes more compact, but all actions remain present.

## Compact: one content pane

Compact has three route-like views inside one persistent shell: collection, editor, and archive collection. Capture remains above the active view.

```text
Collection                              Editor
┌──────────────────────────────┐        ┌──────────────────────────────┐
│ Capture…             [ Add ] │        │ Capture…             [ Add ] │
├──────────────────────────────┤        ├──────────────────────────────┤
│ Inbox · 18          [Archive]│        │ [‹ Inbox]       Saved 10:42 │
│ [ Search saved items…      ] │        │ Questions for the storage…  │
│                              │        │ https://…             [Open]│
│ ▌SQLite is transactional     │        │                              │
│  docs · edited 2m            │        │ Notes                        │
│                              │        │ ┌──────────────────────────┐ │
│  Questions for storage       │        │ │ Plain-text editor         │ │
│  note · edited 14m           │        │ │                          │ │
│                              │        │ └──────────────────────────┘ │
│                              │        │                  [ Archive ]│
└──────────────────────────────┘        └──────────────────────────────┘
```

Opening an item changes the compact view to editor and announces the new heading to accessibility clients without moving the editor session into a new owner. Back returns to the same collection, query, selected row, and scroll anchor. Archive is reachable from the collection header; Inbox is the corresponding action in the archive view.

## Resize behavior

- Preserve active item identity, draft text, caret, selection, undo/redo, editor scroll, list query, selected row, and list scroll anchor across every arrangement change.
- Finish or explicitly cancel active IME composition according to the editor-session contract before transferring platform ownership. Never silently commit different text because the same session is projected in another arrangement.
- If focused content remains present, keep logical focus on it. If an arrangement removes the focused navigation or list control, move focus to the equivalent destination/editor control and expose the move through the normal focus event path.
- Wide or medium to compact while editing opens the compact editor. Wide or medium to compact while focus is in search/list opens the compact collection.
- Compact to medium or wide keeps the current compact route represented: the selected item reappears in the editor if one exists; otherwise the editor shows its empty state. Focus stays on the equivalent control.
- A pane that does not participate in compact layout also does not paint, receive pointer/keyboard input, appear in hit testing, or appear in the semantic tree. Routing-only `Visible` is not the responsive-participation mechanism.

## Overflow and scrolling

The shell never scrolls as a whole. Capture and pane headers remain fixed. The collection list and editor body are independent vertical scroll viewports. Wheel or trackpad input targets the innermost viewport under the pointer that can still scroll in that direction, then bubbles to an eligible parent. The fixed-height list receives its viewport from the assigned Grid/Flex cell; it must not fall back to the whole window while nested in a pane.

Text wraps within its assigned track. Controls keep their hit target and focus ring inside clipped pane bounds. At 200% scaling, logical thresholds remain unchanged and device rounding must not create a one-pixel horizontal scrollbar or clipped focus ring.

