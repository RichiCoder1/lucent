# Issue #16 NativeAOT final smoke

Run `../../run-issue-16-proof.ps1`. It locked-restores, warning-as-error
builds, then publishes the complete `NativeStackProbe` once with NativeAOT and
trimming enabled. The published executable—not `dotnet run`—then supplies every
automated result.

The proof records the actual publish file inventory, hashes, executable/module
paths, locked package identities, and the copied authoritative native-package
notice. It rejects any native binary beyond `NativeStackProbe.exe`, `SDL3.dll`,
`libSkiaSharp.dll`, and `libHarfBuzzSharp.dll`.

`NativeStackProbe.csproj` declares `PublishAot` and `PublishTrimmed`; the
proof also verifies both declarations. `ProbeJsonContext` is the explicit
source-generated `System.Text.Json` registration table used by executable JSON
output. There is no reflection discovery or runtime code generation: the proof
scans only the project/core C# sources, excluding docs and evidence.

Japanese IME and Narrator/Accessibility Insights material is validated as
existing **manual supplemental evidence**. It is never relabeled as an
automated OS-IME or accessibility run.
