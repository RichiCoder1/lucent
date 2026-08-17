# Lucent Workbench

Static multi-file dogfood shell for component inputs, optional slots, conditional
composition, retained child state, native virtualized ListBoxes, and AvaloniaEdit.

```powershell
dotnet run --project examples/workbench/Lucent.Workbench.csproj -- --smoke-test data
dotnet run --project examples/workbench/Lucent.Workbench.csproj -- --smoke-test reliability
```

The workspace tree is a flattened visible-node projection owned by the app;
ListBox owns row containers, selection, focus, scrolling, and recycling.
The reliability smoke and headless suite cover keyboard routing, accessible
control identity, async problem loading/retry, settings persistence, and owned
shutdown without automating native OS pickers.
