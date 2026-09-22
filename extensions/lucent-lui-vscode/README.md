# Lucent .lui for VS Code

This extension provides syntax highlighting, language configuration, completion,
diagnostics, navigation, and cross-language rename support for Lucent `.lui`
files. The extension is a thin VS Code client; it does not include the Lucent
language server or the .NET SDK.

## Setup

Install the matching `Lucent.Lui.LanguageServer.dll` separately, then set
`lucentLui.serverPath` to its absolute path. Set `lucentLui.projectPath` to the
owning `.csproj` for semantic features. Without a project setting, document/range
formatting remains available. Reload the VS Code window after changing either setting.

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
node --test extensions/lucent-lui-vscode/extension.test.cjs
```

Create a local VSIX with the maintained packaging script:

```powershell
./tools/Pack-LuiExtension.ps1
```

The package is written under `artifacts/`; the script stages the repository
license there so the extension source does not need a duplicate tracked copy.
