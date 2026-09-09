# Core components

The public stock recipes live on `Lucent.Core.Components`. The type remains a
single partial class so C# and `.lui` authoring resolve the same component
symbols; the files are grouped by the component family that owns each recipe.

| Family | Source location | Use it for |
| --- | --- | --- |
| Layout | `src/Lucent.Core/Components/Layout` | `Layout`, `Row`, `Column`, and responsive containers |
| Commands | `src/Lucent.Core/Components/Commands` | `CommandScope` and application key bindings |
| Text | `src/Lucent.Core/Components/Text` | Static and live text content |
| Images | `src/Lucent.Core/Components/Images` | `Image` and `Icon`, shared preparation, image placement and accessible intent |
| Buttons | `src/Lucent.Core/Components/Buttons` | `Button`, `IconButton`, and `Selectable` recipes and their stateful presentation |
| Fields | `src/Lucent.Core/Components/Fields` | `.lui` `Field` composition, validation state, and bounded form error coordination |
| Numeric | `src/Lucent.Core/Components/Numeric` | Decimal `NumberField` draft/commit editing and finite controlled `Slider` ranges |
| Text fields | `src/Lucent.Core/Components/TextField` | `TextField`, `TextArea`, editor state, pointer editing, IME and placeholder projection |
| Scrolling | `src/Lucent.Core/Components/Scrolling` | Scroll viewports, virtualized lists and scrollbar presentation |
| Lists | `src/Lucent.Core/Components/Lists` | Keyed `ListBox` selection and noneditable owned-popup `Select` |
| Status | `src/Lucent.Core/Components/Status` | Status and progress recipes, plus `.lui` composites such as `ErrorNotice` |
| Menus | `src/Lucent.Core/ContextMenus.cs` | Context-menu recipes and their shared command, focus and session contracts |
| Split panes | `src/Lucent.Core/SplitPane.cs` | Split-pane recipe and retained resizing/keyboard state |
| Shared | `src/Lucent.Core/Components/Shared` | Mount configuration, validation and state used by more than one family |
| Presentation | `src/Lucent.Core/Presentation` | Shared `ControlThemes` tokens and stock theme values |

The public recipe is the boundary for ordinary application composition. Keep
editor sessions, input routing, reactive ownership, layout, composition and
platform adapters in their own modules even when a component consumes them.
Family implementation files use the internal partial `Controls` type for
mounting and projection details; application code should use the public recipe
or an ordinary `Style` instead of reaching into that helper.

Keep the underlying editing, input, virtualization, resource ownership and
low-level semantic mechanisms in C#. Prefer `.lui` for composition and
presentation built from those primitives, including component-local state and
owned setup through the normal authoring surface.
The `Components/Status/ErrorNotice.lui` composite is the reference shape: its
C# adapter exposes the message reader and retry callback, while the markup owns
the layout, status content and conditional retry button.

## Fields and validation

`Field` owns the persistent visual label, optional help, inline validation,
and the stable accessibility relationships between those elements and one
primary editor. Its editor is a typed factory, so ordinary `.lui` can bind the
field context without reflected property paths:

```lui
<Field
    label="Email"
    fieldId="email"
    required={true}
    help={ReadEmailHelp}
    validation={ValidateEmail}
    session={form}
    editor={field => Lucent.Core.Components.TextField(
        field,
        value: ReadEmailDraft,
        onChangeRequested: RequestEmailDraft
    )}
/>
```

The visual required marker is absent from `FieldContext.AccessibleName`.
`Components.TextField(FieldContext, ...)` designates the primary editor,
shares the field's `FocusTarget`, and adds label, help, error, invalid, and
read-only metadata to the editor's existing semantic declaration. A custom
editor can use `FieldContext.Relationships` and `FocusTarget` in its own single
semantic owner. A second primary `TextField` using the same context fails the
mount transaction.

`ValidationState` is immutable. Use `Valid`, `Invalid(messages)`, or
`Pending(generation, completion)`. `TryComplete` applies a result only to the
matching pending generation, so an older asynchronous response cannot replace
a later draft. Validation messages are expected application outcomes; keep the
editor draft when parsing fails. Inline errors appear after blur or the first
form submit and update while visible.

`FormSession` is optional and scope owned. It registers mounted `fieldId`
values, rejects duplicates, removes registrations on mount disposal, and
focuses the first participating invalid field. The default
`WhenNotCollapsed` participation excludes collapsed retained fields;
`Always` is an explicit opt-in. `SubmitAsync(Reject)` returns `Pending` while
validation is in flight. `SubmitAsync(Await)` awaits only pending states that
declare completion tasks, re-reads once, and otherwise returns `Pending`.
`FormErrorSummary` presents the session's current errors as focusable field
links after submission. The session coordinates validation presentation and
focus only; it does not save data or own application persistence.

## Numeric fields and ranges

`NumberField` is generated from `.lui` and composes `Field`, a controlled
`TextField`, and separately named decrement and increment buttons. Its
scope-owned `NumericEditSession` stores the raw decimal draft, last applied
value, and current validation separately. Enter or blur attempts commit;
Escape restores the last applied value. A malformed or out-of-range draft
remains visible and never invokes the controlled value callback. Parsing and
successful-commit formatting use the frozen culture in `NumericEditOptions`.
Null values, clamp-on-commit, and external-change adoption each require an
explicit option.

`Slider` accepts only a finite increasing range and a positive increment.
Arrow and page keys publish preview requests, Home and End request bounds,
and gesture completion invokes `onCommit`. Its semantic range reports the
applied interaction draft while the caller remains authoritative. Direction,
vertical orientation, and focused-wheel opt-in are explicit `SliderOptions`;
separate NumberField and Slider instances synchronize only when their caller
binds them to the same application value.

## Lists and choices

`ChoiceItem<TKey>` supplies a stable key, accessible label, enabled state, and
an optional visual-content factory. `ListBox` virtualizes fixed-height rows and
keeps the caller's selected key authoritative. Keyboard focus requests
selection by default; `ListBoxSelectionMode.ExplicitConfirmation` keeps the
active key separate until Enter or Space. Disabled choices are skipped, and a
removed or filtered selected key does not cause the component to choose a
replacement. The required-key overload exposes a required single-selection
container; `SelectedKey<TKey>` represents an explicitly optional selection.

`Select` uses the same descriptors and keyed policy in an owned popup. Opening
starts active navigation at the applied key or first enabled choice. Arrow keys
move only the active choice, Enter commits it, and Escape discards movement.
The closed anchor continues to display the applied label while a controlled
request is pending. If the applied key is absent from the current snapshot, it
shows an unavailable-selection state rather than silently displaying the first
choice. Popup dismissal consumes the outside click so it cannot activate the
underlying owner.

When adding a stock component, keep all overloads for one public recipe in its
family directory, keep its internal presentation and mount code nearby, and
preserve the `Lucent.Core` namespace, public `Components` type and metadata. A folder move is
an organization change, not a new public namespace or assembly.
