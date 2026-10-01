# Native preview feasibility

This package-only probe measures the existing production Skia offscreen seam. It
compiles a small `.lui` card and an official generated starter component with the
real Lucent SDK, then calls their explicit generated factories in disposable
managed workers. Referencing the starter does not execute its application Main.
There is no reflection scan, preview projection, runtime compiler or UI launch.

Run from the repository with PowerShell 7.4 and an authenticated local CI bundle:

```powershell
./tests/Probes/Preview/NativeFeasibility/Run-Probe.ps1 `
  -DescriptorPath '<catalog>/evidence/contents/complete.json' `
  -ArtifactDirectory '<catalog>/evidence/contents'
```

The runner validates the bundle descriptor and bytes, installs its exact template
into an isolated CLI home, materializes `.csproj.input` files in a fresh C: temp
directory, and uses the normal package cache. It retains commands, source hashes,
build logs, per-generation PNGs and results. Package authentication remains the
responsibility of the supplied CI bundle; local byte checks cannot establish it.
The generated starter and worker are self-contained managed win-x64 executables;
NativeAOT and trimming are disabled for this feasibility measurement.

Three successful generations each enter a fresh worker: baseline card, changed
card title, and starter. Each captures 360 × 440 logical pixels at 150% scale.
The card asserts title glyph pixels and real layout, a blue embedded SVG through
its named image semantic, and a separate monochrome Lucide CircleCheck region.
The edited title must change its own pixel hash. Two deliberately failed builds
must report LUI1009 in authored PreviewCard.lui and CS0103 in authored
InvalidPreview.cs respectively. Neither launches; the previous successful frame
is recorded as stale, never current. The C# failure uses an adjacent authored
source because an unknown expression in `.lui` can fail earlier during binding.

Measurements distinguish worker first frame from coordinator launch-to-frame
and edit-to-frame observations (50 ms polling resolution). First frame includes
JIT, mounting, settling, image preload, PNG capture, pixel validation and writing
the frame. Three warm captures measure process-wide managed allocation deltas;
they include queued capture work and exclude native allocation accounting. The
existing capture API creates a bitmap and renderer for each capture, so these
numbers characterize that API rather than a persistent preview surface.

A three-second interval requests no frames, input or resizing; process CPU and
memory are sampled at its boundaries. This supports an idle observation, not an
instrumented count of internal wakes. Both scenarios are small. There is no
large-project, Issue Browser, continuous-preview, Hot Reload, native window,
IME, accessibility or cross-machine performance claim. Every edit rebuilds and
restarts, resetting component state. This runner is a bounded feasibility probe,
not a production preview coordinator.

See [REPORT.md](REPORT.md) for the retained execution and first failures.
