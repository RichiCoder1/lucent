# Plan 005: Set a concise test-authoring bar for agents

## Status and scope

- Status: implemented September 23, 2026 under #311. Root guidance points to the expanded verification policy; TESTING.md links the same source. Content/link review and whitespace validation pass; no build or UI check was required for the policy edit.
- Priority: high; recommended first change.
- Effort: small, documentation only.
- Baseline: `a2e74acd448b488c22ecb71ec5b12d6de436110a`, refreshed September 22, 2026 after Fable review; additional working-tree changes remain.
- Dependencies: none. Plans 003/004 consume the guidance; their implementation is not a prerequisite for adopting it.
- Owned files: root `AGENTS.md`, `docs/agents/verification.md`, and a short link/scope clarification in `docs/TESTING.md` if needed. Update this plan's status/index on completion.

The existing policy already selects checks by affected behavior/risk and avoids repeated verification. It does not give agents enough guidance for deciding whether to add a test, strengthen an assertion, or consolidate overlapping coverage. Add that missing decision guidance without increasing routine ceremony.

Before editing, inspect current guidance and local changes. Preserve unrelated project instructions and established pre-release choices. This plan does not change external user skill files, require a new skill, add CI gates or redefine release certification.

## A1 — Add a short always-visible rule

Place this near the existing Verification paragraph in root AGENTS.md, adapting only to avoid duplication:

> When adding, changing or removing tests, read `docs/agents/verification.md` for the test-authoring bar and coverage-ownership rules. Prefer independent assertions at the cheapest adequate layer; extend existing coverage before adding overlapping tests.

Keep this pointer short. The detailed rules, examples and layer ownership belong in the existing verification document, not repeated in every agent's context. The trigger explicitly includes adding, changing and removing tests.

## A2 — Add decision guidance to the existing verification policy

Add one section on test authoring and consolidation. Keep the existing risk-based execution policy. Include these rules in direct, actionable prose:

1. **Name the defect and contract.** A test should make clear what plausible wrong behavior it would reject. Use outcomes such as accepted/rejected writes, observable state, cleanup, stale-work rejection, authored ranges, independent bytes or attributable pixels. No requirement to add a test for a trivial reversible edit or to prove ordinary language/library field assignment.
2. **Search before adding.** Find the affected behavior's existing owner. Extend or strengthen a case when it has the same failure mode. New state transitions or distinct rejection/cleanup paths can deserve separate cases even within one feature.
3. **Choose the adequate layer.** Keep behavior matrices with the lowest faithful owner. Add higher-level representative wiring cases and failures specific to transport, distribution or execution environment. Do not turn every regression into compiler + Core + package + desktop copies.
4. **Keep expected results independent.** Do not test a local reimplementation or use the same production algorithm to generate both expected and actual. Mocks/fakes may control time, I/O and external outcomes; assert how Lucent responds, not that a mock returned its configured value. Differential/round-trip checks need independent expected cases where a shared defect could make both sides agree.
5. **Assert the claimed behavior.** A non-null result, broad count or successful compilation is insufficient when the claim concerns exact edits, a specific rendered shape or retained identity. Prefer a few precise assertions. Internal counters/identities are legitimate for explicit no-idle-work, lifetime, invalidation and performance contracts; do not ban all internal observations.
6. **Avoid incidental coupling.** Do not pin private generated names, arbitrary call order or full diagnostic prose unless those details are intentional contracts. Public generated identities, exact source spans, sole-emitter ownership and forbidden dependencies remain valid assertions. Formatting text may be exact because formatting output is the feature.
7. **Use meaningful cases.** Parameterize equivalence classes and boundary values; test cross-products only where interaction matters. Use controlled clocks and explicit completions for portable behavior. External/native observations may use bounded polling with useful timeout context. Never retry side-effecting actions until the test passes or discard an initial failure.
8. **Consolidate with a retained owner.** For each removed meaningful assertion, identify the retained test/boundary or explain that it only pinned incidental implementation. Preserve independent cancellation, cleanup, invalid state, stale result, data preservation and transport failures. Do not remove an unresolved failing regression as cleanup.
9. **Prove sensitivity proportionately.** Reproduce bugs and assert their intended outcome. When practical, verify against pre-fix code or a small deliberate fault in isolation. No mandatory mutation framework, minimum coverage percentage or numerical test-count target. Simple assertion wrappers do not automatically need wrapper tests.
10. **Share preparation, isolate state.** Reuse immutable package/binary fixtures within one verification invocation. Give tests fresh mutable state and explicit lifetimes. Preserve cold/warm invalidation cases intentionally. Reuse passing checks only when intervening changes cannot affect them; do not silently reuse stale binaries.

## A3 — Include concrete examples and layer ownership

Use a compact set of examples drawn from this repository:

| Weak or redundant choice | Better choice |
| --- | --- |
| A test formats using its own Roslyn normalization implementation | Run Lucent's formatter and assert expected layout plus preserved runtime values |
| Writing the same signal value and calling that drawing coalescing | Cause real rerecording, keep equal commands, assert retained identity |
| Non-null completion / rename count >= 2 | Expected member and exact authored edit/range set |
| Many shapes plus a global nonzero-pixel count | Attributable samples for each claimed primitive and one combined clip/transform case |
| Another full package app for a compatible positive metadata input | Add the input to the shared package fixture and assert its independent inventory entry |
| Removing a Windows UIA test because Core selection is covered | Keep it when it verifies provider identity, command translation or real native delivery |

Explain owners briefly: Core owns portable state/algorithms; compiler/generator owns syntax/lowering/maps; editor/protocol owns current buffers and authored operations; Skia owns pixels/shaping; Windows owns native/UIA transport; package consumers own distribution/targets/AOT. The existing Lucent.Testing harness is appropriate for component/application behavior, not a replacement for native input or UIA certification.

No per-test metadata schema or new required template. Names and assertions normally suffice. For an expensive new test, one sentence in the change description should identify its unique boundary. For deletion, use a short old-test → retained-contract mapping. This is review evidence, not an approval step.

## A4 — Keep execution guidance consistent

The current verification document says to run an affected suite and warning-clean build before committing. Clarify that this means the affected implementation change, with reuse of unaffected passing evidence; it does not demand rerunning suites on documentation/bookkeeping-only commits. Keep documentation-only checks as content/link review and whitespace validation.

Do not remove warnings/AOT analysis, security checks, precise source-map/freshness regressions or explicit native transport tests to achieve speed. Do not require a desktop smoke for changes confined to test assertions when native behavior is unchanged. Runner or native integration changes still require the relevant real path.

Keep command catalogs and harness setup in `docs/TESTING.md` and `docs/HEADLESS-TESTING.md`; use links instead of copying them into AGENTS.md. The detailed authoring section should remain the single policy source.

## Verification and acceptance

Review the three documents together. Confirm:

- The always-loaded instruction tells an agent when to read the longer policy.
- Every example yields a concrete add/strengthen/retain/consolidate decision.
- No rule requires new tests for every edit, blanket mutation testing, numeric coverage/count gates or redundant full-suite runs.
- Legitimate same-feature/different-boundary coverage remains explicitly allowed.
- Existing risk-based, focus-window and evidence-reuse rules remain intact.
- Links resolve and the documentation describes helper/runner features as planned until actually delivered.

```powershell
git diff --check -- AGENTS.md docs/agents/verification.md docs/TESTING.md
```

Expected: no whitespace errors; manual content/link inspection passes. No builds, tests or desktop interaction are needed for this documentation change. Do not invent a tests-passed record for a policy edit.

Completion is the guidance applied to the live documents with a concise diff, not merely this plan existing. Update the advisory index/status when done. The subsequent cleanup/harness changes provide the first practical examples; no recurring compliance audit or new enforcement bot is required.
