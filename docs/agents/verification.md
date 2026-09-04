# Verification during pre-release development

After the M7 handoff chunk, use risk-based verification so routine work stays fast. This policy supersedes the earlier requirement to run the full milestone gate for every issue closure.

## Default loop

- While editing, run the smallest meaningful regression for the changed behavior. Check both the original failure and the intended result when fixing a bug.
- Before committing, run the affected contract suite, a warning-clean build of affected projects, authored formatting, and `git diff --check`.
- Reuse passing checks when the intervening changes cannot affect them. Re-run for source changes, failures, or a concrete unresolved concern; do not repeat the same gate just to attach a new ceremony to every commit.
- Record the commands/results and any known limitation briefly in the issue. Keep large logs and binaries as artifacts.

## Add checks when the change reaches them

- Compiler/language/editor changes: affected parser/compiler/generator/LSP/extension tests. Use the SDK/NativeAOT consumer proof when lowering, packaging, or generated-runtime behavior changes. Measure the affected tooling operation for a credible performance regression.
- Layout/render/input/accessibility changes: affected Core/application/platform contracts and a published application smoke covering the changed pixels or interaction. Check DPI/resize when geometry changes.
- Lifecycle/hosting/AOT/dependency changes: published NativeAOT startup/close/failure proof, relevant architecture/package inventory checks, and UIA evidence if the host or semantic bridge changes.
- Documentation-only changes: inspect content and links; skip builds and UI tests.

## Larger gates

Run `tools/Verify-M1.ps1` for a planned release, a substantial cross-cutting change, or evidence that targeted checks cannot contain the risk. Run package/performance/Sandbox gates when publishing a release candidate or changing their relevant paths. Independent review is for substantial correctness, architecture, or security risk, not a mandatory second pass on every small change.

Broad manual visual/accessibility/IME walkthroughs are release decisions, not routine issue-closure chores. Real-language IME certification remains supplemental. State exactly what was automated, observed, waived, or left unverified; do not manufacture pass records or silently reuse source-bound evidence for different artifacts.

Keep existing warnings, AOT/trimming analysis, security checks, and exact source-map/freshness contracts intact. Moving faster means choosing relevant checks and accepting explicit bounded pre-release risk, not hiding a known failure or changing a threshold until it passes.