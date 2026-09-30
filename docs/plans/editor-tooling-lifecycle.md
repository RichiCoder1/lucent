# Editor tooling lifecycle

Execution: [#247](https://github.com/RichiCoder1/lucent/issues/247), following the
[compatible release-set contract](../RELEASE-SETS.md). This work selects tooling
for the project's existing pins; it does not upgrade an application's dependencies.

## Selection and ownership

The extension runs on the workspace host. Restricted Mode provides syntax
highlighting without project evaluation, server startup, cache execution or
installation. Once trusted, one selected project owns semantic services for the
window. Multi-root selection and an explicit restart retain that ownership.

The bundled server supplies the initial project evaluator. An advanced absolute
server override is user-trusted and must still pass language/protocol and exact
compiler compatibility checks. Otherwise selection prefers an approved, verified
cache generation for the evaluated project, followed by the compatible bundle.
An incompatible or unavailable generation leaves syntax support available with a
setup error. A newer version is never substituted merely because it is installed.

`--project-requirements --trusted-project <absolute.csproj>` uses MSBuild and NuGet
evaluation plus existing restore records. It reports compiler bytes and source
identity, participating Lucent SDK package identities, restored Lucent packages
and persistent inputs. Ordinary C# project references participate in evaluation
without being assigned a fictitious Lucent SDK. Source-project development is
explicitly distinguished from package consumption. This operation is trusted
project execution, not a sandboxed inspection or a promise of no network access.

The extension rechecks input hashes before starting semantic services and watches
evaluated imports and tooling inputs, including those outside the selected folder.
Changed tooling inputs stop that session and require a restore when appropriate,
then an explicit restart. Cancelled evaluation cannot start a late server.

## Immutable cache and import

The VSIX includes a BCL-only .NET cache helper with its own exact three-file
inventory. The client verifies those bytes before running it. The helper owns
bounded ZIP validation, extraction, hashing, the staged executable identity check,
per-generation OS locking and promotion. It does not authenticate publishers.

The workspace-host cache lives under extension-owned global storage. Generation
keys use the approved server ZIP's SHA-256. Each installation extracts into its
own staging directory, verifies mandatory runtime files and notices, checks the
actual server identity, writes a receipt and renames into an absent generation
directory. Existing generations are verified and reused, never overwritten.
Failure cleans only that operation's staging directory. Crash debris cannot be
selected as a complete generation; automatic cleanup of older generations is
deferred until cross-window live-use ownership is designed.

ZIP policy rejects escaping paths, Windows aliases, duplicate/colliding entries,
links, undeclared payloads and mismatched bytes. Resource bounds are 512 MiB encoded,
512 MiB per file, 1 GiB expanded and 10,000 entries. The absolute expansion limit
provides the resource bound; a compression-ratio heuristic is not used.

Offline import is explicit and performs no network request. A raw server ZIP must
match an approved entry shipped in the extension, including the evaluated SDK
package digest, compiler, source, language and protocol. A user-provided descriptor
or hash cannot establish publisher authenticity. The initial catalog approves
`0.3.0-dev.101.1`, authenticated against CI101's complete GitHub Actions artifact.
`tools/New-LuiReleaseCatalog.ps1` verifies the fixed repository/workflow, run,
artifact digest and complete release descriptor before producing a catalog entry.

Import leaves the active RPC process on its current immutable path. A successful
import offers an explicit restart. Returning project pins to an earlier supported
release selects its exact compatible cached generation on restart; it does not
rewrite the active generation or silently downgrade the project.

## Explicit online acquisition

Online acquisition is an explicit action through VS Code's GitHub
authentication provider. It uses fixed `RichiCoder1/lucent` API routes and verifies
a successful main-branch `.github/workflows/tests.yml` run and its attempt/source,
then authenticates the exact complete artifact ID and GitHub-reported digest.
Tokens are not logged, persisted or forwarded to a download redirect. Normal
activation performs no authentication or download. The command first checks for a
verified matching cache, avoiding sign-in and download when it already exists.
Project inputs are rechecked after authentication and download; canceled or
obsolete work cannot start a server.

The complete artifact's ID/digest and the descriptor's input-package artifact
ID/digest are separate provenance facts. The helper validates the downloaded outer
archive before reading its descriptor. Its descriptor digest must exactly match
the shipped catalog, whose producer validated the complete descriptor, receipts
and payload. The helper then checks the SDK and server binding and installs only
the validated inner server archive through the same immutable cache path.

This first slice downloads only releases approved in the shipped catalog. It does
not discover future releases or accept user-supplied trust anchors. Expired
artifacts remain unavailable unless already cached or imported as an approved
offline server archive. Additional authenticated receipt anchors and automatic
catalog updates are deferred.

Metadata is limited to 128 KiB, complete artifacts to 512 MiB, and acquisition to
two minutes. Cancellation releases response streams and removes only the
operation's temporary download. Installation retains the cache helper's independent
validation and cooperative cancellation boundaries.

## Verification boundaries

Use focused project-evaluation contracts, Node lifecycle/selection tests, helper
archive/locking/cancellation tests and the real server/VSIX packaging fixture.
Keep actual held-open stdin and separate-process lock evidence: in-process mocks
cannot establish those transport boundaries. Compare representative ZIP cases
against the release producer's PowerShell validator. A source snapshot or synthetic
fixture is development evidence, not a complete or published release.

Desktop walkthroughs are separate. Packaging, CLI execution and contract tests
do not establish fresh visual editor or app proof.
