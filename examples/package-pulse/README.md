# Package Pulse

A standalone Avalonia application demonstrating Lucent CSS, motion, and owned
async computation.

```powershell
dotnet run --project examples/package-pulse/PackagePulse.csproj
```

[`MainWindow.lui`](MainWindow.lui) defines the native `Window` and hosts
`PackagePulse {}`. [`PackagePulse.lui`](PackagePulse.lui) owns the search state
and async computation; adjacent [`PackagePulse.css`](PackagePulse.css) compiles
to native Avalonia styles and transitions.

Type `avalonia`, `reactive`, or `toolkit` to filter the simulated catalog. Type
`fail` to exercise error handling. Every query waits 850 ms while stale results
remain visible. `PackagePulse.cs` is the minimal native adapter until direct
Lucent component invocation is executable.
