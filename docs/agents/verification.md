# Verification during pre-release development

Use risk-based verification for pre-release work so routine changes stay fast and the selected checks match the affected behavior.

## Default loop

- While editing, run the smallest meaningful regression for the changed behavior. Check both the original failure and the intended result when fixing a bug.
- Before committing, run the affected contract suite, a warning-clean build of affected projects, authored formatting, and `git diff --check`.
- Reuse passing checks when the intervening changes cannot affect them. Re-run for source changes, failures, or a concrete unresolved concern; do not repeat the same check solely to add ceremony to every commit.
- Record the commands/results and any known limitation briefly in the issue. Keep large logs and binaries as artifacts.

The managed repository check runs the inexpensive Core architecture/public API preflight after restore/build and before managed test execution. The negative dependency/public-API fixtures still run after the managed tests, preserving both fast failure and negative evidence. For VS Code extension changes, also run `node --test extensions/lucent-lui-vscode/extension.test.cjs`.

Synthetic language-server fixtures reference the built Core assembly when they only need its public types. Tests for navigation into Core source still load the Core project; project-graph and freshness tests retain their authored project dependencies. Keep these distinctions when adding tests instead of loading the full framework graph by default.

## Add checks when the change reaches them

- Compiler/language/editor changes: affected parser/compiler/generator/LSP/extension tests. Use the SDK/NativeAOT consumer proof when lowering, packaging, or generated-runtime behavior changes. Measure the affected tooling operation for a credible performance regression.
- Layout/render/input/accessibility changes: affected Core/application/platform contracts and a published application smoke covering the changed pixels or interaction. Check DPI/resize when geometry changes.
- Lifecycle/hosting/AOT/dependency changes: published NativeAOT startup/close/failure proof, relevant architecture/package inventory checks, and UIA evidence if the host or semantic bridge changes.
- Documentation-only changes: inspect content and links; skip builds and UI tests.

## Release checks

Reserve broad package, performance, Sandbox, and manual checks for release candidates, substantial cross-cutting changes, or risks that focused checks cannot contain. Independent review is for substantial correctness, architecture, or security risk, not a mandatory second pass on every small change.

Broad manual visual/accessibility/IME walkthroughs are release decisions, not routine issue-closure chores. Real-language IME certification remains supplemental. State exactly what was automated, observed, waived, or left unverified; do not manufacture pass records or silently reuse source-bound evidence for different artifacts.

Keep existing warnings, AOT/trimming analysis, security checks, and exact source-map/freshness contracts intact. Moving faster means choosing relevant checks and accepting explicit bounded pre-release risk, not hiding a known failure or changing a threshold until it passes.
