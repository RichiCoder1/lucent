# SDK-host generation experiment

This isolated A0 experiment tests generator-phase orchestration. It is not a Lucent SDK implementation and does not prove named components, routing, companion state, editor behavior, packaging or NativeAOT acceptance.

`Run-SdkHostProbe.ps1` builds the experiment tools, then performs one cold SDK build of the positive consumer. The SDK target asks an MSBuildWorkspace host for the evaluated raw compilation, analyzer references, AdditionalTexts and analyzer options. One preparatory `GeneratorDriver` run executes a pre-compilation declaration projection, the real .NET 10 System.Text.Json generator and a deterministic external probe generator. Its ordinary outputs are written as marked binding-only AdditionalTexts. The normal C# compilation runs the generators again, emits each source once, and compares non-Lucent preparation and final output identities and SHA-256 content.

Three additional cold builds deliberately make the external generator change, omit or add an output between preparation and final compilation. Each must fail with `PROBE9001`; no retry or convergence loop occurs.

Output exclusions use the known projection/binding generator identities, never comments inside generated source. A separate comparison negative adds a foreign output containing both Lucent marker comments and requires it to be reported as extra. The cold negative checks require a nonempty expected mismatch category, and the wrapper returns success explicitly after observing all expected failures.

Run from the repository root:

```powershell
./tests/Probes/ApplicationAuthoring/SdkHost/Run-SdkHostProbe.ps1
```

Temporary build products, manifests, captured sources and exact command/exit records are written under `artifacts/a0-sdk-host`.

The tracked `Model.lui.input` is intentionally a future-grammar support fixture rather than a current formatter input. The wrapper copies it to an actual `.lui` path under the temporary consumer before evaluating the project, so the maintained all-`.lui` formatting check remains exhaustive.

## Observed evidence

On 2026-09-14 the wrapper exited `0` from a clean `artifacts/a0-sdk-host` tree using SDK 10.0.401 and `Microsoft.CodeAnalysis, Version=5.9.0.0`. The evaluated project exposed 11 C# generators and two AdditionalTexts. Preparation captured six ordinary outputs: one external-probe output and five outputs from the real .NET 10 `System.Text.Json` generator. The normal SDK compilation produced the same six identities and SHA-256 values, then the built application exited `0` with `{"Name":"Ada"}`.

The `changed`, `missing`, and `extra` builds each exited `1` through the post-compile `PROBE9001` check, naming the expected mismatch category. Their C# compilation itself remained valid. The failure target deleted both the intermediate and final consumer assembly, and the wrapper verified neither remained available to a later copy or publish step. The exact commands and exits are in `artifacts/a0-sdk-host/commands-and-exits.txt`; individual build logs and the positive manifest are beside them.

The MSBuildWorkspace reports one non-fatal `PROBE0001` warning because the repository `.editorconfig` is present both as the analyzer config and as Lucent's explicitly transported AdditionalText. The probe retains and displays that evaluated-project diagnostic rather than treating it as clean. Workspace failures, analyzer-load failures, generator errors, absent generators, and absent preparation outputs are terminal probe failures. A production host would need durable diagnostics and recovery policy beyond this console reporting.

`Host/ProbeGenerationEngine.cs` keeps the experiment's reusable seam independent of MSBuild and file writes: `Prepare` accepts a `CSharpCompilation`, generators, AdditionalTexts, parse options, and analyzer-config options; `Compare` accepts expected and actual output identities and hashes. A later editor-host proof can exercise the same one-pass engine with an in-memory compilation without depending on this command-line wrapper.

## Integration limits from review

The fixture forwards only its explicit outer build properties. Arbitrary global analyzer options, defines, platform/RID and reference selection are not yet carried through or compared. Also, the temporary binding AdditionalFiles are observable by ordinary final generators. The JSON fixture ignores them; that matching result does not establish general input isolation. Both are unresolved A0 production-integration requirements.

The wrapper passes its selected dotnet executable to both SDK subprocesses. Preparation removes any previous consumer assemblies before host validation or generation, and a warm missing-host case verifies stale assembly removal after a prior successful build. This is intentionally an always-rebuilt feasibility fixture, not a production incremental-build policy. Analyzer-load event failures and host exceptions fail explicitly; cleanup evidence still does not cover every possible interrupted process or auxiliary build artifact.
