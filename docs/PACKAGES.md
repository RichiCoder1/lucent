# Prerelease packages

Lucent publishes an experimental package set to [GitHub Packages](https://github.com/RichiCoder1/lucent/packages). The packages share one immutable version: `Lucent.Core`, `Lucent.Renderer.Skia`, `Lucent.Platform.Windows`, `Lucent.Hosting`, `Lucent.Reactive.R3`, `Lucent.Lui.Sdk`, `Lucent.Testing`, and `Lucent.Testing.Skia`. `Lucent.Reactive.R3` is optional; it supplies owned debounce without adding ecosystem dependencies to Core. The two Testing packages belong in test projects: the base harness runs without a desktop or graphics dependency, while its Skia companion adds real text shaping and frame capture. APIs may change between prereleases. Pin an exact version and retain the application's NuGet lock file.

After the main-branch managed tests pass, CI runs the existing Core, R3, renderer and Windows test executables as NativeAOT, then packs `0.3.0-dev.<run-number>.1`. It consumes the maintained Issue Browser exclusively through those packages in a fresh package cache, publishes a Windows x64 NativeAOT executable, checks native assets/notices, opens its real window, and closes it normally. The separate headless package consumer checks compiled `.lui`, reactive breakpoints, retained editing and real Skia capture. Only then is the package set pushed.

Managed checks cancel obsolete runs. Package jobs serialize independently and are not automatically canceled by newer pushes. A retry of the same workflow run uses the same version, skipping existing uploads and completing missing packages; package metadata records the source commit. Manual cancellation or runner failure can still leave a partial set, so consumers should use a version from a successful workflow. NuGet has no atomic multi-package transaction.

## Consumption

Use `Microsoft.NET.Sdk;Lucent.Lui.Sdk/<exact-version>` as the project SDK and exact `PackageReference` versions for Windows and optional Hosting. Core and Skia arrive transitively. The SDK supplies the generator, .lui items and optional formatting tooling. Build-time compiler assets do not become application runtime references.

Configure `https://nuget.pkg.github.com/RichiCoder1/index.json` as a named NuGet source and map `Lucent.*` to it. Keep nuget.org for ecosystem dependencies. [GitHub's NuGet registry](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry) requires authentication even for public packages. For local work, use a classic token with `read:packages` through the user credential configuration or `NuGetPackageSourceCredentials_<source-name>` environment variable. Never commit a token. For another repository's CI, grant that repository package read access and use its `GITHUB_TOKEN`.

Configure public visibility and Light Notes Actions read access for every package ID. GitHub defaults newly created package IDs to private; adding a package requires this separate access setup even when the existing packages are already configured. Public packages still require authentication.

## Local development

From a Lucent checkout, use a unique local version and output feed:

```powershell
./tools/Pack-Packages.ps1 -Version 0.3.0-dev.local.1
./tools/Test-Packages.ps1 -Version 0.3.0-dev.local.1 -Feed artifacts/packages/0.3.0-dev.local.1
```

When foreground checks are paused, add `-SkipDesktopSmoke` to validate restore, NativeAOT and notice output without launching Issue Browser. This is not a startup/close pass; CI runs the full default path.

Point the app's Lucent source at that folder and update its SDK/package pins together. Use a new local version after each change to avoid NuGet's immutable-version cache. This allows source development without coupling the app to prebuilt checkout DLLs. Local dirty-tree packages are development artifacts; only CI packages identify a clean published commit.

The runtime libraries and SDK are MIT licensed. Native and ecosystem dependencies retain their own terms; renderer/Windows/Hosting/R3 targets carry notices into application output, and the SDK includes notices for its bundled Roslyn tooling. The Windows package carries the required x64 VC runtime asset. [package-notices.json](../tools/package-notices.json) is checked against each package archive and the relevant clean consumer output. See [CREDITS](../CREDITS.md). NativeAOT publication requires the [Microsoft Windows build prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/), including the Desktop development with C++ workload.
