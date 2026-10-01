# Fresh-consumer onboarding execution

Issue [#249](https://github.com/RichiCoder1/lucent/issues/249) completes the
[onboarding parent](https://github.com/RichiCoder1/lucent/issues/242). The supported
journey uses authenticated CI artifacts/local packages and VSIX, with nuget.org
available for ecosystem dependencies. Public NuGet.org and Marketplace publication
are not claimed; their publisher credentials and channel ownership are not
established by this work.

## Current evidence

The isolated consumer has a new template home, NuGet cache and VS Code profile.
It uses the existing Windows installation, installed .NET SDK 10.0.401 and native
toolchain. No Lucent checkout is referenced by the generated application.

| Boundary | Observed result |
| --- | --- |
| Official 107.1 template install and app generation | Exact authenticated local package, no template post-actions |
| Restore and managed Debug build | Passed; normal hierarchical configuration and locked restore also passed |
| Restricted Mode | Syntax visible; static environment report returned explicit `notChecked` capability states |
| Workspace Trust | Owner trusted only the generated app folder; the agent did not change security settings |
| Managed app | Counter changed 0 to 1; typed text updated the greeting; normal close |
| Headless test starter | One semantic test passed |
| Authored debugging | Development extension 0.3.7 and Microsoft C# 2.160.4 bound `MainView.lui:17`; Increment hit the breakpoint; Step Over changed `this.count` from 0 to 1 at authored line 18; Continue updated the window; normal close exited 0 |
| NativeAOT | Official 107.1 app published without warnings; real executable counter and typing worked; normal close exited 0 |
| Editor semantics | Official 110.1 project and bundled 0.3.7 VSIX in VS Code 1.140.0: `LayoutAxis` hover, `Column` member completion, unsaved invalid-member diagnostic at line 4/column 22, correction clearing Problems before save |

The debugger observation used the 0.3.7 client packaged with unchanged,
authenticated CI108 tool payloads. It is development evidence, not an official
release claim. Generated backing fields are still visible alongside authored state
in debugger Locals. The normal managed/native window checks are representative
interaction evidence, not broad visual or accessibility certification.

Single-run local timings are characterization on an active machine, not gates:

| Operation | Elapsed |
| --- | --- |
| Exact local template installation | 649 ms |
| Generation and first app restore into new package cache | 10,117 ms |
| First managed Debug build | 3,573 ms |
| Subsequent locked restore | 1,403 ms |
| Subsequent documented Debug build | 3,390 ms |
| First NativeAOT publication, dependencies already cached | 30,209 ms |
| Corrected 110.1 managed Debug build | 3,610 ms, build-reported |
| Corrected installed-server initialization | 1,472 ms, client-reported |
| Framework hover / first completion after an edit | 36 ms / 724 ms; completion-item resolve 59 ms, client-reported |

The fixture root and logs are located by `artifacts/onboarding249-current.txt`.
`managed-debug-observation.json`, `native-publish-timing.json`,
`native-observation.json` and `native-close.json` retain the individual results.
The static Restricted Mode report is saved as `restricted-static-doctor.json`.
`official110-editor-observation.json` and `official110-editor.log` retain the final
installed-editor observations and timings. The existing trusted app was advanced
from 107.1 to 110.1, with original project/configuration/lock/assets preserved in
`before110`. This final check reused the isolated cache; it was not a second cold
restore or a repeat NativeAOT walkthrough. The normal user editor profile was not
changed. The test editor was closed and Computer Use ended after verification.

## Defects exposed by the journey

The extension previously omitted the `.lui` breakpoint contribution. Commit
`9c19e915` adds it and extends the existing manifest contract. The actual debugger
observation above establishes the external-debugger integration separately.

VS Code passes a lower-case Windows drive through its file URI. The requirements
producer compared that path with NuGet's upper-case restore spelling as arbitrary
JSON, rejecting unchanged data as stale. The same installed host and original
assets accepted `C:\...` and rejected `c:\...`. Commit `3205cbbd` compares only known
Windows restore path fields without regard to casing. Ordinary values, ordering,
SDK paths and non-Windows comparisons remain exact. The existing requirements
contract failed before the fix and passed after it; changing a non-path project
name's casing still fails. A copied development producer accepts the original
lower-case argument without changing project, configuration, lock or asset bytes.

CI109 was superseded by the corrective push, not accepted as verification.
[CI110](https://github.com/RichiCoder1/lucent/actions/runs/36801821082) passed managed,
package/native and publication jobs. The complete artifact was authenticated as
artifact `11136980863`, SHA-256
`692cdd9dab5bd131b9fb817ba7b99827e7fe4f912ec4d6812fd249d092c2ee7a`;
the descriptor SHA-256 is
`fba4138c6f0ef3b5c5906aa607166c9a9b2c0b8e2140fbc55ebb7b31e7fdc952`.
The final walkthrough used its coherent published package/server/VSIX set, the
relative project setting and unchanged lower-case drive spelling from VS Code.
The server reported matching evaluated requirements and source `3205cbbd`, with
no checkout server override and no stale-input/error entries in that session log.

## Retained failure coverage

The existing owners continue to cover missing/mixed tools, offline delivery,
Workspace Trust and corrupted archives. Do not reproduce their full matrices in
another expensive desktop fixture:

- `ProjectRequirementsContracts` owns restore freshness and evaluated package identity.
- Extension lifecycle tests own trust, missing host, incompatible identity and
  stale-input rejection before semantic startup.
- Server bundle tests own missing, altered and unexpected bundled files.
- Cache/import tests own exact compiler selection, known offline imports and
  rejection of unknown bytes before helper execution.
- Acquisition tests own authenticated metadata, bounded downloads and cancellation.

The fresh desktop journey owns actual installation, project selection, authored
editing/completion/hover, debugging and window interaction. These boundaries are
now observed with the artifact distinctions above. Public distribution and broad
visual/accessibility certification remain outside this delivery claim.
