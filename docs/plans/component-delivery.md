# Component program execution

Status: delivered September 12, 2026. Parent [#155](https://github.com/RichiCoder1/lucent/issues/155) and children #156–171 are closed. The accepted ticket contracts remain authoritative; the original audit is historical context.

## Owner decisions

- Single-selection ListBox follows keyboard focus by default, with explicit confirmation-based selection available. Select maintains a separate active option and commits on Enter; Escape cancels movement.
- Interactive Popover consumes the outside dismissal gesture by default. Pass-through is explicit. Select also consumes dismissal; modal dialogs retain their existing safe dismissal policy.
- Add `apps/Lucent.ComponentBrowser` as a maintained stock-themed application. It provides searchable component navigation, interactive examples, theme/density/state controls, source displayed from the actual compiled `.lui` example files, copyable examples, and concise usage/accessibility notes.
- Live compilation is excluded. The Design and UI task owns that future epic.

## Execution order and ownership

1. #156 fields/validation and #157 CheckBox/RadioGroup/Switch. Scaffold the component browser alongside them, initially using existing controls.
2. #158 owned Tooltip/Popover surfaces; #159 modal Dialog; #160 ListBox/Select reuse the shared field, selection and surface contracts.
3. #161 NumberField/Slider, #162 Tabs/Disclosure, #163 ProgressBar/InlineNotice and #165 Link.
4. #164 PasswordField and #166 native file/folder selection.
5. #167 ComboBox, #168 DatePicker/TimePicker, #169 TreeView and #170 read-only TableView.
6. #171 completes the gallery, documentation, package/native evidence and relevant adoption in Issue Browser and Light Notes.

Keep portable behavior and semantics in Core, native adaptation in Windows, stock composition `.lui` first, and real application state outside controls. Shared accessibility, input, themes and host files have one integration owner while component-family workers own separate files. Serialize builds and focus tests. Verification follows [the pre-release policy](../agents/verification.md); record actual evidence per delivered slice without inventing a broad certification gate.

Context/injection/navigation #203–222 remains separate work. This component program does not introduce another router, live compiler or product-specific workflow to demonstrate controls.

## Integration — September 9

The implementation includes Field/FormSession, CheckBox/RadioGroup/Switch,
Tooltip/Popover/Dialog, ListBox/Select, NumberField/Slider, Tabs/Disclosure,
ProgressBar/InlineNotice, Link, PasswordField, ComboBox, DatePicker/TimePicker,
TreeView and TableView. Windows provides explicit URI launching and native
open/save/folder selection. Portable contracts do not depend on the Windows host.

The Component Browser compiles and embeds fourteen real `.lui` examples. Its
ordinary managed suite captures every example under stock light, dark and high
contrast with both densities. Issue Browser replaces fixed-choice text filters
with Select; Light Notes adopts Field for URL label semantics while retaining its
workspace-owned EditorSession. Live compilation remains separate.

Computer Use exercised actual Windows open-file multiple selection, save
destination selection and folder selection in the managed browser. Save selection
returned a URI without creating a file. Automated Windows contracts additionally
exercise Grid/Table/ItemContainer through their COM ABI, bounded offscreen
realization, password confidentiality and picker lifecycle outcomes. The desktop
suite includes a published native-dialog cancellation proof.

September 12 integration passes Core 449/449, Windows 120/120 and Component
Browser 11/11, with 84 fresh example captures. All six selected desktop checks
pass against fresh NativeAOT Browser/TestHost outputs: popup dimensions and
lifetimes, password pointer toggling, calendar/suggestion geometry, submenu
placement, modal focus and native file-picker cancellation. The reported visual
defects are fixed in the framework; radio labels and slider alignment were
inspected across all six stock theme/density combinations.

The final implementation is published from `f6fbc4ff29fcd9ab4e5c390825e663fba3d6548a`
as `0.3.0-dev.60.1`; [CI 60](https://github.com/RichiCoder1/lucent/actions/runs/34722441727)
passed managed, NativeAOT and package-only consumer checks. Light Notes independently
adopts it at `48a404d37ff1f1380ab3b549c86d6c46c24f8f07`, with
[App CI](https://github.com/RichiCoder1/light-notes/actions/runs/34723338988) green.
Its local checks passed 22 storage tests, 42 app tests with one opt-in skip, and
three native workflows covering physical Enter capture/autosave/reopen and
responsive URL draft/focus continuity. The physical Enter workflow covers the
TextField command-routing fix made after the component desktop run.

## Final Computer Use walkthrough — September 12

Computer Use became callable in the resumed task. The walkthrough used the real
NativeAOT Component Browser, initially built from `f6fbc4f`, and visited all fourteen
examples. Most interactions used dark/comfortable presentation; representative
light, high-contrast and compact states were also inspected. The complete 84-image
theme/density matrix remains automated evidence, rather than 84 manual passes.

| Example | Observed interaction and presentation |
| --- | --- |
| Buttons | Invocation feedback, disabled controls, centered labels and selected presentation. |
| Text fields | Editing, undo, multiline typing, mouse selection and insertion at a clicked text position; caret aligned with text. |
| Password | Repeated show/hide persisted after release; leaving the field remasked the fake fixture value. |
| Async ComboBox | Three suggestions and a filtered single result sized to their content and trigger; pointer selection applied. |
| Selection | CheckBox, Switch and RadioGroup pointer changes; full radio labels with even spacing. |
| Feedback | Determinate progress, error notice and recovery through Retry. |
| Menus | Right-click, hover-opened submenu, aligned placement, balanced padding, diagonal pointer travel into the child menu and nested invocation. |
| Tooltips/popovers | Tooltip appeared near the pointer with readable wrapped text; interactive popover opened and closed through its button and Escape. |
| Numeric input | Number increment and slider dragging changed values; slider thumb centered on the track. |
| Date and time | Compact aligned calendar, date selection and time increment. |
| Navigation/dialogs | Tabs, list choices, disclosure, typed link action and bounded Review changes dialog with completion feedback. Select sizing required the follow-up below. |
| Tree view | Expansion, child selection and internal scrolling. |
| Native storage | Real Windows open-file selection, save-picker cancellation and folder selection; no file write. |
| Data table | Numeric sorting, switching to 10,000 rows and bounded scrolling with retained headers. |

Native window sizing from 1282 to approximately 953 pixels wide reflowed the
content without a retained black region in the observed frames. This does not
measure animation frame pacing. The application remained responsive and did not
crash during the walkthrough.

[Follow-up #277](https://github.com/RichiCoder1/lucent/issues/277) records the newly
reproduced defects: Select's fixed narrow/tall popup, unbounded gallery navigation,
retained detail/source scroll when changing examples, and delayed owner feedback
after owned-popup state changes. These fixes are committed in `3640331`. Select
geometry and gallery scrolling passed the first native retest. The final native
build then verified immediate Select label updates, Popover open/close feedback,
and dialog completion with owner enablement restored. It required no extra owner
input to repaint the committed choice.

Focused Core surface/list contracts passed 13/13, Windows contracts 121/121 and
Component Browser contracts 13/13, including the 84 captures. Core architecture,
compiled public API and changed-file formatting checks passed. The final NativeAOT
build is `artifacts/component-walkthrough-final/browser/Lucent.ComponentBrowser.exe`,
SHA-256 `48F1396AECAA1014006E6690F690D4C0D337E0010D5AEF32AC8951DB2E0E1DF2`;
its source manifest and verification logs are under `artifacts/component-walkthrough*`.

Computer Use's indexed actions sometimes rejected transient-window elements or
points outside the owner's bounds. Screenshot-guided interaction provided the
pointer checks. Its arrow/Enter injection did not reliably exercise Lucent choice
navigation, although native Windows menu arrows worked. The desktop harness uses
extended scan-code navigation for SDL; this difference is a likely explanation,
not a manually proven keyboard pass. Existing Core and desktop keyboard evidence
retains its own source boundary. This walkthrough does not certify a screen
reader, real-language IME or fresh physical mixed-DPI behavior.

See the [handoff](../agents/remaining-work-handoff.md) for the current build and
follow-up state.

### Additional interaction reports (#278)

The later user recordings exposed paths not caught in that walkthrough. Calendar
weekday labels now center over the same date grid tracks. Tooltip shadows pass
through native hit testing without making the visible description unhoverable.
Select and ComboBox measure the visible popup, including its content padding,
against the live anchor width; owner scene changes reanchor it and unavailable
anchors close their live controller state. An intentional 160px minimum and
intrinsic content can still make a popup wider than a very narrow anchor.

ComboBox suggestions preserve native editor focus, keyboard/text/IME routing and
pointer selection inside the anchor. Core owns suggestion navigation and commit;
the chevron toggles without a focus-change race. Recoverable suggestion failures
offer Retry suggestions and preserve the caller's applied selection.

Integrated Core 459/459, Windows 124/124 and gallery 13/13 pass. Native hit-test
and focus regressions failed before their fixes. Computer Use resumed on the
NativeAOT candidate at `4705924`. Stationary tooltip hover retained the same
surface identity across four samples; moving into its visible body kept it open,
and leaving both body and trigger dismissed it. June and July weekday headers
visually aligned with their date columns. Select matched its trigger width,
followed owner scrolling, closed when the trigger was fully clipped, and reopened
after scrolling back. ComboBox accepted filtered queries, reopened after pointer
selection, and accepted mouse selection/deletion that refreshed all suggestions.

The retest caught two further defects before closure: the applied ComboBox status
updated while its editor retained the shorter query, and an open Select became
offset after restoring a maximized owner. The caption failure was a shared reactive
graph defect: an effect that wrote and reread a derived input cleared its queued
retry but retained a potential-change flag, suppressing the next notification.
Acknowledging the final collected versions now clears that flag. A minimal graph
regression and the complete pointer-selection/first-edit regression failed before
the correction; all 461 Core contracts pass after it. The `cc48a77` native build
passed immediate full captions, first-edit continuity, Beta/Alpha/Gamma selection,
chevron reopening and mouse selection/deletion with refreshed suggestions.

Owner native transitions request one final placement correction after the owner
scene is installed. The final `11bd2a8` correction also sets popup size before
position in both reanchoring and content resize. SDL had constrained the requested
position using the previous, wider size. A native regression failed with the old
order (relative X -632 versus expected 284) and passed with size first; the complete
Windows suite passed 125/125. Core 461/461 and gallery 13/13 results are reused
because this last change affects only native popup geometry. Final Computer Use
then verified maximize/open/restore at the correct width and anchor, followed by
successful pointer selection. The app closed normally and UI testing is paused.

The verified NativeAOT build is
`artifacts/component-input-278-verified/browser/Lucent.ComponentBrowser.exe`,
source `11bd2a8c66dd4eae221458b93e40caeccf634a5f`, SHA-256
`1988530D6D2E63883D1D2B12958FF16E1F74612964A40838D5AA67B685738F46`.
Its source manifest is beside the browser directory. Native publication,
architecture and changed-file formatting checks passed.

The generic Error toggle does not currently
inject a failure into the gallery's ComboBox provider, so suggestion retry remains
Core contract evidence rather than a fresh manual pass. Arrow injection retains
the Computer Use limitation recorded above.
Independent review findings are ordered in #279–286 and the handoff, with source
inspection distinguished from a reproduced failure.

## Review follow-ups with UI testing paused — September 12, 2026

The next batch implements #279–285 in the shared runtime. All desktop input and
focus-taking tests remain paused; earlier #278 Computer Use results are not fresh
evidence for these changes.

| Issue | Correction and regression boundary |
| --- | --- |
| #279 | Only a live tooltip consumes Escape. A tooltip-wrapped dialog action dismisses its tooltip first and cancels the dialog on the next Escape; active IME retains precedence. |
| #280 | Host dismissal ends presentation while an accepted write continues. Success returns its typed accepted value; failure reports the submission failure and settles the dismissed dialog as canceled. A dismissed modal cannot block a new popover before host cleanup. Windows minimize remains dismissal, explicitly documented. |
| #281 | Open calendars recheck live enabled, read-only and inherited availability for pointer, keyboard and semantic date selection; unavailable surfaces close and restored pickers reopen. |
| #282 | Active-drag Escape releases capture and requests the gesture-start value without committing on later release. Idle Escape bubbles; secondary release cannot complete a primary drag. Controlled acceptance, delay, rejection and capture loss remain covered. |
| #283 | Hover over separators or disabled menu items preserves the keyboard-active route, including nested menu navigation and Escape dismissal. |
| #284 | Default TimePicker day-boundary stepping stops at the last reachable step without introducing fractional ticks; explicit fractional bounds remain exact. |
| #285 | Dynamic help notifies the retained editor's semantic relationships. Form errors and first-invalid focus use explicit registration order, avoiding dictionary slot reuse after removal. Valid recovery removes obsolete error relationships. |

The surface regressions failed before correction (closed-tooltip Escape,
dismissed failed session, and replacement popup). Date regressions reproduced
unavailable calendars still requesting dates and midnight steps introducing
`23:59:59.9999999`. Slider/menu regressions also failed on the original behavior;
the existing accepted capture-loss path already passed and remains protected.
Detailed final verification counts are recorded in the handoff and issue updates.

Final automated verification passes Core **486/486**, Windows **125/125** and
Component Browser **13/13** (84 headless theme/density captures). Compiled
architecture/public API and changed-file formatting/diff checks pass. Logs are
`artifacts/review-followups-{core,windows,browser,architecture}.log`. The Windows
project needed a fresh restore after its cached restore state contained a NuGet
connection failure; normal restore succeeded without weakening audit or warning
checks. The focused published desktop smoke remains pending for #279–283,
which remain open rather than implying fresh focus or visual evidence. #284
and #285 are complete with the focused time-boundary and retained-field/UIA
automation; no additional broad walkthrough gates their closure.

Field regressions reproduced stale help and `d,c` invalid-field order after
removing B and appending D. Help notifications now run after the child mount
transaction commits; the ownership guard remains intact. Semantic generations
may advance with metadata changes, so retained-editor checks compare composition
epoch and element identity. The Windows test queries the same retained UIA
provider after help updates and valid recovery through a hidden window. A further
queue regression caught a new surface overtaking a waiting modal; completing the
dismissed request before arbitration preserves modal ordering.

[#286](https://github.com/RichiCoder1/lucent/issues/286) is a design deliverable,
not an assertion of implemented desktop parity. The [interaction design](desktop-component-interaction.md)
defines modifier and IME precedence, per-family wrapping/clamping, viewport
paging, compact bound-aware NumberField actions, free-text confirmation and the
explicit deferral of hold-repeat. Ordered implementation tickets #287–289 are
linked under #286 in Project 4, with native dependencies between the tickets.

### Bounded Computer Use retry

The user reauthorized desktop interaction while in VR and confirmed no
interference. The already-open Debug browser and its Core/Windows assemblies
identified source `1d5bcfa1c8a11ab73a2d191684b6c0da3704d437`. Observed:

- Tab focus displayed the tooltip; Escape dismissed it.
- The calendar retained centered weekday columns and compact geometry. Pointer
  selection changed June 15 to June 16; the immediate capture preceded the final
  owner update, which was confirmed in the next observation.
- A slider drag changed 64 to 40 and retained the centered thumb.
- The date/time example still accepted interaction in its Disabled state because
  the example omitted the availability readers. This is application wiring,
  separate from the Core live-availability repair in #281.

Menu keyboard routing and slider arrow stepping were inconclusive through the
helper. The current examples cannot exercise pending dialog failure or a tooltip
inside a dialog, and the Computer Use API has no held-button/key interleaving for
slider cancellation. #279–283 retain their focused smoke boundary. The helper
failed to launch the published candidate with `accessibility window-opened
handler did not become ready`; this Debug inspection is not NativeAOT evidence.
The browser closed normally after the bounded pass.

The date/time example now supplies retained availability readers to both field
options. Its compiled-example regression failed before the fix with the calendar
still active, then passed dismissal, disabled editor/button semantics and
value-preserving re-enablement. The complete Component Browser suite passes
14/14, including 84 stock theme/density captures; the build is warning-clean.
Logs: `artifacts/review-browser-availability-{red,green}.log`. No fresh published
UI pass is claimed for this final application-only correction.

The CI NativeAOT test-metadata failure is also repaired: the three slider
acceptance cases are named tests over one shared helper, with the test-local enum
removed. Exact Native suite results are Core 486/486, R3 7/7, Skia 80 passed plus
three opt-in skips, and Windows 125/125. See
`artifacts/test/native-enum-metadata-fix-final.log`; package publication still
depends on a fresh successful CI run.
