# Issue-browser comparison contract

This contract is frozen before either issue-browser implementation. Changes require an amendment entry with rationale; scores and thresholds may not change after measurement.

## Build and source boundary

- Native: `Release`, `win-x64`, NativeAOT, files matching `NativeStackProbe/IssueBrowser*.cs` only.
- Avalonia: `Release`, files under `baseline/IssueBrowser.Avalonia/` matching `*.lui`, `*.css`, and authored `*.cs`; generated output is excluded.
- Framework/runtime/proof-harness files are excluded from authoring scores on both sides.
- Both implementations load `gauntlet/issues.seed.json` and implement its exact deterministic row formula.
- The Avalonia baseline is not referenced by `NativeStack.sln` or `Lucent.sln`. The Native project must remain Avalonia-free.

## Executable walkthrough

Each implementation emits one JSON record per step with `step`, `pass`, `expected`, and `observed`; any failed step exits nonzero.

1. **Cold start:** expose 10,000 issues and realize at most three times the visible row count.
2. **Search:** query `auth`; retain stale rows while pending, then commit the exact derived result count.
3. **Latest query:** issue five rapid queries; cancel superseded work and commit only the final query.
4. **Filters:** combine `Open` and `High` with active search; produce the exact intersection.
5. **Scroll:** PageDown, End, and Home reach the expected rows while retaining the realized-row ceiling.
6. **Selection:** pointer and keyboard selection update the details key and selected semantics.
7. **Filtered selection:** removing the selected row selects the row at its prior index, clamped to the result count.
8. **Title edit:** scalar-safe select/paste/commit updates list and details; Escape before commit restores the original.
9. **Failure/retry:** query `fail`; expose error with stale rows, then Retry successfully and clear error.
10. **Theme:** toggle light/dark while search, filters, selection, scroll, and an uncommitted edit are active; preserve all five and honor reduced motion.
11. **Keyboard:** Tab/Shift+Tab traverse search, filters, list, details, and title with visible focus and no trap; Escape returns focus to the list.
12. **Semantics:** every interaction has role and name; list exposes selection; title exposes value/set-value; zero emergency suppressions.

Narrator, manual assistive-technology review, frame percentiles, final memory budgets, and native child-provider transport remain Milestone 3 work.

## Rubric

Score these eight categories independently: state/derived state, structure, async flows, styling, accessibility, lifecycle, testing, and total integration.

| Score | Anchor |
| --- | --- |
| 3 | Declarative, no manual UI synchronization, one obvious authoring site. |
| 2 | Declarative with one recurring piece of framework boilerplate. |
| 1 | Works, but needs manual synchronization or scattered authoring sites. |
| 0 | Cannot satisfy the walkthrough without leaving the framework model. |

For each category record Native score, Avalonia score, evidence reference, note, and confound sensitivity (`none`, `low`, or `high`). Also record authored LOC and imperative UI-synchronization sites. An imperative site is an authored UI-node property assignment outside a reactive callback, app-authored notification/observable plumbing, or manual collection reconciliation.

Native is **materially clearer** only if it scores strictly higher in at least five of eight categories, is never lower by two or more, and the post-score change task touches no more lines than Avalonia. Otherwise Milestone 2 stops.

## Frozen change task

After the first scoring pass, add an `assignee` filter to both implementations. Record files and changed lines; do not rescore before recording the change.

## Authoring confound

`.lui`/CSS versus straight C# is recorded separately, not credited entirely to either runtime. Structure and styling default to high confound sensitivity. Reauthor only the Avalonia filter bar in straight C# and compare its authored LOC and imperative sites with the `.lui` filter bar and Native filter bar; do not build a second application.

## Exclusions

No Native `.lui` or CSS, rich/multiline text, undo stack, variable-height virtualization, grid/wrapping, layout animation, dialogs, drag/drop, multiple windows, pixel-identical visuals, macOS/Linux adapters, or repeat of the already accepted IME matrix.

## Amendments

None.
