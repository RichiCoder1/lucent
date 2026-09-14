# Context, injection and retained navigation execution

Status: authorized after semantic capabilities #291–294 and callback diagnostics
#290. Preparation uses the current C# authoring and semantic-capability runtime;
the older design sketches are not implementation evidence. No context/injection
or routing support is claimed by this plan.

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
Both parent issues remain open until #213 and their children are complete.

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
