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
| Password | `src/Lucent.Core/Components/Password` | Controlled confidential editing, grapheme masking, deliberate reveal and bounded history |
| Text fields | `src/Lucent.Core/Components/TextField` | `TextField`, `TextArea`, editor state, pointer editing, IME and placeholder projection |
| Scrolling | `src/Lucent.Core/Components/Scrolling` | Scroll viewports, virtualized lists and scrollbar presentation |
| Lists | `src/Lucent.Core/Components/Lists` | Keyed `ListBox`, noneditable `Select`, and editable asynchronous `ComboBox` |
| Selection | `src/Lucent.Core/Components/Selection` | CheckBox, RadioGroup and Switch with controlled values |
| Surfaces | `src/Lucent.Core/Components/Surfaces` | Owned Tooltip, Popover and modal Dialog |
| Navigation | `src/Lucent.Core/Components/Navigation` | Retained Tabs and Disclosure |
| Dates | `src/Lucent.Core/Components/Dates` | Gregorian DatePicker and culture-aware TimePicker |
| Trees | `src/Lucent.Core/Components/Trees` | Keyed hierarchy, owned lazy children and fixed-height virtualization |
| Tables | `src/Lucent.Core/Components/Tables` | Read-only virtualized rows, caller-owned sorting and column sizing |
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

Changing a mounted field's help reader refreshes the primary editor's semantic
help without replacing its identity. Returning validation to valid removes its
error relationships and invalid state.

`ValidationState` is immutable. Use `Valid`, `Invalid(messages)`, or
`Pending(generation, completion)`. `TryComplete` applies a result only to the
matching pending generation, so an older asynchronous response cannot replace
a later draft. Validation messages are expected application outcomes; keep the
editor draft when parsing fails. Inline errors appear after blur or the first
form submit and update while visible.

`FormSession` is optional and scope owned. It registers mounted `fieldId`
values, rejects duplicates, removes registrations on mount disposal, and
focuses the first participating invalid field in registration order. Unmounting
removes its place; a later mount appends, even when it reuses a prior identity.
Reordering retained visual children does not reorder form registration. The default
`WhenNotCollapsed` participation excludes collapsed retained fields;
`Always` is an explicit opt-in. `SubmitAsync(Reject)` returns `Pending` while
validation is in flight. `SubmitAsync(Await)` awaits only pending states that
declare completion tasks, re-reads once, and otherwise returns `Pending`.
`FormErrorSummary` presents the session's current errors as focusable field
links after submission. The session coordinates validation presentation and
focus only; it does not save data or own application persistence.

## Selection, navigation and owned surfaces

CheckBox uses typed `CheckState` with an explicit two- or three-state cycle.
Switch is a binary value. RadioGroup uses stable keys and a roving keyboard
target. These controls request changes; they never treat a callback as proof
that the application accepted a new value. Disabled options remain readable
and are skipped by keyboard movement.

Tabs separates the active header from the applied panel. Activation and panel
retention are explicit options; retained inactive panels leave input and
accessibility traversal. Disclosure exposes expanded state and returns focus
before hiding focused content. These are local presentation controls, not an
application navigation service.

Tooltip is descriptive; put interactive content in Popover. Anchored surfaces
use the owner's popup lifecycle and may extend outside the application window.
Hover tooltips open near the latest pointer position when their delay expires;
they stay at that position while open. Keyboard-triggered tooltips use the
focused element as their anchor. Both remain within the monitor's work area.
Popover consumes the dismissing outside gesture by default; pass-through is
explicit. Dialog adds modal focus containment and typed completion. Expected
asynchronous submission failures remain in an attached dialog for recovery.
Host dismissal (including Windows owner hide/minimize) closes presentation; it
does not cancel an application write already in progress. That write still
reports its submission outcome: success completes the typed dialog as accepted,
while failure completes the dismissed dialog as canceled. Reopening after it
settles starts a new session. Minimize is dismissal, not hide-and-restore of the
dialog. Late completion cannot revive a removed owner. A visible tooltip owns
the first Escape; once closed, it passes Escape to the focused control and its
ancestors. Active IME composition retains precedence. The examples show explicit initial
focus, cancellation and destructive-action configuration.

ProgressBar supports determinate and indeterminate presentation. InlineNotice
keeps a durable message, severity icon and optional named action in the layout;
visibility and retry policy belong to the application. Link exposes a semantic
link and requests URI launching through the explicitly provided capability.

Status and InlineNotice remain silent by default. Opt into
`announcement: SemanticAnnouncement.Polite` for an important changing status.
Repeated writes before an owner refresh coalesce to the final text, initial
mounts and equal values are suppressed, and Windows uses UIA's MostRecent
notification policy with a stable activity identifier. This is notification
replacement per owner refresh, not a timed debounce or an assertive announcement.
The framework creates no polling timer, and a client decides when to speak it.

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
Step snapping rounds midpoint quotients away from zero and treats the single
representable `double` immediately below a midpoint as that midpoint. This
one-ULP tolerance keeps decimal-looking steps intuitive, such as `0.3` with a
`0.2` increment, while values at least two representable quotients below the
midpoint round down.
Arrow and page keys publish preview requests, Home and End request bounds,
and gesture completion invokes `onCommit`. Its semantic range reports the
applied interaction draft while the caller remains authoritative. Direction,
vertical orientation, and focused-wheel opt-in are explicit `SliderOptions`;
separate NumberField and Slider instances synchronize only when their caller
binds them to the same application value.
Escape during a captured drag requests the value from gesture start, releases
capture and prevents the later button release from committing. The caller still
decides whether to accept that rollback request. Idle Escape bubbles to the
surrounding UI; a secondary button release cannot complete a primary drag.

## Password fields

`PasswordField` composes the ordinary `Field` label, help and validation
relationships with a confidential specialization of the single-line editor.
The value remains controlled: edits call `onChangeRequested`, while the caller
decides when the applied value changes. Masking emits one bullet per grapheme,
copy and cut never create clipboard requests, and paste remains available.

The Show password action deliberately reveals the draft only while focus stays
with the editor. Losing focus or removing the mount remasks the field and clears
its bounded undo and redo history. Password semantics expose the accessible
name, relationships, password state and SetValue action without exposing Value
or text-range content. Confidential shaping bypasses Core and Skia retained text
caches, including while the value is revealed.

The policy reduces accidental retention; it does not provide secure erasure of
managed strings. Applications must use non-echoing validation messages and must
not place password values in logs, diagnostics or exception text. The framework
does not impose password complexity rules.

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

`ComboBox` adds an editable query without conflating it with the applied
selection. Supply a selected key and label through `ComboBoxSelectedItem`, so
the applied caption survives an unrelated suggestion result. The provider
returns typed success or recoverable failure results; Lucent owns debounce,
cancellation and generation checks. Composing IME text does not trigger a
suggestion request until committed. Selection is required by default; free text
requires both an explicit option and a separate callback. Escape restores the
applied label. Expected provider errors have retry UI; unexpected exceptions
follow the normal application failure policy.

## Example application

Run `dotnet run --project apps/Lucent.ComponentBrowser` to browse the maintained
component examples. They are compiled `.lui` files embedded as resources: the
source view and copy action use those same bytes. Examples demonstrate controlled
values, validation, asynchronous outcomes, menus, surfaces, navigation, data
controls and platform capabilities with stock themes and densities. Helper
dependencies are identified beside examples that require additional source.
Live editing and compilation belong to a separate future feature.

## Read-only tables

`TableView<TKey, TItem>` takes keyed application items and a snapshot of typed
`TableColumn<TItem>` descriptors. Columns supply a stable key, heading, text
reader and bounded initial width. Only realized rows read cell text; fixed row
heights keep the viewport bounded for large collections. This data control is
separate from the style-driven layout grid.

Selection remains caller-owned through `SelectedKey<TKey>`. Keyboard focus
requests selection by default; explicit confirmation is an option. Sorting
publishes a `TableSort` request and never changes the source collection itself.
Applications apply their own sort/filter and retain selection by key. Removing
the selected row does not silently select a replacement.

Column dividers support pointer dragging, arrow keys, Shift for larger steps,
Home/End for bounds and accessibility range commands. Headers and cells share
the same retained widths; horizontal scrolling keeps them aligned. The initial
contract supports text cells and row selection. Cell editing, merged cells,
formulas and variable row heights are outside this contract.

Windows exposes Grid/Table and cell/header metadata for the table, and indexed
ItemContainer enumeration for virtualized choice, tree and table collections.
Offscreen access realizes a bounded row on the owner thread and can scroll the
viewport. Selection queries can realize the selected logical row. Enumeration
currently accepts the standard next-item request (`propertyId = 0`); arbitrary
property searches and a provider for every offscreen item are not implemented.
Retained providers for rows that leave realization report unavailable.

When adding a stock component, keep all overloads for one public recipe in its
family directory, keep its internal presentation and mount code nearby, and
preserve the `Lucent.Core` namespace, public `Components` type and metadata. A folder move is
an organization change, not a new public namespace or assembly.

## External capabilities

`Link` takes a label/content and an explicit action; it never starts a browser
while rendering. Inject `IUriLauncher` when the action should leave the app:

```csharp
ValueTask<UriLaunchResult> LaunchAsync(Uri uri, CancellationToken cancellationToken = default);
```

The Windows implementation requires an explicit `UriLaunchPolicy`, such as
`new WindowsUriLauncher(new UriLaunchPolicy(["https"]))`. An empty allowlist
denies every URI; there is no implicit permissive default. The optional policy
predicate can further constrain the destination. Outcomes are `Launched`,
`Canceled`, `Unsupported`, `Denied` and `Failed`. `Launched` means Windows accepted
the handler request, not that the page loaded; cancellation cannot undo a launch.
The Component Browser's navigation example demonstrates both a local Link action
and an injected external documentation action.

Inject `IFilePicker` for selection without file I/O:

```csharp
ValueTask<FilePickerResult> OpenFilesAsync(OpenFileOptions? options = null, CancellationToken cancellationToken = default);
ValueTask<FilePickerResult> SaveFileAsync(SaveFileOptions? options = null, CancellationToken cancellationToken = default);
ValueTask<FilePickerResult> PickFolderAsync(PickFolderOptions? options = null, CancellationToken cancellationToken = default);
```

Null options use platform defaults; Open selects one existing file unless
`AllowMultiple` is true. `FilePickerFilter` accepts extensions such as `txt`, `.md`
or a sole `*`, not arbitrary wildcard paths. `InitialDirectory` and a save
`SuggestedName` are optional. Results distinguish `Selected`, `Canceled`,
`Unsupported` and `Failed`; only `Selected` contains `FilePickerItem(Location, Name)`
values. Selection grants no guarantee of later access. Save chooses a destination
without creating a file, and cancellation is an ordinary result.

Windows lifecycle code can inject `new WindowsFilePicker(session.Composition)`;
without a mounted Windows host it reports `Unsupported`. The adapter owns the
native STA dialog, serialized requests, cancellation and owner shutdown. See
[Windows presentation](WINDOWS-PRESENTATION.md#native-file-and-folder-selection)
and the compiled Storage example for the complete application wiring.

## Date and time fields

`DatePicker` controls a nullable `DateOnly` and `TimePicker` controls a nullable
`TimeOnly`; neither API represents an instant or performs timezone or daylight
saving conversion. `DatePickerOptions` freezes an explicit culture and installs
its Gregorian calendar for the first delivery. `TimePickerOptions` freezes the
culture that selects 12-hour or 24-hour formatting. Null clearing, inclusive
bounds and the positive time step are explicit options.
Stepping stops at the last reachable step at an implicit day boundary; it does
not wrap or introduce fractional seconds. Explicit fractional bounds remain
exact clamp targets.

Both pickers retain malformed text as an editable draft and publish only parsed,
in-range values. The caller remains authoritative after a request.
`DateTimeFieldOptions` groups optional help, application validation, form
participation, enabled/read-only readers and field style without duplicating
those parameters across both picker APIs.

The date surface uses the culture's first day of week, localized month, weekday
and full spoken day labels, and a six-week Gregorian grid. Arrow keys move by a
day or week, Home and End move to culture-specific week edges, Page Up and Page
Down preserve the focused day where possible across months, Enter commits, and
Escape dismisses through the owned surface. Today, selected and focused dates
have separate presentation, while out-of-range days remain visible and disabled.
An open calendar rechecks live enabled, read-only and inherited availability
before accepting a date, and closes when its owner becomes unavailable.

Ranges, recurrence, alternate calendar systems, timezone selection and implicit
daylight-saving conversion are outside this delivery.

## TreeView

`Components.TreeView<TKey,TItem>` flattens the currently expanded keyed hierarchy through the fixed-height virtualization path. Supply roots and a `TreeDataSource<TKey,TItem>` for stable keys, labels, current children, child affordance, enabled state, and optional lazy loading. Selection and expansion remain controlled application values; keyboard focus keeps a separate local active key.

```csharp
var source = new TreeDataSource<string, FileNode>(
    node => node.Id,
    node => node.Name,
    node => node.Children,
    node => node.IsFolder,
    node => node.CanSelect,
    async (node, cancellation) =>
        TreeChildrenResult.Success(await files.Children(node, cancellation))
);

Components.TreeView(
    "Workspace files",
    () => model.Roots,
    source,
    () => model.Selected,
    model.RequestSelection,
    model.IsExpanded,
    model.RequestExpansion
);
```

Up and Down move through visible nodes without wrapping. Right expands a collapsed parent or enters its first visible child. Left collapses an expanded parent or returns to its parent. Enter and Space confirm the active selection when `TreeViewOptions.SelectionMode` is `ExplicitConfirmation`; the default requests selection as focus moves. Collapsing a focused descendant returns focus to the nearest retained ancestor.

A null result from the data source's children reader means children are not loaded. Lucent owns the optional asynchronous request, cancels pending generations on collapse/removal, ignores stale completion, and renders typed `Failed` results with an explicit Retry action. Unexpected loader exceptions follow the normal failure policy. Automation exposes the visible flattened count and can request a zero-based item index for offscreen realization; realized tree items retain sibling position, set size, hierarchy level, and flattened collection index.
