# Advisory reviews and plans

Read-only advisory reviews and plans for Lucent. These are separate from `docs/plans/`, which contains product architecture and implementation plans.

| ID | Plan | Status | Baseline |
| --- | --- | --- | --- |
| 001 | Exhaustive dynamic security testing (separate local draft) | Proposed; outside this work package | `2806393` plus the in-progress Implementation working tree on 2026-09-09 |
| 002 | [Test value and suite review](002-test-value-and-suite-review.md) | Source review complete; execution split into 003–005 | `8d43638` plus the active working tree on 2026-09-22 |
| 003 | [Existing test cleanup](003-existing-test-cleanup.md) | Implemented under #311; C6 retained | `a2e74acd` plus the active working tree on 2026-09-22 |
| 004 | [Test harness improvements](004-test-harness-improvements.md) | Implemented under #311; H4 deferred; benchmark follow-up #313 | `a2e74acd` plus the active working tree on 2026-09-22 |
| 005 | [Agent test-authoring guidance](005-agent-test-authoring-guidance.md) | Implemented under #311 | `a2e74acd` plus the active working tree on 2026-09-22 |

## Test-quality execution order

1. Apply **005**: concise AGENTS.md pointer and the detailed authoring bar in the existing verification policy.
2. Deliver **003 C1–C4** and **004 H1–H2**: test cleanup and focused editor/pixel helpers with their first consumers. The plans have separate file ownership and can proceed independently after current owners finish. Formatter cleanup owns its local helper; there is no cross-plan dependency.
3. Deliver **004 H3** and **003 C5**: remove implicit Native/unused publication work and batch positive assets using the existing fixture builder.
4. Resolve **003 C6 / 004 H4** proportionately: default to keeping stress coverage; document measured cost and a keep/follow-up decision. Multi-suite orchestration is optional and may be deferred. Grammar tokenizer dependencies and a headless migration are not required.

The three plans are one test-quality work package, independent of security plan 001.
[Fable review and disposition](reviews/test-quality-fable-disposition.md) records the
draft review and applied revisions. #310 is delivered; implementation is tracked in
[#311](https://github.com/RichiCoder1/lucent/issues/311). Historical source observations
in 002 describe their review baseline rather than the updated tests.
