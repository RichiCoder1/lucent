# Plan 004: Improve assertions and remove unnecessary test preparation

## Status and scope

- Status: implemented under #311, September 23, 2026. H1–H3 are complete; H4 orchestration is deferred. Published passes; Performance setup is verified, with existing benchmark failures tracked separately in #313. See the [execution record](../docs/plans/test-quality-execution.md).
- Priority: high for precise editor/rendering oracles; medium for runner preparation.
- Effort: medium for H1–H3; H4 is an optional follow-up, not required for completion.
- Baseline: `a2e74acd448b488c22ecb71ec5b12d6de436110a`, refreshed September 22, 2026; additional working-tree changes remain.
- Recommended prerequisite: plan 005. No dependency on plan 003.
- Ownership: LSP and renderer helpers with their first existing consumers, `tools/Test-Repository.ps1`, and corresponding runner documentation. Plan 003 owns compiler formatting and asset batching. No new public testing package or production runtime changes.

The objective is precise expected results and cheaper setup. Improve existing tests alongside each helper rather than creating a parallel layer of assertions. Retain compiler-map versus LSP-transport, Core behavior versus native input/UIA, and managed versus package/NativeAOT boundaries.

Start after Implementation's current work. Inspect `git status --short` and `git diff a2e74acd -- <owned-paths>` before editing; preserve others' changes and #310 regressions. Match existing fixture conventions and avoid reimplementing already-completed recommendations.

## H1 — Exact editor locations and edits

Owned files: test-local helpers under `tests/Lucent.Lui.LanguageServer.Tests/` and `NamedComponentLanguageServerContracts.cs`. Reuse the existing protocol client; do not introduce another LSP transport.

The original named-method protocol test checks a matching file URI, reference/edit counts `>= 2`, and non-null completion. Those checks accept incorrect ranges, extra edits or irrelevant results.

1. Introduce a small authored-fixture marker helper for expected spans. Strip markers before sending the source. Compute positions independently of Lucent's production parser/maps using UTF-16 code units and the exact source string sent. CRLF is the normal Windows fixture case, not merely an edge case; do not normalize it accidentally.
2. Compare complete expected URI/range collections for definitions/references independently of response ordering. Detect duplicate actual entries before normalizing order. For rename, compare exact replacement text and ranges and reject unexpected document edits.
3. Require the relevant completion member, not exact ordering of unrelated candidates.
4. Migrate existing named-method and same-arity overload scenarios. Preserve unsaved edits and graph/config freshness. Add only missing cases, not tests named after every helper method.

Failure output should show missing/unexpected locations and the authored excerpt. Small focused checks for marker conversion and collection comparison are worthwhile because those helpers can incorrectly report success: cover CRLF, a supplementary character and duplicate/unexpected ranges. Simple wrappers do not need a parallel suite.

Keep diagnostic assertions local for now: existing configuration tests already check code and severity. Extract a diagnostic helper only if actual repeated logic warrants it during implementation; do not require one to complete H1. If introduced, check ID/severity/authored span without pinning full Roslyn prose or discarding unexpected diagnostics.

Acceptance: an incorrect authored range, missing occurrence, duplicate/extra rename edit or wrong replacement fails. The exact compiler-map test stays: this is the public-consumption boundary.

## H2 — Attributable renderer pixels

Owned files: `tests/Lucent.Renderer.Skia.Tests/DrawingRendererContracts.cs` and a small local pixel helper. The combined scene currently checks rectangle/line pixels plus a nonzero-byte count that other shapes can satisfy.

1. Introduce explicit pixel/region assertions that report logical and physical coordinates, scale and actual RGBA against independently specified expected bounds.
2. Use isolated geometry to prove each otherwise unchecked ellipse, arc, rounded rectangle and filled/stroked path. Parameterize only genuinely shared setup. For stroked shapes, choose a sufficiently thick stroke for stable samples and assert a known interior/exterior remains unpainted; do not substitute only a filled shape for stroke coverage.
3. Keep the combined clipping/transform/opacity interaction scene and its three scales. Remove the broad nonzero-byte assertion only after replacing its claimed coverage.
4. Sample stable painted/unpainted regions rather than antialiasing edges. Avoid large golden-image snapshots where small attributable assertions suffice. Capture failed output only where useful, not on every pass.

Acceptance: omitting a primitive replay causes its own assertion to fail even while other shapes render. Expected paint must not be computed by the production renderer.

## H3 — Small runner corrections

Owned files: `tools/Test-Repository.ps1` and corresponding usage text in `docs/TESTING.md`. Leave the explicit Native invocation in `.github/workflows/tests.yml` intact.

Current shape: `Invoke-Published` unconditionally invokes Native after its published desktop tests. `Invoke-Native` publishes/runs four comprehensive test suites. Performance publishes both Issue Browser and TestHost, although its verifier only receives the app executable.

1. Remove the implicit `Invoke-Native` call from Published. Keep Native explicitly selectable and in package verification. Keep NativeAOT execution of the published application/TestHost within Published.
2. Let the existing publication helper prepare only the fixtures needed by its caller. Performance prepares Issue Browser plus the verifier; it must not publish unused TestHost. Published and Accessibility retain their current required fixtures and cleanup.
3. Preserve existing single-suite command syntax, Managed default, environment restoration and artifact-path safety. Avoid multi-suite selection or a new command-runner abstraction in this required step.
4. Update docs to make the suite boundaries explicit. Do not imply Published includes full Native contract coverage after the change.

Acceptance: Published alone no longer invokes Native; Native remains selectable; Performance does not build an unused TestHost; all checks still receive current expected paths. Review the command trace and run the affected real path in an authorized desktop window. Do not add a generic orchestration test framework for these small branches.

## H4 — Optional same-invocation preparation reuse

This is a follow-up proposal, not required to complete the three-plan package. Published and Accessibility independently republish the same fixtures, but CI currently selects Managed and Native separately. First establish that repeated combined desktop runs are common and costly enough to warrant a larger runner change.

If justified, propose one invocation that prepares the union of required immutable binaries once, passes their paths to each check, and keeps mutable application state/processes isolated. Preserve single-suite compatibility. Reject ambiguous Project/Filter combinations before doing work, deduplicate selections, and stop dependent execution on preparation failure. No persistent cross-run artifact cache or evidence database.

Use the smallest verification surface appropriate to the actual branches; a generic scheduler or PowerShell test-driver platform is not a prerequisite. Multi-suite flags and a command-runner seam are not authorized requirements of H3. Record a proceed/defer recommendation after measuring; a defer decision completes H4's advisory disposition.

## Deferred: grammar tokenizer toolchain

The extension's current regex-shape assertions are weak, but running its TextMate grammar requires a compatible tokenizer and regex engine with test dependencies, lockfile/install handling, attribution and CI documentation. The baseline extension tests are dependency-free. That ongoing cost was understated in the initial plan.

Keep the current structural grammar assertions for this package. If highlighting regressions justify a separate improvement, scope real tokenization with the ecosystem engine rather than another hand-written parser, explicitly include dependency/CI costs, and verify current package documentation then. Do not add that toolchain merely to replace two cheap checks now.

## Existing harness guidance

Use `Lucent.Testing` and `Lucent.Testing.Skia` incrementally when touched component/application tests actually benefit. No tracked suite-wide migration or public API expansion is needed here. Plan 005 records owner-thread, snapshot-lifetime and native-boundary rules in the authoring guidance.

## Verification and completion

Run affected commands only: a focused filter during edits, then the affected project suite once at closeout. Commands below are implementation guidance, not current pass evidence:

```powershell
./tools/Test-Repository.ps1 -Suite Managed -Project Lucent.Lui.LanguageServer.Tests -Filter 'FullyQualifiedName~NamedComponent'
./tools/Test-Repository.ps1 -Suite Managed -Project Lucent.Renderer.Skia.Tests -Filter 'FullyQualifiedName~DrawingRendererContracts'
git diff --check
```

For H3, run the affected Published/Performance path only when desktop use is authorized and inspect its command trace; do not rerun all suites to verify removal of an implicit invocation. Existing Native evidence remains reusable when its code/path is unchanged. Test-quality-only changes do not require republishing applications.

Report first consumers, improved failure output and actual build/publish counts. Do not claim elapsed savings without measurement. If a stronger assertion exposes a product bug, retain the reproduction and coordinate a separate correction rather than weakening the expected result. Complete H1–H3 independently; optional/deferred work must not block delivery. Update this plan and the advisory index.
