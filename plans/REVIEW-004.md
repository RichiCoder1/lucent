# Adversarial review disposition for Plan 004

Reviewed on 2026-08-16 by fresh isolated `openai-codex/gpt-5.6-terra`
processes with read-only tools and no extensions or repository write authority.

## Disposition

- **Prerequisites are currently TODO:** rejected as a plan defect. Plan 004
  already depends on and stops for incomplete Plans 001–003.
- **Attached syntax/symbol resolution was underspecified:** accepted. The plan
  now defines qualified member-header parsing, exact setter/property-field
  requirements, expected types, mapped diagnostics, and incomplete completion.
- **Attached tooling lacked one semantic target:** accepted. The setter is the
  definition/hover target and the validated property field supplies metadata.
- **Getter/Add collection projection was ambiguous:** accepted. Exactly one
  viable Add method and expression-only collection elements are required;
  spreads and reactive elements are diagnosed with mapped spans.
- **Focus cleanup could not reach ComponentOwner:** accepted. Workbench uses
  paired Loaded/Unloaded handlers; generated owner disposal only unsubscribes.
- **Host/commands had no injection or lifetime path:** accepted. Desktop host
  and lifetime token are required root inputs; commands are root-owned inputs
  for children; App owns cancellation and component disposal.
- **Native dialog/window operations were not exercised:** accepted. Settings
  and generated-preview use concrete app-owned Window adapters through exact
  Avalonia operations. Cancellation claims are limited to application flow.
- **Test visibility/key routing conflicted with later plans:** accepted.
  InternalsVisibleTo is scoped explicitly; Plan 004 verifies command identity
  and focus, while physical key automation remains Plan 006.

Final blocker/high gate: `PASS`.
