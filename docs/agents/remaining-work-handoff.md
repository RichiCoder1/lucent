# Current work and follow-ups

The review follow-ups, responsive focus continuity, and first production transition slice are delivered. Lucent runtime commit `bab7cc3523aeeab8d9bce42f0aca5b2955fb1387` is published as `0.3.0-dev.55.1`; [CI 55](https://github.com/RichiCoder1/lucent/actions/runs/34394248585) passed managed, NativeAOT and package-consumer checks and published the immutable set. Light Notes independently adopts it at `9c00f75e8c7231074f8dea8df1a5f5a7d26e5b74`; [app CI 21](https://github.com/RichiCoder1/light-notes/actions/runs/34397155971) passed managed tests, desktop-test compilation and NativeAOT publication.

## Completed batch

- #194: retained-field diagnostics, missing-semicolon recovery, completion filtering and clearer derived-value hover.
- #195: initiating-button capture continuity, popup owner focus cleanup and synchronized UIA shutdown dispatch.
- #196: decoder/metadata parity, shared secure SVG admission and cache/disposal evidence. Oversized-image per-paint copies remain an explicit bounded limitation in [asset memory evidence](../ASSET-MEMORY-EVIDENCE.md).
- #142 and #198–202: typed `.lui` policies, composition-owned motion, retained paint reuse, Windows/headless scheduling, immediate actionable stock states and independent consumers. See [the public guide](../TRANSITIONS.md), [runtime evidence](../MOTION-RUNTIME-EVIDENCE.md) and [execution plan](../plans/transition-implementation.md).
- Light Notes #8: opt-in nearest-available focus recovery when a focused pane collapses, preserving editor sessions and avoiding replay of an old focus choice. NoteRow and compact Back declare hover motion in `.lui`.

Local Lucent verification passes 706/706 managed tests: Core 349, headless 46, R3 7, hosting 5, renderer 82, Windows 94, Issue Browser 20, compiler 60, generator 17 and LSP 26. The warning-clean solution build, authored formatting, positive/negative architecture checks, extension tests 13/13 and generated SDK NativeAOT proof pass. The final committed NativeAOT TestHost passes 5/5 physical checks for pointer selection, popup/focus lifetime, autonomous motion, minimize/restore and reduced motion.

Light Notes on the official 55.1 packages passes storage 22/22 and app 42/42, with one separate optional performance probe skipped. Desktop compilation and NativeAOT publish pass. The final published app passes 2/2 physical workflows: responsive focus continuity and capture/autosave/reopen. All six maintained lock graphs were refreshed; only the two graphs whose Lucent dependencies changed produce lockfile diffs.

The local VS Code extension is `lucent.lucent-lui` 0.3.2. Its server is installed at `C:/Users/richa/.lucent/lui/bab7cc3/server/Lucent.Lui.LanguageServer.dll`, and only the corresponding user setting was updated with a private backup. Evaluated-project initialization, completion/hover capabilities, shutdown and exit pass. Existing VS Code windows need **Developer: Reload Window** to activate the installation.

## Earlier delivery and planned work

Assets #144–152, JPEG admission #154, component-family organization and framework-authored ErrorNotice #143, earlier layout/projection #114/#116–118, and confirmed Fable review fixes #172–193/#197 are complete. The [remediation record](../plans/fable-review-remediation.md) maps findings to fixes. [Projection evidence](../SCENE-PROJECTION-EVIDENCE.md) records earlier measurements and their limits.

Physical mixed-DPI #153 was completed in the preceding batch with actual 96/144/96-DPI monitors, including open popup/submenu transitions, pointer editing, focus and multiline UIA text geometry. Its [source-bound delivery record](https://github.com/RichiCoder1/lucent/issues/153#issuecomment-5605452916) remains authoritative; this batch does not claim a fresh physical mixed-DPI or real-language IME candidate-window walkthrough.

Component Gaps #155–171 and context/navigation #203–222 are separate planned work. A separate controls package, broad framework `.lui` conversion, layout/entry animation and public custom interpolators remain deferred.

## Local workspace

Lucent: `D:/src/richicoder1/lucent`. Light Notes: `D:/src/richicoder1/light-notes`. Preserve unrelated `.codex/`, `advisor-plans/`, `docs/plans/windows-sandbox-testing.md` and `docs/research/` content; they were not included in this delivery. Use explicit staging paths. Serialize shared-tree builds and foreground tests. UI testing is authorized until the owner pauses it. Apply the [risk-based verification policy](verification.md); broad manual accessibility/IME certification is not implied by automated checks.

D: is backed by `C:/DevDrive/Dev.vhdx`. If it disappears after restart, inspect attachment state before changing anything. Do not format, change partitions or relax ACLs to recover repository access.
