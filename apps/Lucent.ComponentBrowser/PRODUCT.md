# Lucent Component Browser

<!-- impeccable:product-schema 1 -->

## Platform

Windows desktop, authored with Lucent `.lui` and the Windows host. This is a maintained native reference application for the stock component library.

## Users and purpose

Lucent authors need a quick way to find a stock recipe, try it with ordinary pointer and keyboard input, and copy a truthful `.lui` starting point. The browser is a small working gallery that makes the existing component surface legible while new component families arrive.

## Capabilities and constraints

- Searchable component navigation with family, usage, and accessibility notes.
- Typed `/examples/{id}` routes with Back/Forward history and keyboard Back.
  The browser shell retains search, density, theme and split-pane state. Leaving
  an example disposes its local state; returning mounts a fresh example.
- Interactive examples built from real existing Lucent controls. Examples preserve ordinary hover, pressed, selected, disabled, loading, and error states.
- Numeric, date/time, password, asynchronous combo box, navigation, list, select, tree, link, dialog, table, and native storage selection examples are added as each public contract lands.
- Theme switches for the stock light, dark, and high-contrast themes; a comfortable/compact density switch; and example state switches for default, disabled, busy, and error scenarios.
- Source display and copy use the actual `.lui` files compiled into the application as embedded resources. The browser does not compile user-entered source at runtime.
- Start with the current stock controls and add family examples as those controls land. Live compilation and an editor workflow belong to a future epic.
- Use `ControlThemes`, `PresentationStyles`, and stock component recipes. Application styles provide layout, spacing, clipping, and typography composition only; they do not replace theme tokens or control-state decoration.
- Keep portable component concepts in Core and Windows-specific clipboard/presentation integration in the application or host boundary.

## Authoring and ownership

`ComponentBrowserApplication.lui` is the generated application root. Its `Router`
owns the navigation session and provides it to the shell. The startup callback in
`Program.cs` contributes the framework-controlled theme and borrowed native
picker/launcher capabilities before that root mounts.

`ComponentBrowserState` holds only browser-wide preferences, search, copy feedback,
native capabilities and navigation authority. The catalog associates each source
file with its generated typed route reference. `RouterOutlet` mounts the component
type associated with the matched generated route; example selection does not use
a manual route/recipe registry or a chain of conditional branches.

Simple example state lives in `.lui` declarations and setup. Involved fixture data,
sorting, asynchronous native services and dialog coordination live in adjacent
per-example C# helpers created by their `.lui` owner. Popup bodies share that
example's state. Pending native operations are canceled when their example retires,
and scope-owned dispatch prevents late completion from updating a retired example.
These helpers are part of each example's implementation, not state retained by
the browser. Hoist state explicitly only when a product needs it to survive leaving
a route; this gallery deliberately demonstrates the default mounted lifetime.

## Product principles

1. Show the real recipe doing useful work before showing its source.
2. Keep the source, example, and notes in one selected context so authors can move from discovery to adoption without losing place.
3. Make state and appearance changes visible, reversible, and keyboard reachable.
4. Treat accessibility as part of the recipe: every interactive example has a meaningful accessible name and state explanation.
5. Prefer a small, honest gallery over an editor that promises runtime compilation it cannot provide.

## Evidence on hand

The maintained gallery is composed from existing `Button`, `IconButton`, `Selectable`, `TextField`, `TextArea`, `PasswordField`, `ComboBox`, `NumberField`, `Slider`, `DatePicker`, `TimePicker`, `ListBox`, `Select`, `TreeView`, `Tabs`, `Disclosure`, `Link`, `Dialog`, `TableView`, `Status`, `Progress`, `ErrorNotice`, `ContextMenu`, `MenuItem`, and controlled `Popover` recipes. The storage example receives `IFilePicker` from the Windows application boundary and reports typed selection outcomes without writing files. The `.lui` sources are included in both the compiler's `AdditionalFiles` and the app's embedded resources, so the source view is tied to the files that produced the examples.
