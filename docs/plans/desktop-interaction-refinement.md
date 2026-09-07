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

## Text rasterization decision

The bounded offscreen Skia probe rendered the same Segoe UI samples at logical sizes 12–15 px and at 100%, 125%, and 150% scale. It compared the current Antialias path with SubpixelAntialias on the current surface, plus SubpixelAntialias on opaque surfaces declaring RGB and BGR horizontal geometry. Every SubpixelAntialias variant produced byte-identical RGBA pixels at each scale; the RGB and BGR outputs had identical hashes, and no text pixel showed more than a 3% spread between normalized R, G, and B coverage. The opacity-layer copy had the same grayscale-only result.

SubpixelAntialias did change grayscale glyph coverage relative to Antialias: 16,464 pixels at 100%, 22,359 at 125%, and 34,708 at 150%, with maximum channel deltas of 121, 114, and 103 respectively. Those are raster measurements for the fixed samples, not readability or physical-panel results. The probe did not exercise SDL upload, Windows composition, ClearType policy, monitor pixel order, mixed-monitor movement, or panel perception. A physical-panel comparison has not been performed, so the probe cannot establish an LCD benefit.

Keep SKFontEdging.Antialias as the product default. Do not add a global LCD switch based on this offscreen result. Any future LCD experiment must be a diagnostic-only fixed scene with an opaque eligible destination, known panel geometry, no post-raster scaling, and a grayscale fallback; acceptance requires a separate physical-panel comparison.

## Current desktop acceptance status

The local NativeAOT development candidate passed all four maintained Light Notes desktop tests: autosave/reopen, responsive focus with targeted Axe.Windows scans, native cursor/caret phases, and 20,000-character wheel/thumb scrolling through three Archive/Restore cycles. Captures confirmed the warm focused-selection palette and responsive navigation. Pre-CI testing corrected both missing TextArea Scroll support and a provider mapping that incorrectly advertised scrollable text as a scrollbar. Core and COM ABI regressions cover the contract. Official package consumption and final delivery are recorded in issues #91–#94. The original intermittent Archive exit has no confirmed root cause; preserve that uncertainty and the new crash diagnostics.

## Authoring follow-up

The native capture exposed a styling trap: component-provided compound variants outrank authored single-state variants under the documented priority contract. A custom FocusVisible rule alone therefore does not replace the Selectable default for Selected | FocusVisible. Light Notes explicitly styles that combined state for rows and navigation. A future authoring review should assess discoverability and diagnostics for component variant winners before changing global precedence; this batch preserves the existing priority contract.
