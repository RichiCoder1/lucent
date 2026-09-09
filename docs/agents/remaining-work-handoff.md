# Current work and follow-ups

The assets/icons batch is active. The owner has reauthorized UI/focus tests, requested the remaining image/icon and consumer issues through completion, then actionable backlog work and a fresh Fable review requested through the existing Code Review session. Serialize desktop interaction and shared-tree builds.

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

The #147–151 implementation now includes bounded static SVG, the optional 15-icon Lucide pack and accessible icon controls, generated Windows application artwork, package/tooling checks, and Issue Browser adoption. Focused Core/compiler/renderer/Windows/app checks, the nine-package inventory, package-only headless consumer and SDK asset proofs pass. A packaged NativeAOT Issue Browser started and closed with executable/HWND artwork after its source artwork directory was removed. Final publication and Light Notes #4's independent package integration remain outstanding; record exact source and CI/package identities in the delivery issues. The full scope is #145–151 and Light Notes #4; #144 comment 5592602334 is the accepted design. Follow-up #154 has measured JPEG admission, including conservative sequential multi-scan/CMYK handling, and maintained original measurement fixtures. Full transitions #142 remain separate. UI testing is authorized for the remaining delivery.

Existing focused follow-ups remain #114 (projection), #116 (Grid/overflow), #117 (fractional paragraphs), #118 (custom layout), #152 (public `.lui` XML documentation) and #153 (physical mixed-DPI input/popups). All attached monitors were at 100%; synthetic scale checks and 150% shaping/paint do not certify physical mixed-monitor transitions.

After assets and those existing actionable follow-ups, request the agreed fresh Fable review through the Code Review task and address its findings. The newly designed #155 Component Gaps waves (#156–171) do not reorder these existing commitments.

## Local workspace

Lucent: `D:/src/richicoder1/lucent`. Light Notes: `D:/src/richicoder1/light-notes`. Preserve unrelated `.codex/`, `advisor-plans/`, `docs/plans/windows-sandbox-testing.md` and `docs/research/` content. Use explicit staging paths. Serialize shared-tree builds and foreground tests.

D: is backed by the existing `C:/DevDrive/Dev.vhdx`; it was detached after restart and reattached with the owner's authorization using default DiskPart attachment. If it disappears again, inspect attachment state before changing anything. Do not format, change partitions or relax ACLs to recover repository access.
