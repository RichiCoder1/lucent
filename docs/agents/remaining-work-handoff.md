# Current work and follow-ups

The assets/icons batch is active. The latest owner instruction requires asking before any desktop focus test. Headless builds, rendering and console NativeAOT checks may continue.

## Quality integration

The #119/#143 batch is complete and published as `0.3.0-dev.45.1` from Lucent `3707b21`; Light Notes `eab529a` consumes it. Both CI runs passed, and final source-bound results are recorded in the delivery issues.

- Component organization preserves the existing public type and 256 existing members, including defaults, attributes and bodies. The contributor guide is `docs/COMPONENTS.md`.
- Core generates the stock ErrorNotice from ordinary `.lui`; Issue Browser consumes it. Light Notes retains its app-specific error presentation because the Action-only notice does not preserve its command enabled/busy behavior and branded layout.
- Verified: Core 255/255 and architecture positive/negative checks, Issue Browser 17/17 plus actual Retry keyboard/disposal regression, LSP 23/23, compiler 38/38 and VS Code client 12/12.
- CI 44 exposed a cold-checkout rename failure from unavailable Lucent Debug build-tool analyzer references. The editor now normalizes its rename project graph while preserving unrelated analyzers. The full LSP suite passed in an isolated Release-only checkout, including cross-project rename with both unavailable Lucent build tools.
- An isolated exact-source checkout passed clean generation, incremental markup rebuild, all eight package inventories, package-only headless/Skia consumption and NativeAOT publication. The resulting package-only app passed the native split-pane/adaptive navigation test.
- Earlier published input suite passed 6/6, including pointer selection, word editing, newline undo, popup focus and live resize. Lifecycle proof passed recoverable rejection, retry/drain, startup and cleanup failure, and STA ownership. Preserve those source-bound limits when reusing evidence.
- Tooling measurement: warm completion 13 ms, edit-to-diagnostic 861 ms and rename 1746 ms on this host. No pre-change timing baseline or universal latency budget is claimed. Cancellation is observed around synchronous compilation, not inside an already-running compiler call.

## Next work

The accepted images/icons/assets design is active in #144. Packaged typed assets (#145) shipped in `410ea45`; CI 46 passed its managed and package jobs and published `0.3.0-dev.46.1`. See `docs/ASSETS.md`. Focus-free checks passed: Core asset contracts 4/4, generator tests 16/16, Core architecture checks, and the maintained Assets suite. The suite proves cold editor binding, warm builds, metadata/diagnostic cases and real NativeAOT project/package consumers after source/feed/cache removal.

Owned asynchronous Image/Icon loading (#146) is implemented and locally verified: portable preparation/cache/leases, PNG/JPEG decoding, Image/Icon styles and semantics, independent retained-scene ownership, and host/headless lifetime integration. Checks passed: 548 managed tests across the eight affected suites, 49 renderer tests under NativeAOT, Core architecture positive/negative checks, and all eight package inventories plus the `.lui` Image/Icon headless package consumer. The delivery issue records the commit and publication status. RetainedScene and HeadlessSnapshot callers must dispose frames they release.

Next is #147. Its isolated Svg.Skia feasibility proof passed actual NativeAOT rendering, but the adapter must still implement bounded static-SVG preflight, required-reference validation and explicit unsupported-feature errors before adoption. The full scope is #145–151 and Light Notes #4; #144 comment 5592602334 is the accepted design. Follow-up #154 records progressive/CMYK JPEG measurement gaps and the current conservative source-sized admission policy. Full transitions #142 remain separate. No local desktop focus tests ran for #145 or #146; ask before running them.

Existing focused follow-ups remain #114 (projection), #116 (Grid/overflow), #117 (fractional paragraphs), #118 (custom layout), #152 (public `.lui` XML documentation) and #153 (physical mixed-DPI input/popups). All attached monitors were at 100%; synthetic scale checks and 150% shaping/paint do not certify physical mixed-monitor transitions.

## Local workspace

Lucent: `D:/src/richicoder1/lucent`. Light Notes: `D:/src/richicoder1/light-notes`. Preserve unrelated `.codex/`, `docs/plans/windows-sandbox-testing.md` and `docs/research/` content. Use explicit staging paths. Serialize shared-tree builds and foreground tests.

D: is backed by the existing `C:/DevDrive/Dev.vhdx`; it was detached after restart and reattached with the owner's authorization using default DiskPart attachment. If it disappears again, inspect attachment state before changing anything. Do not format, change partitions or relax ACLs to recover repository access.
