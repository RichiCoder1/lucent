# Claude Opus 5.5 High review

Read-only consultation on October 4, 2026. Requested model: claude-opus-5-5; requested effort: high. CLI returned success and modelUsage records claude-opus-5-5. Recommendations are advisory; see review-resolution.md for accepted and declined changes.

# Lucent native `.lui` preview: UX critique and recommendations

Sources read: `docs/NATIVE-PREVIEW.md`, `docs/plans/native-preview-panel.md`, `extensions/lucent-lui-vscode/preview-panel.js` and `docs/PREVIEW-SCENARIOS.md` (all at the live checkout), plus `artifacts/preview232-preparation.md` for its input and lifecycle contracts. I made no edits and ran nothing.

## 1. Recommended composition

```
┌ [▸ card/empty — Empty card ▾]  400×300 @1.5× · Dark   [Fit ▾] [◐] [⋯]   ● Up to date  [Reset] [■] ┐
├──────────────────────────────────────────────────────────────────────────────────────────────────┤
│ (one-line banner only when needed:  ⚠ Build failed · CS0103 at Card.lui:3:37  [Open source] [Details ▾]) │
│                                                                                                  │
│                         [ canvas: accepted image / live surface ]                                │
│                         stale ⇒ dimmed + corner chip "Out of date · rebuilding"                  │
│                                                                                                  │
└ footer chip (#232 only): "Click to interact" | "Interacting · Shift+Esc to release" ─────────────┘
```

- **Scenario identity** is a button that opens a VS Code **QuickPick**. It is not a custom combobox in the webview. QuickPick is searchable, accessible and matches the editor's theme, and we don't have to build it. Each item shows the title as the label, the ID as the description, and the project as the detail. The in-panel button always shows `id — title`. The panel only needs one new message, `pickScenario`, with no payload. The extension owns the list.
- **Viewport summary chip** shows the metadata of the **accepted frame**, not the requested settings. Clicking it opens a settings sheet: width, height, a short list of common sizes, and an Advanced group (device scale, contrast, fixture density) with **Apply** and **Discard** buttons. In narrow panels (under about 520px) the toolbar wraps to two rows: identity and status on top, controls below. The sheet then spans the full width and pushes the canvas down rather than covering it.
- **Appearance (◐)** is a toggle in the toolbar that applies immediately. It's a single value, so there's nothing to batch.
- **Display:** `Fit` (shrink only, never enlarge) is the default, with 50/100/200% options. The control is labelled "Zoom (display only)" so nobody confuses it with device scale.
- **⋯ overflow menu:** Rebuild (force), Show output, Open preview settings.

**Why this beats the alternatives:**
- **Current form-row-of-everything:** it gives about 40% of a split panel's height to controls that are rarely touched.
- **Settings sidebar:** it takes width from a panel that is already narrow when split.
- **VS Code `editor/title` actions only:** they're hidden whenever the panel isn't the active editor, and they can't tie an action to a delivery identity.
- **Branded dashboard or gallery:** it competes with the canvas and implies features we don't have.

## 2. Five highest-risk ambiguities and resolutions

**A. Keyboard capture, Escape and Tab (#232).** Lucent apps need Tab for focus traversal and Escape for nested menus, so both must reach the app.
- The canvas is a focusable element with two states:
  - **Focused:** Tab passes through to the toolbar. Enter, Space or a click starts capture.
  - **Capturing:** keys on the allowlist are forwarded to the app, including Tab, Shift+Tab and Escape.
- **Shift+Esc** is the one reserved release chord. It is never forwarded and is always shown in the footer chip. That satisfies "no keyboard trap" because the way out is announced.
- Capture is also released by: clicking outside the canvas, the webview losing focus, the panel being hidden, or the generation becoming obsolete.
- The panel never calls `preventDefault` on chords outside the forwarding allowlist. That way VS Code's own keybindings (Ctrl+P, Ctrl+Shift+P, F1, Ctrl+W) still work as an escape route.
- On release, send pointer-cancel and key-up for every held input, as the preparation doc specifies.

**B. Reset vs. Refresh vs. Rebuild.** Today, Reset and Refresh both start a new compiled generation, so users see two buttons that do the same thing.
- **#231:** keep one button that changes with state:
  - **Reset** when current
  - **Retry** after a failure
  - **Start** after Stop or before the first run
  
  Move "Rebuild (force)" to the overflow menu and the command palette.
- **#232:** **Reset** keeps the same accepted build and frozen snapshot and restarts the worker with a fresh fixture and clock. It does not recompile. **Rebuild** takes a new snapshot, recompiles and rediscovers the catalog. Edits trigger rebuilds automatically, so a manual Rebuild is only needed after a failure or when undeclared inputs changed.
- Every tooltip says plainly that state is not preserved.

**C. Current vs. requested vs. draft.** The webview currently overwrites the form whenever the selection identity changes. The frame snapshot also carries only width, height and scale, so the panel can't truthfully label appearance, contrast or density.
- The frame must carry the worker's echoed **effective presentation**. The summary chip renders only that.
- A pending request shows as a status suffix, e.g. `Applying 800×600…`.
- Drafts exist only in the open sheet. Incoming view updates never overwrite a dirty field.
- If the accepted values change underneath an open sheet, show the note "Current values changed" and offer a Discard button.
- Changing the scenario discards the draft explicitly, because the defaults change.
- The appearance toggle sends the selected presentation, never the draft.

**D. Hidden panel behaviour.** The label `suspended: "Preview paused while hidden"` and the docs' wording "resume when visible again" both imply that execution is preserved. It isn't.
- New label: "Stopped while hidden".
- When the panel is shown again, display the retained image as stale with "Restarting (fresh state)…". Rebuild only if the user left it running; an explicit Stop stays stopped.
- Decline `retainContextWhenHidden`.
- A preview in a background tab counts as hidden. Document this so frequent restarts aren't mistaken for a bug.

**E. Obsolete generation while interacting.**
- When any generation becomes obsolete, the webview immediately dims the image, shows the "Out of date" chip and drops capture back to Focused. The worker's sequence check stays the authoritative guard.
- When the new generation becomes current, capture is **not** re-engaged automatically. Keystrokes must not flow into a freshly reset app without a deliberate click or Enter.
- Diagnostics are cleared at the generation boundary, as already fixed. The banner shows only diagnostics tagged with the current display revision.

**Secondary fixes for the current code:**
- The `#scenario` select is rebuilt with `replaceChildren` on every message, which destroys an open dropdown and keyboard position. Diff it instead (this goes away if the QuickPick replaces it).
- `role="alert"` and `role="status"` are rewritten on every delivery, which causes repeated screen-reader announcements. Only write them when the text actually changes.
- Replace the stack of one-button-per-diagnostic with one primary diagnostic and an "N more" disclosure.

## 3. State/action matrix

✓ = enabled, — = hidden or disabled. "Interact" applies to #232 only.

| State (label) | Image | Scenario | Settings/Apply | Appear. | Zoom | Primary | Stop | Interact |
|---|---|---|---|---|---|---|---|---|
| Setup needed / Untrusted / Remote | none | — | — | — | — | *Open setup* | — | — |
| Loading scenarios | prior, stale | — | — | — | ✓ | — | ✓ | — |
| Building / Rendering | prior, stale | ✓ (supersedes) | ✓ (supersedes) | ✓ | ✓ | — | ✓ | — |
| Up to date (#231) | current | ✓ | ✓ | ✓ | ✓ | Reset | ✓ | — |
| Live (#232) | current, live | ✓ | ✓ | ✓ | ✓ | Reset | ✓ | ✓ |
| Out of date · rebuilding | prior, dimmed | ✓ | ✓ | ✓ | ✓ | — | ✓ | — (released) |
| Failed (one error + Open source) | prior, dimmed | ✓ | ✓ | ✓ | ✓ | Retry | ✓ | — |
| Stopped | prior, dimmed | ✓ (no run) | ✓ (no run) | ✓ (no run) | ✓ | Start | — | — |
| Stopped while hidden | n/a | — | — | — | — | — | — | — |
| Cleanup needs attention | prior, dimmed | — | — | — | ✓ | *Show output* | — | — |

In Stopped, changing a control records the choice but doesn't run anything; Start applies it. "Cleanup needs attention" offers no replacement action until the process tree is confirmed empty, which matches the supervisor invariant.

## 4. Scoped adoption by issue

**#231 (finish, low cost):**
- Add the echoed effective presentation to the frame, and have the summary chip render it.
- Merge Reset and Refresh into the state-dependent primary button, with Rebuild in the overflow menu.
- Reword the hidden-state label and the docs' "resume" wording.
- Switch the scenario picker to QuickPick.
- Replace the zoom number input with Fit and fixed steps.
- Move presentation controls into the Apply/Discard settings sheet.
- Keep the image noninteractive and say so in a footer chip, not paragraph text.
- Show one primary diagnostic with a disclosure for the rest, and stop repeated live-region announcements.

**#232:**
- Add the canvas focus/capture state machine from 2A, including the Shift+Esc release and the footer chip.
- Compute coordinates from the painted rectangle, as in the preparation doc.
- Release capture on obsolete generations, hiding or blur.
- Split Reset (restart only) from Rebuild (new snapshot).
- Snapshot status shows "Includes N unsaved files". It never offers to save.

**#233 (onboarding, a step-by-step checklist, each step with one action):**
1. Workspace trust and local Windows check: say clearly that remote workspaces are unsupported.
2. Tools found and the SDK resolved.
3. Development project chosen from a project picker. Writing `lucentLui.preview` to settings needs an explicit confirmation step that shows the change before it's written.
4. Catalog discovered, showing the scenario count.

It never runs the app's entry point and never writes source files.

**#243 (future):**
- Inspect is an explicit toolbar toggle. Only in inspect mode do pointer events go to the inspector instead of the app; outside it there are no hover overlays.
- The details pane is collapsible, sits to the right (below when narrow) and is read-only.
- Don't add placeholder UI or menu items before the APIs exist.

**Decline:**
- `retainContextWhenHidden` and any "pause" or "resume" with preserved execution
- Hot Reload or preserving state across rebuilds
- Device-frame or window-chrome mockups
- Implying IME, UIA or native-chrome parity
- WASM
- A duplicate source editor in the panel
- Generating fixtures automatically, or guessing a scenario from the active editor without an explicit mapping
- A gallery of several scenarios at once
- Saving or exporting PNGs (that writes files; revisit after #233)
- Making preview appearance follow the VS Code theme by default
- Writing settings without confirmation
- Branded chrome

## 5. Open decisions

Defaults I'd take without asking:
- **Persisting the scenario:** remember the last scenario per project in `workspaceState`; that isn't a source write.
- **Pointer behaviour (#232):** the first click is forwarded to the app and also starts capture.
- **Sizes and naming:** the narrow breakpoint is about 520px, and the forced action is labelled "Rebuild".

One decision is genuinely open for the product owner. Scenario descriptors already carry an explicit project/document/component origin. Should a future catalog version expose it, so the panel can offer "Scenarios for the active component" and "Reveal registration"?
- **Default:** no, not in #231–#233. Treat it as a #243-adjacent design question, because exposing origin widens the catalog contract and the trust surface.

Nothing else here needs another round with the product owner.
