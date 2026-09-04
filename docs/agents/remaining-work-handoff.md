# Handoff: seal M7 and prepare the next roadmap

## Mission

Finish and seal the current M7 `.lui` editor remediation, then complete the two open Native QoL issues and leave a clean, issue-driven base for choosing the next roadmap.

Work in this order:

1. seal [#55](https://github.com/RichiCoder1/lucent/issues/55), [#41](https://github.com/RichiCoder1/lucent/issues/41), and [#26](https://github.com/RichiCoder1/lucent/issues/26);
2. fix the Issue Browser resize defect described below;
3. implement [#58](https://github.com/RichiCoder1/lucent/issues/58), then [#59](https://github.com/RichiCoder1/lucent/issues/59);
4. create one follow-up issue for repository-document cleanup and test-harness modernization.

Before the resize and QoL steps, read [execution refinements](remaining-work-plan.md) for the concrete test matrix and unresolved sizing/lifecycle seams.

Do not reconfigure the Oracle model. Follow `AGENTS.md`, `CONTEXT.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, and the issue bodies. Use affected checks while iterating. The full M7 gate below applies to this seal; subsequent issues follow the owner-approved [pre-release verification policy](verification.md), which supersedes the original full-gate-per-issue requirement.

## Starting point

This handoff began at checkpoint `62aacb1`, whose parent is `68fe69f` (`Complete cross-project LUI navigation`). That checkpoint is intentionally **not a sealed M7 release** and contains unsealed cross-project LSP/compiler remediation. Subsequent fixes do not imply sealing; current evidence must identify the accepted source. Inspect the actual commit and working tree before editing:

```powershell
git status --short
git show --stat --oneline HEAD
```

The remediation addresses:

- deterministic dependency-first project-graph traversal;
- graph-aware C#/`.lui` definition, hover, rename, and references;
- exact project/document freshness checks before publication;
- safe paired-tag rename mapping;
- dirty-buffer replay after project reload failure and recovery;
- one physical linked `.lui` file included by multiple projects;
- reference identity independent of equivalent SDK installation paths;
- an explicit VS Code cross-language rename command without stealing ordinary C# rename providers.

The latest focused checkpoint passed Debug compiler/LSP corpora, VS Code Node tests, CSharpier, `git diff --check`, and the no-staged-files check. Release checks and every milestone-level record must be treated as pending until rerun from the checkpoint commit.

`docs/M7-EVIDENCE.md` correctly marks prior package/manual/clean-machine records as stale. Do not reuse evidence bound to `68fe69f` after source changes.

## Step 1: seal M7

### Focused validation

First confirm there is no temporary instrumentation:

```powershell
rg -n "DEBUG-lsp" src tests
```

Then run at least:

```powershell
./.dotnet/dotnet.exe build tests/Lucent.Lui.Compiler.Tests/Lucent.Lui.Compiler.Tests.csproj -c Release --no-restore -warnaserror
./.dotnet/dotnet.exe tests/Lucent.Lui.Compiler.Tests/bin/Release/net10.0/Lucent.Lui.Compiler.Tests.dll
./.dotnet/dotnet.exe build tests/Lucent.Lui.LanguageServer.Tests/Lucent.Lui.LanguageServer.Tests.csproj -c Release --no-restore -warnaserror
./.dotnet/dotnet.exe tests/Lucent.Lui.LanguageServer.Tests/bin/Release/net10.0/Lucent.Lui.LanguageServer.Tests.dll
node --test extensions/lucent-lui-vscode/extension.test.cjs
pwsh tools/Verify-LuiSdk.ps1
pwsh tools/Measure-LuiTooling.ps1 -Verify
pwsh -NoProfile -File tools/Verify-Formatting.ps1
git diff --check
```

The LSP regression matrix must retain explicit coverage for:

- a diamond `ProjectReference` graph with a shared dependency;
- a shared physical linked `.lui` file observed through every owning project;
- dirty overlays surviving a failed reload and later recovery;
- duplicate logical paths with different full freshness identities;
- C# declarations in referenced projects participating in rename/references;
- stale, ambiguous, disjoint, overlapping, or incomplete mappings refusing publication.

Have a fresh read-only reviewer inspect correctness before the full gate. Fixes invalidate that verdict.

### Full automated gate

Run the current-source full gate with PowerShell 7, not Windows PowerShell 5.1:

```powershell
pwsh -NoProfile -File tools/Verify-M1.ps1
```

Commit the implementation candidate only after focused review and the full gate pass. Then regenerate candidate/package evidence from that exact clean commit:

```powershell
pwsh tools/Verify-M6.ps1 -Mode Candidate
pwsh tools/Run-M6Sandbox.ps1 `
  -CandidateZipPath artifacts/m6/lucent-win-x64.zip `
  -CandidateEvidencePath artifacts/m6/m6-evidence.json `
  -GateRecordPath artifacts/m6/clean-machine-gate.json
```

Finalize using the verifier's documented current schema and update `docs/M7-EVIDENCE.md` with exact commit and artifact hashes. Never accept records by existence alone.

### Manual-gate policy

The owner has relaxed repeated broad manual attestation after the current `0.1`/M7 gate. Routine future issue closure should rely on automated contracts, NativeAOT, UIA, pixel, and Sandbox evidence. Repeat broad visual/accessibility/IME walkthroughs only for a planned `0.x` or major release, or when a change invalidates an area that cannot yet be automated.

For this final M7 closure, make the smallest honest source-bound decision: do not silently reuse stale records, but do not demand repeated manual work already replaced by deterministic automation. Record any explicit waiver or remaining manual assertion in the final evidence.

### Closeout

After a fresh technical review and a separate acceptance/evidence review both pass:

1. publish and reinstall the VS Code extension/server from the sealed source;
2. ask the owner only for the bounded editor interaction that cannot be automated;
3. push the implementation and evidence commits;
4. close #55 and #41 with exact validation evidence;
5. close #26 only if its complete roadmap acceptance is satisfied.

## Step 2: fix Issue Browser resizing

Observed defect: the window resizes, but content stays at its original size and the newly exposed area is black.

Known likely cause: application `.lui` styles hard-code the original dimensions—`IssueBrowser.lui` fixes the root to `800 × 500`, while `Header.lui`, `FilterBar.lui`, `IssueRow.lui`, and `Details.lui` fix width to `800`. The host viewport itself resizes.

Fix this at the application/layout-property boundary without adding resize synchronization to application code. Add one focused resize regression and retain the existing external pixel smoke. File or link a GitHub issue before implementation if none exists.

## Step 3: Native QoL issues

### #58 — scalar expression children

Implement the smallest language/compiler extension accepted by the issue. It must lower through the canonical C# authoring API, preserve source maps and diagnostics, and avoid a parallel runtime. Use the existing `.lui` parser/compiler/generator fixtures and NativeAOT SDK proof.

### #59 — `LucentApplication`

Add the portable `LucentApplication` builder named in the issue. `UseWindows()` is an explicit platform marker today; future platform service registration can follow. Keep platform hosting out of Core and remove the current application ceremony of manually creating a `ReactiveGraph`, extracting a composition, and passing it to a raw Windows bootstrap.

Do #58 before #59 unless the current issue dependencies say otherwise.

## Step 4: create the cleanup/testing issue

Create one GitHub issue and add it to Lucent Native Project 4. It should cover two independently executable tracks:

1. **Documentation cleanup**
   - remove stale milestone narration from the active documentation surface;
   - preserve durable architecture and user guidance in `CONTEXT.md`, ADRs, and focused docs;
   - archive historical evidence only when it still has value;
   - move execution history and closure records to GitHub issues instead of duplicating them indefinitely in the repository.
2. **Test modernization**
   - migrate ad hoc executable/UI/E2E checks to an appropriate maintained .NET test framework;
   - evaluate Appium for externally observable Windows UI paths where it fits;
   - retain test-owned published fixture hosts for deterministic NativeAOT/UIA proof;
   - automate current manual gates with Windows Sandbox where practical;
   - clarify what the owner means by “FastPass” before adopting or evaluating it;
   - keep one local command and CI path rather than parallel orchestration.

Do not perform this cleanup before the issue captures scope, migration order, preserved evidence, and deletion criteria.

## Operating constraints

- Check usage with `C:\Users\richa\.pi\agent\scripts\codex-usage-guard.ps1 30`; exit `0` means stop, `1` means continue, and `2` means the check failed.
- Do not weaken warnings, trim/AOT analysis, security boundaries, stale-result checks, or evidence validation.
- Do not add compatibility paths for the archived Avalonia implementation.
- Keep one writer at a time and preserve unrelated working-tree changes.
- Never modify Defender settings. The owner separately approved the current renderer-test exception after a read-only source/build assessment; do not generalize that exception.
- Prefer deletion and existing seams over new abstractions. New framework API requires a real current consumer.

## Completion state

This handoff is finished when M7 is sealed and its issues are closed, the resize defect and #58/#59 are complete with their gates, the cleanup/testing issue is filed in Project 4, and the repository is clean and pushed.
