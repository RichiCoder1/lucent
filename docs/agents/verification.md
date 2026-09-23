# Verification during pre-release development

Use risk-based verification for pre-release work so routine changes stay fast and the selected checks match the affected behavior.

## Default loop

- While editing, run the smallest meaningful regression for the changed behavior. Check both the original failure and the intended result when fixing a bug.
- For implementation changes, run the affected contract suite, a warning-clean build of affected projects, authored formatting, and `git diff --check` before committing. Reuse unaffected passing evidence across bookkeeping commits; documentation-only changes need content/link review and whitespace checks.
- Reuse passing checks when the intervening changes cannot affect them. Re-run for source changes, failures, or a concrete unresolved concern; do not repeat the same check solely to add ceremony to every commit.
- Record the commands/results and any known limitation briefly in the issue. Keep large logs and binaries as artifacts.

The managed repository check runs the inexpensive Core architecture/public API preflight after restore/build and before managed test execution. The negative dependency/public-API fixtures still run after the managed tests, preserving both fast failure and negative evidence. For VS Code extension changes, also run `node --test extensions/lucent-lui-vscode/extension.test.cjs`.

Synthetic language-server fixtures reference the built Core assembly when they only need its public types. Tests for navigation into Core source still load the Core project; project-graph and freshness tests retain their authored project dependencies. Keep these distinctions when adding tests instead of loading the full framework graph by default.

## Add checks when the change reaches them

- Compiler/language/editor changes: affected parser/compiler/generator/LSP/extension tests. Use the SDK/NativeAOT consumer proof when lowering, packaging, or generated-runtime behavior changes. Measure the affected tooling operation for a credible performance regression.
- Layout/render/input/accessibility changes: affected Core/application/platform contracts and a published application smoke covering the changed pixels or interaction. Check DPI/resize when geometry changes.
- Lifecycle/hosting/AOT/dependency changes: published NativeAOT startup/close/failure proof, relevant architecture/package inventory checks, and UIA evidence if the host or semantic bridge changes.
- Documentation-only changes: inspect content and links; skip builds and UI tests.

## Test authoring and coverage ownership

Before adding or changing a test, name the plausible defect and find its existing
coverage owner. Extend a case for the same failure mode; separate transitions,
rejection paths and cleanup boundaries may deserve their own cases. Trivial,
reversible edits and ordinary library field assignment do not require new tests.

- **Assert the contract independently.** Use observable state, accepted/rejected
  writes, cleanup, stale-work rejection, exact authored ranges, independent bytes
  or attributable pixels. Avoid test-local reimplementations and expected values
  computed by the production algorithm. Round trips need independent expectations
  where both directions could share a defect. Fakes control time/I/O; assertions
  establish Lucent's response rather than the fake's configured result.
- **Choose the lowest faithful layer.** Keep behavior matrices with their owner;
  higher layers need representative wiring and their own transport, distribution
  or execution failures. A regression need not be copied into every suite.
- **Match assertions to claims.** Non-null results, broad counts and successful
  compilation cannot prove exact edits, a particular shape or retained identity.
  Internal counters and identity checks are valid for lifetime, invalidation,
  no-idle-work and performance contracts.
- **Keep intentional contracts stable.** Public generated identities, exact spans,
  sole-emitter ownership, forbidden dependencies and formatting output are valid
  assertions. Avoid incidental private generated spellings, arbitrary call order
  and complete diagnostic prose unless those are the contract being tested.
- **Use meaningful cases.** Cover equivalence classes and boundaries; expand a
  cross-product only when interaction matters. Prefer controlled clocks and explicit
  completions for portable behavior. Native observations may use bounded polling
  with useful timeout context. Preserve the first failure; do not retry side effects
  until they pass.
- **Consolidate with a retained owner.** For removed meaningful assertions, record
  a short old-test → retained-contract mapping, or explain why the assertion only
  pinned incidental implementation. Preserve independent cancellation, invalid
  state, cleanup, stale result, data preservation and transport failures. Keep an
  unresolved failing regression.
- **Prove sensitivity proportionately.** Assert the intended bug outcome and,
  when practical, reproduce it against pre-fix code or a deliberate fault in an
  isolated checkout. There is no mandatory mutation framework, coverage percentage
  or test-count target. Simple assertion wrappers need no parallel wrapper suite.
- **Share preparation, isolate state.** Reuse immutable binaries/packages within
  an invocation, with fresh mutable state and explicit lifetimes. Preserve cold/warm
  invalidation cases and current binaries. Reuse passing evidence only when later
  changes cannot affect it.

| Weak choice | Better choice |
| --- | --- |
| A test-local Roslyn formatter | Lucent's formatter, expected layout and preserved runtime values |
| Equal signal assignment described as drawing coalescing | Real rerecording with equal commands and retained drawing identity |
| Non-null completion or rename count ≥ 2 | Relevant member and the complete authored edit/range set |
| Many shapes and a global nonzero-pixel count | Attributable painted/unpainted samples plus combined clip/transform coverage |
| Another package app for compatible positive metadata | One shared fixture with each independent inventory entry checked |
| Deleting UIA coverage because Core selection is tested | Retain provider identity, command translation and native delivery evidence |

Core owns portable state/algorithms; compiler/generator owns syntax, lowering and
maps; editor/protocol owns current buffers and authored operations; Skia owns pixels
and shaping; Windows owns native/UIA transport; package consumers own distribution,
targets and AOT. Use [Lucent.Testing](../HEADLESS-TESTING.md) for component/application
behavior, preserving owner-thread and snapshot lifetimes. It does not replace native
input or UIA checks.

Names and assertions normally provide enough explanation. For an expensive new
test, identify its unique boundary in one sentence in the change description.
Coverage mappings are review evidence, not an approval step or metadata schema.
Assertion-only changes with unchanged native behavior need no desktop smoke;
runner/native integration changes need the relevant real path. Keep command catalogs
and setup in [TESTING.md](../TESTING.md).

## Release checks

Reserve broad package, performance, Sandbox, and manual checks for release candidates, substantial cross-cutting changes, or risks that focused checks cannot contain. Independent review is for substantial correctness, architecture, or security risk, not a mandatory second pass on every small change.

Broad manual visual/accessibility/IME walkthroughs are release decisions, not routine issue-closure chores. Real-language IME certification remains supplemental. State exactly what was automated, observed, waived, or left unverified; do not manufacture pass records or silently reuse source-bound evidence for different artifacts.

Keep existing warnings, AOT/trimming analysis, security checks, and exact source-map/freshness contracts intact. Moving faster means choosing relevant checks and accepting explicit bounded pre-release risk, not hiding a known failure or changing a threshold until it passes.
