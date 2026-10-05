# Design artifact validation

October 4, 2026. Scope: isolated HTML simulation and documentation only.

## Component-first revision

The user subsequently requested active-component preview with optional data/state
extensions and a tight edit loop. The current HTML and
[component-preview-authoring.md](component-preview-authoring.md) supersede the
initial catalog-first identity and onboarding. Original captures below are historical.

Sol 6.1 xhigh performed a focused read-only follow-up at main `132fc85a`: typed
factory/requirements metadata, the existing staged preparation/emitter path,
exclusion of preview bodies before semantic projection, generated-host executable
constraints, and sibling source/output isolation. Those findings are incorporated
with source links. Opus's original review was retained, not rerun or represented
as approving the new syntax.

The revised artifact had one initial inspection, one correction batch and one
confirmation round. Checks covered:

- Default TaskCard opens without a variant picker or handwritten registry UI.
- Following switches TaskCard/Badge; switching clears old-component pixels.
- Pin keeps the selected component while the simulated editor changes; Unpin
  follows the latest eligible file. README focus keeps the last component.
- Stop followed by a file change stages that component and remains stopped.
- Authored variants expose only TaskCard's Default/Empty/Complete choices;
  selecting Empty renders the corresponding illustrative empty state.
- UserCard shows its missing `user: User` input and an explicit Add preview data
  action. The starter is labeled proposed/incomplete and does not edit files.
  Closing it returns focus to Add preview data.
- Narrow 360px panel scroll/client widths match at 358px; split 520px widths
  match at 518px and its frame caption remains inside the panel. Wide 960px
  automatic preview, narrow missing data, and split variants were captured.
- Inline JavaScript parses successfully. No browser JavaScript errors were
  observed during the revised flow.

Current captures: `component-wide-review.jpg`, `component-needs-data-review.jpg`,
`component-variants-review.jpg`. The browser's full-page capture produced one
distorted capture despite normal DOM layout bounds; it was replaced using the
ordinary viewport capture, without changing viewport size or product CSS to fit it.

This simulation does not prove automatic activation, compiler syntax, generated
host setup, durable per-variant overrides, native editor code actions or timing.
The new CP-01–CP-12 checks and prior UX checks remain implementation obligations.
Implementation's independently reported native/editor checks and outstanding
compiler fidelity failures are tracked as coordination context only.

## Evidence and limits

The current-source audit and both requested consultations informed the design:

- Sol 6.1 (`gpt-6.1-sol`), xhigh: static source audit, existing handler mapping,
  state/ownership risks and 50 checked source references.
- Claude Code Opus 5.5 (`claude-opus-5-5`), high: successful read-only review;
  returned model usage names the requested model. Seven consultation turns.
- Parent: reconciled the reviews, checked current VS Code guidance through
  Context7 and official documentation, authored the mockup and controlling guide.

The audit baseline was `c6b6482e` in the authoritative main checkout. Later status
from Implementation reports the Stop/settings fix in `132fc85a` and an unresolved
unsaved-source stock-path/configuration-fidelity gate. These later reports are
coordination context, not runtime checks performed by the design task.

No production source, issues, roadmap, release records or tests were modified.
No native app, VS Code extension host, source compiler, package or process-cleanup
test was executed. No claim of delivered unsaved-source support follows from this
mockup. Runtime input, accessibility, IME and installed-editor shortcut behavior
still require the guide's implementation verification.

## Bounded artifact review

Served the self-contained page on loopback and inspected it in Codex's background
in-app browser, as coordinated with Implementation. No app or editor UI was driven.
One initial review identified narrow footer clipping, a third toolbar row and an
overly narrow canvas beside the future inspector. One correction batch and one
confirmation round resolved those issues. No broader visual redesign was attempted.

| Check | Observed result |
| --- | --- |
| Embedded JavaScript syntax | Parsed successfully with Node's `vm.Script`; no execution required for this check. |
| 960px dark layout | Compact toolbar and centered sample; saved `wide-review.jpg`. |
| 360px light error layout | Two deliberate toolbar rows; Start, source action, status and frame caption remain visible. Panel/toolbar scroll width equals client width: 358px. Caption bottom remains inside panel. Saved `narrow-error-review.jpg`. |
| Presentation sheet at 360px | Bounded sheet scrolls internally; accepted frame caption remains inside panel. |
| 520px high-contrast inspector concept | Details are below the canvas, wholly inside panel. Panel scroll width equals client width: 518px. Saved `inspector-review.jpg`. |
| Sample interaction | Adding a task changes the illustrative list from three to four items. |
| Keyboard release | Shift+Escape from the sample text input returns focus to the Interact button. This proves only the HTML model. |
| Source edit simulation | Input is disabled immediately during Updating; acceptance restores the initial three-item sample, demonstrating the fresh-state contract. |
| Stopped intent | Changing appearance after Stop leaves the simulation stopped and stages the request. |
| Aggregate bounds feedback | Applying logical width 8192 at scale 2 produces a visible combination error. |
| Diagnostic action | Open source displays an explicitly illustrative mapped snippet; closing returns to the panel. No real source file is opened. |
| Setup and blocked states | Specific setup action is present. Unconfirmed cleanup disables run/scenario/presentation controls and keeps Output reachable. |
| Future inspector separation | Selecting the sample in Inspect opens read-only details while application input is unavailable. |
| Browser diagnostics | No JavaScript console errors recorded in the inspected session. |

The mechanical Impeccable detector ran once. Its advisories concern flush tab-strip
spacing, bounded panel overflow and overlay border/shadow styling. These were
reviewed in context: the tab is workbench context, the panel deliberately bounds
its independently scrolling content, and overlays need a clear editor boundary.
They were not treated as native-runtime or accessibility proof.

## Remaining implementation checks

Run the guide's UX-01 through UX-14 against actual supported capabilities. In
particular: prove requested/accepted frame identity, current-generation input,
focus/key cleanup, dirty-draft stability, real host navigation, hidden cleanup,
blocked recovery and supported unsaved-source build fidelity. Qualify F6,
Shift+Escape, user keybindings and Tab Moves Focus in the installed editor.

The prototype's accessible DOM sample, simulated F6 behavior, invented timing,
source snippet and inspector metadata deliberately do not implement these runtime
contracts. Ship only the slices that the underlying implementation proves.
