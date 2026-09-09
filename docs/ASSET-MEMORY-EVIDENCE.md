# Raster preparation memory evidence

The maintained, focus-free probe is
[`tools/AssetMemoryProbe`](../tools/AssetMemoryProbe/). It measures one fresh
process per JPEG input and writes results to the ignored
`artifacts/assets-146-memory/issue154/` directory. The exact reproduction
command is:

```powershell
./tools/AssetMemoryProbe/Run-Probe.ps1 -Label issue154
```

The runner builds only the probe and consumes the current Release
`Lucent.Core.dll` and `Lucent.Renderer.Skia.dll`; build those framework
assemblies with the affected renderer checks before invoking it. It verifies
the three checked-in fixture hashes in
[`tools/AssetMemoryProbe/fixtures/SOURCES.md`](../tools/AssetMemoryProbe/fixtures/SOURCES.md).

The probe uses a 512 MiB encoded-source limit, a 100-million-source-pixel
limit, 64 MiB output/cached/leased limits, and a 1 GiB
temporary-preparation allowance so the measured fixtures can reach the codec.
It is a measurement harness rather than a copy of the default application
budget. Its output admission remains capped at 64 MiB per prepared image. The
JPEG classifier admits only matching 8-bit, three-component standard RGB
sampling layouts: 4:4:4, 4:2:2, and 4:2:0. Known baseline input uses the
34-bytes-per-source-width estimate only when the first scan includes all three
components; known progressive input uses the pinned
6-bytes-per-source-pixel coefficient estimate plus that strip estimate.
Sequential multi-scan, unsupported, malformed, CMYK/YCCK, and unusual-sampling headers use the
conservative 8-bytes-per-source-pixel fallback.

These estimates follow the pinned [Skia JPEG codec](https://github.com/google/skia/blob/bdd0c3a8eaba1afa7148f02bba3a07f94e682847/src/codec/SkJpegCodec.cpp#L512)
and [libjpeg-turbo memory documentation](https://github.com/libjpeg-turbo/libjpeg-turbo/blob/9217719d3a58633923b096af4c1d50d304768a64/doc/libjpeg.txt#L3172).
The pinned [decompressor initialization](https://github.com/libjpeg-turbo/libjpeg-turbo/blob/9217719d3a58633923b096af4c1d50d304768a64/src/jdmaster.c#L705)
also allocates a full coefficient buffer for non-progressive multi-scan input;
the admission classifier therefore checks SOS structure as well as SOF.

The September 9 maintained run measured these representative reservations
and process-private deltas for a 128x128 output. Core SHA-256 was
`0401836F2674C57E248C8B5E3FE1310F1FABC5651EA6B9F18A490CFB2161621E`;
renderer SHA-256 was `AF70DBE81ECB16423B63606A882660B889B2026B55F8AA08DE76A1CC63685A10`.

| Input | Temporary reservation | Peak private delta |
|---|---:|---:|
| 6000x6000 PNG | 432,512,823 B | 144,625,664 B |
| 6000x6000 baseline JPEG | 7,355,015 B | 3,219,456 B |
| 6000x6000 JPEG 4:4:4 | 7,565,952 B | 3,502,080 B |

The earlier conservative baseline-JPEG admission reserved 293,105,015 B for
the same 6000x6000 input. The refinement changes admission accounting rather
than claiming a corresponding reduction in the codec's actual allocations.
The original generated progressive 32x23 and 650x470 fixtures, and the
600x397 CMYK fixture, all reached Ready in fresh processes. Their temporary
allocations completed between samples, so the observed zero reservation peaks
do not establish zero working memory.

The same binaries measured raster cold/warm acquisition at 43.02/0.36 ms and
first SVG cold/warm acquisition at 683.36/0.02 ms. These are managed-process
observations including first-use adapter/JIT cost, not NativeAOT timings or
desktop frame budgets. Twelve rendition requests left 11 ready entries and
8,800,192 cached bytes; the final 100 ms idle observation had no queued,
active or temporary work. Application-icon generation packages PNG renditions
ahead of runtime startup, while ordinary SVG controls still prepare asynchronously.

After running the maintained runner, reproduce the additional cases with:

```powershell
$probe = 'tools/AssetMemoryProbe/bin/Release/net10.0/MemoryProbe.dll'
$fixtures = 'tools/AssetMemoryProbe/bin/Release/net10.0/fixtures'
dotnet $probe "$fixtures/large.png" png
dotnet $probe "$fixtures/large.jpg" jpeg
dotnet $probe "$fixtures/large-444.jpg" jpeg
dotnet $probe performance "$fixtures/large.jpg" jpeg
```

Private bytes, working set, and temporary reservations are sampled every 1 ms
while the load is pending. The probe records `PeakPrivateBytes` and
`PeakPrivateDeltaBytes`, but a native allocation that begins and ends between
samples can be missed. These are fresh-process observations for the listed
inputs, not allocator or codec peak guarantees.
