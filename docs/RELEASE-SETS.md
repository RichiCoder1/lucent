# Compatible release sets

A release set binds exact Lucent package versions, the language server and the
VS Code extension to one source commit. Its descriptor records the tested SDK
selection policy, tooling runtime, language feature level, protocol range, target,
archive lengths and SHA-256 hashes. Project SDK and package pins remain authoritative;
the descriptor never updates an application's dependencies.

The initial distribution is authenticated GitHub Actions artifacts from
`RichiCoder1/lucent`, with packages published separately to its authenticated GitHub
Packages feed. There is no automatic acquisition or Marketplace publication yet.
The extension identity is `lucent.lucent-lui`; new VSIX packages carry the matching
server, its runtime dependencies and notices. `lucentLui.serverPath` remains an
advanced absolute-path override. The supported release target is Windows x64.

## Candidate and complete

`New-ReleaseSet.ps1` creates an immutable **candidate** from existing package,
server ZIP and VSIX files. It checks the explicit package allowlist, package source
and dependency versions, SDK/compiler correspondence, server deployment files and
notices, and client protocol compatibility. Mixed, missing or unsupported inputs
fail without producing a descriptor. Dirty local builds are development candidates.

On main, the managed and package jobs run independently. The package job verifies
NativeAOT and package consumers, exercises the extracted language server, and checks
the extension and descriptor rejection fixtures. Each successful command batch
records its source, CI run, logs and input identity. Failed checks produce no success
record. The publisher downloads both authenticated artifacts with digest validation
and finalizes `complete.json` only when the fixed checks and managed TRX reports pass.

The `complete-release-<run-id>-<attempt>` artifact contains that descriptor, inputs
and completion records. It references the separately uploaded managed reports by
artifact ID and digest. Artifacts currently expire after five days: this is a bounded
CI distribution channel, not a durable release catalog. The complete descriptor means
the verified input bundle is available; it is **not** a NuGet publication receipt.
Use a successfully completed publication job before recommending its package version.

JSON and hashes establish integrity and compatibility, not publisher authenticity.
Obtain these artifacts from the expected authenticated repository/workflow/run;
do not trust a downloaded descriptor merely because it labels itself complete.
The source of truth for policy is [lui-release-policy.json](../eng/lui-release-policy.json),
and the format is [release-set.schema.json](../tools/release-set.schema.json).
For a shipped editor catalog, `tools/New-LuiReleaseCatalog.ps1 -RunId <id>
-RunAttempt <attempt> -OutputPath <new-catalog.json>` uses an ephemeral
`GITHUB_TOKEN` to query only the fixed repository/workflow. It verifies the complete
artifact's GitHub digest before validating its descriptor and payload. It never
accepts a user-supplied archive URL or receipt as publisher evidence. Optional
`-EvidenceDirectory <new-directory>` retains the verified archive and sanitized
metadata; tokens and signed download URLs are not recorded. Its metadata rejection
fixtures run through `tools/Test-LuiReleaseCatalog.ps1` in the release extension check.
See the [editor tooling lifecycle plan](plans/editor-tooling-lifecycle.md) for
project matching, immutable cache/import and explicit approved-release downloads.

## Local validation

```powershell
./tools/Pack-LuiServer.ps1 -ServerDirectory <published-server-directory> -OutputPath <server.zip>
./tools/Pack-LuiExtension.ps1 -ServerArchivePath <server.zip> `
  -ServerDirectory <published-server-directory> `
  -CacheHelperDirectory <published-cache-helper-directory> `
  -DoctorDirectory <published-doctor-directory> -OutputPath <extension.vsix>
./tools/New-ReleaseSet.ps1 -ArtifactDirectory <directory> -Version <exact-version> `
  -SourceCommit <commit> -ServerArchive server.zip -Vsix extension.vsix `
  -OutputPath <candidate.json>
./tools/Test-ReleaseSet.ps1 -DescriptorPath <candidate.json> -ArtifactDirectory <directory>
```

Archive validation does not execute its contents. Packing the server runs its
project-free `--identity` command against the supplied build, which reports actual
assembly hashes and runtime requirements without loading an application project.
Normal project evaluation remains the authoritative semantic path after trust.
The cache helper is published from `src/Lucent.Tooling.Cache` in Release with
`-p:UseAppHost=false` at the same source commit as the server. Its three managed
runtime files are inventoried and hashed inside the VSIX before execution. The
helper validates server archives and immutable cache generations; it does not
authenticate a caller-supplied descriptor or contact GitHub.
The environment doctor is published from `src/Lucent.Tools` with the same settings
and source commit. Its exact three-file runtime inventory is verified before use.
The coordinated `Lucent.Tools` package also exposes it as a standard local .NET
tool; the extension uses its own verified copy. See the
[environment doctor design](plans/environment-doctor.md) for diagnostic scope.
The extension package verifies the server inventory and source identity against
the standalone server archive. At startup, it checks the bundled bytes again
before running the project-free `--identity` command and opening a project.
Older `external-path` VSIX files remain valid inputs for existing release sets;
new packages use `bundled` delivery. See [packages](PACKAGES.md) for feed access
and [SDK/tooling](LUI-SDK-TOOLING.md) for project integration.
