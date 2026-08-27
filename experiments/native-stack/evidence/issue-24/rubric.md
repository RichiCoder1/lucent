# Milestone 2B authoring gate

Scored by the parent against `GAUNTLET-REFRAME.md`. The original issue #13
scores remain unchanged.

| Category | Native | Avalonia | Evidence | Confound | Rationale |
| --- | ---: | ---: | --- | --- | --- |
| Reactive state / derived state | 3 | 1 | Native `State` signals/async derived values; Avalonia `IssueBrowserSession` notifications and `Refresh` | low | Native declarations update retained facets directly. Avalonia manually raises notifications and clears/repopulates `Rows`. |
| Structural composition | 3 | 3 | Native `Author`; `IssueBrowser.lui` | high | Both express one declarative three-region tree with structural/virtualized children. |
| Async / lifecycle ownership | 3 | 1 | Native stale-retaining `ReactiveAsyncComputed` and composition disposal; Avalonia `BeginSearch`/`CompleteSearch` | low | Native owns cancellation, latest generation, stale values, scopes, and teardown. Avalonia models these manually and its generation counter is not authoritative. |
| Styling / state variants | 3 | 3 | Native typed `Style` chains, variants, inherited themes, transition; Avalonia CSS/classes/theme | high | Both are declarative. Native uses concise typed assignment/chaining; Avalonia uses centralized CSS. |
| Accessibility / interaction | 3 | 2 | Native mounted behaviors and complete semantic dump; Avalonia native controls plus session-authored semantic evidence | medium | Native input/focus/semantics share the authored element seam. Avalonia controls are strong, but comparison evidence mirrors semantics in session code. |
| Testing / diagnostics | 3 | 2 | Native deterministic tree/layout/style/semantic dumps and NativeAOT proof; Avalonia mounted headless walkthrough/captures | low | Both are executable; Native exposes framework-owned diagnostic projections rather than application-authored state only. |
| Change locality | 2 | 1 | `change-task.json` | low | Native touches one authored file with no notification plumbing. Avalonia touches `.lui` plus state, properties, notifications, filtering, and fixture code. |
| Total integration | 2 | 1 | Authored source boundaries and W1–W12 | low | Native retains one recurring explicit proof seam; Avalonia still requires manual collection synchronization and scattered notification sites. |

## Gate calculation

- Native scores at least 2 in all eight categories.
- Native scores 3 in reactive state, async/lifecycle, and typed styling.
- Native beats Avalonia in reactive state, async/lifecycle, and change locality:
  **3/5 hypothesis categories**. Structure and styling tie.
- Native has no two-point deficit.
- The frozen task touches one Native authored file versus two Avalonia authored
  files, and eight Native hunks versus eleven Avalonia hunks.

## Decision

**PROCEED.** The reframed authoring model meets the predeclared Milestone 2B
gate. This authorizes Milestone 3 validation; it does not authorize replacing
the existing implementation yet.
