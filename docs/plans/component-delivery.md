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

The additionally requested final Computer Use walkthrough remains pending because
its runtime is unavailable in this task. These automated results do not claim that
walkthrough, broad manual accessibility, real-language IME or fresh physical
mixed-DPI certification. The [handoff](../agents/remaining-work-handoff.md) identifies
the prepared browser build and outstanding manual scope.
