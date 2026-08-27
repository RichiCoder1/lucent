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
