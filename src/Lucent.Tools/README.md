# Lucent.Tools

Lucent.Tools is the Lucent local command-line tool. Install the package as a
.NET tool from the authenticated Lucent prerelease feed, then run `lucent doctor`
to inspect the current environment. Use `lucent doctor --json` for structured
results or `lucent doctor --workspace <absolute-directory>` to inspect a specific
workspace. The doctor reports capability-specific checks; it does not modify the
workspace or install tooling.

The VS Code extension also carries a byte-verified private copy of the doctor.
That bundled copy is independent of any user-installed .NET tool.
