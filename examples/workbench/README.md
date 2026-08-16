# Lucent Workbench

Static multi-file dogfood shell for component inputs, optional slots, conditional
composition, retained child state, native virtualized ListBoxes, and AvaloniaEdit.

```powershell
dotnet run --project examples/workbench/Lucent.Workbench.csproj -- --smoke-test data
```

The workspace tree is a flattened visible-node projection owned by the app;
ListBox owns row containers, selection, focus, scrolling, and recycling.
