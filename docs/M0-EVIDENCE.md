# M0 executable evidence

Run `pwsh tools/Verify-M0.ps1` on Windows. It performs locked restore, warning-clean build and `win-x64` NativeAOT publish, exact asset/notice reconciliation (including a removed-asset negative), Core architecture positives and injected negatives, then three ordinary-close lifecycle smokes from a clean copied publish directory.

The M2 host supersedes the fixed 1.25× test presentation: it declares Per-Monitor V2 before SDL starts, derives logical coordinates from SDL backing pixels and `GetDpiForWindow / 96`, and uses no production readback. The external smoke captures opaque header/page pixels at DPI-scaled coordinates to prove real SDL/Skia content, color-channel order, and one-scale geometry before repeated ordinary close. M0 does not claim a framework canvas, kernel, accessibility, input, installer, or signing surface.
