# Editor sessions and responsive participation

An `EditorSession` owns a plain-text draft, selection, undo/redo history and associated `ViewportState` under an explicit `ReactiveScope`. Keep that owner above arrangement-specific branches. Pass the session into a `.lui` text field; moving between branches creates a fresh control mount while preserving the document state.

```lui
internal component Editor(EditorSession session) {
    <TextField session={session} label="Note title" />
}
```

Use `SwitchDocument` for an intentional document change or reset. `SynchronizeExternalText` applies an authoritative update to the current document: equal text is a no-op, changed text clamps selection and clears undo history. `SynchronizeExternalText` rejects a different document identifier, preventing a delayed result from switching the active document. Local changes through `Text` or editing commands remain undoable. Sessions default to single-line input. Create a multiline session with `new EditorSession(owner, documentId, initialText, multiline: true)` and bind it to `TextArea`; `TextField` retains its single-line contract.

## Labels, placeholders and keyboard policy

`label` is the accessible name exposed by the text-field semantic snapshot. `placeholder` is an optional visual hint for an empty, unfocused field and defaults to `label` for compatibility; pass an empty string to disable the hint. For example, `<TextField label="Search" placeholder="Find issues" />` exposes `Search` to accessibility clients while painting the muted `Find issues` hint. The hint is a projection only: it is absent from the committed value, text range, selection, caret and clipboard operations. Focusing an empty field removes the hint before editing begins, so it can never become editable text.

`TextField` leaves the `Tab` key unhandled so the input router owns focus traversal. `TextArea` follows the same key policy; a tab character can still arrive through committed text input or an explicit `Insert` call, but a `Tab` key does not mutate the document or selection. `Escape` follows the normal route and is consumed by an active IME composition only when it cancels that composition.

Control/Meta word movement and deletion use Unicode grapheme boundaries and group letters, numbers, combining marks and connector punctuation as words. Punctuation and symbols share their own navigable run class, while whitespace is skipped between word runs. Visual `Home`/`End` use the shaped wrapped line, while `PageUp`/`PageDown` move by the mounted viewport's line count and preserve the desired horizontal position. Caret movement, selection changes, clipboard operations, line breaks and IME boundaries break typing undo coalescing. Consecutive inserts or same-direction deletes coalesce until one of those boundaries, and a line-break edit always starts a new undo unit.

## Multiline editing

```lui
internal component NoteEditor(EditorSession body) {
    <TextArea session={body} label="Note body" style={Style.Empty.Height(240)} />
}
```

Keep the multiline session in the application model, above responsive branches. It owns canonical LF text, grapheme-safe UTF-16 selection, caret affinity, undo/redo and viewport continuity. CRLF and CR input normalize to LF. A tab remains one source grapheme and renders with a fixed four-space advance; elastic tab stops are not part of this contract. The mounted editor owns preedit, clipboard requests, pointer capture and native focus. Save models read the committed session text; IME preedit is not a durable edit.

The first target is 20,000 UTF-16 code units. Immutable strings remain the document representation, with 64 retained history entries and a cached grapheme-boundary array for each retained text version. Navigation uses the index instead of rebuilding a whole-document grapheme map. A local edit creates one resulting document string; unchanged snapshots and their indices are shared through undo/redo. This bounded note workload does not justify a rope or piece table yet.

Wrapped caret, selection and pointer placement use the paragraph line/cluster geometry. Up/Down preserve a desired x until a horizontal edit/move or changed paragraph layout resets it. Selection and caret updates reuse the shaped paragraph. The editor scrolls its owned viewport to reveal the caret; remounting does not transfer platform focus or in-progress composition.

Timing and allocation results for the 20,000-unit workload are diagnostics in the affected tests, not new release gates. The supported direction/cluster model is the paragraph contract in [ADR 0004](adr/0004-layout-and-paragraphs.md); this is not exhaustive Unicode bidi or real-language IME certification.

## Mount ownership

Only one established text-field mount and one established viewport mount may use their respective state at a time. Transactional branch replacement may briefly prepare a new mount before retiring the old one; this does not move a retained element between parents.

The mounted control owns focus, pointer capture, clipboard requests and IME preedit. Hiding or removing its focused pane cancels preedit without committing it. Those platform resources never migrate with the session. After replacing an arrangement and installing its fresh scene, the application deliberately chooses a focus target through existing input/semantic APIs. Revealing a retained pane does not automatically steal focus.

A `ViewportState` can be supplied to `ScrollViewport` using its `viewport` parameter so an offset survives remounting. Its offset is still clamped against the new content and viewport. A document switch resets the editor session's associated viewport.

## Retained participation

Use `VisualProperties.Participation`, or `Style.Empty.Participation(...)`, independently of the existing routing-only `Visible` property. `.lui` styles expose it as `Participation`.

| Participation | Layout | Painting | Input, focus, accessibility | Ownership |
| --- | --- | --- | --- | --- |
| `Visible` | Normal | Normal | Normal | Retained |
| `Hidden` | Reserves normal space | Subtree omitted | Subtree omitted | Retained |
| `Collapsed` | No space or sibling gap | Subtree omitted | Subtree omitted | Retained |

Ancestor participation applies to the whole subtree. Collapsed elements keep zero-size retained geometry for identity reconciliation. Switching participation preserves element identity and scopes; conditional branches still dispose their old mounts. Invalid enum values fail rather than acquiring accidental behavior. This distinction avoids arbitrary reparenting or mounted-instance handles.

The application determines its arrangement from available container constraints. General container-responsive measurement belongs to the Grid/Flex work; this contract supplies state continuity and participation without introducing a second responsive layout engine.
