# Milestone 2B authoring comparison contract

This is the registered second attempt. It does not alter `GAUNTLET.md` or the
issue #13 STOP evidence.

## Source boundary

- Native baseline: commit `168763b`; authored application file
  `NativeStackProbe/IssueBrowser.cs`. Native framework/proof files are excluded.
- Avalonia baseline: existing source-pinned files under
  `baseline/IssueBrowser.Avalonia/`: `IssueBrowser.lui`, `IssueBrowser.css`, and
  authored `Program.cs`. Generated files are excluded.
- `StraightCSharpFilterBar.cs` remains the bounded syntax-confound comparison;
  it is not a second application and is excluded from totals.
- Both implementations retain the same seed and W1–W12 behavior. The first
  comparison's captures and scores remain historical evidence.

## Categories and anchors

Score independently:

1. reactive state and derived state;
2. structural composition;
3. async and lifecycle ownership;
4. typed styling, state variants, and bounded animation;
5. accessibility and interaction behaviors;
6. testing and diagnostics;
7. change locality; and
8. total integration.

| Score | Anchor |
| --- | --- |
| 3 | Concise declarative composition/assignment/chaining, no manual UI synchronization, and one obvious authoring site. |
| 2 | Declarative intent with one recurring framework boilerplate mechanism or two obvious sites. |
| 1 | Manual synchronization, notification plumbing, scattered sites, or framework escape code is required. |
| 0 | The walkthrough cannot be expressed within the framework model. |

Record evidence and confound sensitivity for every score. Styling is judged on
authoring clarity and state/theme behavior, not merely whether declarations
live in a separate file. Visual quality and control breadth must remain at
parity but do not earn authoring points.

## Frozen change task

Add a **Closed** status filter next to Open.

- Open and Closed are mutually exclusive; selecting the active status clears it.
- With query `auth`, Closed produces exactly 667 results from the frozen seed.
- Selection, scroll, draft, theme, reduced motion, and stale-result behavior are
  preserved.
- Add executable evidence on both sides.
- Record changed authored files, added/deleted lines, touched authoring sites,
  and any imperative synchronization or notification plumbing.

Do not refactor unrelated code while implementing the task. Do not rescore until
both diffs and executable results are recorded.

## Gate

Native must:

- score at least 2 in every category;
- score 3 in reactive state/derived state, async/lifecycle ownership, and typed
  styling/state variants;
- beat Avalonia in at least three of reactive state, structural composition,
  async/lifecycle, styling/state variants, and change locality;
- have no two-point category deficit; and
- complete the frozen task with no more touched authoring sites than Avalonia.

Passing records **PROCEED** to Milestone 3. Failing records **STOP** without
changing thresholds.
