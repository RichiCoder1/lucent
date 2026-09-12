# Current work and follow-ups

## Published checkpoint — September 12, 2026

The component program #155–171 is delivered, closed and Done in Project 4. [Final delivery evidence](https://github.com/RichiCoder1/lucent/issues/171#issuecomment-5649183562) records the source boundaries and verification limits.

- Lucent: `f6fbc4ff29fcd9ab4e5c390825e663fba3d6548a`, immutable package `0.3.0-dev.60.1`. [CI 60](https://github.com/RichiCoder1/lucent/actions/runs/34722441727) passed managed, NativeAOT and package-only consumer checks, then published the verified packages.
- Light Notes: `48a404d37ff1f1380ab3b549c86d6c46c24f8f07`, independently consuming `0.3.0-dev.60.1`. [App CI](https://github.com/RichiCoder1/light-notes/actions/runs/34723338988) passed managed tests, desktop-test compilation and NativeAOT publication.

The maintained Component Browser contains fourteen compiled `.lui` examples with their actual source. Issue Browser adopts Select for fixed-choice filters. Light Notes adopts Field for URL semantics while preserving its workspace-owned EditorSession, draft and focus through responsive layouts. The reported dialog width, calendar alignment, suggestion sizing, radio clipping, slider alignment, password toggling, menu padding and submenu placement defects are fixed; hover tooltips anchor near the pointer.

### Verification and performance

Local suites passed Core 449/449, Windows 120/120, Component Browser 11/11 with 84 stock theme/density captures, Issue Browser 20/20, Skia 80 with three opt-in skips, and LSP 26/26. Architecture and formatting checks passed. Six selected desktop tests passed against fresh NativeAOT Component Browser/TestHost outputs; radio and slider captures were inspected across all six theme/density combinations. Logs and captures are under `artifacts/component-native-final*`.

The later Enter hotfix lets single-line TextField bubble Enter to CommandScope when no commit callback owns it. Light Notes verifies this final package with physical Enter capture/autosave/reopen, responsive URL draft/focus continuity, and focus rehoming: all three selected native workflows passed. Its managed suites passed storage 22/22 and app 42 with one intentional opt-in skip. Evidence is under Light Notes `artifacts/delivery-*-60.1.log`.

Test setup now uses Core metadata for synthetic LSP fixtures that do not require Core source navigation, removes duplicate test-discovery processes, rejects empty runs, and records TRX timings. Managed and package verification run concurrently; publication waits for both and consumes the exact verified artifact. No existing assertions or native/package checks were removed. Measured local LSP time fell from 10m05s to 6m33s (35%); [CI 58](https://github.com/RichiCoder1/lucent/actions/runs/34721289961) took 15m23s versus CI 57's 37m59s. These are observed runs, not runtime IDE-performance claims. A separate Skia churn-fixture fix deterministically admits its intended workload while retaining renderer entry/byte limits and the existing timeout.

## Outstanding manual walkthrough

The owner requested one more manual Computer Use walkthrough of every component after delivery. This remains pending: the skill is installed, but this task has no callable `node_repl` runtime. Automated desktop checks and inspected captures do not substitute for the requested walkthrough.

The fresh NativeAOT browser is ready at `artifacts/component-manual-ready/browser/Lucent.ComponentBrowser.exe`, built from `f6fbc4ff29fcd9ab4e5c390825e663fba3d6548a`; `artifacts/component-manual-ready/source.json` records its SHA-256. When Computer Use becomes callable, exercise all fourteen examples, stock appearances and densities, pointer/keyboard interaction, hover/press/focus, popup placement and resizing. Repeat the owner's calendar, repeated password-toggle, suggestion-width, radio-label, menu/submenu, tooltip-anchor and slider-alignment cases. Record observed results and fix reproduced defects before claiming this pass complete.

This delivery also does not claim broad manual accessibility, real-language IME or fresh physical mixed-DPI certification. Earlier #153 hardware evidence remains valid for its recorded source boundary.

## Next implementation

[Tooling follow-up #276](https://github.com/RichiCoder1/lucent/issues/276) is Todo in Project 4. Metadata-only Find References shares rename's source-definition requirement and can omit authored uses of stock properties. The existing source-project regression and assertions remain intact; the issue records focused acceptance criteria.

The next approved phase is [C# authoring #265](https://github.com/RichiCoder1/lucent/issues/265), children #266–275, before context/navigation #203/#204. Begin with #266 on the final package baseline above: prove the bounded recipe/capability shape without changing production factory returns. #269 owns the later atomic factory/compiler/metadata/consumer migration. The Design and UI task's plan received Fable High review and needs no further owner decisions. Its local design is `D:/.codex/worktrees/1b0a/lucent/advisor-plans/002-csharp-authoring.md`; preserve independently owned advisor plans when integrating it.

## Scope and workspace constraints

The completed component batch was limited to #155–171. Live compilation, context/injection/navigation, and other #203–264 work remain separate. The Windows file picker selects locations without file I/O. TableView remains read-only with fixed-height virtualized rows. Preserve Core portability; Windows owns hosting, input, IME, accessibility, presentation and native-dialog adaptation.

Lucent is at `D:/src/richicoder1/lucent`; Light Notes is at `D:/src/richicoder1/light-notes`. Preserve unrelated `.codex/`, `.dotnet-home/`, `advisor-plans/`, `docs/plans/windows-sandbox-testing.md`, and `docs/research/` content. Use explicit staging paths, serialize shared-tree builds and foreground tests, and follow [risk-based verification](verification.md). The Dev Drive is backed by `C:/DevDrive/Dev.vhdx`; if `D:` disappears after restart, inspect attachment state before changing anything. Do not format the volume, change partitions or relax ACLs.

Earlier asset, motion, focus-continuity, responsive-layout, native-menu and mixed-DPI records remain authoritative for their committed source boundaries. See [the component execution plan](../plans/component-delivery.md), [testing guide](../TESTING.md) and individual delivery issues for details.
