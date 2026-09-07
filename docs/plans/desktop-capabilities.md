# Desktop editing, recovery and menus

Accepted September 7, 2026 after the owner review and [fresh architecture/capability audit](../../plans/desktop-capability-review-2026-09-07.md). GitHub issues own live status; this document records the agreed scope and dependencies.

## Execution

| Ticket | Owner and outcome | Dependencies |
| --- | --- | --- |
| [#96](https://github.com/RichiCoder1/lucent/issues/96) | Lucent: application-controlled selection remains authoritative when activation is rejected, delayed or fails. | Independent |
| [#97](https://github.com/RichiCoder1/lucent/issues/97) | Lucent: single-line mouse caret placement/drag selection, text-metric caret/selection geometry, and investigate reported focus loss. | Independent |
| [#98](https://github.com/RichiCoder1/lucent/issues/98) | Light Notes: durable recovery drafts, explicit discard, inline validation and immediate per-collection continuity. | Draft ownership before collection state; consumes #96 |
| [#99](https://github.com/RichiCoder1/lucent/issues/99) | Lucent: live native sizing progress and portable cursor intent with Windows mapping. | Reproduce actual sizing loop before correcting it |
| [#100](https://github.com/RichiCoder1/lucent/issues/100) | Lucent popup/menu foundation, default text-editing menus and authored Light Notes row menus. | #96, #97, #99; app integration after #98 |
| [#95](https://github.com/RichiCoder1/lucent/issues/95) | Explain compound style winners and reconcile stale supported/deferred authoring docs. | Preserve current precedence; update with delivered surface |

## Product contracts

Incomplete titles and invalid URLs are recoverable drafts, not storage failures. Persist their raw content separately from valid saved notes across normal close/reopen. Navigation, capture and other editing continue; invalid URLs disable Open and show inline guidance. Real write failures retain truthful recovery/retry feedback. **Discard draft** removes pending recovery content and restores the last successful valid autosave; earlier autosaves are not version history.

Inbox and Archive each retain selected identity, query and viewport. Cached items appear immediately while refresh runs independently. A query hiding the current note does not replace its editor. Handle moved/deleted remembered items deterministically without losing a draft.

Menus are Lucent-rendered popup windows and must extend outside the owner client from the first slice. Share command availability, invocation, focus restoration, dismissal and semantics across right-click, Shift+F10 and the context-menu key. Default text menus provide undo, redo, cut, copy, paste and select all. Note menus target the clicked row without selecting/opening it; Open is explicit. Start with flat command groups and separators, not a general catalog of submenu/custom-widget behaviors.

The host owns popup windows, DPI, screen placement, input transport and UIA adapters. Portable Core owns menu/command meaning. Popup dismissal must not cancel accepted application work. Keep `.lui` as the primary composition surface and keep command identity independent of the presenter.

## Ecosystem comparison

See [host research](desktop-host-research.md) for SDL, Avalonia, SkiaSharp and Uno comparisons against performance, safe defaults and authoring experience. Use supported lower-level capabilities where they fit current ownership; do not import a full UI framework to obtain a popup. Record adopted inspirations and dependencies in CREDITS.md before using them.

## Verification and delivery

Use failing regressions at the affected seam, focused suites, affected warning-clean builds and formatting. The host/input changes also need published NativeAOT checks of actual typing, menu placement, dismissal/focus, sizing progress and targeted UIA behavior. Serialize desktop drivers and use synthetic isolated data. Geometry after a resize is not evidence of presentation during a held border drag. Do not claim the historical #91 Archive exit root cause is resolved merely because new checks pass.

Commit/publish framework changes, consume the exact resulting prerelease packages in Light Notes, then verify the independently published app. Record actual test results and remaining limitations in the issues without introducing a release gate.

## Future opt-in platform behavior

[#101](https://github.com/RichiCoder1/lucent/issues/101) records a later slice for native platform styles/defaults and presentation adapters across menus, scrollbars and related behaviors. Keep customizable Lucent presentation available. Native menu hosting is more than a style; assess its command, accessibility and custom-content constraints when that work is selected. No second presenter or speculative platform-profile API belongs in this batch.
