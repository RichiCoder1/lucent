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
