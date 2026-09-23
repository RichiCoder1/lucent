# Plan 003: Clean up existing tests without losing behavioral coverage

## Status and scope

- Status: implemented under #311, September 23, 2026. C1–C5 and affected checks are complete; C6 retains the measured low-cost stress coverage. See the [execution record](../docs/plans/test-quality-execution.md).
- Priority: high for misleading assertions and duplicated setup; measurement-dependent for stress cases.
- Effort: medium, split into the small changes below.
- Baseline: `a2e74acd448b488c22ecb71ec5b12d6de436110a`, refreshed September 22, 2026 after Fable review; additional working-tree changes remain.
- Recommended prerequisite: adopt plan 005. No cleanup step depends on plan 004.
- Ownership: existing Core/compiler/tooling tests and `tools/Verify-LuiAssets.ps1`. Plan 004 owns LSP/renderer assertion migrations and the repository runner; do not duplicate its edits. Grammar tooling is deferred.

Lucent is pre-release, Windows-first and NativeAOT-compatible. The objective is meaningful coverage with less repeated work, not a target reduction in test count. Keep independent source-map, stale-state, lifetime, package and native transport evidence. No production behavior changes, package publication, blanket snapshot rewrites or new test projects belong in this plan.

The current task is finishing #310 verification/publication. Before editing, inspect `git status --short` and `git diff a2e74acd -- <owned-paths>`, and compare the method anchors below with current source. Preserve others' changes and treat already-completed recommendations as complete rather than reimplementing them. Start this package after Implementation's current work; coordinate shared files if other owners remain active.

## C1 — Retire the formatter prototype, preserve its useful inputs

Files: `tests/Lucent.Lui.Compiler.Tests/FormattingPreservationTests.cs` and `WholeFileFormattingTests.cs`.

`SyntaxOnlyCSharpAdapterCanPreserveRuntimeLiteralsAndChooseSameLineBraces` normalizes syntax and rewrites braces inside the test, then executes its own result. It never invokes Lucent's formatter. `FormattedMemberExecutionPreservesRawVerbatimAndInterpolatedValues` already executes production-formatted source.

1. Bring over the prototype's distinct body comment, escaped newline in its interpolated string, and same-line-brace checks into the existing production-path test. Wrap the plain C# specimen as a `.lui` member. The prototype's Unicode text is a runtime argument, not formatted source; do not claim it already establishes source-Unicode preservation.
2. Preserve both branches, raw/verbatim/interpolated values and comment retention. Keep explicit expected formatting assertions where whitespace is the product contract; value equivalence alone cannot prove formatting layout.
3. If it stays readable, compile each original/formatted specimen once and invoke all meaningful inputs against those two outputs. Keep this local to the same change; no prerequisite helper extraction or shared global assembly cache is needed.
4. Remove only the prototype method and helpers made unused by that removal. Keep the remaining production formatter and layout-document tests.

Acceptance: no test-local formatting algorithm remains in this method's place; the retained tests invoke Lucent's formatter and independently assert the meaningful output. The change description maps the removed test to its retained cases.

## C2 — Replace the false drawing-equality oracle

File: `tests/Lucent.Core.Tests/DrawingContracts.cs`.

`TrackedRecordingCoalescesEqualCommandsAndDoesNoIdleWork` writes `value.Value = 1` while the value is already 1, then expects the recorder count to stay 1. That proves signal suppression, not equality of separately recorded drawing commands.

1. Make a dependency change that causes recording to run again while producing the same command content, for example two distinct inputs mapped to the same explicit line endpoint.
2. Assert recording count increased and retained drawing identity did not change.
3. Change the input so command content actually changes; assert a new drawing identity. Retain no-idle-recording and old-scene resource lifetime assertions.

Acceptance: removing drawing-content coalescing would fail the identity assertion; removing signal invalidation would fail the recorder assertion. No extra nearly identical test is needed.

## C3 — Strengthen file-preservation assertions in place

File: `tests/Lucent.Lui.Tooling.Tests/ToolingCommandTests.cs`.

`AtomicReplacementPreservesEncodingAndBom` currently uses ASCII-only `original` and `replacement`, checks the BOM and reads the output using the production reader.

1. Retain all six existing encoding/BOM equivalence classes.
2. Use meaningful non-ASCII input and replacement, including a supplementary character.
3. Compare complete file bytes to independently constructed expected preamble plus encoded replacement. Do not use `SourceFileSnapshot.Read` as the only oracle for `SourceFileTransaction.Replace`.
4. Keep the separate concurrent-edit rejection and temporary-file cleanup case. It protects a different data-loss boundary.

Acceptance: output text corruption or incorrect encoding fails even if the production reader and writer share the same mistake. No new encoding matrix or test project is introduced.

## C4 — Remove only demonstrated same-boundary duplication

Files: `tests/Lucent.Core.Tests/ApplicationTests.cs`, `ReactiveDrainBoundaryContracts.cs`, and `tests/Lucent.Lui.Compiler.Tests/NamedComponentTests.cs`.

1. Remove the three-self-posted-callback fragment from `SessionContextMarshalsCallbacksBoundsDrainsAndDropsLatePosts`; the focused `DefaultApplicationEventDrainIncludesSelfPostedCallbacksInItsBudget` already proves reposting, bounded failure and remaining-work recovery through the application session. Rename the enclosing test to describe what remains.
2. Keep its synchronization-context installation/restoration, owner-thread continuation, cross-thread Send rejection and late-post assertions.
3. In the named-component runtime test, remove assertions on private generated spellings only where compilation/execution already demonstrates the intended behavior. Current examples include `__luiCompanionState_CompanionCount` and implementation-specific construction text.
4. Preserve public factory/type identity, deterministic initialization, independent mounts, cleanup and sole-emitter ownership checks. Specifically keep the `Signal<int>` and `Derived<int>` assertions: the current runtime result proves values but not reactive cell kinds. Keep the two negative sole-emitter checks and standalone C# ComponentState generator tests: those exercise another emitter.

Acceptance: record a short removed-assertion → retained-contract mapping. Do not remove the entire application test or collapse the two state-generation pipelines.

## C5 — Batch compatible positive asset metadata cases

File: `tools/Verify-LuiAssets.ps1`.

The `$metadataCases` loop creates/restores/builds nine separate projects before asserting inventory metadata. Seven invalid cases also run separately. The positive cases do not require independent project identities.

1. Reuse the existing `New-CaseProject` helper, which accepts multiple files and item XML, to create one package-consuming fixture containing the nine positive assets. Filenames and logical paths must be distinct case-insensitively; preserve each density setting.
2. Restore/build that fixture once and assert every expected inventory entry, including format, dimensions, density and relative dimensions. Assert exactly nine unique entries with the expected paths so missing, extra or overwritten inputs cannot pass.
3. Keep all seven negative cases isolated initially. A first failing input must not mask another expected diagnostic.
4. Preserve cold consumption, project/package references, generated accessors, asset removal/config invalidation and NativeAOT execution elsewhere in the script. Do not repack candidate packages for each scenario.

Acceptance: the positive metadata group performs one consumer restore/build instead of nine, with all nine independent metadata results checked. No claim about elapsed savings until measured. This does not change the package-verification CI selection.

## C6 — Make a measured decision about 10k stress cases

File: `tests/Lucent.Core.Tests/ReactiveScalingContracts.cs`.

The dependency replacement and individual-disposal cases print timings/allocations for 10,000 nodes but gate on behavioral assertions. They may still expose scale faults; no fresh runtime cost was measured in the review.

1. Use current TRX and one representative focused run, when needed, to establish their cost and whether the measurements are consumed. Separate build time from execution.
2. Preserve direct-effect dependency replacement and individual node/child-scope disposal semantics. Similar derived-consumer tests are not equivalent.
3. Default to keeping the current tests. If measured cost is material and the size adds no demonstrated failure coverage, document a specific follow-up proposal before changing selection. Do not add a new opt-in category, default runner filter or environment-variable mechanism as part of this cleanup.
4. Do not introduce universal timing thresholds, a dashboard or a new performance framework. The measurement and a documented keep/follow-up decision complete this step; no runtime reduction is required.

## Verification

These are implementation commands, not already-run evidence. Use only commands affected by the actual batch; run a narrow filter during edits, then the affected project suite once at closeout. Passing runner commands include warning-clean builds. Do not repeat unchanged checks between bookkeeping commits.

```powershell
./tools/Test-Repository.ps1 -Suite Managed -Project Lucent.Lui.Compiler.Tests -Filter 'FullyQualifiedName~Formatting|FullyQualifiedName~NamedComponent'
./tools/Test-Repository.ps1 -Suite Managed -Project Lucent.Core.Tests -Filter 'FullyQualifiedName~DrawingContracts|FullyQualifiedName~ReactiveDrainBoundaryContracts|FullyQualifiedName~ApplicationTests|FullyQualifiedName~ReactiveScalingContracts'
./tools/Test-Repository.ps1 -Suite Managed -Project Lucent.Lui.Tooling.Tests
./tools/Verify-LuiAssets.ps1 -Feed <candidate-package-directory> -Version <exact-version>
git diff --check
```

Expected: affected tests pass with no unexpectedly empty selection; asset script succeeds against the same candidate used for comparison. Test-quality-only changes do not require new desktop evidence. Reuse package evidence if intervening edits cannot affect it.

For a misleading assertion, demonstrate sensitivity against the original defect or a small deliberate fault when practical. Keep deliberate faults in an isolated disposable checkout; never modify the shared active implementation merely to prove a test fails. This is a targeted confidence check, not a mandatory mutation gate for every test.

## Closeout and limits

Report removed/replaced assertions, their retained owners, actual setup reduction, selected checks and unmeasured costs. Match repository formatting and fixture cleanup conventions. Keep cold/warm state and resource lifetimes explicit.

If a deletion would remove the sole evidence for an unclear contract, retain it until the contract is resolved. If a stronger oracle exposes a production bug, report it and preserve the failing reproduction; do not weaken the assertion or turn test cleanup into an unrelated runtime rewrite. Update this plan's status and the advisory index after implementation.
