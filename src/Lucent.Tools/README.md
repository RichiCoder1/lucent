# Lucent.Tools

Lucent.Tools is the Lucent local command-line tool. Install the package as a
.NET tool from the authenticated Lucent prerelease feed, then run `lucent doctor`
to inspect the current environment. Use `lucent doctor --json` for structured
results or `lucent doctor --workspace <absolute-directory>` to inspect a specific
workspace. The doctor reports capability-specific checks; it does not modify the
workspace or install tooling.

The VS Code extension also carries a byte-verified private copy of the doctor.
That bundled copy is independent of any user-installed .NET tool.

For an explicit trusted-project requirements check, supply both absolute paths:

```text
lucent doctor --trusted-project <absolute.csproj> --server <absolute.server.dll> [--json]
```

This separate action executes the caller-trusted language server and evaluates
the project through its existing requirements operation; MSBuild may execute
project-supplied tooling. It verifies server/compiler identity, compatibility and
current input hashes, and reports the producer's target framework and effective
runtime identifier when available. The explicit server override is unauthenticated
as a release artifact. Semantic readiness, build, native publication and feed access
remain `notChecked`. The command performs no installation or restore and cannot
be combined with `--workspace`. Ctrl+C cancels the bounded evaluation.

Check installed Windows native prerequisites separately:

```text
lucent doctor --native-prerequisites [--json]
```

This uses the installed Visual Studio discovery tool to look for a complete Visual
Studio instance with the x64 C++ tools and a Windows SDK. It does not build a project
or prove NativeAOT publication; those remain separate checks.

Inspect effective NuGet source selection for an exact package version:

```text
lucent doctor --feed Lucent.Core --version <exact-version> --workspace <absolute-directory> [--json]
```

Add `--online` to request anonymous feed metadata. These checks run in a separate
adapter using NuGet's configuration and protocol libraries. Configuration inspection
supports local Windows fixed drives and never restores packages or writes settings.
Unsupported configuration is reported as unavailable rather than falling back to a
loader that can create configuration files.

Online checks send no saved, environment, Windows, proxy or plugin credentials.
They report only anonymous reachability and whether the exact version is advertised;
they cannot establish configured authentication, private package availability or
future restore success. Reports identify sources by ordinal and omit their names,
URLs and credentials. Ctrl+C cancels the operation.
