# Input routing

Pointer and key routes start at the eligible target and bubble toward its ancestors. A handled route stops there. Cursor intent and context menus use the same nearest-first order: an inner editor keeps its text cursor and editing menu inside a selectable row. An authored menu on that editor overrides its standard editing menu. Right-clicking a row opens its menu without selecting the row.

`InputProperties.Enabled = false` disables input for the subtree while its visible bounds block pointer activation behind it. Use `InputProperties.PointerTransparent = true` explicitly for a subtree that should be skipped by pointer hit testing; this does not disable keyboard focus or accessibility. `Visible = false` removes the subtree from input entirely. These properties are available in `.lui` styles.

`CommandScope` routes focused shortcuts through the nearest matching scope. With no focused control, a single outer command scope owns application shortcuts, including when layout wrappers surround it. Nested scopes do not win by mount order. Multiple independent outer scopes require focus to disambiguate; applications wanting global shortcuts should wrap them in one application scope. A declared binding consumes its chord even while its command is disabled or busy.

Keyboard navigation into a submenu establishes keyboard modality and visible focus in the new popup. Plain Escape bubbles out of an editor; Escape that cancels an active IME composition is consumed by the editor. Hosts reconcile native composition using `InputRouter.HasTextComposition`.

Controlled selection callbacks request application state changes. Pointer activation remains handled even when the application declines selection. Semantic commands distinguish a delivered request from applied selection; see the [UI Automation contract](UIA-MATRIX.md).
