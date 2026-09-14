# Typed mount requirements and retained navigation

Status: Accepted September 14, 2026 after the mount/ownership proof in #206.
Production integration is in progress; joint delivery remains subject to #213.

## Context

Component-local state and typed C# authoring now share retained recipes and
synchronous scope ownership. Passing every application capability through every
component obscures the actual dependencies of nested content. Navigation adds a
related placement dependency: a route outlet must supply its own route context
while retaining unchanged ancestors and application-owned editing sessions.

Service lifetime is a separate concern. ADR 0003 currently restricts service
resolution to the composition root. Extending resolution to declared component
requirements must preserve the application scope, negotiated shutdown, accepted
writes, and terminal cleanup policy.

## Decision

Components declare two distinct borrowed requirement kinds. Context resolves the
nearest provider of the exact closed type at the mount position. Injection
resolves a required service from a lifecycle-owned root binding. Both are cached
for that mount. Context requirements resolve first, then service requirements,
then component initializers and setup. Missing requirements fail without fallback.

Providers introduce no visual element or lifetime owner. An immutable mount
environment follows deferred recipes, named content, retained regions and later
virtualized realization. A provider affects only its enclosed content. Borrowing
a value never transfers its disposal ownership. Provider placement preserves the
one-root component rule and transactional mount rollback.

Generated code and handwritten C# use the same closed typed requirement plans.
Ordinary component state receives resolved values, not an ambient lookup API.
The existing `ComponentContext` continues to describe owned state operations;
`MountContext` replaces the older `CompositionContext` name for mounting operations.
Theme propagation uses the mount environment while preserving explicit theme
adapters and unstyled structural controls.

Core defines the lifecycle service-binding contract without depending on a
container. Hosting adapts its existing asynchronous application scope. It does
not create component or route DI scopes. A binding belongs to one session, accepts
one root attachment, rejects foreign owners, stops new resolutions during terminal
shutdown, and is revoked after UI borrowers are released and before the provider
is disposed. Providing an ordinary context value cannot install a service binding.

`[Owned] readonly` explicitly transfers a caller-created synchronous disposable
to the existing component scope immediately after initialization. It does not
introduce asynchronous unmount or transfer container-owned services. Diagnostics
reject recognizable double ownership; this is not a general borrow checker.

Navigation has one authoritative route table with generated typed definitions,
bounded canonical in-app locations and no runtime discovery. Route identity,
journal-entry identity and responsive layout remain distinct. One session owns
the bounded journal and one active root outlet. Outlets retain matching route
prefixes and stage changed suffixes against provisional route context.

Navigation proceeds through preparation, staging, publication and retirement.
Expected preparation failures preserve the current route. Latest-intent
cancellation invalidates stale work without blocking on uncooperative callbacks
or cancelling accepted application writes. Publication makes the retained tree,
current route and journal coherent before observers run. Input scenes, focus and
viewport restoration follow committed structure. Unexpected staging or cleanup
failures use the terminal policy from ADR 0003.

## Consequences

The compiler, editor and runtime expose one requirement contract. `.lui` remains
the primary authoring surface; generated output needs no reflection, dynamic
generic construction, runtime route discovery or second service registry.

Issue Browser and Light Notes prove routing against real retained components,
responsive layout, popup lifetimes and application-owned state. Their migrations
are part of delivery rather than optional examples. Core-only and Hosting package
consumers must execute under NativeAOT; a successful source build is insufficient.

The provider interpreter, service binding and navigation transaction are explicit
runtime responsibilities. The foundation proof must reject simpler alternatives
when they lose placement, disposal or publication guarantees. Performance evidence
must measure resolution counts and retention, without assuming that a typed API
alone eliminates allocations.

URI restoration, native activation, navigation animation, parallel mounting,
multi-window hosting and optional/keyed service injection remain separate work.
This decision does not extend the approved release unit to those features.

## Evidence

[#206](https://github.com/RichiCoder1/lucent/issues/206) owns the mount and lifecycle
proof; [#214](https://github.com/RichiCoder1/lucent/issues/214) owns the independent
location kernel. [The execution plan](../plans/context-navigation-execution.md)
records dependencies and verification. The foundation passes 602 Core contracts,
six Hosting contracts, architecture positive/negative checks and separate managed
and NativeAOT package consumers. The package consumer verifies context and service
resolution before setup, component cleanup before service disposal, and closed
generic requirements across assemblies. Stable drains and repeated layout perform
zero additional service resolutions. Weak-key environment caching does not retain
a discarded theme. The independent route kernel passes 44 contracts and its
package-only NativeAOT executable.

Both initial package proofs use local candidate `0.3.0-contextnav.20260914.1`,
Core SHA-256 `6ec197652494aafe87ce03e854e463d63f2020306ee24e6f2ceefe208b581ecc`.
This establishes the foundation, not the later Hosting adapter, popup borrowing,
generated route/outlet integration or application migrations. Those retain their
own acceptance requirements. Architectural references and existing dependencies are in
[CREDITS.md](../../CREDITS.md).
