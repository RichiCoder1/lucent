I've verified the plans' source claims. Here is the review.

## Verdict: ready with revisions

All three plans describe the cited tests accurately. Every weak oracle they name exists in the current tree: the Roslyn-only formatter prototype at `tests/Lucent.Lui.Compiler.Tests/FormattingPreservationTests.cs:57`, the same-value drawing write at `tests/Lucent.Core.Tests/DrawingContracts.cs:84`, ASCII-only encoding cases at `tests/Lucent.Lui.Tooling.Tests/ToolingCommandTests.cs:19`, the count-based LSP checks at `tests/Lucent.Lui.LanguageServer.Tests/NamedComponentLanguageServerContracts.cs:142-180`, the nonzero-byte pixel assertion at `tests/Lucent.Renderer.Skia.Tests/DrawingRendererContracts.cs:83`, and the implicit Native call at `tools/Test-Repository.ps1:132`. Production code supports the proposed C2 oracle: the drawing binding compares content and keeps the existing lease at `src/Lucent.Core/Components/Drawing/Drawing.cs:117`. The tracked tree is clean at a2e74acd, so the method anchors still hold.

The revisions below concern sequencing, one underestimated dependency, and harness scope. None of them changes the coverage intent.

## Must-fix plan changes

1. **Merge 004 H2 into 003 C1 and remove the C1 dependency on H2.** Both steps own `tests/Lucent.Lui.Compiler.Tests/WholeFileFormattingTests.cs` (003 C1 "Files", 004 H2 "Owned files"). The sequencing forces the highest-priority fix to wait for a helper whose only benefit is avoiding two extra Roslyn compilations. Deliver one change: delete the prototype, add its distinct inputs to the existing production-path test, and restructure the local `Evaluate` only if it stays readable. Also correct C1 step 1: the prototype's "Unicode" text is a runtime argument at line 127, not formatted source. The genuinely distinct inputs are the body comment, the escaped `\n` inside the interpolated string, and same-line brace choice. Wrap them as a `.lui` member, since the prototype specimen is plain C#.

2. **004 H3 grammar steps 4-5: state the real cost and default to deferral.** The plan says "dev dependency/lockfile only if necessary". Running the shipped TextMate grammar with its compatible engine requires `vscode-textmate` plus the WebAssembly Oniguruma package, a `package-lock.json`, an `npm ci` step in `.github/workflows/tests.yml` before line 25, and a CREDITS.md entry. The extension currently has no dependencies at all (`extensions/lucent-lui-vscode/package.json`) and docs/TESTING.md line 123 documents it as dependency-free. That is a new CI install gate plus toolchain for two assertions guarding a low-severity highlighting regression. Either accept those costs explicitly in the plan or move steps 4-5 to a deferred list and keep the current structural assertions.

3. **Split 004 H4 into a small required part and an optional part.** Required: remove `Invoke-Native` from `Invoke-Published` (line 132), and let Performance skip the TestHost publish since line 189 uses only the app executable. Both are a few lines in existing functions. Optional: multi-suite selection, union fixture planning, and "focused orchestration checks" with a private command-runner seam. CI runs only Managed and Native (tests.yml lines 34 and 80), so combined desktop invocations happen only in a developer's authorized window. Building a PowerShell test driver for that path is the plan's largest new maintenance surface and detects no product defect. If H4b proceeds, it should not add script tests until a second consumer exists.

4. **003 C6: do not invent an opt-in mechanism.** The plan says to make large workloads "explicitly opt-in using the repository's existing characterization conventions". No such convention exists for managed tests. The only precedent is a name suffix at `tests/Lucent.Renderer.Skia.Tests/RendererTests.cs:362` and a desktop-only `TestCategory` for physical DPI. Adding a category plus runner filter would be a new gate. Rewrite C6 as: measure from the existing TRX, keep the tests unless cost is material, and record the decision. The 10k cases are two drains over plain signals and are almost certainly cheaper than any single LSP process test in the same suite run.

## Optional simplifications

- **003 C5:** reuse `New-CaseProject` in `tools/Verify-LuiAssets.ps1:298`, which already accepts multiple files and raw item XML with `Density`. No new fixture generator is needed. Add one assertion that the batched inventory has exactly nine assets so an extra entry also fails.
- **004 H1:** the diagnostic helper (step 4) has little to consolidate. Existing tests already filter by code and severity at lines 320-335 and 545-551. Keep the marker/range-set helper, drop the diagnostic helper unless a third consumer appears.
- **004 H1:** the C# raw string literals in the LSP test take the checkout's line endings, and the test does not normalize them the way `WholeFileFormattingTests` does at line 57. The marker helper must compute positions from the exact string sent, which the plan already requires. Add a note that a CRLF fixture is the realistic case on Windows, not an edge case.
- **004 H3 renderer:** stroked ellipse and arc at scale 1 have a two-pixel stroke band, so "stable interior" sampling is fragile. Use thicker strokes or filled variants in the isolated geometry, and assert the ellipse interior is unpainted for stroked cases.
- **004 H5:** none of the touched tests are app or component tests, so H5 will be a no-migration note. Fold it into 005 A3, which already names the harness, and remove H5 as a tracked step.
- **005 A1:** the pointer paragraph is about 70 words and repeats three of the ten A2 rules. Two sentences plus the link would do.
- **Baseline:** re-anchor all three plans to a2e74acd. The tracked tree is clean, so the "compare anchors" step is trivial.

## Coverage that must be preserved

- The exact compiler map test `NamedMethodRewritePreservesExactIdentifierMaps` alongside the LSP protocol test. Map construction and protocol consumption are different boundaries.
- In `NamedComponentTests.cs`, the `Signal<int>` and `Derived<int>` lowering assertions at lines 109-110. The runtime string proves values, not reactive cell kinds. Remove only `__luiCompanionState_CompanionCount` and constructor spelling. Keep the two negative sole-emitter checks at lines 120-123.
- In `ApplicationTests.cs`, the synchronization-context restore, owner-thread continuation, cross-thread `Send` rejection, and late-post assertions. Only the three-callback fragment at lines 376-386 duplicates the budget test.
- In `DrawingContracts.cs`, the idle no-rerecord assertion and the retained-scene lifetime check at line 91.
- The separate concurrent-edit rejection test in `ToolingCommandTests.cs`, all six encoding classes, and the BOM-less UTF-8 default path in `DetectEncoding`.
- All seven negative asset metadata cases as isolated builds, and the case-collision negative, which means batched positive paths must differ case-insensitively.
- NativeAOT execution of the published application within Published, and the explicit Native suite in package CI.
- The combined clip/transform/opacity renderer scene and its three scales.

Rough token estimate for this turn: about 95,000 input tokens and 2,200 output tokens. This is an approximation only.
