# Owned native preview scenarios

Implementation plan for [#229](https://github.com/RichiCoder1/lucent/issues/229),
following [composition purpose #228](https://github.com/RichiCoder1/lucent/issues/228).
This records the implementation boundary and focused verification below.
The [native feasibility report](../../tests/Probes/Preview/NativeFeasibility/REPORT.md)
establishes the initial headless rendering seam.

## Boundary

Add a development-only `Lucent.Preview` library depending on Core. A separate
scenario project references this library and the actual compiled component
project. Shipping applications never reference the scenario project or preview
catalog. Keep the first library non-packable until the preview delivery work
defines its distribution contract.

Registration explicitly names a stable, ordinal scenario ID, source project,
document and component, immutable presentation metadata, typed setup and a typed
root factory. Building and enumerating a catalog must not run setup, invoke a root,
start production `Main`, scan assemblies or infer constructors. Reject duplicate
IDs before executing any scenario. Preserve explicitly supplied application data.

The catalog owns immutable registration metadata. Each launch owns fresh fixture
data, cancellation, service bindings, clock and component state. Closed generic
factories keep fixture values typed internally; an untyped object bag is not the
extension mechanism. There is no new `.lui` declaration syntax in this slice.

## Launch and cleanup

A scenario binds once to a fresh `LucentApplicationBuilder` and returns its root
factory. Setup runs through the existing application startup lifecycle on the
owner thread. Reject binding reuse and root creation for another session.

`PreviewSetupContext` exposes the scenario metadata and chosen clock, explicit
root context/service binding, and the existing stop/final-disposal registration
mechanisms. Register acquired resources immediately, before awaiting later work.
Expire the context when setup returns or throws. Merely implementing
`IDisposable` does not transfer ownership of a fixture to the catalog.

Fake services use `IComponentServiceSource` and the existing single application
binding. Register `StopAccepting` and `Revoke` in setup's `finally`, while the
startup registration context is still active, after fixture cleanup callbacks
have been registered. Reverse stop ordering then closes admission before fixture
shutdown callbacks; reverse final cleanup revokes access before disposing the
backing resources, while component and popup cleanup still runs with its borrowed
services alive.
Existing Hosting integration remains the route for Microsoft dependency
injection; do not add a parallel container.

Check cancellation before setup, after its await, and before accepting or
returning the root. Root creation also requires its own session's active startup
phase, without a close request or disposed composition. A superseded setup may
finish cleanup but must not publish a root. Do not abandon a running setup task
with a detached timeout wrapper.
Generation-based frame admission, process termination and uncooperative code
belong to [worker supervision #230](https://github.com/RichiCoder1/lucent/issues/230).
Preview mode remains an authoring signal, not a security boundary.

## Headless integration

Add a general headless start overload accepting a root factory and an
`Action<LucentApplicationBuilder>`. Invoke configuration on the application owner
thread before startup, and then enforce the headless host, purpose, title and
theme from the snapshotted options. Compose these callbacks with the existing
factory lifecycle. Mirror this overload in the Skia harness.
The new overload requires its third, nullable options argument so the existing
`StartAsync(factory, null)` call remains unambiguous.

Presentation metadata includes logical viewport and scale, appearance, theme
factory, density, culture/UI culture and an initial clock instant. Density is an
explicit fixture input; Core currently has no universal density switch. Snapshot
culture values as read-only copies, install them on the dedicated owner thread
before theme/shaper/lifecycle construction, and restore them when the thread
exits. Do not mutate process-wide default cultures.

The fixture and headless host receive the same fresh controlled clock. A scenario
cannot redirect code that explicitly calls system time; document that limit.
The preview catalog itself should not require the testing clock package.

## Evidence ownership

Extend the headless harness suite for builder callback order and owner-thread
execution, enforced host/options, culture across awaits and later queued work,
and the shared clock. Keep existing Core and Hosting lifecycle matrices with
their owners.

The preview suite owns inert registration, duplicate IDs, fresh launch state,
explicit data preservation, cancellation before/after setup, partial failure
cleanup, expired context, cross-session rejection and services remaining usable
during component teardown. Use controlled task completion rather than sleeps.

Supply compiled empty, loading, error and long-text scenarios against a real
component. Assert attributable content and preserved authored values. Verify
the shipping project's reference graph and output exclude preview dependencies.
These tests do not prove worker process isolation, editor integration, continuous
preview latency, or native input behavior.

## Follow-on ownership

- #230 owns worker generations, build cancellation, bounded failure reporting
  and process supervision.
- #231 owns the opt-in editor panel and source-bound scenario selection.
- #231 owns mapped compiler diagnostics; #232 owns unsaved builds and interactive
  frames. Runtime inspection and source origins belong to #243/#251–255.
- #233 owns integrated consumer proof and distribution.

No additional owner decision is required for this bounded scenario slice.

## Verification, September 30, 2026

The preview suite passes 18/18 cases, including real Skia measurement of the
compiled long-text component. The full headless suite passes 51/51, including
the existing nullable-options calls for both harnesses. Both graphs build without
warnings. Failed startup now waits for cleanup and observes terminal failure
before releasing the private headless wait handle.

The architecture check also reads the compiled fixture's evaluated restore graph,
runtime dependency manifest and output assemblies. It passes on the actual build
and rejects four planted violations: an unused resolved preview dependency, an
evaluated testing reference, a testing runtime dependency and a stray preview DLL.
The Core architecture/public API preflight and authored formatting checks pass.

Adversarial review identified and corrected service admission/revocation ordering,
root creation after session shutdown, and the ambiguous overload. Root admission
checks occur before and after the factory. Cancellation tests preserve cleanup
while rejecting obsolete roots. Tests inspect the nested lifecycle failure and
observable cleanup, rather than assuming that failures have no wrapper.

The first failed integration run remains alongside the passing results under
`C:\Users\richa\AppData\Local\Temp\lucent-preview229-a98dd340f3e74c89934c703dc46a2e2d`.
Its long-text assertion exposed use of the lightweight text measurer; the compiled
scenario tests now use the production Skia shaper. The other initial failures
were overly narrow exception-type assertions. This is managed, in-tree evidence;
it does not claim an external preview package, editor panel, process supervision,
NativeAOT preview worker or desktop input validation.
