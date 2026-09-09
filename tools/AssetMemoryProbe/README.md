# Asset memory probe

This focus-free probe measures the raster preparation path in a fresh process.
It consumes the already-built Release `Lucent.Core` and
`Lucent.Renderer.Skia` assemblies; it does not build the shared solution.

From the repository root, run:

```powershell
./tools/AssetMemoryProbe/Run-Probe.ps1 -Label issue154
```

The runner builds only this standalone probe, verifies the SHA-256 values of
the maintained JPEG fixtures, runs one `MemoryProbe` process per fixture, and
writes JSON under `artifacts/assets-146-memory/issue154/`. The probe performs a
16x16 PNG warm-up in each process before measuring the requested image.

The process sampler polls private bytes, working set, and temporary reservation
metrics every 1 ms. A native allocation that begins and ends between samples
can be missed; the results are measurements for the tested inputs, not a
claim that the allocator or codec peak is bounded by the sampled value.

`MemoryProbe generate` creates the ignored large PNG/JPEG inputs used by the
#146 evidence. `MemoryProbe performance <path> <png|jpeg>` records cold/warm
raster and SVG preparation, rendition churn, cache metrics, and a 100 ms idle
check for the #150 evidence.
