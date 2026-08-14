---
status: superseded by ADR-0003
---

# Require explicit render regions

Block-bodied Lucent components separate setup and declarations from rendered output with a contextual `render` region. Lucent owns the declarative grammar inside that region and delegates bounded C# islands to Roslyn. We chose the explicit boundary over a mixed implicit component body because it makes output, side effects, parser recovery, and editor behavior visible, accepting one additional keyword; expression-bodied shorthand may be added later without changing this contract.

## Consequences

- A block-bodied component has exactly one render region and produces one fragment, which may contain zero or more rendered nodes.
- Slot declarations remain component metadata outside the render region; renderable slot content and `yield` sites occur inside it.
