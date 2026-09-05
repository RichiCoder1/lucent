# Lucent

Lucent is an experimental, NativeAOT-compatible desktop UI stack for .NET. The new implementation is Windows-first while keeping platform-specific hosting, accessibility, text input, and presentation behind explicit adapters.

The prior Avalonia implementation and the validation spike that informed this direction are preserved on [archive/avalonia-final](https://github.com/RichiCoder1/lucent/tree/archive/avalonia-final). The historical spike record remains available at the immutable [703d8e2 history tree](https://github.com/RichiCoder1/lucent/tree/703d8e267c6590603350db7819aa553822a30b87/docs/history/native-spike/); active documentation keeps the current contracts and decisions.

Read the active [architecture](docs/ARCHITECTURE.md), [application/service guide](docs/APPLICATIONS.md), [roadmap](docs/ROADMAP.md), [testing guidance](docs/TESTING.md), and [domain glossary](CONTEXT.md). GitHub Issues and the [Lucent Native project](https://github.com/users/RichiCoder1/projects/4/views/1) are the execution source of truth.

## Getting started

Use Windows 11 24H2 or later on x64 with the .NET SDK pinned in `global.json`. NativeAOT publishing also requires the Visual Studio C++ build tools and Windows SDK.

Run the reference application:

```powershell
dotnet run --project apps/Lucent.IssueBrowser
```

Run the managed tests:

```powershell
./tools/Test-Repository.ps1
```

See [testing guidance](docs/TESTING.md) for focused tests, NativeAOT publication, desktop interactions, and accessibility scans.
