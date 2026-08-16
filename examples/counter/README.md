# Counter example

A standalone Avalonia application containing the smallest state-and-event
Lucent example.

```powershell
dotnet run --project examples/counter/Counter.csproj
```

[`MainWindow.lui`](MainWindow.lui) defines the native `Window` and hosts
`Counter {}` as its content. [`Counter.lui`](Counter.lui) owns the reactive
counter, while adjacent [`Counter.css`](Counter.css) supplies its compiled
styles. `Counter {}` resolves directly to `Counter.lui`; no native control
adapter or component wrapper is involved.
