# Current work and follow-ups

## Component delivery status — September 12, 2026

The authorized component program is #155–171. Its component families and the maintained Component Browser are implemented in the shared working tree. This checkpoint records verified local evidence and remaining delivery work; it does not claim completion, publication, or issue closure.

### Verified local evidence

- Lucent Core: 448/448.
- Lucent Platform Windows: 120/120, including public password/IME, Slider, Tree, DatePicker, ComboBox, notification, popup lifetime, focus return, native UIA ABI, and custom popup-host compatibility contracts.
- Component Browser: 11/11, including 84 stock theme/density captures across the fourteen compiled `.lui` examples.
- Issue Browser: 20/20.
- Skia renderer: 80 passed with three opt-in skips.
- Repaired real-project LSP reference fixture: 1/1.
- Positive and negative architecture checks: passed.

The reported Dialog width, calendar alignment, suggestion-list sizing, radio-label clipping, slider-thumb alignment, password-toggle, menu-padding and submenu-placement defects are fixed. Hover tooltips now anchor near the pointer. Native popup windows resize when asynchronous content changes; programmatic focus waits for fresh layout instead of spuriously blurring and remasking a password. Escape cleanup also avoids accessing an already-disposed popup.

Fresh NativeAOT Component Browser and TestHost outputs passed all six selected desktop tests, covering these popup/password interactions, modal ownership/focus, and actual native open/save/folder cancellation. Evidence is in `artifacts/component-native-final-desktop.log`; captures are in `artifacts/component-native-final/captures`. The fresh radio/slider captures were inspected in all six theme/density combinations. Formatting and `git diff --check` pass. Run desktop checks separately from other native Windows tests. The computer-use plugin files are present but its node_repl runtime is absent from this session; do not claim a free-form computer-use walkthrough.

Automated UIA and synthetic SDL text-input tests do not certify end-to-end screen-reader delivery or a real installed-language IME candidate window. The existing physical mixed-DPI evidence from #153 remains valid for its recorded source boundary; this batch does not claim a fresh hardware walkthrough.

### Remaining steps for #155–171

1. Commit and push the verified Lucent batch, await required CI and immutable package publication, and record the published package identity. No new CI result, package, or issue closure exists for this batch yet.
2. Update Light Notes from `0.3.0-dev.55.1` to the newly published immutable package, refresh its lock graphs, and validate the three source changes already waiting in its tree. Those changes adopt `Field` while preserving the workspace-owned URL `EditorSession`; they are source-only and unverified against a new package.
3. Update the plan, roadmap and child issues with the source/package/native evidence. Close #171 and parent #155 after Light Notes adoption is recorded.

The Design and UI task has published [C# authoring #265](https://github.com/RichiCoder1/lucent/issues/265) and children #266–275 as the next phase, immediately after #155/#171 package and Light Notes closeout and before #203/#204. Begin with #266 on the final accepted source/package baseline; it proves the bounded recipe/capability shape without changing production factory returns. #269 owns the later atomic factory/compiler/metadata/consumer migration. The plan received Fable High review and needs no further owner decisions. Its local design is `D:/.codex/worktrees/1b0a/lucent/advisor-plans/002-csharp-authoring.md`; preserve independently owned advisor plans when integrating it.

## Scope and constraints

This batch is limited to #155–171. Context, injection, navigation, Design and UI, and other tickets #203–264 are outside it. Do not broaden the current delivery to live compilation, another router, product-specific workflows, or deferred component capabilities.

The Windows file picker returns selected locations and performs no file I/O. `TableView` remains read-only with fixed-height virtualized rows; editing, formulas, merged cells, grouping, and variable-height rows remain out of scope. Preserve Core portability: Windows owns hosting, input, IME, accessibility, presentation, and native-dialog adaptation.

Lucent is at `D:/src/richicoder1/lucent`; Light Notes is at `D:/src/richicoder1/light-notes`. Preserve unrelated `.codex/`, `advisor-plans/`, `docs/plans/windows-sandbox-testing.md`, and `docs/research/` content. Use explicit staging paths, serialize shared-tree builds and foreground tests, and follow [the risk-based verification policy](verification.md). The Dev Drive is backed by `C:/DevDrive/Dev.vhdx`; if `D:` disappears after restart, inspect attachment state before changing anything. Do not format the volume, change partitions, or relax ACLs.

## Last published baseline

The last published Lucent baseline remains commit `bab7cc3523aeeab8d9bce42f0aca5b2955fb1387`, package `0.3.0-dev.55.1`. [CI 55](https://github.com/RichiCoder1/lucent/actions/runs/34394248585) passed its managed, NativeAOT, and package-consumer checks and published that immutable set.

Light Notes still adopts `0.3.0-dev.55.1` at commit `9c00f75e8c7231074f8dea8df1a5f5a7d26e5b74`. [App CI 21](https://github.com/RichiCoder1/light-notes/actions/runs/34397155971) passed managed tests, desktop-test compilation, and NativeAOT publication for that baseline. Do not attribute the current component working tree or its pending Light Notes changes to those published results.

Earlier completed asset, motion, focus-continuity, responsive-layout, native-menu, and physical mixed-DPI delivery records remain authoritative for their committed source boundaries. See [the component execution plan](../plans/component-delivery.md), [testing guide](../TESTING.md), and individual issue records for their detailed evidence.
