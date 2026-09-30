# Lucent templates

`Lucent.Templates` contains four standard `dotnet new` templates. It is part of the
coordinated release inventory: packing stamps the release's exact pins, the
descriptor binds the package bytes, and CI installs that package for consumer
verification before publication. Use a successfully published release. A local
template proof does not establish the complete onboarding journey.

| Short name | Result |
| --- | --- |
| `lucent-app` | Windows executable with `.lui` root, owned editor/counter, and typed Lucide artwork |
| `lucent-library` | Portable component library with per-mount state, a theme token, and embedded SVG |
| `lucent-tests` | Executable MSTest project exercising a real semantic action and retained state |
| `lucent-component` | One parameterized `.lui` component in the existing C# project's root namespace |

`lucent-tests --Skia` also includes a production-renderer capture test with physical
size and painted-color assertions. The base test template uses the portable
headless harness. Neither test launches a window or injects physical input.

## Generate projects deliberately

Install an explicitly selected, verified template package with `dotnet new install
<path-to-Lucent.Templates.VERSION.nupkg>`. Then, for example:

```powershell
dotnet new lucent-app -n Example.Desktop -o Example.Desktop
dotnet new lucent-library -n Example.Cards -o Example.Cards
dotnet new lucent-tests -n Example.Tests -o Example.Tests
dotnet new lucent-tests -n Example.CaptureTests -o Example.CaptureTests --Skia
```

From an existing, explicitly restored Lucent C# project directory:

```powershell
dotnet new lucent-component -n Greeting
```

Generation has no post-actions: it does not restore, install tools, acquire an
editor server, or run a project. The SDK template host supplies standard project
name transforms and the item's `msbuild:RootNamespace` binding. Components still
need a project using `Lucent.Lui.Sdk` to compile.
The SDK host requires a restored project for item binding and refuses otherwise;
it does not restore on the template's behalf. Invoke this project-context command
only in a project you trust, since MSBuild evaluation is part of SDK host binding.

The app includes the exact SDK policy selected when the template package was
packed. Standard `dotnet new` conflict handling refuses to replace an existing
different `global.json` unless the user explicitly forces replacement. Library,
test, and item templates generate no SDK, NuGet, tool, or editor configuration.
Use the existing workspace SDK policy when adding them to a repository. Inspect
generated output before explicitly restoring it.

All Lucent package and SDK pins are the exact template release. Configure the
[supported package feed](PACKAGES.md) outside generated source; no credentials or
developer-machine paths are embedded. First restore creates the project's real
`packages.lock.json`; commit it and use locked restore in CI. A cached Lucent
package alone does not imply an offline dependency closure. Installing a newer
template package does not upgrade existing projects.

## Maintainer proof

`templates/Pack-Templates.ps1 -Version <version> -SourceCommit <commit> -OutputDirectory <new-output>`
stamps exact Lucent and repository SDK/MSTest pins into an isolated staging copy
and packs content only, including the supplied source commit in NuGet metadata.
It refuses to replace an existing package. `tools/Pack-Packages.ps1` invokes this
content-only packer for the `Lucent.Templates` allowlist entry. The coordinated
descriptor binds the resulting package after stamping and packing; stamping does
not depend on the final descriptor hash.

```powershell
# Authoring checks only; no claim about package compatibility.
./tools/Test-Templates.ps1 -GenerationOnly -Version 0.3.0-dev.templates.1

# Real consumer checks against an already validated compatible set.
./tools/Test-Templates.ps1 -DescriptorPath <candidate-or-complete.json> -ArtifactDirectory <downloaded-inputs>
```

Both modes use a new isolated CLI home, package cache, and output workspace under
`artifacts`; existing template installations are untouched. Logs survive failures.
Full mode verifies the supplied descriptor and local bytes, installs its exact
template package without rebuilding it, then tests generation,
names with Unicode/spaces, exact pins, ordinary conflicts, item namespace binding,
configuration preservation, first and locked restores, semantic actions, optional
Skia capture, and a packed generated library consumed through NuGet rather than a
project reference. It publishes the generated app with NativeAOT without starting
it. Windows SDK/native compiler prerequisites remain necessary for that publish.

Descriptor integrity checking is not publisher authentication. Obtain CI bundles
through the authenticated release workflow, or explicitly select a local candidate
whose provenance you control. Editor acquisition/trust and the clean-machine
run/debug/NativeAOT journey remain separate onboarding work.
