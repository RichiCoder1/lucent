# Native preview review resolution

October 4, 2026. Consultations requested by the user:

**Later user refinement:** component preview is the primary editor experience;
scenarios are internal infrastructure and optional data/state variants. The revised
[authoring design](component-preview-authoring.md) and interaction guide supersede
catalog-first discovery, mandatory handwritten development-project setup and the
earlier recommendation to never follow editor selection. Follow is now default,
with explicit Pin. Existing lifecycle/currentness protections remain.

The original Opus review below has not been rerun for this refinement. Sol 6.1 xhigh
provided a focused follow-up source check at `132fc85a`; its compiler/projection,
generated-host and isolation findings are incorporated in the authoring design.

- Claude Code: explicit `claude-opus-5-5`, `--effort high`, read-only Read/Glob/Grep
  tools. CLI returned success after seven turns; returned model usage names
  `claude-opus-5-5`. [Review](opus-review.md).
- Codex subagent: `gpt-6.1-sol`, `xhigh`, bounded static audit. It owned only
  [implementation-audit.md](implementation-audit.md). No runtime edits or UI tests.

## Adopted

Both reviews supported compact VS Code chrome, progressive presentation settings,
separate accepted/requested/draft state, native Quick Pick, explicit stale input
rejection, sticky Stop, and clear fresh-state behavior after hide/show. The design
also adopts mapped diagnostic actions, one visible reset control, a separate
display fit mode, and a future-only read-only inspector.

Sol found concrete current-source issues: saved-only images say “Up to date”; the
snapshot omits frame provenance; cross-field size rejection is silent; changing
controls restarts a stopped preview; stopped status precedes cleanup completion;
an initial scenario ID is needed before users can browse the catalog; and status
updates replace interactive DOM. These are documented with source links in the
audit and prioritized in the implementation guide.

After the audit, Implementation reported the Stop/settings fix in local
`132fc85a`; do not interpret the audit baseline as the latest defect inventory.
Unsaved-source build fidelity remains unproven. The Next mockup is a design
target, not a statement that #232 is delivered.

## Resolved differences

| Suggestion | Resolution |
| --- | --- |
| Sol: reserve bare Escape to leave the preview | Declined. App menus/dialogs require Escape. Adopt Opus's Shift+Escape release proposal, retain host focus commands, and qualify installed-editor keybinding behavior. No claim that a webview automatically receives every chord. |
| Opus: #232 Reset should reuse the accepted build without recompilation | Deferred. Current accepted work rebuilds/restarts with strict freshness. This optimization needs independent artifact/freshness proof; the UI does not require it. |
| Opus: source-origin catalog exposure is an unresolved product decision | Resolved by source evidence: authored metadata already exists in protocol/scenario descriptors. Safe inert labels and explicit matching do not require another user decision. Actual navigation still requires host-verified capabilities; runtime element origins remain #243. |
| Opus: a setup checklist | Use native VS Code Quick Picks, Walkthroughs and settings, reached from the panel's specific empty state. Official webview UX discourages a wizard inside a webview. |
| Sol: show Resume for stopped preview | Use Start preview, with explicit fresh-state copy. This avoids implying a retained worker or application state. |
| Both: one primary reset/retry/start affordance | Keep Reset when running/current, Retry beside errors, Stop/Start at a stable location. Remove duplicate Refresh from the visible toolbar; retain manual Rebuild in the command palette. |
| Opus: accepted viewport chip; Sol: separate requested/accepted values | Combine: toolbar viewport summary and canvas footer identify accepted pixels; status and pending selection identify the next request. Settings draft is independently owned. |

The implementation guide takes precedence over unmerged advisory language in
either review. The mockup's DOM sample and simulated diagnostics/inspector are
clearly labeled and are not evidence of native runtime capabilities.

## Validation boundary

Only the isolated HTML artifact may be inspected in Codex's background in-app
browser; Implementation explicitly confirmed that coordination. No production
VS Code, native input/focus, process ownership, renderer, package or release tests
are part of this design task. See [bounded mockup checks](validation.md).
