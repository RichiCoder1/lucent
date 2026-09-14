# Named component companion feasibility probe

This isolated executable answers the narrow A0 identity and initialization question behind issues #301 and #302. A single constrained Roslyn emitter reads proposed named-component sources from `*.lui.input`, uses the real `LuiParser`, emits an early partial declaration and one final implementation, compiles that output with the authored companion files, and executes it on the current `Lucent.Core` retained runtime.

The valid fixture proves:

- the component class is the per-mount state identity;
- the erased `ComponentRecipe Create()` declaration is visible early and receives one implementation;
- ordinary C# fields initialize before generated companion `[State]` cells, which initialize before LUI fields in source order, and a LUI initializer can read earlier companion state through the shared instance;
- one `ComponentContext` flows through state allocation and the companion setup bridge;
- two mounts have distinct state and lifetime ownership;
- methods and properties cross the companion/LUI file boundary;
- initializer failure stops later state and rolls back owner cleanup;
- authored companions contain no constructor or handwritten generated entry point.

Negative generator-driver runs prove fail-closed diagnostics for inferred-derived state, duplicate setup, an authored constructor, and `[ComponentState]` on a named component companion. The valid LUI fields carry explicit `[Once]` markers, so the prototype preserves their accepted writable per-mount meaning instead of silently converting a derived expression into a `Signal`.

Run from the repository root:

```powershell
pwsh -NoProfile -File tests/Probes/ApplicationAuthoring/Companion/Run-CompanionProbe.ps1
```

## Deliberate limits

This is a throwaway architecture probe, not production compiler support or full LUI language parity. The emitter accepts parameterless components, explicit `[Once]` initialized writable single-variable fields, ordinary methods, and a companion `[State(Initializer = nameof(...))]` shape. Inferred-derived and `readonly` LUI declarations fail with the prototype's unsupported-input diagnostic; the probe does not claim their production representation. It emits a no-op root recipe, does not lower markup, requirements, styles, async setup, or parameters, and does not establish final diagnostic IDs or public API names. Proposed grammar stays in `.lui.input` so the maintained repository formatter does not mistake it for supported `.lui` source.
