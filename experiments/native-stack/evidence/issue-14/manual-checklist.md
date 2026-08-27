# Issue #14 manual UIA/Narrator gate — passed

Completed by the project owner on 2026-08-27 after the automated proof.

1. Publish with `./run-issue-14-proof.ps1`, then run `NativeStackProbe.exe --issue-browser` from its published directory.
2. In Accessibility Insights Live Inspect, verify the issue browser exposes named Button, Edit, List, ListItem, Text, Group, and Region controls; verify Name, Value, enabled, selection, and keyboard focus update after each action below.
3. With Narrator running, use Tab to reach Search, Ada/Open/Closed/High filters, the Issue list, Title, Save changes, and Retry. Confirm each has the expected role and name.
4. Type `auth` in Search; toggle filters; use ArrowDown/PageDown/End/Home in the Issue list; confirm the selected issue and details are announced.
5. Tab to Title, replace its single-line value, activate Save changes, trigger the failing search, activate Retry, and switch theme. Confirm no control loses its accessible name, value, focus, or enabled state.
6. Record tool version, Windows build, Narrator voice, screenshots/export, and any divergence in this directory before marking this gate complete.

Result: **PASS**. Accessibility Insights exposed the expected Window, Group,
Button, Edit, List, ListItem, Text, and Region controls. Names, values, enabled
state, selection, and focus remained correct through the keyboard walkthrough.
Narrator completed the walkthrough with the configured system-default voice.
No divergence was reported.

Environment and evidence:

- Windows 11 Pro build 26200
- Accessibility Insights for Windows 1.1.2924.1
- Narrator configured system-default voice
- `manual/insights-tree.png`
- `manual/native-window-focus.png`
- `manual/insights-list-item.png`
- `manual/insights-button.png`

The first manual attempt exposed an invalid FragmentRoot vtable and crashed in
`ntdll.dll` with `0xc0000005`. `crash-2026-08-27/diagnosis.md` records the
failure and correction. The passing review used the corrected SDK ABI and the
broad external-query regression recorded by `proof.json`.
