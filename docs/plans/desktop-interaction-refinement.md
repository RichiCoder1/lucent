# Desktop interaction refinement

September 6, 2026. Active owner-feedback batch following the first Light Notes product/framework review. Live execution status and acceptance live in GitHub issues #91–#94.

## Order and ownership

1. Reproduce the exact long-note fixture freeze and Archive exit (#91). Correct text/layout or lifecycle defects at their owning Lucent/app seam, and retain useful unexpected-exit diagnostics.
2. Correct hover/pressed propagation and presentation (#92): search reload stability, readable composed row press, navigation spacing/text and centered Add. Preserve the incumbent warm light design.
3. Add Windows I-beam and caret timing (#93). Keep hit ownership portable, native resources/platform settings in the Windows adapter, and blink painting independent of scene projection and IME/UIA geometry.
4. Add default interactive scrollbars (#94), shared viewport ownership and replaceable platform/theme visuals; consume them in the .lui collection and multiline editor.

Sol medium handles the long-note/workflow investigation. Luna max handles shared control state/scrollbars and app presentation. The coordinator owns Windows caret/cursor, integration, research, tickets, documentation and delivery. Coordinate shared builds and file ownership; avoid overlapping native interaction drivers.

## Interaction reference

[Avalonia TextPresenter](https://github.com/AvaloniaUI/avalonia-docs/blob/main/api/avalonia/controls/presenters/textpresenter.mdx) separates caret show/hide and configurable blinking. [Flutter Scrollbar](https://api.flutter.dev/flutter/material/Scrollbar-class.html) shares an explicit scroll position with thumb drag and track paging, while themes choose hover/drag colors, thickness and visibility. These are design references, not new framework dependencies or source copies; CREDITS records them.

Follow the user's Windows [caret blink preference](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getcaretblinktime), including steady caret mode. Use the existing SDL owner-thread event loop and system cursors. A blinking caret must not trigger fresh document layout or accessibility projection on every tick.

## Evidence and review

Use exact synthetic content and repeatable input sequences, with focused race/performance tests for task-blocking failures. Compare actual retained-control default/hover/pressed/focus/disabled/typing states, including composed row labels; static screenshots alone were insufficient. An HTML interaction mockup is optional only if the actual shell cannot demonstrate a design choice clearly.

Keep focus-taking work paused until the owner authorizes the coordinated native batch. Background managed tests and offscreen rendering may continue. Inspect representative responsive and interaction states together, fix the identified defects, and confirm once. Record interrupted runs distinctly. No full-release or manual certification gate is added.
