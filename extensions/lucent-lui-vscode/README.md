# Lucent .lui for VS Code

This extension provides syntax highlighting, language configuration, completion,
diagnostics, navigation, and cross-language rename support for Lucent `.lui`
files. The VSIX includes the matching Lucent language server and its runtime
dependencies. It does not include the .NET SDK.

## Setup

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
project path must be inside the selected workspace folder. Use **Lucent: Restart
Language Services** after changing either setting or switching projects.

In Restricted Mode, VS Code's `.lui` syntax highlighting remains available while
the extension starts no server and evaluates no project. Grant Workspace Trust to
enable the language services. Project paths and server overrides are restricted
settings. A stopped server stays stopped until the explicit restart command; this
prevents repeated crashes from becoming a process loop.

The language server loads the evaluated `.csproj`, including its project
references and `.lui` additional documents. Use the repository's pinned .NET
SDK when building the server.

Keep the server and Core/SDK authoring versions aligned; property discovery uses
Core's attributed metadata. For repository project references, build the selected
application once in the default Debug configuration before opening its editor
project so MSBuild can load the generated style/state analyzers. Package consumers
receive those analyzers during SDK restore. Use workspace-specific settings when
different applications are pinned to different Lucent versions.

Document/range formatting follows the shared `.editorconfig` policy, including
embedded C#. Format-on-save follows your VS Code setting. Semantic quick fixes
identify their lint rule and resolve against the current document/project before
providing edits. See [formatting and linting](../../docs/LUI-FORMATTING.md) for
supported configuration, safe content conversions and scoped exceptions.
Open `.editorconfig` buffers are synchronized with the language server, so unsaved
policy changes apply to editor diagnostics and actions.

Named-component projects also support ordinary declarations and support-only `.lui`
files. Models and component companions participate in hover, completion, signature help,
definition, references, and cross-language rename against current unsaved text. Enable
`LucentLuiNamedComponents` through the matching SDK; see
[application authoring](../../docs/APPLICATION-AUTHORING.md). No generated C# source files
need to be checked in for these editor features.

## Development

Run the extension tests from the repository root:

```powershell
node --test extensions/lucent-lui-vscode/extension.test.cjs extensions/lucent-lui-vscode/server-bundle.test.cjs
```

Create a local VSIX with the maintained packaging script:

```powershell
./tools/Pack-LuiExtension.ps1 -ServerArchivePath <server.zip> `
  -ServerDirectory <published-server-directory>
```

The package is written under `artifacts/`; the script stages the repository
license there so the extension source does not need a duplicate tracked copy.
