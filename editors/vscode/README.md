# Lucent for Visual Studio Code

This experimental extension provides expanded Lucent and embedded C# syntax
highlighting and runs the language server for diagnostics, project-aware native
member/value completion and hover, and project-source go-to-definition.

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
symbols. `Class:` values complete indexed adjacent and explicitly installed
theme classes, including inside string literals. Component-parameter
completion, document symbols, metadata-as-source navigation, formatting, and
semantic tokens remain future work.

## Troubleshooting

Run **Lucent: Show Language Server Output** from the command palette to inspect
structured project-discovery and analysis logs. The default `messages` trace
records the selected project, context source/reference counts, fallback use,
and diagnostic codes without logging source text. Logs use Microsoft's
source-generated logging and built-in JSON console formatter. Set
`lucent.languageServer.trace` to `verbose` for cache and MSBuild-start events,
or `off` to disable tracing.
