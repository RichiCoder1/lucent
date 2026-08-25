# Example visual evidence

These deterministic native captures are review evidence, not screenshot goldens.
They were refreshed for Plan 010 after every example switched to Fluent plus the
shared `ShadcnTheme`; adjacent CSS uses only `Shadcn.*` semantic resources.

## Capture matrix

| Profile | Theme/state | Logical size | File | SHA-256 |
| --- | --- | --- | --- | --- |
| counter-light | light, incremented | 420×300 | `captures/counter-light.png` | `207c67927051d19a51a7140ae23a2f79090dc8df98afc56c003a3b927bfbc05f` |
| counter-dark-focus | dark, focus | 420×300 | `captures/counter-dark-focus.png` | `e630dd6e1ac06b3577df9842769effa84e7dfeeeaf9660a3f01c20b94ef05d3d` |
| todo-light-populated | light, populated | 900×760 | `captures/todo-light-populated.png` | `b9319238ea4c6fcdc2022dc6409258d4f92f7a94d50223dfd8b1337c510413ad` |
| todo-dark-empty | dark, completed/empty state | 700×560 | `captures/todo-dark-empty.png` | `df05833edc2b948a314c6ce2c9e429a1ad7689c87a03e6a00c47e5504302c817` |
| pulse-dark-results | dark, results | 820×760 | `captures/pulse-dark-results.png` | `f9b130a0c324ec10f4719e1d55fd4cc1b08ccb0c8fe3e9d147121c650a189c73` |
| pulse-light-error | light, stale results and error | 600×560 | `captures/pulse-light-error.png` | `e1a3a3c44a9ab3fab9b034515296f0d0808ca3d72b0ef2f1cabe7e1235fe2ac5` |
| workbench-light-shell | light, shell | 1280×800 | `captures/workbench-light-shell.png` | `d284d1ccd77a75f283643e1580179a2ad636e35cfb9e64d2efd25027bfdc9734` |
| workbench-dark-palette | dark, palette | 960×680 | `captures/workbench-dark-palette.png` | `2481c47a9109ec69aa8109e478b2981253584b9f4eca946e4ea59be667f28f2c` |

All commands use `--quality-capture <profile>` from the example README. The
profiles still exercise normal application rendering and existing minimum-size,
focus, loading, error, and palette paths. Logical-size evidence is the executable
gate. The user approved physical 200% Windows display scaling as a manual
follow-up; this record does not claim a 200% pass.

## Boundaries

Fluent and AvaloniaEdit templates remain native. The Lucent mark remains the
window icon only. No utility catalog, compatibility resource aliases, runtime
CSS, or Workbench project/compiler integration was added.
