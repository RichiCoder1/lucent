# Sol Advisor learnings for Pi subagents

## Finding

The useful idea is **selective routing**, not another orchestration layer. The linked
benchmark reports that Sol Advisor's earlier forced-lane design used 3.93× more Sol,
7.05× more total tokens, and 3.11× more time while scoring below Sol alone. Its revised
workflow defaults to `solo` and escalates only to `delegate`, `audit`, or exceptional
`full` routes when task-specific risk warrants it.

Pi already has the required mechanics: role-specific models, fresh/forked contexts,
single-writer workflows, worktree isolation, acceptance evidence, and an edit-gated
watchdog. The improvement should therefore be a small routing policy over the current
setup.

## Recommended changes

1. **Default to solo.** Use no child for contained work. Delegate only when a complete
   work packet substitutes for parent implementation; audit only when independent
   scrutiny is worth the cost; use both only for broad/high-risk changes.
2. **Require a bounded worker packet.** Include objective, owned files, interfaces,
   constraints, verification, and an evidence-bearing return. The parent still
   inspects the complete diff and reruns checks.
3. **Make model strength an escalation, not a default.** Keep Luna for scout/routine
   implementation, Terra for judgment-heavy implementation, and Sol for architecture
   or deliberate review. Avoid Luna worker + Luna reviewer + Sol watchdog on every
   change.
4. **Risk-gate the watchdog.** The current globally enabled Sol/medium watchdog is a
   forced review lane after every changed turn. Prefer enabling a strong complementary
   watchdog only for risky sessions, or retain the cheap boundary watchdog but skip a
   separate reviewer unless the route is `audit`/`full`. Keep child watchdogs off by
   default.
5. **Keep scope cadence at boundary-only.** Pi already has scope context enabled. Do
   not add every-N-tools cadence unless measured scope drift justifies its repeated
   model calls. Keep auto-follow bounded and visible.
6. **Treat watchdog output as advisory.** The current watchdog incorrectly warned
   that a global package/config change lacked approval even though the immediately
   preceding user message explicitly authorized it. Route/authority evidence must be
   checked before acting on a warning.

## Current setup implications

- `scout = Luna/medium`, `worker = Luna/high`, and `oracle = Sol/high` are sensible.
- `delegate` inherits the Terra default, so generic delegation can be more expensive
  than intended; route routine delegation through `worker` or explicitly lower it.
- `reviewer = Luna/xhigh` is not model-family-independent from the Luna worker.
- The main watchdog is `Sol/medium`, the same family as the Sol parent. Pi currently
  recommends authenticated `openai-codex/gpt-5.5:high` as the complementary reviewer.
- Child watchdogs and cadence checks are already off; keep them off unless a measured
  need appears.

## Do not copy

- Sol Advisor's Codex-specific TOMLs, hard Sol/High prerequisite, or universal
  one-auxiliary limit.
- Forced reviewer/fix loops for routine changes.
- Claims of read-only isolation based only on prompts; verify actual tools/sandbox.
- Watchdog use as bash policy. Pi explicitly leaves bash outside native child
  permission gating.

## Sources

- [Linked benchmark post](https://x.com/daniel_mac8/status/2089013299484492104)
- [Sol Advisor README](https://github.com/DannyMac180/sol-advisor)
- [Selective orchestration contract](https://raw.githubusercontent.com/DannyMac180/sol-advisor/main/plugins/sol-advisor/skills/orchestration/SKILL.md)
- [Worker and reviewer contracts](https://raw.githubusercontent.com/DannyMac180/sol-advisor/main/plugins/sol-advisor/skills/orchestration/references/role-contracts.md)
- [Runtime and isolation rules](https://raw.githubusercontent.com/DannyMac180/sol-advisor/main/plugins/sol-advisor/skills/orchestration/references/operations.md)
- [Pi subagents 0.49.0 watchdog documentation](https://raw.githubusercontent.com/nicobailon/pi-subagents/v0.49.0/docs/watchdog.md)
- [Pi subagents 0.49.0 configuration](https://raw.githubusercontent.com/nicobailon/pi-subagents/v0.49.0/docs/configuration.md)
