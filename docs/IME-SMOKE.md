# Japanese IME manual smoke

This external harness does not install or enable a language and does not add an app test mode. On a monitor already configured at each declared scale (`1`, `1.25`, `1.5`, `2`), run:

```powershell
tools/Smoke-JapaneseIme.ps1 <publish-directory> <scale> <evidence-json>
```

Click **Filter issues**, select an already-installed Japanese IME, then answer the five bounded prompts: preedit, one commit, cancel, focus loss/queued text rejection, and candidate placement at the visible caret. Keep the JSON and referenced screenshots as the evidence format; each scale needs its own file.

The production host sets SDL_IME_IMPLEMENTED_UI=composition before SDL initialization so the custom field receives and paints preedit while Windows retains the native candidate UI. The historical 100% scale result remains available at the immutable [703d8e2 evidence record](https://github.com/RichiCoder1/lucent/blob/703d8e267c6590603350db7819aa553822a30b87/docs/M3-IME-EVIDENCE.json); current runs should retain one evidence JSON and its screenshots per tested scale.

## Automated composition ownership

`ImeCompositionContracts` and `WindowsImeContracts` cover empty preedit termination, active versus plain Escape, composition-owned navigation/deletion keys (including modified navigation), native cancellation, focus migration and rejection of queued text from the previous editor. Core consumes these keys during preedit so ancestor controls cannot move selection or focus; the Windows adapter leaves their editing behavior to the native IME and reconciles cancellation through `InputRouter.HasTextComposition` and SDL_ClearComposition.

These transport and retained-state checks run without changing the user's input language or activating a window. They do not establish real-language candidate placement or replace the manual smoke above.
