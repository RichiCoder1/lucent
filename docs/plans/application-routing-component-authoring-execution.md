# Application, routing, and component authoring execution

Status: resumed at the owner's request on 2026-09-21 for final verification and delivery. The accepted design is tracked by
[parent issue #301](https://github.com/RichiCoder1/lucent/issues/301). The [A0 evidence](application-routing-component-authoring-a0.md) selects the bounded pipeline.
A0's SDK-wrapper repair also passed managed, package verification, and publication CI
([run 35532769554](https://github.com/RichiCoder1/lucent/actions/runs/35532769554)).
Compiler/companion, lifecycle, routing, and consumer changes are saved in `d9cf2db`.
Editor integration and package acceptance still need final closeout; each slice closes
only after its prerequisites and acceptance checks pass. The [handoff](../agents/remaining-work-handoff.md)
records verified checks and the remaining desktop-testing boundary.

## Issue map

| Slice | Issue | Project status | Native prerequisites | Completion focus |
| --- | --- | --- | --- | --- |
| Parent | [#301](https://github.com/RichiCoder1/lucent/issues/301) | In Progress | None | All-LUI application behavior, optional companion pattern, deterministic tooling, and executed packaged NativeAOT acceptance |
| A0 | [#302](https://github.com/RichiCoder1/lucent/issues/302) | Done | None | Select and prove the bounded cross-generator, named-component, editor, and package architecture |
| A1 | [#303](https://github.com/RichiCoder1/lucent/issues/303) | In Progress | #302 | Ordinary C# declarations in LUI, support-only files, cross-file binding, external generation, and correct invalidation |
| A2 | [#304](https://github.com/RichiCoder1/lucent/issues/304) | In Progress | #303 | Named partial components, one mounted state identity, optional companions, setup, requirements, and typed Create |
| A3 | [#305](https://github.com/RichiCoder1/lucent/issues/305) | In Progress | #302, #304 | Deferred root factories, additive lifecycle callbacks, Hosting consolidation, and preserved cleanup ordering |
| A4 | [#306](https://github.com/RichiCoder1/lucent/issues/306) | In Progress | #303, #304 | Generated route/component mappings, Router ownership, RouterOutlet consumption, and shell navigation |
| A5 | [#307](https://github.com/RichiCoder1/lucent/issues/307) | In Progress | #306 | Same-URI reactive destination replacement with retained identity and transactional navigation |
| A6 | [#308](https://github.com/RichiCoder1/lucent/issues/308) | In Progress | #303-#307 | Cross-language editor, source maps, diagnostics, formatting, linting, fixes, and unsaved-buffer parity |
| A7 | [#309](https://github.com/RichiCoder1/lucent/issues/309) | In Progress | #304-#308 | Component Browser migration, bootstrap-only acceptance app, docs, package build, and NativeAOT execution |

All eight slices are native sub-issues of #301. The table records the direct blockers
published through GitHub's blocked-by relationship; transitive prerequisites still apply.
Every issue is labeled `ready-for-agent` and is present in Lucent Native Project 4.

## Gate and sequencing rules

A0 must record concrete versions, commands, exits, generated-output ownership, cache inputs,
diagnostics, positive fixture counts, editor measurements, and executed package evidence. It
does not pass on API discovery or source inspection. That prerequisite is complete; the
selected pipeline now underpins A1–A7 rather than leaving them blocked on the design spike.

The remaining slices follow their native prerequisites. A6 integrates alongside A1-A5 so
tooling gaps surface as syntax and runtime seams land, but it closes only after all five.
A7 removes existing adapters and registries only after their replacements are proven.
Focused verification follows `docs/TESTING.md`; package and NativeAOT checks remain required
where the issue explicitly calls for them.

## Tracker result

- Road to 1.0 [#223](https://github.com/RichiCoder1/lucent/issues/223) now places #301 after
  completed formatting/linting #295-#300 and before the previously ordered remaining backlog.
- #302 is complete. #301 and #303–#309 remain In Progress. The owner resumed on September 21; focus-taking verification remains paused and no dependent acceptance has been waived.
- GitHub accepted all parent/sub-issue and blocked-by mutations. The current GraphQL API
  required global issue node IDs for `addBlockedBy`; numeric database IDs were rejected.
- Ticket bodies contain the implementation and acceptance contracts without copying the
  full design or adversarial-review transcript.

The detailed product and architecture contract remains in
`docs/plans/application-routing-component-authoring.md`; ADR 0011 records the accepted
identity and ownership decision.

## Implementation and verification — September 21

`d9cf2db` implements ordinary LUI declarations, named partial components and optional
companions, borrowed optional services, additive application lifecycle hooks, generated
route bundles, and reactive destination replacement. Component Browser uses the generated
root and router; `apps/Lucent.AuthoringSample` and its companion variant exercise the
bootstrap-only application model. The [authoring guide](../APPLICATION-AUTHORING.md)
documents the supported API and ownership boundaries.

Editor integration maps ordinary and component declarations back to authored source,
including cross-language rename and unsaved changes. It preserves linked-project
fail-closed maps and retains referenced projects' LUI generators during named-project
preparation. This fixes the real Browser `PasswordField` diagnostic rather than hiding it.

The first integrated CI run (`35633695200`) exposed a separate provider-dispatch gap:
new generated route descriptors worked through `Router` but failed through the existing
explicit `RouteOutlet` path and standalone `ProvideContext` overload. Both overloads now
use the generated typed context/provider pair when present. Two regression cases first
reproduced the null-reference failures; the existing Issue Browser suite and packaged
navigation probe also cover the affected public paths. This fixes current explicit
composition support rather than adding a legacy implementation or migration shim.

| Check | Result |
| --- | --- |
| Source suites for the unchanged implementation checkpoint | Compiler 144, Generator 51, Core 654, Hosting 12, Tooling 8, Component Browser 20 passed |
| CI-discovered route-provider fix | Core 656/656, Issue Browser 22/22, Component Browser 20/20, formatting, warning-clean affected builds and architecture negative fixtures passed |
| Editor focused regressions | Linked-project rename, declarations, and requirement hover 6/6; referenced stock factory 1/1; freshness/named/diamond graph 3/3 passed |
| Final full editor suite | 40/40 passed, including real Issue Browser and Component Browser projects |
| Integrated solution | Locked restore and Release build passed with zero warnings/errors |
| Formatting and architecture | Authored C# and all 115 LUI files passed; Core metadata/dependency checks and negative fixtures passed |
| VS Code client | 15 tests passed; VSIX 0.3.4 packaged, not installed by this work |
| Candidate packages | All nine packages packed as `0.3.0-dev.a1a7.20260921.1` from `d9cf2db` |
| Isolated authoring consumers | Managed and NativeAOT C#/LUI integration, lint/configuration checks, routed lifecycle execution, and production SDK negatives passed |
| Package-backed editor | Ordinary class, mixed component/class, and positional record fixtures passed 3/3 with isolated package restore |
| Windows package samples | Inline and companion variants built and executed in managed and NativeAOT modes; all four desktop smoke checks passed |

The final cold package editor measurements were 0.83–3.24 seconds for project load plus first
hover, 0.1–3.0 ms for warm hover, 101–195 ms for declaration edits and 63–88 ms for body
edits. These local measurements are diagnostic and ran alongside other checks; they are
not performance thresholds. Fixtures cover authored navigation, references, rename,
completion, signature help, symbols, diagnostics, tokens, and unsaved invalidation.

The Windows candidates are retained under `artifacts/aw/17fd7275bd81` (inline) and
`artifacts/aw/2dd7d8c8a9b3` (companion), with package/executable hashes in `candidate.json`.
After the owner resumed focus-taking tests on September 21, all four prepared binaries
exited successfully with `AUTHORING SAMPLE PASS` and `AUTHORING SAMPLE CLEANUP`.
Both companion runs also emitted `AUTHORING COMPANION PASS`, proving isolated state
across two mounts and cleanup of both setups. Their `execution` value is now `passed`;
managed/native execution logs and exits are retained beside each manifest. This is
automated desktop execution, not a manual visual walkthrough. Before the September 20
pause, the source-built Browser history/state smoke also passed.
