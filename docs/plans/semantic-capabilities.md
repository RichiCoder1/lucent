# Semantic capabilities and coherent snapshots

Status: implemented in `d44f265`, published as `0.3.0-dev.74.1` after [CI 34805441710](https://github.com/RichiCoder1/lucent/actions/runs/34805441710) passed managed and NativeAOT/package-consumer verification. [S1 #291](https://github.com/RichiCoder1/lucent/issues/291), [S2 #292](https://github.com/RichiCoder1/lucent/issues/292), [S3 #293](https://github.com/RichiCoder1/lucent/issues/293), and [S4 #294](https://github.com/RichiCoder1/lucent/issues/294) follow native issue dependencies in that order. The [inventory and performance comparison](semantic-capabilities-baseline.md) records the pre-migration `910b2dc` contract and final results: allocations fell in all five scenarios, with unchanged generation churn. Local affected suites passed 828 tests, plus architecture positive/negative and formatting checks. Equality suppression remains deferred. Reviewed draft and verbatim review are retained under artifacts/reviews/semantic-capabilities-20260913/. Fable reviewed the draft and requested revisions; the final revision has not received a second Fable approval.

## Outcome

Replace the 23-argument SemanticDeclaration construction interface with common metadata and composable, typed capabilities. Preserve one semantic owner and one coherent immutable publication per retained element. This is an internal/framework authoring refactor, not a change to user-visible accessibility behavior or a new .lui language feature.

Pre-migration evidence at `910b2dc`: Behavior.cs contained SemanticDeclaration, individual snapshots and SemanticSnapshot; Element.Authoring.cs manually forwarded every field to override name/description; Element.cs repeated projection and unconditionally incremented semantic generation on effective updates; WindowsUiaProvider.cs projected fields, detected events and chose patterns. Existing range, text, selection, relationships and grid snapshots provided useful groupings. The implementation extends them rather than introducing a parallel abstraction stack.

## Design

1. Common metadata contains role, accessible name/description, relationships and announcement policy. Keep applied enabled/focused state reconciliation and selection ownership explicit; author AriaMetadata may change only its currently permitted metadata, never capabilities or behavior state.
2. Typed capability values group related data and operations: toggle; expansion; value/text (including read-only and password confidentiality); range; selection container/item; logical collection membership; grid/container/item. Capabilities compose: a tree item may be selectable and expandable; a numeric editor may expose both editing and range data. Do not build a role-per-subclass hierarchy.
3. Use a fixed, typed immutable capability aggregate with optional slots, not a Dictionary<Type,object>, reflection registry, interface lookup on the hot path, or separately reactive capability objects. Reuse existing immutable payloads. Small flags can remain inline. Final storage choice follows the allocation baseline; no requirement to allocate one object per capability or per fluent method.
4. Keep SemanticDeclaration as the public type and use one validated builder/factory interface with a private construction path. Illustrative intent, not binding syntax: `SemanticDeclaration.Create(role, name).Toggle(appliedState, canToggle: true).Build()`. Duplicate capabilities are rejected, not silently last-wins. Capabilities carry explicit supported-operation flags; presence alone must not grant commands. Actions is compiled from these flags plus standalone operations such as Invoke and remains the single dispatch gate in Element.Allows. Do not infer command support from role. Preserve existing per-producer enabled/read-only operation behavior in this refactor; standardizing currently inconsistent support policies is separate user-visible work. A final cross-capability validator enforces the rules below.
5. Behavior command dispatch remains owned by the existing behavior, using the current command route and generation checks. Capability values contain immutable data, not callbacks, subscriptions or platform providers. Keep portable concepts in Core and UIA mapping in Windows.
6. Share the immutable semantic payload through authored metadata merge and snapshot projection. Metadata updates must not enumerate/copy every capability. Exported snapshots add identity, reconciled state and children without proliferating a second divergent capability schema. The Windows adapter may flatten into its own optimized node representation, with deliberate mapping tests.
7. Treat equality suppression as a separate, measured change. First preserve current generation/invalidation behavior. Only suppress equivalent updates after defining equality for all observable payloads and proving stale command rejection, accepted-selection generation and accessibility event delivery. Reference equality alone is insufficient for freshly rebuilt equivalent payloads; deep text/tree comparison on every update is not an acceptable unmeasured substitute.

## Invariants

- Requested values are not applied values. Selection/toggle/range retain their existing authoritative-state contracts under declined, delayed and normalized requests. Text editing semantics instead describe the visible draft and matching caret/ranges, including temporarily invalid drafts; do not replace them with the persisted or caller-applied value. Pin each control's current behavior before migration.
- Password semantic values never contain secret text, including intermediate builders, snapshots, logs or UIA projection. Validate before publication.
- Immutable snapshots never change after publication; retained identities, relationships, virtualized indices and grid/header references remain stable and validated.
- Disabled/read-only behavior, supported patterns, action gating and focus reconciliation retain existing contracts. No arbitrary callback to a disposed owner.
- Preserve .lui and C# authoring parity, trimming and NativeAOT. No additional runtime/package dependency is needed. Record any adopted external design/dependency in CREDITS.md.

## Ordered implementation slices

### S1 — Inventory and baseline

After current integration, capture the actual baseline commit. Inventory every SemanticDeclaration/SemanticSnapshot producer and consumer, including tests, Lucent.Testing, Issue Browser, context menus, compiler authoring and Windows flatten/event/command paths. Build a mapping table of each existing field, ownership, validation, command support and destination. Record focused baseline measurements for simple controls, metadata-only changes, text caret updates, and a virtualized list/table: allocations, semantic update/projection time, generation churn and per-node reconciliation including ancestor InputAvailable walks. Extend tests/Lucent.Performance.Verifier/SceneProjectionProbe.cs and the existing SemanticSnapshotBuilds counter; no broad new performance harness or release gate.

### S2 — Typed payload and representative proof

Introduce the shared payload and one validated construction interface. Prove Button, CheckBox/Switch, Slider and TextField/Password plus author metadata merge. Keep the tree published atomically. Migrate directly within the implementation branch rather than publishing a second construction API or adapter layer. Confirm combinations and disabled/read-only semantics before wider migration.

### S3 — Full migration and removal

Migrate selection, collections, trees, grids, menus, dates, status, fields and all remaining producers; snapshot projection; Windows mapping and tests. Preserve current behavior, supported patterns and commands. Remove the giant positional constructor and redundant forwarding paths by the end of this slice. This is prerelease: prefer one coordinated source-breaking change over an indefinite compatibility layer. Update all repository consumers and coordinate any Light Notes package/pin changes with Implementation's normal publication workflow.

### S4 — Performance decision and documentation

Compare against S1. Avoid material allocation/latency regression; report actual measurements rather than claiming a split is faster. If equality suppression is independently justified, implement and verify it in a separate commit; otherwise retain current generations and record a measured follow-up. Windows already compares flattened values to emit events: Core equality suppression primarily addresses generation churn and allocation, but regression tests must still protect event delivery. Defer cosmetic folder moves until the active work is integrated; extraction can follow naturally when introducing payload types, without a separate mass move. Document semantic ownership, capability composition, validation and metadata overlay in the existing architecture/component docs. Update roadmap and issue tracking through Implementation's normal process.

## Acceptance and focused verification

- No 23-field metadata forwarding or giant positional declaration constructor remains; adding a capability has one portable definition and explicit platform mapping.
- Illegal combinations are rejected with actionable capability-specific diagnostics rather than the generic finite-role/actions error.
- Representative and mixed-capability controls preserve role, name, values, patterns, relationships and command behavior through metadata changes.
- Test password redaction, applied/requested separation, read-only versus disabled, stale generations, virtualized collection/grid metadata and authored .lui metadata.
- Run affected Core, Windows UIA and authoring/compiler contracts, architecture checks and the existing focused NativeAOT/package consumer proof. Choose checks under docs/TESTING.md and docs/agents/verification.md. Native focus-taking checks only when needed and currently authorized, not merely for reorganizing files.
- Report measured performance, intentional source compatibility changes, and any deferred optimization. Do not claim all accessibility behavior newly validated from a source-only refactor.

## Handoff

Implementation owns source changes after its current work completes. This plan author owns only this document and the independent review artifacts. Refresh moving-source references before implementation. No new user grill is necessary for these structural decisions; user-visible behavior changes must be split out rather than smuggled into the migration.

## Fable review resolutions: binding design details

### Pattern support and action ownership

S1 must capture the current pattern truth table, including role-derived SelectionPattern for List/RadioGroup/TabList/Tree/Calendar/Table and ValuePattern for ComboBox. In the new model, declare those pattern capabilities explicitly at producers; pattern discovery reads capabilities, not role alone. Require the selection-container capability for that existing role set and the value-pattern capability for ComboBox. Keep a formatted display Value in common metadata: a progress bar, header or status value does not by itself advertise editable ValuePattern. Model read-only ValuePattern without a SetValue operation.

The migration preserves each producer's old pattern availability and command support; any intentional normalization is a separate change. Explicit operation flags distinguish support from payload presence. Attachment still rejects declared actions without BehaviorOwnership.Action, and command registration still requires a declaration. Builders cannot silently confer action ownership or install handlers. Core's Actions gate and the Windows pattern projection derive from one validated payload but represent different concepts. Capture both in tests, especially disabled rows and read-only text/range.

### Validation and confidentiality table

| Concern | Required construction/publication rule |
| --- | --- |
| Toggle | Explicit toggle operation and state remain coherent; CheckBox/Switch require toggle semantics; Switch rejects indeterminate. Read-only informational toggle support is not added incidentally. |
| Expansion | Expansion operation requires expansion state, preserving current paired validation. |
| Range | Preserve finite ordered range and positive increments; Splitter requires range. Preserve current read-only/SetRangeValue coupling in migration. |
| Selection | Existing selection-container roles explicitly declare their pattern capability. Selection-item applied state is grouped separately; preserve accepted-selection generation and writer eligibility. |
| Membership | Group PositionInSet/SizeOfSet with paired presence and one-based bounds; retain valid nonnegative collection indices and positive TreeItem level. Keep logical and realized counts distinct. |
| Virtualization/grid | RealizeItem requires collection metadata; retain existing grid dimensions, identities, header and cell validation. Do not ban legitimate mixed capabilities by guessing from role. |
| Password | Use a distinct confidential editing variant that accepts no content or text snapshot. Build rejects combination with non-null display Value or text-bearing editing data, regardless of method order; confidential editing requires TextField role. Builder APIs must not accept secret contents for this variant. Do not introduce a new ban on unrelated range/selection capabilities merely because today's metadata tests combine them. |
| Metadata | Preserve finite enums/action masks, nonempty name and optional description, relationship identity checks, invalid/error coupling and announcement validation. Duplicate capability registration yields a named diagnostic. |

Validation is both local to payload construction and cross-capability at Build. The above preserves current prohibitions and makes required pattern declarations explicit. Additional restrictions discovered during inventory require a documented compatibility rationale, not an assumption that every unusual combination is invalid.

### Snapshot shape and freshness

SemanticSnapshot becomes identity, reconciled state, immutable payload reference, and children. Preserve existing flat read-only property accessors to contain read-site churn; migrate positional constructors, deconstruction and `with` mutation sites explicitly. In particular, Composition.Interaction.DisableInteraction and supplemental-description rewriting must create coherent replacements without bypassing validation. Do not retain two independent sets of writable fields. Repository fixtures and public API checks move atomically; document this prerelease construction-source break.

Preserve lazy ReconcileSemanticState on IsCurrent, snapshot creation, semantic state reads and diagnostic dumps: focus/enabled changes must invalidate stale identities even before another projection. Windows retains providers keyed by epoch/element and obtains command identity from the current snapshot; neither old provider reuse nor payload reuse authorizes stale commands. AcceptedSelectionGeneration remains separate.

Metadata precedence remains behavior base → allowed author name/description override → supplemental tooltip description appended at projection. Clearing author metadata restores base values. BehaviorContext.SemanticName currently reads the base name; classify each internal reader as base/effective instead of globally changing that meaning.

### Disposition of nonblocking suggestions

Accepted: retain SemanticDeclaration name; one construction interface; formatted Value without an editable pattern; grouped set membership; existing performance probe; bounded snapshot accessors; avoid conflict-heavy folder moves. Reuse an empty capability aggregate for structural/group nodes where safe, not one metadata-bearing payload for nodes with different names. Defer deletion of reportedly unused _emittedSemantics until an independent reference check at implementation baseline; it is not necessary to this refactor. Do not adopt a fixed count of capability types before the field inventory, or Fable's proposed universal read-only/action normalization, since those could change existing behavior.
