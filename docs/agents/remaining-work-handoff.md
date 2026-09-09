# Current work and follow-ups

The September 8 restart checkpoint has been resumed. UI testing was reauthorized by the owner after the restart; later instructions take precedence.

## Quality integration

The implementation and local verification for #119 and #143 are complete. Final publication, CI results and the independently consumed Light Notes package version are recorded in those issues. Confirm their delivery status before starting the next batch.

- Component organization preserves the existing public type and 256 existing members, including defaults, attributes and bodies. The contributor guide is `docs/COMPONENTS.md`.
- Core generates the stock ErrorNotice from ordinary `.lui`; Issue Browser consumes it. Light Notes retains its app-specific error presentation because the Action-only notice does not preserve its command enabled/busy behavior and branded layout.
- Verified: Core 255/255 and architecture positive/negative checks, Issue Browser 17/17 plus actual Retry keyboard/disposal regression, LSP 23/23, compiler 38/38 and VS Code client 12/12.
- CI 44 exposed a cold-checkout rename failure from unavailable Lucent Debug build-tool analyzer references. The editor now normalizes its rename project graph while preserving unrelated analyzers. The full LSP suite passed in an isolated Release-only checkout, including cross-project rename with both unavailable Lucent build tools.
- An isolated exact-source checkout passed clean generation, incremental markup rebuild, all eight package inventories, package-only headless/Skia consumption and NativeAOT publication. The resulting package-only app passed the native split-pane/adaptive navigation test.
- Earlier published input suite passed 6/6, including pointer selection, word editing, newline undo, popup focus and live resize. Lifecycle proof passed recoverable rejection, retry/drain, startup and cleanup failure, and STA ownership. Preserve those source-bound limits when reusing evidence.
- Tooling measurement: warm completion 13 ms, edit-to-diagnostic 861 ms and rename 1746 ms on this host. No pre-change timing baseline or universal latency budget is claimed. Cancellation is observed around synchronous compilation, not inside an already-running compiler call.

## Next work

The accepted images/icons/assets design is queued in #144, with #145–151 and Light Notes #4. Read #144 comment 5592602334 and `docs/ROADMAP.md` before beginning it. Full transitions #142 are a separate design.

Existing focused follow-ups remain #114 (projection), #116 (Grid/overflow), #117 (fractional paragraphs), #118 (custom layout), #152 (public `.lui` XML documentation) and #153 (physical mixed-DPI input/popups). All attached monitors were at 100%; synthetic scale checks and 150% shaping/paint do not certify physical mixed-monitor transitions.

## Local workspace

Lucent: `D:/src/richicoder1/lucent`. Light Notes: `D:/src/richicoder1/light-notes`. Preserve unrelated `.codex/`, `docs/plans/windows-sandbox-testing.md` and `docs/research/` content. Use explicit staging paths. Serialize shared-tree builds and foreground tests.

D: is backed by the existing `C:/DevDrive/Dev.vhdx`; it was detached after restart and reattached with the owner's authorization using default DiskPart attachment. If it disappears again, inspect attachment state before changing anything. Do not format, change partitions or relax ACLs to recover repository access.
