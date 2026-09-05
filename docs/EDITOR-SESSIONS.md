# Editor sessions and responsive participation

An `EditorSession` owns a single-line draft, selection, undo/redo history and associated `ViewportState` under an explicit `ReactiveScope`. Keep that owner above arrangement-specific branches. Pass the session into a `.lui` text field; moving between branches creates a fresh control mount while preserving the document state.

```lui
internal component Editor(EditorSession session) {
    <TextField session={session} label="Note title" />
}
```

Use `SwitchDocument` for an intentional document change or reset. `SynchronizeExternalText` applies an authoritative update to the current document: equal text is a no-op, changed text clamps selection and clears undo history. `SynchronizeExternalText` rejects a different document identifier, preventing a delayed result from switching the active document. Local changes through `Text` or editing commands remain undoable. The initial implementation is single-line; the multiline editor is separate work.

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
