# Scene projection evidence

Issue [#114](https://github.com/RichiCoder1/lucent/issues/114) measured repeated retained projection before selecting an optimization. The maintained probe measures only calls to `SceneLayout.Project`; graph mutation, input installation, and snapshot disposal occur outside the measured interval. If input installation rejects a scene after a responsive branch remount, the sample includes each projection attempt needed to reach an accepted scene. Allocated bytes are `GC.GetAllocatedBytesForCurrentThread` deltas, so they describe managed allocation churn on the projection thread rather than retained heap growth.

The September 9, 2026 run used MORO-DESKTOP with an AMD Ryzen 9 9900X (12 cores, 24 logical processors), 66,188,967,936 bytes of physical memory, Windows 10.0.26200, .NET 10.0.11, X64, and Release configuration. Each scenario used 10 warmups and 40 recorded samples. The wide fixture projected 501 boxes, the deep fixture projected 129 boxes, and Issue Browser projected 94 boxes with 12 realized rows.

The baseline used the committed `SceneLayout.cs` Git blob `66d75caf3b893ad11ab9476f861dda5098d544db`. Its evidence is `artifacts/scene-projection-114/baseline.json`, SHA-256 `bd01af7d1e804e7b96bcc95b05df75b8094c63563f395b6358b00c072026c685`. The after runs used worktree `SceneLayout.cs` SHA-256 `589c63cb161bb6e55804e6e4e85b025ac9d9422bb062bd2be1a95f3092885462` and probe SHA-256 `b65a8554e0fc932fe62c26acfbfbdf5cf93d7bf05e4aa661db9268acb50b7697`. The repeat evidence is `artifacts/scene-projection-114/after-repeat.json`, SHA-256 `53a4774776cc7ddd0768140bd6ad49bb458c9dbd5a36ee081640d26bdd802a99`.

| Scenario | Baseline p50 | After-repeat p50 | Baseline p50 allocation | After-repeat p50 allocation |
| --- | ---: | ---: | ---: | ---: |
| Wide, unchanged | 32.43 ms | 27.15 ms | 118,997,528 B | 93,869,504 B |
| Deep, unchanged | 76.58 ms | 51.09 ms | 314,743,320 B | 234,311,776 B |
| Issue Browser, unchanged | 43.96 ms | 39.22 ms | 74,669,840 B | 66,970,248 B |
| Issue Browser, same wide bucket | 46.29 ms | 36.59 ms | 74,672,920 B | 66,973,328 B |
| Issue Browser, breakpoint change | 85.11 ms | 69.69 ms | 128,187,760 B | 116,531,616 B |
| Issue Browser, scale change | 43.80 ms | 37.25 ms | 74,672,824 B | 66,973,232 B |
| Issue Browser, scroll change | 44.53 ms | 39.26 ms | 76,077,888 B | 68,378,872 B |
| Issue Browser, input change | 43.74 ms | 35.84 ms | 74,670,208 B | 66,970,616 B |

The first after run is retained at `artifacts/scene-projection-114/after.json`, SHA-256 `de7e8a2fb35a22b425aa4fe6ea47e613d49d66911179457337cb3d3cae79501d`. Its scale-change p50 was 44.55 ms while the repeat was 37.25 ms, which demonstrates enough timing variance to reject a new fixed latency gate. Allocation results varied by less than 0.1% for most scenarios. Breakpoint transitions required exactly two projection attempts per sample before and after because the branch remount invalidates the first input projection by design.

The implemented optimization retains resolved style values already captured for the input mutation guard and reuses them during the final layout in the same projection pass. Paint-only background, opacity, and text-color reads remain outside input tracking. A fresh post-layout signature still detects style, typography, scrolling, input, and participation changes made while producing the scene. The snapshot does not survive into another projection and does not implement dirty-subtree caching.

The after worktree also contained the focused #116 Grid/Flex allocation correction and #117 paragraph-width correction, so elapsed-time changes cannot be attributed solely to #114. The allocation reduction is consistent with removing one repeated property-resolution pass, but it is still evidence from the combined source. Allocations remain substantial, especially for deep inherited-property resolution, and warrant later measurement before another narrow optimization. The probe currently includes Issue Browser and synthetic trees. Light Notes remains a separate package consumer and was not included in this measured corpus. These results do not establish a release threshold, a universal frame budget, cold-start behavior, renderer cost, or performance on other hardware.

Commands used:

```powershell
dotnet build tests/Lucent.Performance.Verifier/Lucent.Performance.Verifier.csproj -c Release --no-restore -p:BuildProjectReferences=false
tests/Lucent.Performance.Verifier/bin/Release/net10.0-windows10.0.26100.0/win-x64/Lucent.Performance.Verifier.exe --scene-projection

dotnet build tests/Lucent.Core.Tests/Lucent.Core.Tests.csproj -c Release --no-restore
tests/Lucent.Core.Tests/bin/Release/net10.0/Lucent.Core.Tests.exe --no-ansi --progress off

dotnet build tests/Lucent.Performance.Verifier/Lucent.Performance.Verifier.csproj -c Release --no-restore
tests/Lucent.Performance.Verifier/bin/Release/net10.0-windows10.0.26100.0/win-x64/Lucent.Performance.Verifier.exe --scene-projection
```
