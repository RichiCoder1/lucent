# Component program execution

Status: authorized September 9, 2026. Parent [#155](https://github.com/RichiCoder1/lucent/issues/155) and children #156–171 own the full baseline component delivery. The accepted ticket contracts remain authoritative; recheck them against the current runtime rather than treating the original audit as current code.

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

## Integration checkpoint — September 9

The first implementation includes Field/FormSession, CheckBox/RadioGroup/Switch,
Tooltip/Popover/Dialog, ListBox/Select, NumberField/Slider, Tabs/Disclosure,
ProgressBar/InlineNotice and Link with an explicit Windows URI-launch service.
The Component Browser compiles and embeds its real `.lui` examples, and its test
project participates in normal affected-project verification.

Verification: 397 Core contracts, 102 Windows contracts and five browser contracts
passed in Release. Browser coverage includes a real Skia render; Windows coverage
includes compound automation patterns, confidential-value rejection and modal
scrim pixels. The compiled Core API/dependency preflight also passed. These are
automated contract checks, not a fresh interactive walkthrough. The computer-use
launch timed out awaiting app approval, so that interaction check did not run.

This checkpoint does not close the component program. PasswordField and the native
file-picker adapter remain in progress (their shared semantic/service contracts
are present). Editable ComboBox, DatePicker/TimePicker, TreeView, TableView, broader
browser examples, consumer adoption and published NativeAOT interaction evidence
remain. Extend tests and documentation with each delivered family; do not infer
their completion from the shared foundations above.
