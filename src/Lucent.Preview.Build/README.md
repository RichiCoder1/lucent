# Preview build evidence

This development-only tool builds an explicit managed preview executable with the
real SDK. It never runs the executable, application Main, recovered editor
projection or signature stubs. Every invocation must be Workspace Trusted and
owned by the Windows preview supervisor; the tool does not establish trust.

```text
dotnet Lucent.Preview.Build.dll build --request C:\work\request.json --report C:\work\generation\report.json
dotnet Lucent.Preview.Build.dll verify --report C:\work\generation\report.json
```

Request fields (camel case): `protocolVersion:1`, opaque `sessionId`, `generation`
and `requestId` strings, absolute `projectPath`, `configuration`, an explicitly
declared `targetFramework`, `runtimeIdentifier:"win-x64"`, a new absolute
`outputDirectory`, and `extraInputs` (absolute file paths). Output directories
must be outside source project directories. Configuration and target names are
bounded ASCII tokens. The root must be an executable with compatible platform
settings; the worker remains managed and self-contained. No runtime roll-forward
choice is deferred to launch.

The selected workspace SDK is resolved through MSBuild Locator using the project
directory and global.json. Real restore and publish run with build/compiler-server
reuse disabled. Compiler output is retained in bounded stdout/stderr log files;
the tool's stdout is one JSON result. Build success names the atomically written
report. Verify emits `preview-build-verification` / `fresh` and echoes correlation
IDs and all three digests, with exit 0. Failure emits `preview-build-failure` /
`unavailable` and exits 1; it never creates a success report.

The report records graph node identity as canonical project path and effective
MSBuild globals, ordered evaluated items and metadata, imports and absent controls,
restore assets/configuration/lock files, source/additional/configuration files,
references/analyzers, resources and assets. SDK targets additionally capture
inputs at asset generation, resource processing, compiler/prepared-emitter and
publish consumption. Generated inputs are hashed then retained for verification.
The SDK-declared publish file list must match the actual self-contained closure.
Input and artifact digests are separate.

NuGet lock outputs are also isolated per project. The original effective custom
or conventional lock file is a freshness input, including its absence and the
project-specific conventional filename. Existing bytes are copied into the owned
generation before restore; NuGet receives that staged path with the authored
`RestorePackagesWithLockFile` and `RestoreLockedMode` policies unchanged. Restore
may update an unlocked staged copy, but never the authored lock. Locked-mode
incompatibility fails normally. The final staged lock is retained and hashed.

`globWatchRoots` records bounded recursive roots from the actual evaluated MSBuild
glob rules, including empty external asset globs. `watchDirectories` contains
parents of exact input files and is not a recursive watch instruction. Glob rules
and their exclusions/removals participate in input identity.

After publish and on each verify, the graph is freshly evaluated so added or
removed glob members and metadata changes reject admission. File hashes and the
runtime closure are independently rechecked. Verify never reruns targets or
modifies the report. The coordinator must verify immediately before launch and
again after successful worker cleanup/termination before admitting a frame.

Isolation uses the SDK artifacts output layout with project-specific namespaces.
Project-local Common hooks remain intact. The SDK's known
`UseArtifactsOutputPath.props` Directory hook is preserved. Other occupied
`CustomAfterDirectoryBuildProps` / `CustomAfterDirectoryBuildTargets` hooks,
escaping custom outputs, cross-targeting graph nodes, ambiguous intermediate
contexts, reparse outputs and resx external-file resource shapes are unsupported
and fail closed. Target-time globs outside the owned generation directory are
unsupported: move their membership to evaluated items and declare custom
dependencies. Recursive SDK/package-cache or filesystem-root globs are rejected.
Current bounds are 128 graph nodes, 512 glob watch roots, 8,192 evaluated/consumed
items per node/stage, 256 MiB per file, 4,096 runtime files, 32 Mi characters per
SDK output stream and a ten-minute SDK operation deadline. The supervisor owns
the outer process tree, cancellation and a shorter policy deadline if desired.

This is optimistic freshness of evaluated and declared inputs, not a hermetic
build or a security sandbox. Arbitrary targets/runtime code may read undeclared
files, environment values or the network; pre/post hashes cannot detect transient
changes that return to their original bytes. Declare custom dependencies through
`LucentPreviewInput` items or `extraInputs`. This tool does not claim incremental
preview speedups or persistent rendering performance.

The focused actual-SDK fixture requires `LUCENT_PREVIEW_BUILD_FEED` and
`LUCENT_PREVIEW_BUILD_VERSION` pointing at an explicit CI or local candidate
package set with its exact version. Candidate checks precede publication; they
establish no authentication or publication claim.
It retains its C: temporary evidence, including failures. Local bytes do not
establish official package publication or authentication.
The negative generation contains invalid C# in an otherwise valid `.lui` method:
the real prepared SDK reports its mapped Roslyn binding failure as `LUI2000`
against `Card.lui`, before downstream emitter compilation. No success report or
application entry-point invocation is admitted.
