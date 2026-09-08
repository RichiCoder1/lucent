# Restart checkpoint — September 8, 2026

The owner is restarting the PC. Work is paused. **UI/focus testing is paused until the owner explicitly authorizes it again.** Headless work may resume when requested. Do not infer a fresh focus grant from older messages.

## Delivery and branch

- Lucent `main` is pushed through `3c144e7` (editor navigation/placeholders, typed constant-null style lowering, lifecycle fixture correction, native editor tests, roadmap checkpoint).
- Light Notes `main` is pushed through `edfe395`, consuming official Lucent `0.3.0-dev.41.1` and propagating close cancellation while retaining accepted writes. Light Notes CI run `34289678191` passed; managed checks passed 22 storage + 33 app tests.
- The restart branch `codex/quality-restart-20260908` preserves the pending #137/#143 work and this handoff. It is a checkpoint, not a claim that its final package/tooling checks passed. Finish these checks before integrating it into `main`.
- Lucent CI run `34291023115` for `3c144e7` was still running at checkpoint. Inspect its outcome after restart. CI 41 (`34287493688`, source `e42beaf`) passed managed, NativeAOT and package-only checks and published all eight `41.1` packages.
- Unrelated untracked `.codex/`, `docs/plans/windows-sandbox-testing.md`, and `docs/research/` were left alone. Do not add or delete them as cleanup.

## Active scope and issue disposition

Finish quality parent #119 and component organization / framework `.lui` dogfooding #143. The owner authorized commits, pushes, packages and cross-repository integration. Prioritize performance, ownership, NativeAOT, `.lui` authoring and stock-theme consumers; use focused risk-based verification, not a new release gate.

Closed with published fixes and recorded evidence: #120–123, #125–133, #135–136, #138, #140–141. #124 has a final evidence comment but remains open pending closeout. #134/#137/#139/#143 remain open for final integration and package delivery. Parent #119 remains open. Existing #114/#116–118 have explicit separate scope and are not silently counted as implemented. Transition design #142 is separate.

Concrete follow-ups created:

- #152: public `.lui` XML comments do not emit onto generated public component methods; warning-as-error builds report LUI2000/CS1591. Keep the approved documented C# adapter over internal `.lui` composition for now.
- #153: actual physical mixed-DPI input/popup check. All three attached monitors report 100%; automated 1/1.25/1.5/2 scale contracts and 150% shaping/paint are not a physical mixed-monitor certificate. No display settings were changed.

After #119/#143, the accepted asset design is queued in #144 with children #145–151 and Light Notes #4. See #144 comment 5592602334 and the roadmap. Do not start that work while the current checkpoint is unfinished.

## Pending #137 editor incrementality

Files: `LuiProjectContext.cs`, language-server `Program.cs`, `IncrementalToolingContracts.cs`, `docs/LUI-SDK-TOOLING.md`. Current source caches project evaluation within an epoch, reuses unaffected compiled documents, batches watcher reloads, preserves explicitly resolved references under `bin`, ignores unrelated build output, and gates result freshness tracking atomically.

Five focused incremental tests passed before the final lock/documentation adjustment. The language-server project then rebuilt with zero warnings/errors. **The full 21-test LSP suite is not verified on final source.** Earlier paired-tag expectations were stale after the new Core `.lui` document and deleted app Error.lui; `LanguageServerTests.cs` now includes Core `.lui` references and moved Layout declaration paths. Verify these changes rather than suppressing rename/source-map expectations.

Measured ten-document fixture: cold 3310 ms, warm 1 ms, after unrelated edit 939 ms; 20 actual document reads versus an explicitly estimated former 30 reads. Do not present the estimate as a measured baseline. Cancellation/freshness remains mandatory. #134 semantic null regression passed 1/1, compiler project rebuilt warning-free; prior compiler suite 37/37 passed before this follow-up. Run the final compiler suite too.

## Pending #143 organization and generated stock component

Stock recipe/configuration/state source is split into `src/Lucent.Core/Components` family folders and shared themes under `Presentation`. Public namespace/type remain `Lucent.Core` / partial `Components`; no package or assembly split. Shared runtime, editor session, input router and layout remain separate. `docs/COMPONENTS.md` explains the organization. Review public API/signature/default metadata preservation before final integration; the move is intended to preserve source bodies.

`Components/Status/ErrorNotice.lui` supplies the substantive layout, live Status message and conditional Retry button. The documented public C# adapter forwards readers/callbacks. Core references the in-repository compiler/generator as analyzer-only private build tooling, with architecture and package checks rejecting compiler/Roslyn runtime leakage. Issue Browser uses the stock notice and deletes its own Error.lui.

Passing final checks:

- Core 255/255 + positive/negative architecture checks: `artifacts/issue143-core-organization-final.log`.
- Issue Browser 17/17: `artifacts/issue143-issue-browser-final-green.log`.
- Integrated real Skia error-notice geometry/render check; capture `artifacts/style-layout-review/issue-browser/issue-browser-error-narrow.png`.
- Isolated same-assembly generation probe passed clean and incremental `.lui` / C# rebuilds under `artifacts/issue143`; final production clean-checkout proof is still needed.

Remaining: final formatting/diff/API review; production clean-build generation; package-only consumer and NativeAOT verification using updated `Pack-Packages.ps1` and `Test-HeadlessPackages.ps1`; final LSP path/reference tests. Do not manufacture a Light Notes ErrorNotice migration: its branded heading/surface and enabled/busy command semantics do not fit this Action-only notice without losing behavior. Record that justified non-adoption in #143.

Then integrate reviewed commits, push, confirm CI/package publication, update Light Notes to the final official package + locks, run affected headless app checks, and update the installed LSP/VSIX when permissible. Do not close #119/#143 before their required checks and consumer version integration are done. Update the plan status and roadmap when delivered.

## Native checks already performed (do not repeat without relevant changes)

- Published Issue Browser source `3277ddd` (behavior equivalent to official `41.1`): all nine paths covered across a full run (8/9) and focused submenu recheck (3/3). The one failure was a native-menu HWND destruction race in the harness, fixed in `d85589f`. Targeted Axe reported zero rule errors. Do not claim the final full run itself was 9/9.
- Published editor-session script passed single-line and multiline resize/remount/selection/undo/focus/canceled-preedit, including 150% shaping/paint: `artifacts/quality-3277ddd-editor-session.log`.
- Fresh NativeAOT TestHost `artifacts/issue124-lifecycle-fixed/Lucent.Platform.Windows.TestHost.exe`: lifecycle proof passed rejected first close, retry/drain, repeated-close coalescing, startup/cleanup failure and STA ownership. Log `artifacts/issue124-lifecycle-fixed.log`; hosting focused 5/5. Fixture `b2c7082` returns false for expected save rejection; escaping exceptions remain fatal.
- That TestHost includes `2b0533c` editor source plus then-current #134/#143 working changes. Published input suite passed **6/6**, log `artifacts/issue139-native-input-full.log`: command keys/wheel, multiline UIA ranges, popup placement/focus, held-border resize, physical pointer selection, Ctrl-word edits and separate newline undo.
- Arrow-key test injection must use existing `TypeNavigation` with extended scan codes; FlaUI virtual-key-only injection gave incorrect SDL input. Fixed test in `5f90289`; no production workaround was needed.
- Desktop activation helper raises only the owned fixture temporarily, verifies occlusion/focus, and always removes topmost in finally. Do not manipulate unrelated browser windows to get tests past focus failures.

## Environment and execution

PowerShell on Windows, .NET SDK 10.0.400 with roll-forward disabled. Shell builds/git/auth may need the established escalated execution because sandbox global Git/NuGet/TLS access fails. CSharpier is available via `dotnet csharpier format`; run on explicit authored files. Serialize shared-tree builds and all desktop activity. At checkpoint all workers were asked to stop and no owned test/build process remained active; idle MSBuild worker nodes may remain.

Reusable workers: `headless_tests` (Luna max, organization), `headless_harness` (Sol medium, Core `.lui`/package/Notes integration), `platform_presentation_research` (Sol medium, compiler/LSP). They are paused at checkpoint; re-dispatch bounded responsibilities after restart. Preserve others' edits and use explicit staging paths.
