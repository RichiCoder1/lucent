# Routed package acceptance

This maintained package check began as the A0 feasibility consumer. `Program.cs` passes the named
`Application.Create` method group; all component, model, route, JSON context and
acceptance logic lives in `.lui`. It exercises generated route-to-component
associations, a real navigation session, a retained shell and nested outlet,
typed route context, JSON generated from a LUI-authored model, two independent
root mounts, child replacement, stale-command rejection and teardown. The A1–A7
integration uses `Router`, `RouterOutlet`, and the generated `Routes.Bundle` directly.
The shell borrows the router's `NavigationSession`; no handwritten table/descriptor
pair or outlet factory remains.

The same executable runs deferred-root lifecycle success and factory-failure cases,
checking mounted notifications, reverse resource cleanup and the recorded startup outcome.

`Test-Package.ps1 -Feed <candidate-directory> -Version <candidate-version>` copies
the sources into a fresh isolated consumer and performs its first build as a
NativeAOT publish. It accepts only candidate NuGet packages and executes the
published console program without taking desktop focus. Logs and exact package
hashes are saved under `artifacts/a0-routed-package`. The disposable package cache
is removed after each run; pass `-KeepPackageCache` when debugging package files.

This fixture requires the named-component SDK pipeline. Its source alone is not
passing evidence; the execution record must cite an executed candidate and commands.
The separate companion fixture covers C# partial state and initialization.
