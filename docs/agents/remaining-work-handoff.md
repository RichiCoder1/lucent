# Current work and follow-ups

## Active interaction repairs — September 12, 2026

[#278](https://github.com/RichiCoder1/lucent/issues/278) follows the user's additional recordings after the earlier walkthrough: calendar weekday centering, tooltip shadow hover interception, popup outer width and scrolling anchors, and ComboBox editing/reopening. Its implementation keeps suggestion-window focus on the editor, preserves clicks within the editor anchor, and separates popup content width from its padding. Core 459/459, Windows 124/124 and Component Browser 13/13 pass, including the gallery's 84 theme/density captures. Architecture and changed-file formatting checks pass. Logs are `artifacts/component-input-*.log`.

The fresh Computer Use recheck is pending after a physical Escape interruption. Ask to resume UI input while continuing independent code work; do not treat an interruption as cancellation of the whole task. Do not close #278 or claim the original hover/scroll/editing recordings fixed by manual observation until that recheck is performed. The earlier walkthrough below is historical evidence, not a pass for these changes.

The NativeAOT recheck candidate is `artifacts/component-input-278/browser/Lucent.ComponentBrowser.exe`, built from `4705924ea85c6c5aa45f1fa6219e8b23156d7257`, SHA-256 `9E96D0A9FE5F785C871FAF185F2975FE6A07D04AA4FD0746441AEDA31711092D`. Its source manifest is `artifacts/component-input-278/source.json`. Launch this candidate for the recheck; the earlier baseline app may still be open. This is a local published executable, not a claim of NuGet publication.

The independent Fable High review and primary-source control comparison are complete. The review inspected a frozen source snapshot and ran no tests or UI checks. Active-fix overlap is tracked in #278; independent follow-ups are ordered below and require focused reproductions before implementation:

1. [#279](https://github.com/RichiCoder1/lucent/issues/279): closed-tooltip Escape routing.
2. [#280](https://github.com/RichiCoder1/lucent/issues/280): pending dialog acceptance, host dismissal and recovery.
3. [#281](https://github.com/RichiCoder1/lucent/issues/281): calendar availability changing while open.
4. [#282](https://github.com/RichiCoder1/lucent/issues/282): slider cancellation and pointer-button ownership.
5. [#283](https://github.com/RichiCoder1/lucent/issues/283): menu keyboard navigation after separator hover.
6. [#284](https://github.com/RichiCoder1/lucent/issues/284): TimePicker stepping at the end of the day.
7. [#285](https://github.com/RichiCoder1/lucent/issues/285): dynamic field help and first-invalid order.
8. [#286](https://github.com/RichiCoder1/lucent/issues/286): planned desktop key/paging and numeric-stepper parity; distinguish design decisions from defects.

The local comparison, review and snapshot provenance are under `artifacts/reviews/control-behavior-20260912-194108/`. Public tickets contain bounded reproductions and acceptance criteria, not the full local review snapshot. Resolve the correctness follow-ups before beginning the next authoring phase.

## Published checkpoint — September 12, 2026

The component program #155–171 is delivered, closed and Done in Project 4. [Final delivery evidence](https://github.com/RichiCoder1/lucent/issues/171#issuecomment-5649183562) records the source boundaries and verification limits.

- Lucent: `f6fbc4ff29fcd9ab4e5c390825e663fba3d6548a`, immutable package `0.3.0-dev.60.1`. [CI 60](https://github.com/RichiCoder1/lucent/actions/runs/34722441727) passed managed, NativeAOT and package-only consumer checks, then published the verified packages.
- Light Notes: `48a404d37ff1f1380ab3b549c86d6c46c24f8f07`, independently consuming `0.3.0-dev.60.1`. [App CI](https://github.com/RichiCoder1/light-notes/actions/runs/34723338988) passed managed tests, desktop-test compilation and NativeAOT publication.

The maintained Component Browser contains fourteen compiled `.lui` examples with their actual source. Issue Browser adopts Select for fixed-choice filters. Light Notes adopts Field for URL semantics while preserving its workspace-owned EditorSession, draft and focus through responsive layouts. The reported dialog width, calendar alignment, suggestion sizing, radio clipping, slider alignment, password toggling, menu padding and submenu placement defects are fixed; hover tooltips anchor near the pointer.

### Verification and performance

Local suites passed Core 449/449, Windows 120/120, Component Browser 11/11 with 84 stock theme/density captures, Issue Browser 20/20, Skia 80 with three opt-in skips, and LSP 26/26. Architecture and formatting checks passed. Six selected desktop tests passed against fresh NativeAOT Component Browser/TestHost outputs; radio and slider captures were inspected across all six theme/density combinations. Logs and captures are under `artifacts/component-native-final*`.

The later Enter hotfix lets single-line TextField bubble Enter to CommandScope when no commit callback owns it. Light Notes verifies this final package with physical Enter capture/autosave/reopen, responsive URL draft/focus continuity, and focus rehoming: all three selected native workflows passed. Its managed suites passed storage 22/22 and app 42 with one intentional opt-in skip. Evidence is under Light Notes `artifacts/delivery-*-60.1.log`.

Test setup now uses Core metadata for synthetic LSP fixtures that do not require Core source navigation, removes duplicate test-discovery processes, rejects empty runs, and records TRX timings. Managed and package verification run concurrently; publication waits for both and consumes the exact verified artifact. No existing assertions or native/package checks were removed. Measured local LSP time fell from 10m05s to 6m33s (35%); [CI 58](https://github.com/RichiCoder1/lucent/actions/runs/34721289961) took 15m23s versus CI 57's 37m59s. These are observed runs, not runtime IDE-performance claims. A separate Skia churn-fixture fix deterministically admits its intended workload while retaining renderer entry/byte limits and the existing timeout.

## Completed manual walkthrough and follow-up

The requested Computer Use walkthrough is complete. The runtime became callable in the resumed task; all fourteen examples were visited in the real NativeAOT browser. The [execution plan's walkthrough record](../plans/component-delivery.md#final-computer-use-walkthrough--september-12) distinguishes observed interactions from the automated theme matrix and keyboard evidence.

[Follow-up #277](https://github.com/RichiCoder1/lucent/issues/277) fixes the remaining Select popup sizing, gallery navigation/scroll reset and popup-to-owner repaint defects at `364033142d869d4c45646b82802a362e6b04a6ac`. Core surface/list contracts pass 13/13, Windows 121/121 and Component Browser 13/13 with 84 captures. Architecture/public API and formatting checks pass. Geometry/navigation passed a native retest; the final build additionally passed immediate Select feedback, Popover open/close feedback and modal completion through Computer Use.

The current tested browser is `artifacts/component-walkthrough-final/browser/Lucent.ComponentBrowser.exe`; `artifacts/component-walkthrough-final/source.json` records the exact source and SHA-256. The earlier `component-manual-ready` build is superseded. Package `0.3.0-dev.60.1` above is the prior published consumer baseline; consult #277 and its CI run for follow-up publication rather than treating the local executable as a published package.

Computer Use's transient-element cache and arrow/Enter injection were unreliable in some cases; the record does not claim a fresh manual keyboard pass for those paths. This delivery also does not claim broad manual accessibility, real-language IME or fresh physical mixed-DPI certification. Earlier #153 hardware evidence remains valid for its recorded source boundary.

## Next implementation

[Tooling follow-up #276](https://github.com/RichiCoder1/lucent/issues/276) is Todo in Project 4. Metadata-only Find References shares rename's source-definition requirement and can omit authored uses of stock properties. The existing source-project regression and assertions remain intact; the issue records focused acceptance criteria.

The next approved phase is [C# authoring #265](https://github.com/RichiCoder1/lucent/issues/265), children #266–275, before context/navigation #203/#204. Begin with #266 on the final package baseline above: prove the bounded recipe/capability shape without changing production factory returns. #269 owns the later atomic factory/compiler/metadata/consumer migration. The Design and UI task's plan received Fable High review and needs no further owner decisions. Its local design is `D:/.codex/worktrees/1b0a/lucent/advisor-plans/002-csharp-authoring.md`; preserve independently owned advisor plans when integrating it.

## Scope and workspace constraints

The completed component batch was limited to #155–171. Live compilation, context/injection/navigation, and other #203–264 work remain separate. The Windows file picker selects locations without file I/O. TableView remains read-only with fixed-height virtualized rows. Preserve Core portability; Windows owns hosting, input, IME, accessibility, presentation and native-dialog adaptation.

Lucent is at `D:/src/richicoder1/lucent`; Light Notes is at `D:/src/richicoder1/light-notes`. Preserve unrelated `.codex/`, `.dotnet-home/`, `advisor-plans/`, `docs/plans/windows-sandbox-testing.md`, and `docs/research/` content. Use explicit staging paths, serialize shared-tree builds and foreground tests, and follow [risk-based verification](verification.md). The Dev Drive is backed by `C:/DevDrive/Dev.vhdx`; if `D:` disappears after restart, inspect attachment state before changing anything. Do not format the volume, change partitions or relax ACLs.

Earlier asset, motion, focus-continuity, responsive-layout, native-menu and mixed-DPI records remain authoritative for their committed source boundaries. See [the component execution plan](../plans/component-delivery.md), [testing guide](../TESTING.md) and individual delivery issues for details.
