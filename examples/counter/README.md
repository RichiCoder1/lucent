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
adapter or component wrapper is involved. Its compact surface enforces a
420×300 minimum so the primary count and action remain reachable.

```powershell
dotnet run --project examples/counter/Counter.csproj -- --quality-capture counter-light
dotnet run --project examples/counter/Counter.csproj -- --quality-capture counter-dark-focus
```

The application installs Fluent plus `ShadcnTheme`; adjacent CSS uses only
`Shadcn.*` semantic resources.
