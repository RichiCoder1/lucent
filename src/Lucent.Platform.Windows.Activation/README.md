# Optional Windows activation

`Lucent.Platform.Windows.Activation` is an opt-in Windows App SDK adapter. Core,
Hosting and the base Windows host do not depend on Windows App SDK. The supported
first target is a win-x64 application on Windows 10 1809 or later. The application
must use an `[STAThread]` synchronous entry point and keep `WindowsActivation.Run`
on that thread until its Lucent host returns. Instance-key registration and release
then happen on the same native thread. An asynchronous `Main` that resumes on a
worker thread violates this contract.

The application chooses a stable key containing its application and channel
identity, a lower-case URI scheme, an ASCII authority host, a safe typed fallback,
and explicit `CanOpen` and `CanRequestAttention` policy functions. The key has no
framework default. Startup calls `Run` before creating any session, services or
window. A secondary process redirects within five seconds and returns a finite
`ActivationRunResult`; it never calls the primary callback. A failed redirect is
not retried. The primary callback builds the application and runs it synchronously.

Configure the builder with `UseWindowsActivation` before `Build`, passing its
`ActivationInbox`, an accessor for the mounted `NavigationSession`, the application's
`NavigationRestoration`, a bounded snapshot reader, and the policy. The binding
attaches at `OnMounted`, applies a valid cold protocol first, otherwise attempts
restoration or the safe fallback, routes warm protocol requests through ordinary
navigation guards, and gates deliveries during close preparation. A rejected warm
request leaves route and focus intact. If the application wants native attention,
capture `WindowsWindowAttention` through `WindowsWindowOptions.AttentionReady` and
pass its `Request` method to the binding. `WindowsAttentionResult` reports actual
foreground acquisition separately from navigation; Windows may deny foreground.

The adapter accepts only `scheme://host/path?query` using exact configured scheme
and host. It rejects user info, ports, fragments, opaque forms, foreign authorities,
invalid UTF-8 text and raw protocol values above 4,096 UTF-8 bytes. It preserves
the escaped path and query verbatim; Core validates the route grammar, traversal,
escapes, Unicode and route values. All activations, including redirected ones, have
`UntrustedExternal` provenance. A route may display content; consequential actions
still need an independent explicit user action.

For an **unpackaged** app, set `WindowsPackageType=None`,
`WindowsAppSDKSelfContained=true` and
`WindowsAppSdkDeploymentManagerInitialize=false` on the executable project. Publish
win-x64 with its native runtime assets. An installer or explicit setup command may
call `UnpackagedProtocolRegistration.Register` with the application's scheme,
absolute executable path, logo resource and display name; the matching remove
command calls `Unregister`. Normal startup never changes protocol associations.

For an **MSIX full-trust desktop** app, include a `windows.protocol` extension in
the package manifest and let package installation/removal own the association. Do
not call the unpackaged registration methods. Supply the deployment-specific
package identity, publisher/signing material and protocol name outside Lucent.
For example, inside the packaged `<Application><Extensions>` element, with the
`uap` manifest namespace declared on `<Package>`:

```xml
<uap:Extension Category="windows.protocol">
  <uap:Protocol Name="your-app-scheme" />
</uap:Extension>
```

The protocol name must match `WindowsActivationOptions.Scheme`. See
[Microsoft's protocol manifest guide](https://learn.microsoft.com/en-us/windows/apps/develop/launch/handle-uri-activation)
for packaging details.
Sparse/external-location packages, AppContainer, elevated or cross-user redirection,
framework-dependent runtime installation, and single-file packaging are outside
the initial contract.

The model suite is console-only. The source-level NativeAOT redirection/raw-URI
probe lives at `tests/Probes/Navigation/Activation`; the package-only NativeAOT
consumer lives below its `PackageConsumer` directory and checks the shipped
dependency closure in two hidden processes. Registered shell delivery, MSIX
installation, and physical foreground results require distinct native proof.
