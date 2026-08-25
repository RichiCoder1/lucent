# Package Pulse

A standalone Avalonia application demonstrating Lucent CSS, motion, and owned
async computation.

```powershell
dotnet run --project examples/package-pulse/PackagePulse.csproj
```

[`MainWindow.lui`](MainWindow.lui) defines the native `Window` and hosts
`PackagePulse {}`. [`PackagePulse.lui`](PackagePulse.lui) owns the search state
and async computation; adjacent [`PackagePulseResults.css`](PackagePulseResults.css)
compiles to native Avalonia styles, dynamic resources, and transitions.

Type `avalonia`, `reactive`, or `toolkit` to filter the simulated catalog. Type
`fail` to exercise error handling. Every query waits 850 ms while stale results
remain visible. The light error quality profile first renders the real `lucent`
results, then changes the query to `fail` so the stale result context and Retry
action are visible together with the actual failure.

```powershell
dotnet run --project examples/package-pulse/PackagePulse.csproj -- --quality-capture pulse-dark-results
dotnet run --project examples/package-pulse/PackagePulse.csproj -- --quality-capture pulse-light-error
```

The application installs Fluent plus `ShadcnTheme`; adjacent CSS uses only
`Shadcn.*` semantic resources.
