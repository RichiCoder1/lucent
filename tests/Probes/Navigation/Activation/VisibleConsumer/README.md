# Visible activation consumer

This small NativeAOT fixture covers the actual Lucent window/binding boundary for
#222. It consumes only authenticated `0.3.0-dev.101.1` packages from source
`6270d75762c9318d3a64eb35cf512f6631fb485e`. The existing RegisteredConsumer owns
real OS shell protocol delivery in unpackaged and MSIX Sandboxes; this fixture
adds visible routing, guard, focus, attention and disposal evidence.

`Prepare-Fixture.ps1 -Feed <authenticated-feed> -CandidateDescriptor <descriptor>`
validates package hashes/nuspec identities, checks restored package bytes, and
publishes into a fresh C: temp directory using the normal user package cache.
It records source/package/executable provenance in `manifest.json`. Preparation
does not launch a window or change registration/certificates.

After preparation, a desktop operator invokes:

```powershell
./Invoke-Command.ps1 -Fixture <prepared-directory> -Request home
./Invoke-Command.ps1 -Fixture <prepared-directory> -Request second
./Invoke-Command.ps1 -Fixture <prepared-directory> -Request launch
```

Protocol requests use the Windows App SDK encoded argument
`----ms-protocol:<URI>`. They enter the real `WindowsActivation.Run` primary or
secondary path and `UseWindowsActivation` binding. This is **direct-command
transport**, not shell URI dispatch or proof of protocol registration. The
fixture never creates synthetic inbox envelopes. Every process uses the same
fresh instance key from its prepared settings; no secondary creates a competing
Lucent window.

The manual walkthrough is deliberately representative; deterministic activation
and navigation suites retain their behavior matrices:

1. Cold `/home`: observe the actual window and `Committed` binding record. Warm
   `/second`: observe the route change and journal entry. Keep the primary PID.
2. Toggle the route guard to VETO, click the retained editor and type distinct
   text. Invoke `/home` from a secondary process. The `route-prepare` record and
   `NavigationStayed` binding record must retain route, entry, journal and the
   focused editor identity. Continue typing to confirm retained native input.
   A guard rejection must not request attention.
3. Plain `launch`: observe `LaunchAttention` with the same route/entry/history.
   Observe the independent `foregroundAcquired` and
   `taskbarAttentionRequested` values; a denied foreground request must remain
   denied in the evidence. Windows decides whether it grants foreground; one
   environment granting it does not certify the denied branch.
4. Minimize with the native title bar, invoke plain `launch` again, and verify
   restoration visually together with `restored=True`. Repeat with another
   foreground application when assessing denied foreground/taskbar behavior.
   The fixture uses only the host-provided `WindowsWindowAttention.Request`;
   no synthetic input, focus forcing, foreground retries or minimize P/Invoke.
5. Arm "Veto next close", close via the native window control, and observe
   `allow=False` with the window still live. Toggle the route guard to ALLOW;
   warm protocol navigation must work after the close veto. Close again. Check
   `binding-disposal`, `navigationDisposed=True`, teardown attention rejection,
   and `activation-returned kind=Primary` after key/receiver cleanup. Invoke
   `/home` again to establish a fresh primary and close it normally.

Per-process numbered text records live in `output/`; PID, owner thread, timestamp,
route, entry, journal cursor and focused semantic identity are included where
their owners are live. Editor changes record UTF-8 base64 text. Binding attention
booleans describe the actual host result, including a taskbar **request**, not
proof the taskbar visibly flashed. `Record current state` supports operator
checkpoints but clicking it changes focus, so guard focus comparisons use the
preparation and binding records without clicking between them.

Use retained logs and desktop observations together. A successful publish alone
does not certify visible focus, minimized restoration, foreground denial or OS
shell transport. Remove the fresh fixture directory only after preserving the
manifest and observations; there is no registration to unregister.
