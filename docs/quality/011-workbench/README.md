# Workbench dogfood evidence

`WorkspaceServiceTests` creates temporary SDK projects with real `LucentSource`
items and evaluates them through `ProjectContextLoader` and
`LucentCompiler.CompileProject`. It verifies the physical tree, diagnostics,
edited and activated-document generation, cancellation/latest-generation
publication, diagnostic columns, and generated-preview caret mapping back to
Lucent source. Tree discovery skips reparse points and is bounded to 16 levels
and 10,000 entries; inaccessible or disappearing directory entries are skipped.

The existing seven `UserFlowTests` remain the keyboard-first shell walkthrough:
picker cancellation, quick open, palette, tree/editor navigation, owned
loading/retry/cancellation, settings persistence, and close cleanup. Native
folder-picker and modal-dialog routing remain native platform behavior. The
headless walkthrough invokes the modal settings action directly, while a
separate mounted-input-root test proves the settings F2 route with physical-key
input.

Workbench consumes `LucentProjectContext`, `LucentCompiler.CompileProject`,
`CompilationResult.Diagnostics`, and `CompilationResult.SourceMap`. The
generated preview's **Go to Lucent source** action maps its caret when a compiler
mapping exists. `App.RestoreWorkspaceAsync` is covered with a saved workspace.
No Workbench-only compiler hook is used.

## Final verification

- `dotnet build Lucent.sln --no-restore --disable-build-servers`: passed with
  zero warnings and errors.
- `dotnet test Lucent.sln --no-restore --disable-build-servers -m:1`: 374 passed
  (Compiler 195, MSBuild 30, Language Server 48, Runtime 33, Workbench 57,
  Analyzers 11).
- VS Code `npm run prepare-server && npm test`: server prepared; 6 tests passed.
- Workbench `data` and `reliability` smokes: exit 0.
- Package Pulse POC smoke: all loading/success/empty/failure/stale/latest/close
  markers observed; exit 0.
