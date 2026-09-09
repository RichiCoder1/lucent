# Current work and follow-ups

The assets/icons batch and its actionable older backlog are delivered. The requested fresh Fable review returned through the Code Review task; confirmed framework fixes are published from `905bd1c` as `0.3.0-dev.53.1`. [CI 53](https://github.com/RichiCoder1/lucent/actions/runs/34356784239) passed managed, NativeAOT and package-consumer checks and published the immutable package set.

## Completed delivery

- Assets #144–152, JPEG admission #154, and earlier layout/projection #114/#116–118 are complete. Light Notes #4 independently adopted the artwork and packages.
- Review fixes #172–193 and #197 cover runtime ownership/recovery, image admission, input geometry and convergence, focus contrast, compiler/editor correctness and allocation, and executable verification. The [remediation record](../plans/fable-review-remediation.md) maps findings and corrections to tickets. [Projection evidence](../SCENE-PROJECTION-EVIDENCE.md) contains measurements and their limits.
- Light Notes #5/#6/#7/#9 are delivered at `4c3b47ccdad8fcd847b47b620b01003fdfe7ade7`, consuming `0.3.0-dev.53.1`. [CI](https://github.com/RichiCoder1/light-notes/actions/runs/34379115230) passed managed tests, desktop-test compilation and NativeAOT publication. Local storage 22/22 and app 40/40 contracts passed; the app/fix commit `cd3f865` passed the published desktop suite 7/7. The final SDK-only follow-up restores exact SDK 10.0.400 selection so servicing updates cannot silently invalidate locked restore. SDK upgrades and lock refreshes move together.
- The local VS Code language server is installed at `C:/Users/richa/.lucent/lui/905bd1c/server` and its configured path is updated. Evaluated-project initialization, capabilities, shutdown and exit passed. Existing VS Code windows need **Developer: Reload Window** to activate it.
- Physical mixed-DPI #153 is complete: two opt-in tests passed with actual 96/144/96-DPI monitors, including open popup/submenu transitions, pointer editing, focus and multiline UIA text geometry. [The delivery record](https://github.com/RichiCoder1/lucent/issues/153#issuecomment-5605452916) contains exact source/binary identities and limits. A real-language IME candidate-window walkthrough is not claimed. Display settings were left intact, and all focus tests are finished.

The mixed-DPI test additions are committed in `0b8dbfc`; its [CI run](https://github.com/RichiCoder1/lucent/actions/runs/34378586116) owns the subsequent verification status. Runtime delivery remains the verified `905bd1c`/53.1 package set. Documentation-only closeout does not require another package upgrade.

## Separate follow-ups

- #194: retained-data diagnostics and completion ergonomics.
- #195: multi-button capture, popup focus and UIA shutdown investigations.
- #196: image metadata/admission parity and large-image cache behavior.
- Light Notes #8: focus intent across responsive hiding and virtualized eviction.
- #155–171: the separately ordered Component Gaps waves.
- #142: transitions and `.lui` animation design.

These investigations require concrete reproducers or explicit behavior decisions; the review did not establish every suggested defect. Component-family organization and the framework-authored `.lui` ErrorNotice are already delivered in #143. A separate controls package and broad conversion remain deferred.

## Local workspace

Lucent: `D:/src/richicoder1/lucent`. Light Notes: `D:/src/richicoder1/light-notes`. Preserve unrelated `.codex/`, `advisor-plans/`, `docs/plans/windows-sandbox-testing.md` and `docs/research/` content. Use explicit staging paths. Serialize shared-tree builds and foreground tests. UI testing is authorized until the owner pauses it. Apply the [risk-based verification policy](verification.md); broad manual IME/accessibility certification is not implied by automated checks.

D: is backed by `C:/DevDrive/Dev.vhdx`. If it disappears after restart, inspect attachment state before changing anything. Do not format, change partitions or relax ACLs to recover repository access.
