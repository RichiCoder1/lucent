# Native TodoMVC example

This example exercises Lucent's current direct-Avalonia path rather than a
Lucent-specific control library. [`Todo.lui`](Todo.lui) contains the rendered
control tree and reactive behavior; [`TodoModel.cs`](TodoModel.cs) supplies
ordinary .NET record and enum types.

It covers:

- explicit and implicit native content;
- bounded `Thickness` and `CornerRadius` literal conveniences;
- generalized `State<T>` values and updater functions;
- direct one-way property refresh plus explicit reverse events; and
- keyed add, edit, toggle, delete, filter, mark-all, and clear-completed
  behavior with retained Avalonia row controls.

Run it from the repository root:

```powershell
dotnet run --project src/Lucent.Poc/Lucent.Poc.csproj
```

Run the automated native-window path:

```powershell
dotnet run --project src/Lucent.Poc/Lucent.Poc.csproj -- --smoke-test
```

The example is proof-of-concept code. Its keyed region is intentionally limited
to one native root per item in a dedicated panel; see
[POC 0004](../../docs/poc/0004-native-controls-todo.md) for the exact
boundaries.
