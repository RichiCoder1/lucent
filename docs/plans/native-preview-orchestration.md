# Trusted native preview orchestration

Implementation direction for [#230](https://github.com/RichiCoder1/lucent/issues/230),
following the owned scenarios in #229. This plan is not execution evidence.

## Owners and first delivery

The editor coordinator owns trust, selected scenario, generations and current/stale
state. A development build tool owns evaluated inputs and executable outputs. A
Windows supervisor owns each build/worker process tree. An explicit development
executable calls a worker hosting library with its statically constructed catalog.
The LSP and shipping application entry point never execute preview scenarios.

The first worker captures one frame, disposes its scenario and exits. The
coordinator admits that frame only after successful cleanup, process termination
and a final freshness check. Retain last-good pixels separately from disposable
build directories. Compilation or runtime failure makes them explicitly stale.
Continuous interaction, unsaved overlays and external package distribution remain
with subsequent preview slices. The development libraries stay non-packable.

## Freshness

Use the real SDK build and its generated-source comparison. Record graph node
identity as canonical project path plus effective global properties; do not apply
the selected root's framework/RID indiscriminately to dependencies. Isolate each
generation's intermediate and output paths, and reject outputs escaping them.

Capture project/import/control files, restore assets and locks, source/additional
files, analyzers and configuration, references, resources and assets. Preserve item
order and relevant metadata. Capture target-produced inputs at their consumption
boundary. Reevaluate membership and metadata after the build, including new or
removed glob members, then recheck content before launch and frame admission.
Declared extra inputs cover custom fixture/build dependencies.

This is optimistic validation of declared and evaluated inputs, not a hermetic
build. Arbitrary tasks or runtime code can read undeclared files, environment values
or the network. Pre/post hashes cannot prove an input never changed transiently.
Unsupported known input shapes fail with an actionable diagnostic. Do not relabel
the LSP tooling-requirements hash as an executable-build identity.

## Process ownership

The supervisor creates build/worker processes suspended, attaches them to a Windows
job without breakaway and with kill-on-close, then allows execution. Disable shared
build and compiler-server reuse. Parent-channel closure initiates cleanup. Request
cooperative cancellation first, then bound the grace period and terminate the job.
Check that no owned processes remain before admitting a replacement or deleting its
directory. Uncertain termination blocks replacement. Forced termination does not
claim that managed disposal callbacks ran. Process isolation is not a sandbox.

## Protocol and files

Use a shared versioned schema with common valid/invalid test inputs. Correlation
includes session, generation and request IDs, selected project/target, scenario,
input digest and artifact digest. Generation IDs are strings. Application output
is bounded logging, separate from protocol records.

Write a bounded PNG first and atomically publish result metadata last. Verify the
expected relative filename, directory containment/reparse policy, byte length,
hash and dimensions independently. Result identity includes the presentation and
frame sequence. A nonce correlates a result; it does not authenticate trusted app
code. Explicit bounded image preparation precedes capture; no arbitrary sleeps.

Workspace Trust is checked before executable evaluation, build and worker launch,
and again after awaited work. Stop, trust loss, saves, watched input changes and
scenario changes supersede the generation immediately. Initial support is local
Windows desktop workspaces only. The LSP retains independent ownership.

## Verification owners

- Coordinator tests: racing saves/scenario changes, late frames, trust loss,
  last-good/stale state, malformed/oversized results and stop/restart.
- Build fixtures: actual SDK ordering, generated C# failures, source/glob/import/
  reference/asset changes and isolated outputs.
- Supervisor fixtures: cancellation, crash/hang, descendant cleanup, parent-channel
  closure and explicit uncertain-termination handling.
- Worker fixtures: compiled scenarios, image readiness, setup/cleanup failures and
  bounded frame metadata. Desktop walkthroughs remain separate evidence.

The Astra oracle review accepted this bounded direction on September 30, 2026.
No further owner decision is pending. The implementation and verification below
establish the bounded development-tool delivery.

## Implementation and evidence

The non-packable `Lucent.Preview.Protocol`, `Hosting`, `Build` and `Supervisor`
projects implement the separate owners. The extension's `preview-*` modules
provide the coordinator, strict frame reader, supervised process adapter and a
minimal PNG panel. [Development setup](../NATIVE-PREVIEW.md) uses explicit project,
scenario, target and tools settings. Native input injection and production
application startup are not part of this worker.

Adversarial review corrections cover new external glob members, overlapping Start
commands, binary32 extent arithmetic, bounded diagnostic retention, custom NuGet
lock files, and pre-restore/pre-build output containment. The independent Main
marker uses an absolute path. The SDK maps invalid authored C# in `.lui` to
`LUI2000` during preparation, before emitting or launching an executable; the
negative fixture asserts that actual boundary rather than expecting raw `CS0103`.

Passing evidence is retained outside the constrained D: drive:

- `C:\Users\richa\AppData\Local\Temp\lucent-preview-worker230`: eleven worker
  contracts, compiled process capture/stop/EOF/cleanup-failure observations, and
  the 176-by-132 fractional-scale capture. The old producer fails the independent
  fractional metadata and actual capture checks; its failures remain recorded.
- `C:\Temp\lucent-preview-supervisor-230`: twelve real Windows process contracts,
  including descendant cleanup and an injected unconfirmed query result. No
  visible windows or desktop-input claims.
- `C:\Users\richa\AppData\Local\Temp\lucent-preview-build-contract-eok1o1u0.5f2`:
  real SDK source/import/reference/asset/glob/lock changes, isolated destinations,
  artifact tampering and attributable `.lui` rejection. Its exact-source manifest
  is under `lucent-preview-build230-manifest-e714884218c043dcb3c30130465840e9` in the
  same Temp directory. The package fixture consumed authenticated 110.1; it does
  not claim publication of the new development tools.
- `C:\Users\richa\AppData\Local\Temp\lucent-preview230-pipeline-b7f9ce740c7646d7a1f6d770ee427b23`:
  production JavaScript coordinator/runtime through the Windows supervisor, actual
  SDK build, compiled component, Skia PNG admission and successful directory
  cleanup. Frame 320 by 240; the proof records its bytes and build identity.
- `artifacts/preview230-editor-final.log`: 143 editor contracts passed.
  The earlier sandbox-limited doctor tree-cleanup invocation lacked process
  permission; the authorized full run includes that real owned-process check.
  Proxy-backed UI tests cover VS Code's non-cloneable configuration proxy exposed
  by the editor walkthrough. Held-cleanup tests cover replacement superseded by
  the original configuration or by Stop followed by restart; retirement stays
  owned even when the initiating command is obsolete.

The final integrated pipeline is under
`C:\Users\richa\AppData\Local\Temp\lucent-preview230-pipeline-final-93603437c2694e4ab6e52c0cdb32a727`.
It admits the same 320-by-240 PNG, removes the generation output and independently
compares all 65 source lock paths and hashes before and after execution.
The earlier pipeline exposed NuGet lock writes into source folders. Exact backups
and a JSON comparison of the generated RID-only additions were retained before
restoring those files. The build now stages per-project locks, preserves authored
locked-mode policy, and leaves original lock bytes and absence unchanged.
The source-preservation SDK fixture, including successful, compiler-failed and
`NU1004` locked-mode builds, is under
`lucent-preview-build-contract-0c3wll41.pqk` in C: Temp.

An additional adversarial case pauses actual restore and creates the higher-priority
project-specific lock after the conventional lock was staged. The pre-fix tool
incorrectly admitted it. The final build compares pre/post-restore lock selection,
including candidate absence and authored policy, before accepting a new baseline.
The full SDK fixture passes in
`lucent-preview-build230-race-green-c08df1188d0c421ba055390a23bf676b`; the first
failing result remains in `lucent-preview-build230-race-red-8e928ea891aa4eb7a3be6595ae58ac13`.
The integrated pipeline preceded this guard; the final real-SDK fixture verifies
the additional guard. A held-cleanup editor regression also independently failed
before retirement canceled pending watcher timers, then passed with the fix.

CI113's existing standalone doctor timeout test hit its two-second process-startup
watchdog. The revised asynchronous harness allows startup separately from the
doctor timeout contract. Removing the actual deadline reference still fails the
exact-result assertion; sensitivity evidence is under
`lucent-doctor-ci113-f731df3b45974b62a7eb84e3ca2766e4` in C: Temp. CI113's package
verification succeeded, but its managed failure prevented publication. Official
112.1 remains the latest confirmed release at this checkpoint.

CI114 exposed a separate Windows path-spelling defect: its temporary folder uses
an 8.3 profile alias, while canonical paths expand the profile name. The original
lexical comparison wrongly rejected that owned output. Commit `fcd2783c` rejects
actual links along the ancestor chain, then compares canonical containment. The
independent alias regression fails against the preceding implementation; the
corrected baseline passes 145 editor tests, including linked-ancestor rejection.
Evidence: `C:\Users\richa\AppData\Local\Temp\lucent-preview114-path-ia68NQ` and
`artifacts/ci114-managed-failed.log`. This is a source correction, not a claim that
CI114 published a release.

The package-dependent build fixture runs through `tools/Test-PreviewBuild.ps1`
with an explicit feed/version in package CI. Ordinary managed tests need no
preconfigured preview feed. Runtime tests retain only three confirmed diagnostic
bundles totaling at most 16 MiB; unknown or uncertain-termination directories are
never automatically deleted. Deliberate external verification evidence listed
above is separate from that runtime cache.
Package CI retains preview SDK runner logs and TRX reports on success or failure.

The actual Computer Use walkthrough used the already trusted isolated onboarding
folder and the source development extension. It displayed the compiled 320-by-240
component. Changing its disposable preview project to include an invalid C# file
immediately marked the old image stale, then showed a build failure while retaining
the pixels and the exact `CS0103` / `MissingPreviewValue` diagnostic in
`sdk-publish.stdout.log`. The existing 110.1 language server remained Ready. That
server plus the current development client are recorded as a local mixed setup,
not an authenticated new extension release. Closing the panel released ownership;
the editor was closed, Computer Use reset, and disposable project restored.
Evidence is under the onboarding folder's
`vscode-data/User/globalStorage/lucent.lucent-lui/preview/diagnostics/failure-we74mf`.

Presentation controls and mapped diagnostics are recorded separately in
[panel #231](native-preview-panel.md). Continuous preview, inspection and
authenticated external delivery remain #232–233. This work establishes no
incremental-build latency target.
