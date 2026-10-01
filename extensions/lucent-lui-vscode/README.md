# Lucent .lui for VS Code

This extension provides syntax highlighting, language configuration, completion,
diagnostics, navigation, and cross-language rename support for Lucent `.lui`
files. The VSIX includes the matching Lucent language server and its runtime
dependencies. It does not include the .NET SDK.

## Setup

Follow the [Windows getting-started guide](https://github.com/RichiCoder1/lucent/blob/main/docs/GETTING-STARTED.md)
for explicit release installation, managed run/debug, tests and NativeAOT publishing.
The [onboarding record](https://github.com/RichiCoder1/lucent/blob/main/docs/plans/onboarding-execution.md)
records the verified package, debugger and installed-editor boundaries.

Run **Lucent: Open Getting Started** for the built-in walkthrough. **Lucent: Check
Environment** inspects installed .NET tooling and configuration without restoring,
evaluating a project, contacting feeds or changing settings. It is available in
Restricted Mode. The report distinguishes editor, managed build, restore and native
capabilities; unperformed checks remain **notChecked**. **Lucent: Review and Copy
Environment Report** previews the report before explicitly copying it for sharing.
Opening setup or the static environment commands alone does not start project evaluation.
Language services start when a `.lui` document is opened, or through an explicit
project selection or language-service restart.

**Lucent: Check Trusted Project** separately evaluates the selected project's
requirements through the verified server. MSBuild may execute project-supplied
tooling. This command does not request restore, install tools or start/restart
language services. An active project's evidence is reused only after its inputs
and selected server are checked again. The report includes the evaluated target
framework/RID and tool identity; older servers that omit target identity report
it as **notChecked**. A successful requirements check does not establish a
successful build, native publication or feed access. The same review/copy command
previews this report without including project paths or raw failure messages.

**Lucent: Check Windows Native Prerequisites** inspects the installed Visual Studio
toolchain separately. It distinguishes observed C++/Windows SDK components from
missing or unavailable discovery; it does not prove that NativeAOT publication works.

**Lucent: Check NuGet Feed Configuration** asks for a package ID and exact version,
then inspects effective source selection without network access. **Lucent: Check
Anonymous Feed Access** adds an explicit, trusted-workspace metadata request. This
request sends no stored credentials and does not restore packages. A successful
anonymous observation does not prove private package access or future restore
success. Both reports omit source names, URLs and credentials and use the same
review/copy surface. Configuration inspection currently supports local Windows
fixed drives; unsupported configuration is reported as unavailable.

The **Lucent** status item shows the current setup state and applicable actions.
**Lucent: Select Project** chooses an owning folder and `.csproj`, writes only that
folder's project setting and restarts language services. Removing the owner folder
stops its server and cancels pending results. The extension does not automatically
switch to another project.

Install a VSIX from a compatible Lucent release set. The extension verifies its
bundled server files and exact project-free `--identity` response before it sends
`initialize`. Advanced users may set `lucentLui.serverPath` to an absolute path
on the workspace host; the override still gets the compatibility handshake, but
the extension cannot authenticate it as part of the VSIX. Set `lucentLui.projectPath`
to the owning `.csproj` for
semantic features. Without a project setting, document/range formatting remains
available. In a multi-root workspace, the extension asks which workspace folder
to serve, then reads that folder's settings. One project is active at a time;
documents from other workspace folders are not sent to that server. An absolute
project path must be inside the selected workspace folder. Changing either tooling
setting cancels stale work and restarts services in a trusted workspace. Use
**Lucent: Restart Language Services** after repairing a stopped server or restoring
changed project inputs.

Before semantic services start, the verified bootstrap server evaluates the
selected project's tooling requirements and compares the current NuGet graph
with the restored graph. The chosen server must use that exact compiler, including
when an advanced override is configured. Missing restore data, changed package
settings, incomplete project evaluation and incompatible compiler identities leave
syntax highlighting available and report an actionable error. The extension does
not edit package pins or request a restore.
The current prerequisite evaluator supports single-target projects. Multiple target
frameworks, or a restore that resolves several versions of one package in that
project, receive an explicit unsupported-project diagnostic.

This prerequisite check uses trusted MSBuild project evaluation. Imported project
tooling can execute; it is not a sandboxed or guaranteed offline inspection. The
extension checks input hashes again before startup and stops semantic services
when the evaluated tooling inputs change. Restore if needed, then use **Lucent:
Restart Language Services** to evaluate the new inputs. Canceling or restarting
while evaluation is pending prevents the old result from starting a server.

In Restricted Mode, VS Code's `.lui` syntax highlighting remains available while
the extension starts no server and evaluates no project. Grant Workspace Trust to
enable the language services. Project paths and server overrides are restricted
settings. A stopped server stays stopped until the explicit restart command; this
prevents repeated crashes from becoming a process loop.

For package projects, the extension can select an exact compatible server from
its verified cache on the workspace host. **Lucent: Install Matching Language Tools**
uses VS Code's GitHub sign-in to download the project's exact approved release.
An already verified matching cache is reused before asking for sign-in.
It verifies the workflow run, artifact identity and downloaded bytes before
installing. Downloads are explicit, cancellable and limited to releases approved
by the installed extension's catalog. Normal activation does not sign in or download.
Credentials are not stored by Lucent or sent to the redirected download host.

**Lucent: Import Server Archive** accepts
a raw server ZIP only when its bytes match an approved release in the installed
catalog. Selecting a ZIP does not authenticate it; an unknown release remains
unavailable until its authenticated catalog entry is supplied by an extension
update. Import performs no network request. Project evaluation still follows the
trusted MSBuild rules above.

Both commands preserve the running server and existing cache generations. Use **Restart
Language Services** after a successful installation. Returning a project to an older
supported pin selects that exact compatible cached generation; it never substitutes
the newest server or changes the project's pins. The shipped catalog currently
approves `0.3.0-dev.107.1`, authenticated against its complete GitHub Actions release
artifact. Unknown releases require an extension catalog update. Expired GitHub
artifacts cannot be downloaded; an existing verified cache or an approved offline
server archive remains usable. The compatible bundle and an explicit trusted
override are also available.

The language server loads the evaluated `.csproj`, including its project
references and `.lui` additional documents. Use the repository's pinned .NET
SDK when building the server.

## Development native preview

**Lucent: Start Native Preview (Development)** builds an explicitly configured
development executable and displays a frame from Lucent's real Skia renderer.
The separate preview tools currently require a source checkout, a local Windows
desktop workspace and Workspace Trust. They are not bundled executable tools in
this VSIX. Configure `lucentLui.preview` using the
[development setup guide](https://github.com/RichiCoder1/lucent/blob/main/docs/NATIVE-PREVIEW.md).

Save or **Refresh Native Preview** starts a fresh build and worker. Failed builds
retain a visibly stale last-good image. **Stop Native Preview** or closing the
panel cancels active work without stopping language services. The
**Lucent Preview** output channel reports separate build/worker diagnostics.
The panel selects registered scenarios, logical size, device scale, appearance,
contrast and fixture density. Display zoom needs no rebuild. Reset uses a fresh
fixture and clock. Hidden panels suspend execution; explicit Stop stays stopped.
Mapped compiler diagnostics can open authored source inside the workspace.
Unsaved-buffer compilation and interactive inspection remain separate roadmap work.

## Source checkout language services

Keep the server and Core/SDK authoring versions aligned; property discovery uses
Core's attributed metadata. For repository project references, build the selected
application once so MSBuild can load the generated style/state analyzers. For a
source checkout, build the server and the application in the configuration evaluated
by the editor (Debug by default), and use `lucentLui.serverPath` to select that
server. Debug and Release compiler
binaries can differ even at the same commit; a Release VSIX does not satisfy a
Debug source project's exact compiler requirement. Package consumers receive their
analyzers during SDK restore. Use workspace-specific settings when different
applications are pinned to different Lucent versions.

Document/range formatting follows the shared `.editorconfig` policy, including
embedded C#. Format-on-save follows your VS Code setting. Semantic quick fixes
identify their lint rule and resolve against the current document/project before
providing edits. See [formatting and linting](https://github.com/RichiCoder1/lucent/blob/main/docs/LUI-FORMATTING.md) for
supported configuration, safe content conversions and scoped exceptions.
Open `.editorconfig` buffers are synchronized with the language server, so unsaved
policy changes apply to editor diagnostics and actions.

Named-component projects also support ordinary declarations and support-only `.lui`
files. Models and component companions participate in hover, completion, signature help,
definition, references, and cross-language rename against current unsaved text. Enable
`LucentLuiNamedComponents` through the matching SDK; see
[application authoring](https://github.com/RichiCoder1/lucent/blob/main/docs/APPLICATION-AUTHORING.md). No generated C# source files
need to be checked in for these editor features.

## Development

Run the extension tests from the repository root:

```powershell
./tools/Test-LuiEditor.ps1
```

Create a local VSIX with the maintained packaging script:

```powershell
./tools/Pack-LuiExtension.ps1 -ServerArchivePath <server.zip> `
  -ServerDirectory <published-server-directory> `
  -CacheHelperDirectory <published-cache-helper-directory> `
  -DoctorDirectory <published-doctor-directory>
```

The package is written under `artifacts/`; the script stages the repository
license there so the extension source does not need a duplicate tracked copy.
