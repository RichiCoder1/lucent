# Isolated registered protocol transport fixture

`Prepare-Fixture.ps1 -Feed <local-candidate-feed> -Version <candidate-version>`
publishes a package-only, self-contained NativeAOT handler and creates a random
`lucent<GUID>` scheme under ignored `artifacts/windows-activation-registered`.
Preparation does not register a protocol or launch an application. The fixture
records its exact executable hash, source hash, package version, scheme, key and
output directory. Review the manifest and executable before native execution.

`Invoke-RegisteredFixture.ps1 -Fixture <prepared-directory> -RunRegistered`
is the separate, explicit native step. Run it only in the reviewed isolated
Windows test environment. It refuses an existing association, calls the public
unpackaged registration API with the absolute fixture EXE and its icon resource,
uses the shell to deliver cold valid, invalid secondary and two warm valid URIs.
The invalid secondary must exit with a finite rejection before redirecting;
the primary receives only the valid URIs. The script checks copied raw URI,
provenance, primary PID, and all observed process exit codes. A prepared fixture
can run only once, so stale event files cannot establish a later pass.
It creates no Lucent window and synthesizes no user input. A default-app picker,
missing shell delivery or unexpected normalization causes a bounded failure,
not an automated click-through.

The run script always signals and, if necessary, stops only the verified fixture
PID, calls the matching public unregister API after a registration attempt, and
checks that the unique scheme is absent from the current user's merged class
view. Evidence and cleanup failures remain in `registered-evidence.json`. If
cleanup fails, retain the isolated fixture and inspect the exact scheme and
executable before any manual repair. Do not delete broad class keys or change
another application's defaults.

Prefer a disposable Windows Sandbox. Its guest state is discarded at close;
host source and package builds remain outside. This fixture establishes real
shell protocol transport, not Lucent route guard or native foreground behavior.
The model suite owns routing policy; visible foreground testing uses a separate
desktop fixture coordinated with the owner.

`Build-MsixFixture.ps1 -Fixture <prepared-directory> -GuestOutputDirectory
<absolute-guest-path>` copies the published payload into a distinct staging
directory and uses the pinned Windows SDK MakeAppx to produce an **unsigned,
uninstalled** full-trust MSIX with a unique package identity and matching
`windows.protocol` declaration. The output directory is embedded only in this
disposable fixture's configuration; use a guest-owned path. Its manifest and
package hashes are recorded beside the package.

Installation proof requires signing with a certificate whose subject exactly
matches the manifest publisher, trusting that certificate inside a disposable
guest, installing the package there, invoking cold/warm shell URIs, removing
the package and verifying the protocol/package are gone. The local Windows SDK
has MakeAppx and SignTool, but their presence does not establish a usable
signing certificate. Do not install an unsigned executable MSIX on the host;
Microsoft documents that this commonly requires an all-users elevated install.
Do not use the unpackaged registration API for MSIX.

The manifest follows [Microsoft's manual desktop MSIX guide](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion)
and [protocol extension reference](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions).
The signed guest step must follow the [MSIX signing guide](https://learn.microsoft.com/en-us/windows/msix/package/sign-msix-package-guide);
Microsoft documents the elevated all-users behavior of [unsigned executable packages](https://learn.microsoft.com/en-us/windows/msix/package/unsigned-package).
