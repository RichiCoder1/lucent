# Lucent .lui for VS Code

This extension provides syntax highlighting, language configuration, completion,
diagnostics, navigation, and cross-language rename support for Lucent `.lui`
files. The extension is a thin VS Code client; it does not include the Lucent
language server or the .NET SDK.

## Setup

Install the matching `Lucent.Lui.LanguageServer.dll` separately, then set
`lucentLui.serverPath` to its absolute path. Set `lucentLui.projectPath` when
the workspace contains more than one project or the evaluated project cannot
be inferred from the first workspace folder. Reload the VS Code window after
changing either setting.

The language server loads the evaluated `.csproj`, including its project
references and `.lui` additional documents. Use the repository's pinned .NET
SDK when building the server.

Keep the server and Core/SDK authoring versions aligned; property discovery uses
Core's attributed metadata. For repository project references, build the selected
application once in the default Debug configuration before opening its editor
project so MSBuild can load the generated style/state analyzers. Package consumers
receive those analyzers during SDK restore. Use workspace-specific settings when
different applications are pinned to different Lucent versions.

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
