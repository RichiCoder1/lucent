# Isolated registered protocol transport fixture

`Prepare-Fixture.ps1 -Feed <local-candidate-feed> -Version <candidate-version> -CandidateDescriptor <validated-release-set-json>`
publishes a package-only, self-contained NativeAOT handler and creates a random
`lucent<GUID>` scheme under ignored `artifacts/windows-activation-registered`.
Preparation does not register a protocol or launch an application. The fixture
requires a coherent release-set descriptor, checks package hashes and nuspec
repository commits, and rejects an unexpected Lucent package version during
restore. It records the package source commit separately from the fixture
preparation commit, plus the exact executable/source hashes, scheme, key and
output directory. Older fixture manifests are refused. Review the manifest and
executable before native execution.

`Invoke-RegisteredFixture.ps1 -Fixture <prepared-directory> -RunRegistered`
is the separate, explicit native step. Run it only in the reviewed isolated
Windows test environment. It refuses an existing association, calls the public
unpackaged registration API with the absolute fixture EXE and its icon resource,
uses the shell to deliver cold valid, invalid secondary and two warm valid URIs.
The invalid secondary must exit with a finite rejection before redirecting;
the primary receives only the valid URIs. The script checks copied raw URI,
provenance, primary PID, and actual handler exit codes. Each handler holds its
completion until the observer verifies its PID and exact executable, then
acknowledges it. Shell broker handles may be absent and are not used as handler
identity. A prepared fixture
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

`Build-MsixFixture.ps1 -Fixture <prepared-directory> -GuestOutputDirectory C:\LucentOutput\app-evidence\<scheme>`
copies the published payload into a distinct staging
directory and uses the pinned Windows SDK MakeAppx to produce an **unsigned,
uninstalled** full-trust MSIX with a unique package identity and matching
`windows.protocol` declaration. The output directory is embedded only in this
disposable fixture's configuration. Its manifest and package hashes are recorded
beside the package. The required output path is inside the guest's writable
mapped evidence directory, outside MSIX AppData virtualization.

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

For a disposable packaged test, build the MSIX with guest output exactly
`C:\LucentOutput\app-evidence\<scheme>`
where `<scheme>` is the prepared manifest's random scheme. Then call
`Prepare-MsixSandbox.ps1 -MsixBuild <msix-build-directory>`; the default only
creates a reviewed input bundle, a fresh writable evidence directory, and a
`.wsb` config. The config maps input read-only, maps only that fresh evidence
directory writable, disables networking and device/clipboard redirection, and
runs `Invoke-MsixGuest.ps1` after guest logon. The CLI execution path connects
the same named Sandbox session so its LogonCommand gets an active guest session.
The guest checks its Sandbox user
and nonce marker before any certificate trust or install; it signs with a new
guest-only certificate, installs the exact identity, checks actual packaged URI
deliveries, and removes the package and certificate in `finally`.

The optional `-RunSandbox` path is held until the desktop/session owner reviews
and authorizes that concrete run. It starts one named Sandbox session, waits at
most two minutes for guest evidence, then stops only that session ID. Failure to
stop is reported separately and requires owner inspection. Never run the guest
script on the host, map the source checkout writable, or use an existing signing
identity. See [Microsoft's Sandbox configuration](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-configure-using-wsb-file)
and [CLI](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-cli)
for the mapped-folder, logon command and per-session lifecycle contract.
