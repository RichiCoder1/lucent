# Layout engine evaluation probe

This test-owned probe supports [ADR 0004](../../../docs/adr/0004-layout-and-paragraphs.md). It is not in the solution, is not a release gate, and does not add a production Lucent dependency. Generated files and binaries remain under ignored `artifacts/layout-evaluation`.

- `TaffyNative` is a Rust `cdylib` pinned by `Cargo.lock` to Taffy 0.14.0.
- `AotHost` is a .NET 10 NativeAOT executable. It uses source-generated P/Invoke, runs the bounded cases, destroys the opaque native owner, and verifies the live-owner count returns to baseline.
- `ManagedCurrent` references a frozen copy of the existing Release `Lucent.Core.dll` to compare current supported behavior without rebuilding or changing main outputs.

## Reproduce

The recorded run used Windows x64, .NET SDK 10.0.400, rustc 1.94.0, and cargo 1.94.0 on 2026-09-05.

```powershell
$repo = (Get-Location).Path
$env:CARGO_TARGET_DIR = Join-Path $repo 'artifacts/layout-evaluation/taffy-target'
cargo build --manifest-path tests/Probes/Layout/TaffyNative/Cargo.toml --release --locked

dotnet publish tests/Probes/Layout/AotHost/AotHost.csproj -c Release -r win-x64 --self-contained --artifacts-path artifacts/layout-evaluation/dotnet -o artifacts/layout-evaluation/publish
Copy-Item artifacts/layout-evaluation/taffy-target/release/lucent_taffy_probe.dll artifacts/layout-evaluation/publish/lucent_taffy_probe.dll
./artifacts/layout-evaluation/publish/AotHost.exe
```

Both release builds completed with zero warnings and zero errors. The NativeAOT host printed:

```text
{"taffy":"0.14.0","wide":[184.0, 320.0, 556.0],"medium":[64.0, 300.0, 476.0],"flex":[344.0, 80.0],"wrapped_flex_lines":3,"paragraph":{"width":210,"height":72,"measure_calls":4},"nested_scroll":{"viewport_height":472,"content_height":1200,"scroll_extent":1200},"virtual_last_exclusive":12,"rounded_tracks":[34.0, 33.333336, 34.0],"nodes":1001,"elapsed_us":878}
native-aot-load-dispose=pass
```

The paragraph callback uses a synthetic seven-pixel advance and 18-pixel line height. It proves constrained measurement callback wiring, not text shaping quality or performance. The fixed-virtualization result is arithmetic applied after the measured 472-pixel Grid cell; it is not a Taffy virtualization feature.

The published host was 986,112 bytes with SHA-256 `7BE8F498A341351D830537350A890EE187A9386024C718FA55260AD7999415FB`. The native library was 809,472 bytes with SHA-256 `8A4D971A2EBD33E3140CC222295C65961CAAD25B5EF13A90F5FE4C6015A389D6`. Timing and size are diagnostic evidence for this small probe only.

## Current managed comparison

The comparison snapshots `src/Lucent.Core/bin/Release/net10.0/Lucent.Core.dll` into ignored artifacts before running. The frozen Release DLL was observed during a concurrent batch whose HEAD was `8fea38ad14d8e14f732eac9aa34421e4009cbc4c`; dirty working-tree edits may be present, so the authoritative identity is SHA-256 `5BC42866737BD21C0045A31C2071135207FFEAD116925219270BCFB63F7DDA8F`.

```powershell
New-Item -ItemType Directory -Force artifacts/layout-evaluation/baseline | Out-Null
Copy-Item src/Lucent.Core/bin/Release/net10.0/Lucent.Core.dll artifacts/layout-evaluation/baseline/Lucent.Core.dll
dotnet run --project tests/Probes/Layout/ManagedCurrent/ManagedCurrent.csproj -c Release --artifacts-path artifacts/layout-evaluation/managed-dotnet
```

Observed output:

```text
managed-row=[184,320,556]
constrained-row=[240,80]
text-210-height=16; text-105-height=16
requests-equal=True; request=length=113,fontSize=16,language=en,direction=LeftToRight,scale=1
baseline-commit=8fea38ad14d8e14f732eac9aa34421e4009cbc4c
core-sha256=5BC42866737BD21C0045A31C2071135207FFEAD116925219270BCFB63F7DDA8F
```

The current managed engine can allocate positive remainder for the three body widths. Under negative remainder, its 240- and 80-pixel children retain their bases and overflow the 300-pixel parent once the eight-pixel gap is included. The synthetic shaper receives identical requests at 210 and 105 available pixels and returns the same single-line height because the current request has no constraint fields.

The probe does not include real Skia shaping, production packaging, accessibility, input scrolling, or a full retained application tree.
