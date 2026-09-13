# Desktop component interaction follow-up

Design for [#286](https://github.com/RichiCoder1/lucent/issues/286), following the
correctness repairs in #279–285. This document specifies additions; it does not
claim these keys, paging rules or compact NumberField controls already ship.

## Goals and boundaries

Keep controlled values authoritative, immediate interaction feedback, stable
editor ownership, `.lui` composition and stock theme defaults. Reuse existing
selection, viewport, numeric edit and popup ownership rather than adding a
second selection engine. No new dependency is needed. Windows owns native key
translation; Core owns component policy.

The current code already has PageUp/PageDown keys, fixed-height virtualized
choices, `ViewportState`, requested versus applied selection and decimal step
options. It lacks portable F4, viewport paging in choices and directional step
eligibility. NumberField currently presents two full text buttons. The shared
keyed policy wraps, so list clamping must be an explicit per-family policy,
preserving RadioGroup and Tabs behavior.

## Dropdown keys and editor precedence

| Input | Select, editable ComboBox and DatePicker |
| --- | --- |
| Unmodified F4, first key-down | Toggle the existing popup; closing cancels uncommitted roving choice. |
| Alt+Down only, first key-down | Open if closed; if already open, consume without moving or committing. |
| Alt+Up only, first key-down | Close if open without committing; when closed, leave unhandled. |
| Escape | Cancel IME first, otherwise dismiss a live popup; a closed popup consumes nothing. |
| Tab / Shift+Tab | Dismiss an open popup without committing and continue normal focus traversal. |
| Alt+F4, Control/Meta chords, mixed modifiers | Leave to the existing application/platform shortcut route. |

F4 is appended to the portable enum to avoid renumbering existing keys. Add its
Windows mapping and focused translator coverage. A small internal classifier
can share toggle/open/close decisions; it must not own popup lifecycle or state.
Handle the command in both owner and popup routes where native focus can live.
Repeated toggle keys do nothing; repeated unmodified navigation remains usable.

For Select, retain unmodified Enter/Space/Up/Down opening and explicit Enter or
Space choice confirmation. For editable ComboBox, Space, Left/Right, Home/End,
Shift selection and Control editing shortcuts belong to the editor even when
suggestions are open. Unmodified Up/Down navigate suggestions; PageUp/PageDown
navigate only an open suggestion list. Enter confirms the active eligible
suggestion before considering free text. With no eligible suggestion, free text
commits only when the caller explicitly enabled that existing mode. Escape
dismisses suggestions and preserves the typed query; it is not an implicit
draft reset. IME composition owns all composition-related commands before the
dropdown classifier can act.

DatePicker uses the same toggle/open/close chords, while its open calendar keeps
its existing day movement and PageUp/PageDown month movement. TimePicker has no
dropdown and receives no artificial popup key behavior.

## Collection paging and boundaries

ListBox, Select suggestions, ComboBox suggestions and TableView clamp at their
first/last eligible item. RadioGroup, Tabs and Menu retain their existing wrap
policy. Calendar retains date/month movement and date bounds; NumberField and
Slider retain numeric bounds. Do not globally change `KeyedSelectionPolicy` to
achieve a list-specific behavior.

A page advances `max(1, floor(actualViewportHeight / rowHeight) - 1)` source rows,
retaining one row of context. Use the installed viewport, not the default popup
height or a fixed count of options. Clamp the source target index, then choose
the nearest eligible item in the requested direction; if that side has none,
choose the last eligible item between the start and target boundary. Disabled
rows occupy layout space but cannot become active. An all-disabled or empty
collection remains unchanged. Before the first measured scene, consume paging
without guessing geometry; once measured it uses the current viewport.

Paging moves the roving key, requests the existing ensure-visible operation and
resolves the realized focus target after projection. It must not realize the
whole collection. FollowsFocus mode requests selection once; explicit mode only
moves the active item until confirmation. Rejected or delayed caller requests
do not manufacture an applied selection. Keep these rules across resize,
filtering, offscreen realization and item removal. TableView pages vertically
while preserving its active column.

## Compact numeric steppers

Compose the NumberField editor and two existing icon buttons in `.lui`, using
layout styles and stock theme tokens. Use the same icon/asset mechanism as the
existing date/time editor actions. Keep named actions “Decrease {field name}”
and “Increase {field name}”; their icon appearance must not remove accessible
names, focus indication or minimum control target size. Place actions adjacent
to the editor, allowing the text area to grow and shrink without clipping.

Add a side-effect-free step preview shared by eligibility and invocation.
Evaluate the current parseable draft, configured increment and inclusive bounds.
Disable only the direction that cannot produce a different allowed value.
Malformed, empty, out-of-range or conflicted drafts disable both steppers while
remaining editable and recoverable; do not silently replace them with zero or
the last applied value. Explicit clamp-on-commit remains a separate operation.
Checked decimal overflow makes that direction unavailable. A valid step that
crosses an explicit bound clamps to the bound; a no-op emits no request.

Recheck enabled/read-only/inherited availability and the preview on activation.
Step requests use the existing controlled request path and retain editor
identity. Pointer activation should preserve editor focus/selection and avoid a
blur-triggered second commit; keyboard and semantic actions remain usable.
Delayed or rejected applied values continue to follow NumericEditSession's
explicit reconciliation policy. Preserve culture, nullable and fractional-bound
behavior.

First delivery uses one action per click or keyboard activation. Hold-repeat,
wheel stepping and drag scrubbing are deferred explicitly; do not introduce
timers or alter global Button repeat behavior. Numeric keyboard stepping can be
added separately after the editor's editing shortcut ownership is proven.

## Delivery and verification

Implement in three ordered tickets: [#287 dropdown key routing](https://github.com/RichiCoder1/lucent/issues/287),
[#288 collection paging and family boundary policy](https://github.com/RichiCoder1/lucent/issues/288),
then [#289 compact NumberField actions](https://github.com/RichiCoder1/lucent/issues/289). Each builds on #279–285
without reopening their fixes. Component Browser supplies the stock `.lui`
examples with enough choices to page and numeric bounds/malformed drafts.
Issue Browser supplies a larger list/TableView consumer where relevant.

Use deterministic routed Core regressions for modifiers, IME, controlled-value
acceptance/rejection, disabled items, realized focus and numeric preview parity.
Use Windows translator tests for F4, compiled `.lui` tests for authored controls
and the existing six theme/density capture combinations for numeric geometry.
Do not add sleeps or duplicate whole-application test suites. A final published
desktop smoke covers actual F4/Alt chords, native popup focus, paging after
resize and pointer focus retention. That focus-requiring smoke remains paused
until the user authorizes it. No further user design decision is needed for this
bounded proposal; hold-repeat and new free-text modes remain separate work.
