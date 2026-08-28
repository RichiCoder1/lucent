# M2 presentation evidence

`WindowsBootstrap` sets Per-Monitor V2 before SDL initialization. `GetDpiForWindow / 96` is scale authority; SDL render output is backing size; logical Core viewport is backing divided by scale. Skia scales once into a persistent premultiplied RGBA CPU surface, then uploads it to a persistent ABGR SDL streaming texture. Vsync is required. The pair is recreated only for backing size or format changes.

`tests/Lucent.Platform.Windows.Tests` covers 100/125/150/200% conversion, 3840x2160 and fractional backing, resource reuse, repeated PMv2 initialization, minimize/restore/zero-size, the restore/scale-before-backing event order, negative virtual-desktop moves, idle coalescing, and separate phase timings. `tools/Verify-M1.ps1` runs it managed and NativeAOT, then uses an external GDI capture (not production readback) of DPI-scaled opaque header/page pixels before ordinary close.
