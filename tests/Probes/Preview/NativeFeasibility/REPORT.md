# Retained feasibility execution

Executed on 2026-09-30, Windows 10.0.26200 x64, 24 logical processors. This is a
single-machine characterization, with three warm captures per successful worker,
not a percentile benchmark or a production performance budget.

Package set: **0.3.0-dev.108.1**, SDK **10.0.401**, package source commit
`f14bfe477ff96dcd834b91876a3fda323157160d`. The supplied authenticated CI108
catalog was `C:\Users\richa\AppData\Local\Temp\lucent-ci108-catalog-51bc0515ecdb46bfba354e72a6d222ca`.
The validated descriptor SHA256 was
`6E7CD036CED64E14361049A9568FA1236604A929BC172A3191DA11E288B7721E`.

```powershell
./tests/Probes/Preview/NativeFeasibility/Run-Probe.ps1 `
  -DescriptorPath 'C:\Users\richa\AppData\Local\Temp\lucent-ci108-catalog-51bc0515ecdb46bfba354e72a6d222ca\evidence\contents\complete.json' `
  -ArtifactDirectory 'C:\Users\richa\AppData\Local\Temp\lucent-ci108-catalog-51bc0515ecdb46bfba354e72a6d222ca\evidence\contents'
```

Final passing evidence, including exact subprocess arguments, source hashes,
logs, diagnostic attribution, three PNGs and JSON measurements:
`C:\Users\richa\AppData\Local\Temp\lucent-native-preview-30352f9c55504ab8abed98f445254424\report.json`.
The report predates this prose report; the executable fixture sources and README
were present when its source inventory was captured.

| Generation | PID | Build ms | Worker first frame ms | Coordinator launch-to-frame ms | First capture ms | Managed capture allocation bytes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Card baseline | 5256 | 6356.74 | 401.46 | 529.34 | 19.75 | 996072 |
| Card edited | 66444 | 2508.28 | 378.86 | 503.26 | 19.18 | 996192 |
| Official starter | 26328 | 2358.30 | 399.27 | 509.34 | 21.32 | 1971400 |

Actual edit-to-validated-frame observation was **3016.49 ms**, including the
successful rebuild and fresh worker. Coordinator observations have **50 ms
polling resolution**. Completion of all edited-worker measurements and disposal
took 6120.03 ms; this includes the intentional three-second idle measurement and
is a different boundary from first frame.

Warm capture ranges were 8.69–11.44 ms for the card and 9.49–11.82 ms for the
starter. Process-wide managed allocations were approximately 0.994–0.997 MB per
card capture and 1.969–1.972 MB per starter capture. After captures, private
memory was 24.26–25.04 MB, working set 71.48–73.99 MB and managed heap
1.92–2.88 MB (decimal MB). Native allocations are not separately measured.
Each final-run idle interval lasted about three seconds and observed 0 ms
process CPU; an earlier successful run observed 15.625 ms in the starter.
Internal wakes are not instrumented, so neither observation proves zero wakes.

Both card generations independently painted the named embedded blue SVG (1296
blue pixels) and the separate packaged CircleCheck icon (638 dark pixels).
The title's pixel hash changed from `56841EB0...6422B6BB06` to
`2F10BAE2...FABEF9B103`. All frames were 540 × 660 physical pixels. The starter
used its actual light button-icon presentation. All three workers disposed their
headless application and exited successfully. Successful builds were warning
clean. Syntax rejection required **LUI1009 / PreviewCard.lui**, and C# rejection
required **CS0103 / InvalidPreview.cs**. Launch count remained two across both
failed generations, with the edited frame explicitly marked stale.

First failures were retained rather than discarded:

- `lucent-native-preview-baf870bb8fc545afba698e4d7d47357e`: PowerShell cleanup
  incorrectly used null-conditional disposal. Corrected to explicit null checks.
- `lucent-native-preview-c3e740df602b43b2a9ab769a0fac70bf`: NETSDK1151 from
  referencing the self-contained starter with a framework-dependent worker.
  Matched authored worker configuration; no production change.
- `lucent-native-preview-bb23c1c10bf845a593e67b91ae162b30`: a missing `.lui`
  expression symbol produced LUI2009 before C# compilation, so it failed the
  intended CS0103 oracle. Used a separate authored C# negative input.
- `lucent-native-preview-1a10751f62d2406eb909cb97f31eb1fe`: that negative C#
  input was inadvertently retained into the starter build. Corrected cleanup.
- `lucent-native-preview-bef5e68b275f4407a6cbcc7225ed5307`: starter capture
  correctly rejected a dark-stroke expectation for its light button icon.
  Corrected the fixture's scenario-specific pixel expectation.

All failure directories are under `C:\Users\richa\AppData\Local\Temp`.
CSharpier and the existing LUI formatter checks passed for the authored fixture,
as did scoped whitespace validation. No UI was opened. The existing production
Skia headless seam is usable for these two small compiled components; larger
projects, continuous preview, editor coordination and persistent-surface
allocation behavior remain outside this evidence.

Passive frame inspection also confirms that the card's long body line is clipped
under its authored default text style. The sample sentence mentions wrapping but
does not enable it; this fixture asserts column placement, not text wrapping.
