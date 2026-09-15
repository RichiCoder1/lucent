# Application, routing, and component authoring execution

Status: A0 complete; paused at owner request, 2026-09-14. The accepted design is tracked by
[parent issue #301](https://github.com/RichiCoder1/lucent/issues/301). The [A0 evidence](application-routing-component-authoring-a0.md) selects the bounded pipeline.
A1–A7 remain Todo; resume with A1 only after the owner restarts work.

## Issue map

| Slice | Issue | Project status | Native prerequisites | Completion focus |
| --- | --- | --- | --- | --- |
| Parent | [#301](https://github.com/RichiCoder1/lucent/issues/301) | In Progress | None | All-LUI application behavior, optional companion pattern, deterministic tooling, and executed packaged NativeAOT acceptance |
| A0 | [#302](https://github.com/RichiCoder1/lucent/issues/302) | Done | None | Select and prove the bounded cross-generator, named-component, editor, and package architecture |
| A1 | [#303](https://github.com/RichiCoder1/lucent/issues/303) | Todo | #302 | Ordinary C# declarations in LUI, support-only files, cross-file binding, external generation, and correct invalidation |
| A2 | [#304](https://github.com/RichiCoder1/lucent/issues/304) | Todo | #303 | Named partial components, one mounted state identity, optional companions, setup, requirements, and typed Create |
| A3 | [#305](https://github.com/RichiCoder1/lucent/issues/305) | Todo | #302, #304 | Deferred root factories, additive lifecycle callbacks, Hosting consolidation, and preserved cleanup ordering |
| A4 | [#306](https://github.com/RichiCoder1/lucent/issues/306) | Todo | #303, #304 | Generated route/component mappings, Router ownership, RouterOutlet consumption, and shell navigation |
| A5 | [#307](https://github.com/RichiCoder1/lucent/issues/307) | Todo | #306 | Same-URI reactive destination replacement with retained identity and transactional navigation |
| A6 | [#308](https://github.com/RichiCoder1/lucent/issues/308) | Todo | #303-#307 | Cross-language editor, source maps, diagnostics, formatting, linting, fixes, and unsaved-buffer parity |
| A7 | [#309](https://github.com/RichiCoder1/lucent/issues/309) | Todo | #304-#308 | Component Browser migration, bootstrap-only acceptance app, docs, package build, and NativeAOT execution |

All eight slices are native sub-issues of #301. The table records the direct blockers
published through GitHub's blocked-by relationship; transitive prerequisites still apply.
Every issue is labeled `ready-for-agent` and is present in Lucent Native Project 4.

## Gate and sequencing rules

A0 must record concrete versions, commands, exits, generated-output ownership, cache inputs,
diagnostics, positive fixture counts, editor measurements, and executed package evidence. It
does not pass on API discovery or source inspection. A1-A7 remain Todo until A0 selects a
pipeline that satisfies the full interoperability contract or reports a minimal product-level
blocker.

The remaining slices follow their native prerequisites. A6 integrates alongside A1-A5 so
tooling gaps surface as syntax and runtime seams land, but it closes only after all five.
A7 removes existing adapters and registries only after their replacements are proven.
Focused verification follows `docs/TESTING.md`; package and NativeAOT checks remain required
where the issue explicitly calls for them.

## Tracker result

- Road to 1.0 [#223](https://github.com/RichiCoder1/lucent/issues/223) now places #301 after
  completed formatting/linting #295-#300 and before the previously ordered remaining backlog.
- #302 is complete. #301 remains In Progress; #303-#309 are Todo with their native dependencies. Execution is paused after A0 at the owner's request.
- GitHub accepted all parent/sub-issue and blocked-by mutations. The current GraphQL API
  required global issue node IDs for `addBlockedBy`; numeric database IDs were rejected.
- Ticket bodies contain the implementation and acceptance contracts without copying the
  full design or adversarial-review transcript.

The detailed product and architecture contract remains in
`docs/plans/application-routing-component-authoring.md`; ADR 0011 records the accepted
identity and ownership decision.
