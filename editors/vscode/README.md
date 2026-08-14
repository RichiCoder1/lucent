# Lucent for Visual Studio Code

This experimental extension provides `.lui` syntax highlighting and runs the
Lucent language server for source diagnostics, native-symbol hover, and
project-source go-to-definition.

Prepare the bundled .NET 9 language server before launching or packaging the
extension:

```powershell
npm install
npm run prepare-server
```

The default server path is `server/Lucent.LanguageServer.dll`. Override
`lucent.languageServer.command` or `lucent.languageServer.path` when testing a
different build.

The current language server discovers the owning SDK project through its
`LucentSource` items and uses design-time MSBuild for project-aware Avalonia
symbols. Completion, document symbols, metadata-as-source navigation,
formatting, and semantic tokens remain future work.
