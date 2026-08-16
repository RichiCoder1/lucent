# Native TodoMVC example

A standalone Avalonia TodoMVC-style application composed directly from native
controls.

```powershell
dotnet run --project examples/todo/Todo.csproj
```

[`MainWindow.lui`](MainWindow.lui) defines the native `Window` and hosts
`Todo {}`. [`Todo.lui`](Todo.lui) contains the reactive UI, and
[`TodoModel.cs`](TodoModel.cs) supplies ordinary .NET model types. The example
covers state, events, keyed rows, retained controls, filtering, editing, and
bounded native value conveniences.

`Todo.cs` is the minimal native adapter until direct Lucent component invocation
is executable. See [POC 0004](../../docs/poc/0004-native-controls-todo.md) for
the keyed-region boundaries.
