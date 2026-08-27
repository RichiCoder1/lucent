# Issue #3 platform evidence

Captured on Windows `10.0.26200.9168`, `win-x64`, with the dependency versions locked by `../NativeStackProbe/packages.lock.json`.

## Automated host and UIA

```powershell
./run-probe.ps1 --automated
./run-uia-proof.ps1
```

The automated SDL proof passed create/show, exact resize, nonzero and stable HWND/DPI, positive display scale, focus gain/loss, `WM_CLOSE` close request, duplicate subclass installation, callback delivery, subclass removal, and destruction.

The published NativeAOT UIA host reported `DynamicCodeUnsupported=true`, six genuine root `WM_GETOBJECT` deliveries, nine property calls, successful disconnect (`HRESULT 0`), subclass removal, provider release, and HWND destruction. The external helper read the exact Name `NativeStackProbe UIA root` and AutomationId `NativeStackProbe.Root` three times, including from a worker thread.

Accessibility Insights for Windows `1.1.2924.1` Live Inspect confirmed the visible provider Name and Window control type. The reviewer also confirmed AutomationId `NativeStackProbe.Root`; the executable helper above independently asserts it. See `issue-3-accessibility-insights.png`.

## Japanese IME

The manual harness used Microsoft Japanese IME in Hiragana (`あ`) mode with `SDL_IME_IMPLEMENTED_UI=composition`; native candidate UI remained enabled.

- `issue-3-ime-ja-JP.jsonl` records visible blue/underlined preedit, native candidates at the moving caret, empty-preedit cancellation, committed `日本語`, focus loss/gain, and close.
- `issue-3-ime-focus-ja-JP.jsonl` records active `てすと` preedit followed by focus loss, with preedit cleared and no committed text.
- The parent and user visually confirmed preedit-to-commit color transition, candidate placement, Escape cancellation, and focus-loss cancellation.
- `--text-self-check` separately covers a surrogate-pair commit (`😀`), rune-index handling, and the `😀中` mid-preedit cursor prefix so a caret cannot split the surrogate pair.

Runnable checks:

```bash
jq -e 'any(.Kind == "editing" and (.Text|length)>0) and any(.Kind == "editing" and .Text == "") and any(.Kind == "input" and .Text == "日本語") and any(.Kind == "focus-lost") and any(.Kind == "focus-gained") and any(.Kind == "close")' -s issue-3-ime-ja-JP.jsonl
jq -e '.[-1].Kind == "close" and .[-1].Committed == "" and any(.Kind == "editing" and .Preedit == "てすと") and any(.Kind == "focus-lost" and .Committed == "" and .Preedit == "")' -s issue-3-ime-focus-ja-JP.jsonl
```

## Artifact hashes

```text
6575c79327454191afa72a48738c4880317c8ad865a48afb17f635f40bc93522  issue-3-accessibility-insights.png
35d0361440ec2f52a74df92122b0f6af6e2e78fbe8cc3205ed95b02b8c81f9da  issue-3-ime-focus-ja-JP.jsonl
37d84d46abbed3fc97b431a4859ebb436f5628482a8e973086b7f511d8d9c62e  issue-3-ime-ja-JP.jsonl
```

This evidence passes issue #3's bounded platform proof. It does not claim the later Narrator walkthrough, full semantic tree, or Milestone 1 rendering/layout gates.

## Milestone 1 combined evidence

`../run-milestone-1-gate.ps1` writes `milestone-1/proof.json`. It executes the
published NativeAOT SDL host, text-state, UIA, scene, and layout/shaper checks,
then validates this issue's two JSONL IME transcripts and their SHA-256 values.
The transcript validation is machine-readable supplemental evidence for real
SDL/Windows IME delivery; only the text-state matrix is automated.

## Issue #4 retained-scene evidence

`../run-scene-proof.ps1` performs the locked NativeAOT publish and two independent seeded captures. It exits nonzero on stable-ID, dirty-facet isolation, structural parity, raster, frame scheduling, native present, resize, or artifact-hash failure. The captured machine-readable result is [`issue-4/proof.json`](issue-4/proof.json); its canonical dumps and PNGs are alongside it.

The recorded tolerance is **0 RGBA pixels**. `native-framebuffer.png` is read from SDL with `SDL_RenderReadPixels` after texture composition and before present; it matched the independently rendered `headless-input.png` at zero differing pixels. The native path recorded 96 DPI, SDL scale 1, and a 128×96 → 256×192 resize. The same proof records zero idle/semantic-only present calls, one changed-bounds layout present, and one changed-style paint present. The canonical snapshots include panel bounds `[10,8,108,80]` and semantic name `Ready semantic`.

```text
3f000af4e00f5b398927ead432928128e17797616e772e6466f786a2904e834a  elements.json
22d9b8c1181584bf34bb2cf5aa8510f1c1ba326e5f31e4ec44c6a3390b2ab1c1  headless-input.png
86a8e994c976fe0748d1291c4ca52f7b7eb0afd1fb5a68eadcf2f5dfd3ef4e3e  layout.json
22d9b8c1181584bf34bb2cf5aa8510f1c1ba326e5f31e4ec44c6a3390b2ab1c1  native-framebuffer.png
4ba8996b03db4792eaaff208d6e821836e73720714d4ad950e96946fdc081315  semantics.json
7e118e21b2f36cf20b00b30934526a3b9952c78b00eefb18111230f25d510c9f  style.json
```

## Issue #5 bounded layout and shaped-text evidence

`../run-layout-proof.ps1` locked-restores, warning-free NativeAOT-publishes,
then runs the layout proof twice. The canonical dump and raster are under
[`issue-5/`](issue-5/); the script rejects any artifact-set or SHA-256 mismatch.
It covers ligature-sensitive Latin, combining marks, RTL Arabic, CJK fallback,
mixed script, emoji/surrogate pairs, a missing requested font, executable
en-US/tr-TR culture independence, and 1.25×/2× scale rounding. The current
published evidence pins SkiaSharp/SkiaSharp.HarfBuzz 4.151.1 and HarfBuzzSharp
14.2.1.1; the dump records their actual assembly versions, glyph IDs, clusters,
shape hashes, language, direction, script, and resolved faces.

```text
1232a09611a6f2311f67ffa24536ae9ffa0749e6a1b99253a83cde94681e7ca9  layout-text.json
2d7ce3622de61443802808ad76858fc0662b0f6ef1612894cd2c16faef5effbe  layout-text.png
```

## Issue #9 style semantics evidence

`../run-style-proof.ps1` locked-restores, warning-free NativeAOT-publishes, and
writes [`issue-9/proof.json`](issue-9/proof.json). The executable proof covers
explicit last-write-wins composition, token dependency updates on a live
light/dark switch without losing selected state, every overlapping state layer,
reduced motion at startup and mid-transition, scheduler-owned clock disposal,
and zero idle frames.

## Issue #10 composed controls evidence

`../run-controls-proof.ps1` locked-restores and warning-free NativeAOT-publishes
the probe, then writes [`issue-10/proof.json`](issue-10/proof.json). It proves
composed button activation, single-line scalar editing and IME preedit,
clipboard/focus/disposal behavior, and portable `edit`/`set-value` semantics;
text, structural, and style checks are rerun as regressions. It does not claim
a native child UIA provider, multiline/rich text, or undo.

## Issue #11 scroll and virtualization evidence

`../run-virtualization-proof.ps1` locked-restores and warning-free
NativeAOT-publishes the probe, then writes [`issue-11/proof.json`](issue-11/proof.json).
It covers wheel and keyboard movement, keyed selection/focus scrolling,
reorder/removal behavior, 10,000 rows with a 14-row fixed-height realized
ceiling, disposal of offscreen scopes/input/focus/capture/scene/semantics, and
coarse managed-memory measurements. The final managed-memory budget remains
issue #15; variable-height virtualization is not claimed.
