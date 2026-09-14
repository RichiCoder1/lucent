# Context, injection and retained navigation execution

Status: delivered in `b3f3d59` and published as `0.3.0-dev.76.1` after
semantic capabilities #291–294 and callback diagnostics #290. Both proving
applications pass focused managed and desktop verification. The older design
sketches are not implementation evidence.

The public execution contracts are [#203](https://github.com/RichiCoder1/lucent/issues/203)
and [#204](https://github.com/RichiCoder1/lucent/issues/204), their native child
issues, and their dependency links. Original research/design snapshots remain
local; this document records implementation order and integration constraints.

## Delivery order

| Work | Dependencies | Integration responsibility |
| --- | --- | --- |
| #206 mount/ownership spike | None | Prove deferred provider placement, declared requirements and lifecycle-owned service borrowing; settle the joint ADR before rollout. |
| #214 location/matching kernel | None | Strict bounded URI parsing, canonicalization, static codecs and deterministic ambiguity checks, independent of composition. |
| #207 mount environment and rename | #206 | Replace `CompositionContext` with `MountContext`; preserve the distinct C# `ComponentContext` state-authoring interface. |
| #208 owned declarations | #206 | Reuse existing synchronous scope ownership and rollback. |
| #209 context/inject/Provide lowering | #207, #208 | Resolve declared context first, injected services next, then initializers and Setup. |
| #210 Hosting binding | #207 | Borrow from the existing application scope; preserve negotiated shutdown. |
| #211 popup environment bridge | #207, #210 | Borrow the origin environment/services while keeping the popup theme and lifetime distinct. |
| #212 editor/index metadata | #209 | Expose requirement kinds and exact authored diagnostics across source/package consumers. |
| #215 generated routes | #214, #207 | Generate typed codecs/references and statically reachable closed route-context factories. |
| #216 journal and transactions | #214 | Bounded history, guarded preparation, latest-intent cancellation and explicit publication phases. |
| #217 retained outlet | #215, #216, #209 | Stage changed suffixes, retain matching prefixes and publish the tree/journal coherently. |
| #218 interaction and semantics | #217 | Restore eligible focus/viewport state after commit and preserve popup command freshness. |
| #219 Issue Browser | #217, #218, #210, #211 | Prove typed navigation with stock presentation and fixture services. |
| #220 Light Notes | #219 | Integrate against current editor, recovery-draft and accepted-write ownership. |
| #213 joint delivery proof | #209–212, #219, #220 | Package-only Core/SDK and Hosting NativeAOT consumers, application evidence and final documentation. |

Context and injection are one delivery unit. The kernel can develop alongside
the foundation spike; the outlet joins them after their interfaces are proven.
Joint verification #213 combines the child contracts and both proving applications.

## Current execution

The context, service binding, compiler, editor and typed navigation implementation
is integrated in both proving applications. [CI 34822437908](https://github.com/RichiCoder1/lucent/actions/runs/34822437908)
passed managed verification and the package-only authoring, generated-route and
real Hosting managed/NativeAOT probes, then published all nine packages as
`0.3.0-dev.76.1`. Implementation source is
`b3f3d59c91352b89761b9aefde42ef1c149b6e77`.

Affected managed checks pass Core 645, Compiler 92, Generator 35, Hosting eight,
and Issue Browser 22. Light Notes consumes published `0.3.0-dev.76.1` with full navigation
interaction enabled: app 44 pass plus one explicit opt-in skip, storage 22 pass,
and its actual Windows NativeAOT publish succeeds. All thirty editor tests pass across the full run and the focused correction of a stale real-app markup assertion.

Final review regressions pass for retained live target replacement, persistent
shell focus and partial-publication cleanup. The ten interaction contracts and
all 645 Core contracts pass. Light Notes also passes four focused desktop workflows:
responsive navigation, autosave/reopen, recovery/discard and long-note archiving.
The final NativeAOT Issue Browser passes four desktop cases: adaptive Back/Alt+Left navigation, popup-origin route activation in Lucent and Windows-native menus, and Axe accessibility scanning with zero rule errors. Screenshots confirm the wide collection and compact detail layouts.

Light Notes retains its visible list/editor controls under the stable workspace
route. Typed collection/note suffixes mount route anchors, and committed navigation
updates workspace selection. This preserves existing editor sessions, drafts and
accepted writes; it does not claim the visible panes remount through leaf outlets.
Light Notes commit `9b8cb1b06bb70ed9f1cb478d31e3ee788041bcc4` records the integration.
It is pushed to `main` with successful pre-promotion
[CI 34824660762](https://github.com/RichiCoder1/light-notes/actions/runs/34824660762).
The same verified commit was promoted after explicit user authorization;
implementation, verification and delivery are complete.
Its [validation matrix](https://github.com/RichiCoder1/light-notes/blob/9b8cb1b06bb70ed9f1cb478d31e3ee788041bcc4/docs/CONTEXT-NAVIGATION-VALIDATION.md)
records that boundary and the official package evidence.

## Joint evidence map

| Contract | Maintained evidence |
| --- | --- |
| Placement, exact shadowing, one resolution per mount | Core `MountRequirementContracts`; compiled `RequirementLoweringTests`; package authoring consumer across a separate library |
| Initial and virtualized missing requirements, borrowed service rollback | Compiler missing-context contract, Core virtualized realization contract, Hosting missing-service rollback contract |
| Owned initialization and cleanup failure | Compiler `OwnedDeclarationTests`, including later initializer/Setup/child failure and aggregated cleanup |
| Popup origin lifetime and nearest context | Core popup/submenu mount-requirement contracts; published Issue Browser popup navigation |
| Staging, late cancellation and terminal cleanup | Core `NavigationSessionContracts`, `RouteOutletContracts` and `NavigationInteractionContracts` |
| Real provider scope, accepted writes and close decline/retry | Hosting contracts and `tests/Probes/ContextNavigation/Hosted`, executed managed and NativeAOT |
| Responsive editor/draft continuity | Light Notes app/storage and published desktop suites; its `docs/CONTEXT-NAVIGATION-VALIDATION.md` maps each acceptance case |

The real-provider package probe characterizes sixteen warmed changed-leaf mounts
while retaining the route root. Official CI measured 22,807 B/remount managed
and 22,575 B/remount NativeAOT, creating exactly sixteen transient services. These
are machine-specific observations, not timing/allocation thresholds or claims of
improvement. The probe also checks zero repeated resolution during stable
reads/layout and collection of disposed service references after provider cleanup.

## Invariants to preserve

- One retained root per component and placement-site context resolution. Providers
  borrow exact-type stable values; they do not acquire disposal ownership.
- Context and service requirements are distinct, cached once per mount, with no
  fallback or arbitrary lookup on ordinary component state. A lifecycle-owned
  root binding controls service borrowing; components/routes create no DI scopes.
- Core remains independent of Microsoft.Extensions. Hosting resolves from its
  existing application scope and releases UI borrowers before service disposal.
- Navigation has one authoritative route table and one active root outlet per
  session. Route identity, journal-entry identity and responsive participation
  remain separate. Guards never activate hidden components to obtain services.
- Prepare, stage, publish and retire are explicit phases. Expected pre-staging
  failures leave the old view authoritative; unexpected mount/cleanup failures
  retain the terminal policy from ADR 0003. Superseding navigation never cancels
  an accepted application write.
- `.lui` stays first. Generated consumers use accessible closed typed interfaces,
  without runtime reflection, route discovery or object parameter dictionaries.

## Verification and coordination

Use focused contract suites for each changed behavior, then the shared package
and application evidence in #213. The initial #206 and #214 proofs include their
own small NativeAOT consumers. Record allocations, lookup counts, ownership and
failure results where required; builds alone do not prove those contracts.

Keep shared-tree builds serialized. Assign ownership by runtime, compiler,
Hosting/platform and consumer files, and review the agreed interfaces before
dependent implementation. Preserve unrelated workspace artifacts and private
design documents. Update ADRs/glossary only after the corresponding proof settles
the durable decision. Restoration, Windows activation, navigation animation,
multi-window hosting and optional container features remain follow-up work.
