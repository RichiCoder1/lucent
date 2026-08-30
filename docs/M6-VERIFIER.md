# M6 automated verifier

Run `pwsh tools/Verify-M6.ps1 -Mode PreCommit` for the automated dirty-tree subset. It records a commit/tree/working-tree identity before publish, then writes JSON evidence under `artifacts/m6/` and the compact tracked status at `docs/M6-AUTOMATED-SUMMARY.md`. Pre-commit evidence is never final.

`-Mode Final` first requires a clean source tree and runs `tools/Verify-M1.ps1`; it still fails until supplied manual and clean-machine gate records exist. The external NativeAOT `Lucent.M6.Verifier` drives only the copied, extracted NativeAOT application through posted Win32 keyboard messages and `SetWindowPos` resize operations. Each issued operation must produce its expected SDL frame type; warmup and corpus frame delimiters are recorded separately. Posted input avoids stealing the user's foreground window while exercising the same host translation, routing, projection, and presentation path.

`M6-BASELINE.json` is the pre-measurement contract. The script rejects schema/value/tag changes and records SHA-256 hashes for it and the verifier source. It fixes warmup, vsync, compacting/LOH GC, clock endpoints, corpora, latency, virtualization, memory, and resource bounds. Cold launch, working set, tested HWND monitor/DPI/scale, and publish/zip bytes are observations. The verifier checks the exact recursive asset/notice inventory (including hidden/system rogue negatives), hashes the publish directory and extracted zip, and launches the extracted copy without the SDK.

The verifier does not perform visual review, Accessibility Insights/Narrator, real Japanese IME interaction, or a clean-machine package run. Those remain parent-owned gates and are intentionally reported as pending rather than passed.
