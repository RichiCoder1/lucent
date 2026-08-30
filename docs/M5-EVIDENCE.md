# Milestone 5 evidence

## Frozen challenges

The challenge text was frozen before implementation.

| Challenge | SHA-256 |
|---|---|
| `docs/challenges/m5-density.md` | `db4ebb10d2e3f5fe91e33c2bef80a16ae0b67b70d8fda4ee0699577db319f0e1` |
| `docs/challenges/m5-optimistic-status.md` | `f02e5ec3fa7c2371aa9a520f67cc61f58d715b87441804c4d1de3f8fa86bbac0` |

## Implementation evidence

### Authored composition/style/behavior seams

- `apps/Lucent.IssueBrowser/IssueBrowser.cs` owns `IssueDensity`, the density tokens, the accessible `Density: Comfortable/Compact` button, and the scroll-anchor calculation. Appearance selects palettes only; density selects typed token values. The frozen relative viewport offset is literal logical pixels: restyle preserves `index * nextRowHeight + relative`, not a normalized row fraction.
- The same application file owns `IIssueStatusSource` and its `Saved`, `Rejected(reason)`, and `TransientFailure(reason)` result shape. `FixtureIssueStatusSource` is the ordinary deterministic offline source: issue numbers divisible by three reject, numbers congruent to one transient-fail once, and the remainder save.
- Saves live in root-owned `IssueStatusMutation` instances, not virtual rows or inspector content. Each uses a latest-generation `AsyncValue`; its root scope disposal cancels pending source work. The inspector owns Open/Close and a conditional Retry action; Retry exists only for the selected issue's latest transient outcome and is removed while its retry is pending or after Saved/Rejected.
- `IssueStatusMutation` reads `AsyncValue.Error` before `Value`. Unexpected source exceptions become optimistic, retryable `TransientFailure`s with a useful source reason capped at 160 characters, so an old Saved value cannot be reused after a later fault.
- `src/Lucent.Core/Composition.cs` adds the general `VirtualizedRegion.SetRowHeight` seam. It keeps keyed entries and updates fixed-height layout; the application preserves the logical scroll anchor before the next projection. Core contains no issue, status, density, or source policy.

### Frozen compiler-facing contract

`docs/ARCHITECTURE.md` now freezes the initial `.lui` target: retained composition and `CompositionContext` factories, bounded `Controls` recipes, typed properties/styles/tokens/themes/transitions, precompiled behavior attachment, runtime-tracked reactive expressions, and composition-owned scopes. `VirtualizedRegion.SetRowHeight` is the one new supported dynamic-density seam established by these challenges. Manual region driving, input/semantic/diagnostic/scene/renderer/platform internals, and any direct-dependency registration API are explicitly outside the compiler target; a generated-only registration seam remains deferred until M7 measures a need.

### Focused proof and diagnostics

- `tests/Lucent.IssueBrowser.Tests/Program.cs` proves Comfortable → Compact → Comfortable at a focused mid-list row: top key, exact literal relative offset, selection/focus, semantic identity, bounded realization, light/dark/high-contrast installation, row height, and font size. It also proves overlapping per-issue saves, stale completion rejection, rejection rollback/reason, transient Not synced/manual retry, Retry absence before/pending/after Saved or Rejected, filter/selection survival, ordinary fixture outcomes, root disposal cancellation, and first-fault plus saved-then-fault exception regressions.
- `tests/Lucent.Core.Tests/LayoutSceneContracts.cs` proves the live fixed-row-height Core seam keeps realization bounded and restores cleanly.
- `tools/Invoke-VirtualizationProof.ps1` extends the published NativeAOT UIA proof through the density button and verifies retained selected top-row runtime ID, key, literal-pixel relative anchor, compact/comfortable heights, and realization bounds. UIA activation correctly transfers focus to the invoked density button; the proof checks that transfer and identity after each reflow. The in-process direct semantic Invoke contract separately proves an already focused row remains focused through the reflow itself.
- The same external proof polls UIA after each selection before acting, avoiding the known post-command semantic-snapshot observation race. Against the ordinary fixture it proves #10000 transient → Retry → Saved, #9999 rejection rollback with its displayed reason, and #9998 Saved; Retry is absent initially, after Saved, and after Rejected, and present only after the transient result.
- No new diagnostic transport or dump format was added. Existing composition/style dumps now naturally show the application density token winners and the changed virtual row height; retained scene and UIA identities remain stable through restyle.

### Churn and lifecycle

Final authored churn: 10 files, 510 added lines and 33 removed lines. There is one framework method; all mutation outcome, retry, exception classification, and offline-source policy remains application code.

Changing density does not remount the retained keyed top row. Filtering, virtualization departure, and selected-inspector replacement do not own mutation scopes. Replacing a generation cancels its predecessor; late uncooperative completion is discarded by `AsyncValue`; disposing the application root cancels all remaining saves.

### Milestone 6 checks invalidated for #38 and #39

Rerun the reference walkthrough (including the new header and inspector actions), keyboard/Narrator walkthrough, UIA external proof and Accessibility Insights manual review, high-contrast/light/dark focus-pixel smoke, 10,000-row realization/performance and memory checks, managed and NativeAOT warning-clean contract/publish/smoke checks, clean-directory package/asset/notice/checksum inventory, and final visual review. The M6 IME check is not behaviorally changed but remains part of the full post-M5 viability rerun.
