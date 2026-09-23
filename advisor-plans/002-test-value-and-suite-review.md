# Lucent test value and suite review

Status: source review complete; implementation work is split into [existing test cleanup](003-existing-test-cleanup.md), [test harness improvements](004-test-harness-improvements.md), and [agent test-authoring guidance](005-agent-test-authoring-guidance.md). No tests removed or runners changed.
The executable scope in those plans supersedes the initial suggestions below. [Fable's review disposition](reviews/test-quality-fable-disposition.md) explains simplifications and explicit deferrals.
Reviewed September 22, 2026 against `8d43638860b8077d3c7f9c447993214e7c14e318` plus the active working tree. Recheck touched files after #310 lands.

## Assessment

Keep the behavioral coverage; reduce duplicated execution and improve weak assertions before deleting whole tests. The suite is not predominantly tautological in the sampled areas. Ownership, stale work, source maps, file preservation, native input and accessibility have valuable independent contracts. However, some tests exercise local prototypes, pin incidental generated spelling, or claim more coverage than their assertions establish. The runner also repeats expensive preparation across otherwise useful suites.

This was a source-only, hotspot-weighted review of managed Core/reactive/renderer/application tests, compiler/generator/editor/tooling tests, desktop/platform tests, package probes, the extension, and test orchestration. No builds, UI interaction, test execution, mutation testing or fresh timing measurements were performed. A static inventory found 1,183 `[TestMethod]` declarations in 202 files across 13 test projects; this is not the number of discovered cases, because data rows expand tests and scripts/Node tests are separate. Light Notes' separate repository was not audited. This is not an exhaustive assertion-by-assertion deletion list.

## Recommended bar

Each test should answer: **what plausible defect does this catch, and why is this the cheapest adequate place to catch it?**

A useful oracle is an independently stated expected result: authored ranges, accepted/rejected operations, resource lifetime, observable state, independently expected bytes, or attributable pixels. Do not calculate expected behavior by invoking the same production algorithm used by the actual result. Differential and round-trip tests are useful, but add a known expected case when both sides could share the same defect.

Same feature does not mean duplicate coverage. Count the failure boundary, not the feature name. A Core selection test and a Windows UIA selection test can both be necessary: one protects state ownership and the other protects provider/command translation. Conversely, repeating the same callback sequence in two Core fixtures without an additional boundary rarely earns its maintenance cost.

### Proposed AGENTS.md addition

Add this under Verification, with the longer examples in `docs/agents/verification.md`:

> Write tests for observable contracts and plausible failures, not for implementation steps or test counts. Before adding a test, search the affected suite and prefer extending an existing case when it exercises the same failure mode.
>
> Use the lowest layer that can faithfully reproduce the defect. Add integration or desktop coverage only for an additional boundary such as compiler-to-runtime behavior, editor transport, package consumption, NativeAOT, native input, pixels or accessibility. Keep shared behavior matrices at their owning layer; higher layers need representative wiring cases and boundary-specific failures.
>
> Expected results must be independent of the implementation under test. Do not test a local reimplementation, mocked behavior itself, trivial field round-trips, or incidental generated names/call ordering. Internal observations are appropriate when they establish an explicit contract such as retained identity, no idle work, ownership, source mapping or forbidden dependencies.
>
> Regressions should fail for the original defect and assert the intended outcome. When practical, demonstrate that against the pre-fix code or a small deliberate fault. Strengthen an existing weak assertion before adding a second test for the same scenario. Do not require mutation tooling or an extra test for every edit.
>
> Parameterize meaningful equivalence classes and boundaries; avoid full cross-products unless their interactions matter. Use deterministic clocks and explicit completion signals for portable behavior. Use bounded waits for real external/native boundaries.
>
> Remove or consolidate tests only after identifying which retained test protects each meaningful assertion. Preserve distinct cleanup, stale-result, rejection and transport failures. Reuse builds within one verification invocation; do not silently reuse stale artifacts. Do not add test-count, coverage-percentage or universal timing gates.

No per-test metadata schema, mandatory essay, or approval gate is needed. Test names and assertions should normally make the contract apparent. For a new expensive scenario, one sentence in the change description explaining its unique boundary is enough. For deletions, provide a short old-test → retained-test mapping in the change description.

## Findings and actions

All file links below are repository-relative. Line anchors describe the reviewed working tree and may move.

| Priority | Evidence | Action | Effort / change risk / confidence |
| --- | --- | --- | --- |
| 1 | [FormattingPreservationTests.cs](../tests/Lucent.Lui.Compiler.Tests/FormattingPreservationTests.cs#L57) implements Roslyn normalization and brace replacement inside the test, then executes that locally generated result. [WholeFileFormattingTests.cs](../tests/Lucent.Lui.Compiler.Tests/WholeFileFormattingTests.cs#L41) already executes output from the actual formatter. | Retire the prototype method from routine regression execution after preserving its distinct Unicode/comment/brace inputs in production-path coverage. Keep the other production tests in the file. The prototype makes four dynamic compilations but can pass with Lucent formatting broken. | S / low / high |
| 1 | [DrawingContracts.cs](../tests/Lucent.Core.Tests/DrawingContracts.cs#L56) claims command coalescing, but its equal step writes `1` over `1` and asserts recording did not run. | Replace that step with a changed dependency producing identical drawing commands. Assert recording ran again while retained drawing identity stayed unchanged, then assert changed commands replace it. Signal suppression does not prove drawing equality. Preserve idle/resource-lifetime checks. | S / low / high |
| 1 | [Test-Repository.ps1](../tools/Test-Repository.ps1#L131) implicitly calls `Invoke-Native` after Published. Native publishes and executes all four Core/R3/Skia/Windows suites; [tests.yml](../.github/workflows/tests.yml#L81) already selects Native in package verification. | Make Published cover its published interaction boundary only. Keep Native explicitly selectable and in package verification. Do not remove the published application's NativeAOT run. This removes actual repeated work when Native and Published are selected together. | S / low / high |
| 2 | [Test-Repository.ps1](../tools/Test-Repository.ps1#L100) resets and republishes fixtures independently for Published, Accessibility and Performance. Performance uses only Issue Browser but also publishes TestHost. Desktop runner build is repeated. | Within one selected verification invocation, build the union of required fixtures once and pass them to the distinct checks. Publish only the app for Performance. Start with same-invocation sharing rather than a persistent cross-run cache. | M / medium / high |
| 2 | [Verify-LuiAssets.ps1](../tools/Verify-LuiAssets.ps1#L537) restores/builds nine positive metadata cases and seven negative cases as separate projects. | Batch the nine compatible positive assets into one package consumer and assert every inventory entry independently. Keep negatives isolated initially so one failure cannot mask another. Retain project/package-reference, incremental invalidation and NativeAOT checks. | M / low for positives, medium for negatives / high |
| 2 | [extension.test.cjs](../extensions/lucent-lui-vscode/extension.test.cjs#L68) pins an exact grammar regex; the transition check only searches a regex string for `transition`. | Replace these weak grammar-shape assertions with a few actual tokenization cases: declaration, keyword versus ordinary identifier/text, and transition back into markup. Use the grammar execution path, not another hand-written matching algorithm. Keep manifest/registration assertions where configuration itself is the contract. | S–M / low / high |
| 2 | [NamedComponentLanguageServerContracts.cs](../tests/Lucent.Lui.LanguageServer.Tests/NamedComponentLanguageServerContracts.cs#L118) accepts a definition in the right file, at least two edits/references, and non-null completion. | Strengthen the existing protocol scenario to assert exact unordered authored range sets, declaration range and a relevant completion item. Keep the exact compiler-map test too: map construction and protocol consumption are separate boundaries. Coordinate with active #310 edits instead of creating competing tests. | S / low / high |
| 2 | [ToolingCommandTests.cs](../tests/Lucent.Lui.Tooling.Tests/ToolingCommandTests.cs#L13) checks six encodings using ASCII-only strings and reads the result through the production reader. | Use non-ASCII and supplementary characters and compare complete expected bytes independently. Retain all encoding/BOM classes and the separate concurrent-edit protection test. Strengthen in place rather than adding another matrix. | S / low / high |
| 2 | [DrawingRendererContracts.cs](../tests/Lucent.Renderer.Skia.Tests/DrawingRendererContracts.cs#L12) draws many primitives but samples rectangles/line; the broad nonzero-byte count at line 90 can pass without arcs/paths/ellipses. | Give each asserted primitive attributable painted/unpainted samples, using a compact parameterized case where suitable. Keep one combined clip/transform/opacity case. Replace the broad assertion rather than accumulating another large smoke scene. Avoid antialiasing-edge-sensitive exact pixels. | S / medium / high |
| 3 | [ApplicationTests.cs](../tests/Lucent.Core.Tests/ApplicationTests.cs#L374) checks three self-posted callbacks; [ReactiveDrainBoundaryContracts.cs](../tests/Lucent.Core.Tests/ReactiveDrainBoundaryContracts.cs#L25) covers self-posting, budget failure and subsequent recovery through the same application event boundary. | Remove only the three-callback fragment and rename the enclosing test if needed. Preserve its synchronization-context, owner-thread, cross-thread Send and late-post assertions. The budget/recovery case remains the primary owner. | S / low / high |
| 3 | [NamedComponentTests.cs](../tests/Lucent.Lui.Compiler.Tests/NamedComponentTests.cs#L106) checks private generated spelling before compiling and executing the output. | Remove incidental spellings where runtime assertions already establish the contract. Retain public factory/type identity, deterministic initialization and sole-emitter checks. Keep standalone ComponentState generator coverage because it is a separate emitter. | S / medium / high |
| Investigate | [ReactiveScalingContracts.cs](../tests/Lucent.Core.Tests/ReactiveScalingContracts.cs#L311) prints time/allocation for 10k dependency replacement/disposal but asserts behavior that can be checked at smaller scale. | Measure cost before changing execution policy. Keep small direct-effect ownership and disposal contracts; retain large cases as explicit stress/characterization if they detect scale-specific faults or their results are reviewed. No claim that these cases are currently slow or worthless. | S / medium / high on source shape, unmeasured cost |

## Coverage ownership to preserve

| Primary owner | Broad cases live here | Higher-level coverage earns its place by proving |
| --- | --- | --- |
| Core/reactive | State transitions, stale work, cancellation, disposal, accepted writes, geometry algorithms | The app wires the real behavior correctly; native transport delivers it |
| Compiler/generator | Syntax/diagnostics, exact source spans, lowering, distinct emitters | Generated code executes; public editor operations consume maps correctly |
| Editor context and protocol | Unsaved graph/config freshness, authored targets, edits | The extension forwards actual document events and preserves versions |
| Skia renderer | Attributable pixels, clips, shaping and resource ownership | Published scheduling/DPI/native surfaces display the right result |
| Windows platform/UIA | Provider identity, command mapping, native input/window lifetime | A small published user workflow works across the real transport |
| Package/SDK consumers | Evaluated targets, dependency assets, cold consume, invalidation, AOT execution | Distribution works without relying on source-project access |

For example, the debounce suite's quiet-period, queued cancellation, replacement, scope disposal, owner-thread delivery and closure release cases protect different failures. Do not collapse those into a single happy-path timer smoke. Likewise do not delete UIA or NativeAOT cases merely because managed behavior tests pass.

## Better assertions, helpers and harnesses

The user explicitly welcomed these improvements. Prefer a few local, domain-specific helpers over a new assertion library or framework. A helper should remove mechanical setup and improve the failure report; it should not hide the behavior under test or compute the expected result using Lucent's implementation.

| Improvement | Concrete shape and first consumers | Constraint |
| --- | --- | --- |
| Exact editor assertions | In the LSP test project, use authored fixture markers to construct expected ranges and compare complete unordered URI/range sets for definitions, references and rename. An assertion should show missing and unexpected locations, plus expected replacement text. Start with `NamedComponentLanguageServerContracts.cs`. | Marker parsing must be independent of production source maps. Preserve UTF-16 offsets and CRLF cases. Never sort away duplicates without first detecting them. Keep setup positions separate from asserted output positions. |
| Diagnostic assertions | A local helper checks diagnostic code, severity and authored span, with optional stable message fragment. Report unexpected diagnostics instead of accepting any error containing a word. Reuse across named-component/config tests. | Do not pin full Roslyn prose, diagnostic ordering or unrelated warnings. Explicitly identify the diagnostic set whose absence/presence is contractual. |
| Pixel assertions | A renderer-local helper reports logical position, physical sample, scale and actual RGBA against explicit expected bounds. Use isolated geometry or named regions for arcs, paths and ellipses in `DrawingRendererContracts.cs`. | Avoid broad image hash baselines and edge pixels sensitive to antialiasing. Do not derive expected paint from the production renderer. Save a failing image only when useful; do not make captures a mandatory artifact for every pass. |
| Production-path formatting helper | Reuse fixture setup that formats with Lucent and executes before/after member values; compile each version once and invoke all meaningful inputs. Migrate the prototype's distinct literals into `WholeFileFormattingTests.cs`. | The helper must not implement normalization/formatting itself. Expected formatting text is still appropriate where formatting output is the public contract. |
| Existing headless application harness | Prefer `Lucent.Testing` for app/component semantics, input and retention tests, and `Lucent.Testing.Skia` when actual shaping/pixels are necessary. `docs/HEADLESS-TESTING.md` already explains owner-thread invocation, explicit async completion and retained snapshot disposal. | Do not migrate pure algorithm tests to a host. Do not replace native focus/capture, resize-loop, clipboard, out-of-window popup or UIA tests with headless evidence. Adopt incrementally in touched tests, not as a suite-wide rewrite. |
| Shared expensive preparation | Prepare immutable package/binary fixtures and the desktop driver once per verification invocation, then give each scenario fresh application state and deterministic data. Batch positive asset inputs while preserving per-input inventory assertions. | Sharing setup is not sharing a mutable app across tests. Keep process, ownership and cleanup boundaries explicit; no test-order dependency or retry-until-green helper. |

Assertion helpers deserve small tests only when they contain meaningful logic that can misreport success, such as marked-span conversion or set comparison. Simple wrappers do not need a parallel test suite. Give diagnostics of failure more attention than fluent syntax. Do not extract a broad common harness merely because two fixtures look similar: cold project evaluation, warm invalidation and native lifetime tests intentionally need different setup lifetimes.

For expensive asynchronous boundaries, timeout messages should include the expected condition and last observed state. Portable state tests should use controlled clocks/completions rather than sleep. A helper must not silently retry actions with side effects or discard the first failure; polling an observation with a deadline is different from rerunning a failing test.

## Suggested execution sequence

1. Adopt the short policy and examples in AGENTS.md/verification docs. Preserve the existing risk-based check selection. Documentation-only validation: inspect links and `git diff --check`; no builds or UI tests.
2. Make the small assertion changes and prototype removal as one focused test-quality change. Introduce exact-range/pixel helpers only alongside their first concrete consumers. Retain named ownership of every removed assertion. Use existing affected test projects and filters; avoid new projects, dependencies or a mandatory mutation framework. Demonstrate selected oracles with a temporary deliberate fault only in an isolated working copy, not the shared active implementation checkout.
3. Decouple Published from Native and share fresh preparation within a single invocation. Keep all distinct checks selectable. Record source/configuration and fixture paths in the invocation result; publish/build failure must prevent dependent execution. Never treat a BuildOnly result as a desktop pass.
4. Batch positive asset cases; assert all nine entries, with unique paths so collisions do not accidentally become the tested behavior. Leave independent negative and invalidation tests intact.
5. Use existing TRX/process logs to compare representative before/after runs, separating restore/build/publish time from test time. Only then decide whether to move the 10k cases or additional matrices. Avoid a required benchmark dashboard or numeric speedup target.

Verification commands already supported by the repository:

```powershell
./tools/Test-Repository.ps1 -Suite Managed -Project Lucent.Core.Tests -Filter 'FullyQualifiedName~DrawingContracts|FullyQualifiedName~ReactiveDrainBoundaryContracts|FullyQualifiedName~ApplicationTests'
./tools/Test-Repository.ps1 -Suite Managed -Project Lucent.Lui.Compiler.Tests -Filter 'FullyQualifiedName~Formatting|FullyQualifiedName~NamedComponent'
./tools/Test-Repository.ps1 -Suite Managed -Project Lucent.Lui.Tooling.Tests
./tools/Test-Repository.ps1 -Suite Managed -Project Lucent.Lui.LanguageServer.Tests -Filter 'FullyQualifiedName~NamedComponent'
./tools/Test-Repository.ps1 -Suite Managed -Project Lucent.Renderer.Skia.Tests -Filter 'FullyQualifiedName~DrawingRendererContracts'
node --test extensions/lucent-lui-vscode/extension.test.cjs
git diff --check
```

These commands are proposed for implementation, not executed evidence. Run only those affected by the actual change. Package batching uses `tools/Verify-LuiAssets.ps1 -Feed <candidate-directory> -Version <exact-version>` with the same candidate before/after. Published runner changes require an authorized desktop window; portable test-quality changes do not. Respect any newer focus restrictions.

Completion means the named defects remain detectable, meaningful assertion ownership is preserved, and repeated setup is removed where proposed. A lower test count alone does not establish success. Do not alter production behavior to make cleanup pass, remove unresolved failing regressions, or weaken AOT/security/freshness checks as a shortcut. If a candidate test is the only evidence for an undocumented behavior, resolve whether that behavior is intended before deleting it.
