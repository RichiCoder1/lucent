# Prerelease packages

Lucent publishes an experimental package set to [GitHub Packages](https://github.com/RichiCoder1/lucent/packages). The five packages share one immutable version: `Lucent.Core`, `Lucent.Renderer.Skia`, `Lucent.Platform.Windows`, `Lucent.Hosting`, and `Lucent.Lui.Sdk`. APIs may change between prereleases. Pin an exact version and retain the application's NuGet lock file.

After the main-branch managed tests pass, CI packs `0.3.0-dev.<run-number>.<attempt>`, consumes the maintained Issue Browser exclusively through those packages in a fresh package cache, publishes a Windows x64 NativeAOT executable, checks native assets/notices, opens its real window, and closes it normally. Only then is the package set pushed. Package metadata records the source commit. A failed partial upload is not a complete package set; retry creates a new version, and consumers should use a version from a successful workflow.

## Consumption

Use `Microsoft.NET.Sdk;Lucent.Lui.Sdk/<exact-version>` as the project SDK and exact `PackageReference` versions for Windows and optional Hosting. Core and Skia arrive transitively. The SDK supplies the generator, .lui items and optional formatting tooling. Build-time compiler assets do not become application runtime references.

Configure `https://nuget.pkg.github.com/RichiCoder1/index.json` as a named NuGet source and map `Lucent.*` to it. Keep nuget.org for ecosystem dependencies. [GitHub's NuGet registry](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry) requires authentication even for public packages. For local work, use a classic token with `read:packages` through the user credential configuration or `NuGetPackageSourceCredentials_<source-name>` environment variable. Never commit a token. For another repository's CI, grant that repository package read access and use its `GITHUB_TOKEN`.

Package visibility initially follows GitHub's private default. Visibility and cross-repository Actions access are separate settings; publishing successfully does not claim those settings have been changed.

## Local development

From a Lucent checkout, use a unique local version and output feed:

```powershell
./tools/Pack-Packages.ps1 -Version 0.3.0-dev.local.1
./tools/Test-Packages.ps1 -Version 0.3.0-dev.local.1 -Feed artifacts/packages/0.3.0-dev.local.1
```

Point the app's Lucent source at that folder and update its SDK/package pins together. Use a new local version after each change to avoid NuGet's immutable-version cache. This allows source development without coupling the app to prebuilt checkout DLLs. Local dirty-tree packages are development artifacts; only CI packages identify a clean published commit.

The runtime libraries and SDK are MIT licensed. Native and ecosystem dependencies retain their own terms; packaged Windows/Hosting targets carry notices into application output, and the Windows package carries the required x64 VC runtime asset. See [CREDITS](../CREDITS.md). NativeAOT publication requires the [Microsoft Windows build prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/), including the Desktop development with C++ workload.
