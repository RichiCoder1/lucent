# Named components and declarative application composition

Status: accepted design direction and shared understanding, 2026-09-14; independently
reviewed by Fable High and refined for the authorized A0-first handoff in
[the application/routing/component plan](../plans/application-routing-component-authoring.md).
This is not an implementation-status claim.

The acceptance target is a simple application authored entirely in .lui apart from a
small C# bootstrap entry point. Companion files organize code by choice; required state,
route or lifecycle-adapter helper files would fail that target. Supporting declarations
and application behavior therefore belong in the authoring design, not only UI markup.

Give each .lui component one named partial identity, shared with an optional .lui.cs
companion, and a statically bound Create recipe factory. A recipe/factory is the application
root; lifecycle configuration belongs on the application builder. This replaces the need
for separate author-facing application adapters and unrelated component-state helpers
without replacing the existing application session or creating shared mounted state.
The owner chose this over mandatory runtime Type activation and over a separately named
partial state model. Detailed initialization ordering and compiler integration require
the plan's feasibility proof before migration.

The owner subsequently selected one framework-invoked setup hook, authored in either
portion, after mount ownership and requirements are ready and before UI creation; authored
component instance constructors are excluded. .lui retains concise state inference while the C# companion
uses explicit generated state properties. Ordinary C# fields keep ordinary semantics,
and moving a declaration between files must preserve its state kind explicitly.

The LUI component pipeline alone emits the named component's implementation, consuming
companion [State] declarations. The standalone [ComponentState] generator remains a
separate authoring option and cannot target that same component. Companion managed cells
initialize before source-ordered .lui declarations, followed by one ComponentContext-based
setup bridge. Ordinary C# field construction timing is unchanged. Early component identity
declarations and qualified factory binding must support both named Create factories and
existing static C# component factories without duplicate registrations or shadowed calls.

Ordinary C# type declarations, including attributed route records/modules and helper
classes, may appear alongside components or in support-only .lui files. Keep at most one
component per file. Supporting types retain normal C# semantics and constructors; they
do not acquire reactive state or component ownership. Ordinary .cs files remain equally
appropriate for supporting types when they provide material equivalence. Do not encourage
support-only .lui files solely to maximize use of the extension. First-class external
source-generator interoperability is required, including both consuming declared types
and using generated APIs from .lui. The exact pipeline is a feasibility gate, not an
assumption that ordinary generators can see each other's outputs in one pass.

The owner permits newer compiler libraries and .NET versions, including .NET 11, where
they simplify or enable this design. Evaluate a compatible newer Roslyn pipeline before
adding build stages solely to preserve the old pin. Establish exact compiler/editor/SDK
minimums through the feasibility proof; an SDK upgrade does not automatically require
raising the application's runtime target.

Builder lifecycle callbacks compose in registration order for startup and reverse order
for stop/cleanup. Any close-preparation callback may veto closing. Registration is additive
and preserves the existing application's startup, negotiated-close and disposal phases.
Root binding/context decoration occurs after factory creation and before mount. Cleanup
tracks actual acquisition, including partial startup failure. A veto invokes attempt-scoped
decline restoration for invoked participants before returning to running; fatal errors
retain the existing terminal policy and cannot reopen write admission.

A Router establishes the subtree's shared navigation session and owns it when created;
an externally supplied session remains borrowed. RouterOutlet displays the destination
and shares the session with shell controls. Generated route/component mappings provide
defaults, while navigation decisions and destination rendering have separate typed hooks.
Keep the existing navigation transaction and retained-identity model. The owner selected
a fixed validated route table with dynamic guards/rendering; live route-table mutation
and a second route/service registry are outside this work.

Rendering selection reacts to application state at the current URI. An unchanged resolved
component type/key retains its mounted state; a changed destination identity replaces that
destination without creating a history entry. This needs a new rendering invalidation
contract over the retained outlet, rather than assuming today's mount-only factory already
provides reactive selection. Guards run for navigation only. Same-URI rendering replacement
is an ordinary UI change; approval-sensitive transitions must use navigation instead of
depending on a rendering callback to invoke guards.
Replacement waits for navigation to become idle and then re-evaluates the latest selection,
including after canceled or rejected navigation. It never competes for navigation's staged
slot. Stable typed destination/wrapper identity and bounded reactive scheduling govern
replacement, preserving transactional publication and cleanup.

Nullable inject declarations request optional borrowed services; non-nullable declarations
remain required. Missing optional services yield null, but provider construction failures
still surface. This extends ADR 0009's required-only injection restriction while preserving
application ownership, resolution before component initialization, and cleanup ordering.
No per-component DI scope or transfer of container-owned disposal is introduced.
