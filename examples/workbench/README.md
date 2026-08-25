# Lucent Workbench

Project-backed dogfood shell for .NET/Lucent workspaces, compiler diagnostics,
generated-C# inspection, native virtualized ListBoxes, and AvaloniaEdit.

```powershell
dotnet run --project examples/workbench/Lucent.Workbench.csproj -- --smoke-test data
dotnet run --project examples/workbench/Lucent.Workbench.csproj -- --smoke-test reliability
```

Open a folder containing a `.csproj` and `LucentSource` files. Workbench uses the
shared evaluated project context and `LucentCompiler.CompileProject` path also
used by tooling; unsaved AvaloniaEdit text overlays the project generation.
Problem selection opens its mapped source file and generated preview captures
one immutable source-map generation; navigation is rejected after that workspace
generation changes. Native folder picker behavior remains manual; the rest is
covered by the headless walkthrough.

The workspace tree is a flattened visible-node projection owned by the app;
ListBox owns row containers, selection, focus, scrolling, and recycling.
The reliability smoke and headless suite cover keyboard routing, accessible
control identity, async problem loading/retry, settings persistence, and owned
shutdown without automating native OS pickers.

`Workbench.css` styles the native shell around FluentTheme, ShadcnTheme, and AvaloniaEdit;
the editor template remains native. `ShadcnTheme` owns the light and dark
`Shadcn.*` resources consumed through compiled dynamic resource setters. The shell keeps a bounded project sidebar at the persisted
`SidebarWidth`, a dominant editor, a compact problems edge, status actions, and
a raised command palette without replacing native control templates.

```powershell
dotnet run --project examples/workbench/Lucent.Workbench.csproj -- --quality-capture workbench-light-shell
dotnet run --project examples/workbench/Lucent.Workbench.csproj -- --quality-capture workbench-dark-palette
```
