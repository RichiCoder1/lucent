# Interaction and states

## Capture and retrieval flows

### Capture

1. `Ctrl+N` focuses and selects the capture field without discarding the active editor session.
2. Paste or type a URL or thought. `Enter` activates `Add`; `Shift+Enter` has no special meaning in this single-line field.
3. Blank or whitespace-only input leaves focus in the field and shows an inline validation message.
4. A URL creates a link item with the captured URL preserved exactly. Other text creates a standalone note and supplies the initial title/body according to application policy.
5. The new item becomes the selected inbox row and its editor session opens. Focus moves to the title, selected for quick replacement.
6. Creation is not reported as saved until durable creation completes. A failure keeps the captured text and draft available with `Retry`.

The capture field clears only after durable creation succeeds. This avoids turning a transient storage failure into lost input.

### Search and open

`Ctrl+F` focuses search and selects the existing query. Results update locally across title, URL, and note text. Each row identifies why it matched with a short, escaped snippet when the match is only in URL or body. `Enter` on a focused row opens it in the editor; double-click is an equivalent pointer path. `Ctrl+Enter` in an item with a URL invokes `Open link`.

Changing Inbox/Archive or changing a query does not destroy editor sessions. If the selected item leaves the result set, the editor remains open and the list shows no selected row; `Esc` from the editor returns focus to the search results without discarding work.

### Archive and restore

Archive is explicit and reversible from the Archive collection. Archiving the open item waits for the current accepted write ordering, then moves it out of Inbox. In wide and medium layouts the editor shows a brief archived confirmation and selects the next row if one exists. In compact layout it returns to the collection with focus on the nearest surviving row or the collection heading when empty.

## Keyboard and focus model

| Input | Result |
| --- | --- |
| `Ctrl+N` | Focus/select capture field |
| `Ctrl+F` | Focus/select search field in the active collection |
| `Ctrl+S` | Request immediate save for the active dirty session; no-op with an accessible `Saved` status when clean |
| `Ctrl+Enter` | Open the active item's URL when focus is in its editor or selected list row |
| `Ctrl+Z` / `Ctrl+Y` | Undo/redo in the focused editor field only |
| `Tab` / `Shift+Tab` | Move through visible controls in visual/semantic order; never enter nonparticipating panes |
| `Up` / `Down` in list | Move focus and selection by one row, realizing the target when needed |
| `Home` / `End` in list | Move to first/last result |
| `Enter` on row | Open/focus item editor |
| `Esc` in search | Clear a nonempty query; if already empty, keep focus |
| `Esc` in editor | Compact: return to collection; wide/medium: return focus to selected list row |
| `Alt+Left` | Compact editor: same as Back; elsewhere no application action |

Wide focus order is capture, navigation, search, list, editor title, URL/Open, body, archive/restore. Medium uses the same logical order. Compact collection order is capture, collection switch, search, list; compact editor order is capture, Back, title, URL/Open, body, archive/restore. Save status is announced when it changes but is not a tab stop.

Pointer selection does not create a permanent keyboard-focus ambiguity: clicking a row both selects it and gives the row focus. Selection fill and focus ring are visually distinct so a selected row in an unfocused list remains understandable.

## Editor-session and save states

An editor session is keyed by item identity and outlives any one multiline control mount. It owns the draft title/body, caret and selection for each field, undo/redo history, IME composition state, editor scroll positions, the durable base revision, and the latest local revision.

```text
Clean ──edit──> Dirty ──save starts(r7)──> Saving(r7)
  ^                ^                         │       │
  │                └──edit creates r8────────┘       ├──failure──> Failed(r7)
  └────────save r7 succeeds, no newer edit───────────┘

Saving(r7) + edit(r8) + success(r7) -> Dirty(r8), then coalesced save
Failed(r7) + edit(r8) -> Failed with current draft retained; Retry saves r8
```

| State | Visible treatment | Behavior |
| --- | --- | --- |
| Clean | `Saved 10:42` in muted text | Closing is immediate |
| Dirty | `Unsaved changes` | Debounced ordered save is scheduled; `Ctrl+S` starts it now |
| Saving | `Saving…` with nonblocking progress indicator | Editing continues; accepted writes retain per-item order |
| Saved | `Saved just now`, then timestamp | Announce once politely; transition to Clean |
| Failed | Persistent error strip: `Couldn’t save this item` plus `Retry` and details disclosure | Draft remains editable and in memory; closing negotiates retry, keep application open, or explicit discard |

Completion of an older write never overwrites a newer accepted edit. A success advances the durable revision only for the revision it wrote. Storage errors never clear undo history, selection, or captured input.

## Loading, empty, and error states

| Context | State design |
| --- | --- |
| Startup loading | Keep capture disabled until storage is ready; show the shell and three stable list-row placeholders. Focus stays on the shell heading, then moves to capture only when the user invokes `Ctrl+N`. |
| Empty Inbox | Heading `Your inbox is clear`; one sentence explains capture; focusable primary action `Capture something` moves to the capture field. Do not render an empty virtualized viewport. |
| Empty Archive | Heading `Nothing archived`; explain that archived items can be restored. No primary action. |
| No search results | Heading `No matches for “query”`; `Clear search` action. Preserve the query and open editor session. |
| No selected item | Editor heading `Select an item`; short guidance. The blank editor fields are absent from input and semantics. |
| Startup/storage failure | Replace list content with an error panel containing a plain explanation, `Retry`, and `Open data location` only if that action is safe and supported. Capture remains intact if already entered. |
| Search failure | Keep the previous committed result set, label it stale, and offer `Retry search`; do not replace it with the empty state. |
| Open-link failure | Inline status near the URL with `Try again` and `Copy address`; the note remains editable. |

Loading indicators do not cause pane dimensions to jump. Errors receive programmatic status semantics, but focus moves only when the failed operation leaves no usable focused control.

## Long content and editing details

- The title is single-line editable but visually wraps in the editor's reading state only if the final component design separates display and editing. If it remains an input, it scrolls horizontally and exposes the full value to accessibility clients.
- The multiline body wraps to the editor viewport, retains blank lines, supports grapheme-safe navigation/deletion, clipboard operations, selection, undo/redo, and IME composition.
- The editor scrolls the caret into view without resetting the user's horizontal intent or list scroll.
- Search highlighting never changes stored text and does not split grapheme clusters. In the editor, search matches may be presented later; first scope only requires result snippets.
- URLs display their full accessible value. Visual wrapping may break after URL punctuation and, when needed, at grapheme boundaries. Copy always returns the original string.
- A long save error is summarized in the persistent strip; technical detail is disclosed on demand and may be copied. It must not resize the editor on every retry.
- Archive confirmation and save-status announcements use polite live-region behavior. Validation that blocks capture uses an assertive announcement tied to the field.

## Close and restart

Window close enters negotiated shutdown. Clean sessions close normally. Saving or dirty sessions keep the window available while accepted work drains. If a save fails, the user can retry, cancel close, or explicitly discard the affected draft; closing never treats cancellation of obsolete reads as permission to abandon accepted writes. On restart, the selected item and transient navigation need not return, but every operation previously reported as saved must be durable.

