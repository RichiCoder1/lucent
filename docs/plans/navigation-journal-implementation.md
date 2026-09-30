# Bounded journal restoration implementation

This is the next implementation slice of [#221](https://github.com/RichiCoder1/lucent/issues/221),
following the accepted [restoration and activation plan](navigation-restoration-activation.md).
Baseline: active-location restoration commit `e67f7ac69ce71735d0be18d76130939eff5b5ad7`.
Independent review of the location slice found no required corrections. This document makes implementation decisions;
it does not claim that journal or interaction restoration is implemented or verified.

## Public boundary and format

Keep `NavigationRestoration`, `NavigationRestorePlan` and `NavigationSession.Restore`.
Append an optional immutable `NavigationRestorationOptions` constructor argument;
existing callers keep location-only behavior. Its mode defaults to `Location`,
maximum entries defaults to 32 and is bounded to 1–64, and registered state codecs
default to none. `Journal` mode accepts both version 1 location and journal input;
location-only mode rejects journal input. Capture emits the configured mode.

Initially expose one explicit codec selection, `NavigationRestorationStateCodecs.Interaction`.
It selects statically referenced Core code, not a type name or discovery mechanism.
Do not introduce a public generic object serializer, open codec callback registry,
service resolution or arbitrary application state in this slice. A later codec must
have an explicit typed implementation, identity/version and validation contract.

Journal format retains the schema, version and scope fields, replacing `active`:

```json
{"schema":"lucent.navigation","version":1,"scope":"workspace-routes-v1","mode":"journal","activeKey":2,"entries":[{"key":1,"definition":"home","location":"/home"},{"key":2,"definition":"item","location":"/items/42","state":{"codec":"lucent.interaction","version":1,"focus":"heading","viewports":[{"target":"items","x":0,"y":240}]}}]}
```

Keys are unique snapshot-local integers in 1–64. Assign consecutive keys in retained
order on capture; accept nonconsecutive valid keys on decode. They are never runtime
entry IDs. Fields have fixed names; envelope/entry duplicates, unknown fields,
missing required fields, invalid active references and malformed JSON reject the
whole snapshot. Preserve the existing 256 KiB payload limit, depth eight and strict
UTF-8 behavior. Entry count cannot exceed the configured maximum, even if later
validation would drop entries.

The optional state object is at most 4 KiB in its encoded JSON representation.
Only `codec`, integer `version`, nullable `focus` and `viewports` are accepted by
interaction version 1. Focus/viewport target IDs use the existing nonblank 128-byte
UTF-8 bound. Require unique viewport IDs, at most 16 positions and finite nonnegative
numeric offsets. Keep an absent focus distinct from an empty invalid identifier.
Unknown codec/version, duplicate state fields or invalid bounded state discard the
entire entry state while preserving its route. Invalid JSON structure or an oversized
state rejects the envelope. Parse unrecognized state with bounded token traversal;
do not allocate a JSON DOM. Disabled codecs are treated as unregistered.

## Capture and validation

Capture on the owner thread at the location slice's existing safe boundaries.
Snapshot journal metadata once. Choose a contiguous window containing the active
entry, taking preceding entries first and then filling from forward entries, with
capacity `min(configured maximum, session capacity, 64)`. Filter nonpersistable
entries inside that window; do not fetch distant replacements after filtering.
If the active entry is disallowed, return `NoSnapshot` so storage clears older state.
Capture during pending preparation uses committed history only.

Extend the internal transaction participant with an optional interaction-state
capture seam; the root outlet delegates to its configured `NavigationInteraction`.
The application enables state both through that existing outlet owner and the
restoration codec selection. Do not register a second interaction owner on the
session. Capture the active entry freshly from registered targets on the committed
outlet; stored departure state is stale after current scrolling/focus changes.
Inactive entries use their retained immutable interaction state. Missing interaction
ownership omits state. A disposed/replacing participant returns an invalid capture
boundary rather than consulting a retired branch. Do not mutate retained state while
capturing. An oversized captured state returns `TooLarge` without partial output.

Decode envelope structure and keys before accepting the active reference. Apply the
existing canonical-location, current definition and route-policy checks to each
entry. Drop invalid inactive routes in original order; reject an invalid active route
and select the safe fallback. Recompute its index after filtering. Report finite
counts for discarded routes and state, never their values. Recheck admission at
`Restore`, preserving the existing reentrant newer-intent check. If the validated
journal still exceeds the destination capacity, select the safe fallback; do not
silently evict valid restored history. No resource/authorization guard runs at decode.

## Atomic import and interaction ownership

Add an internal `NavigationJournal.PlanImport` that builds a complete immutable
`NavigationJournalPlan` without mutating the journal. Allocate fresh IDs from the
journal allocator in retained order and map snapshot keys to those IDs explicitly.
Build the active `NavigationSnapshot` once and retain its identity through preparation,
staging and publication. A failed or superseded attempt publishes no entries or state;
no cross-process numerical uniqueness promise is made for runtime IDs.

Carry the validated import on `NavigationAttempt`. `BeginStage` chooses its import
plan instead of ordinary `Plan(Push)` only while the target is the original replay
target. Any redirect or fallback clears imported history/state before further
preparation and commits a fresh single entry. Dormant entries remain metadata: mount
and run Enter guards only for the active route at startup, then normal guards on
Back/Forward. Preserve the existing one-fallback and per-operation redirect limits.

Pass imported interaction state, keyed by fresh runtime IDs, through the internal
publication object. The root outlet's existing interaction hook imports the map
inside the same publication batch before choosing the active desired state.
Recognize explicit restored state separately from ordinary Push reset behavior.
Prune against the candidate journal, not the pre-publication session snapshot.
Sessions/outlets without an interaction owner safely ignore decoded interaction state.
No public arbitrary journal mutation or entry-state setter is added.

Apply viewport positions through existing live viewport setters so extents clamp
them; use the existing post-layout focus reconciliation and fallback selection.
Generation and active-entry checks must reject late reconciliation after newer
navigation. Missing, hidden, disabled or foreign-root targets cannot receive focus.
Retain terminal semantics for unexpected stage/publication/retirement failures.

## Implementation sequence and evidence owners

1. Extend `NavigationRestorationContracts` with independent journal bytes and hostile
   structure/state cases: keys, active references, partial drops, all bounds, Unicode,
   unknown codecs, state rejection and window selection. Keep the byte matrix here.
2. Extend `NavigationJournal` and the existing model-only
   `NavigationRestorationSessionContracts` partial: atomic publication, fresh ID map,
   active-index preservation, dormant routes without startup guards, Back/Forward,
   capacity fallback, rechecked policy, redirect/fallback clearing and supersession.
   Keep existing `NavigationSessionContracts` push/replace/traversal assertions as
   regression coverage rather than duplicating their matrix.
3. Extend `RouteOutletContracts` only for import wiring and retained route contexts.
   Extend `NavigationInteractionContracts` for fresh active capture, restored state,
   pruning, target disappearance, clamping and late reconciliation. These interaction
   executions remain deferred while the owner's UI/focus/input pause is active;
   do not substitute offscreen input dispatch for approval.
4. Extend the existing package-only `tests/Probes/Navigation/AotHost` consumer with
   representative generated-route replay in managed and NativeAOT executables.
   Its console path uses no Windows host or `NavigationInteraction`, never dispatches
   input and never requests focus. Record source, package and executable identities.
   This proves distribution/trimming, not interaction behavior.
5. Add application-owned atomic persistence separately: write generations, stale write
   rejection, clear-on-`NoSnapshot`, failure recovery and independent document writes.
   Keep storage outside Core. Update `docs/NAVIGATION.md` only as surfaces land.

No additional owner policy decision is needed. Journal interaction proof, application
storage integration and real resumed focus behavior remain separate acceptance
boundaries, including when model or package-only checks pass.
