# Japanese IME manual smoke

This external harness does not install or enable a language and does not add an app test mode. On a monitor already configured at each declared scale (`1`, `1.25`, `1.5`, `2`), run:

```powershell
tools/Smoke-JapaneseIme.ps1 <publish-directory> <scale> <evidence-json>
```

Click **Filter issues**, select an already-installed Japanese IME, then answer the five bounded prompts: preedit, one commit, cancel, focus loss/queued text rejection, and candidate placement at the visible caret. Keep the JSON and referenced screenshots as the evidence format; each scale needs its own file.

The production host sets `SDL_IME_IMPLEMENTED_UI=composition` before SDL initialization so the custom field receives and paints preedit while Windows retains the native candidate UI. The completed 100% scale smoke is recorded in [`M3-IME-EVIDENCE.json`](M3-IME-EVIDENCE.json); other physical scales remain best-effort follow-up coverage rather than issue #34's single real-IME gate.
