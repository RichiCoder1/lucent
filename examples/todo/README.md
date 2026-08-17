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
bounded native value conveniences. Completed rows expose an explicit
Completed/Active label as well as semantic success styling; `Todo.css` consumes
the same Lucent role resources in light and dark theme dictionaries.

```powershell
dotnet run --project examples/todo/Todo.csproj -- --quality-capture todo-light-populated
dotnet run --project examples/todo/Todo.csproj -- --quality-capture todo-dark-empty
```
