# Example visual evidence

These deterministic native captures are review evidence, not screenshot goldens.
They were refreshed for Plan 010 after every example switched to Fluent plus the
shared `ShadcnTheme`; adjacent CSS uses only `Shadcn.*` semantic resources.

## Capture matrix

| Profile | Theme/state | Logical size | File | SHA-256 |
| --- | --- | --- | --- | --- |
| counter-light | light, incremented | 420×300 | `captures/counter-light.png` | `a1e8ed16b4efc764ced23afdb463c3834973e9b7799f2b28bcaf17d8cc373d53` |
| counter-dark-focus | dark, focus | 420×300 | `captures/counter-dark-focus.png` | `6388d15e5d4d7be623901b4d893071aae29f583427cfa7bc5a001191ac316f33` |
| todo-light-populated | light, populated | 900×760 | `captures/todo-light-populated.png` | `1e01811b269d3a59cf5d9f9f776e239d87ef90973a7413ec2e03039bc15895a3` |
| todo-dark-empty | dark, completed/empty state | 700×560 | `captures/todo-dark-empty.png` | `bc5b0ad90d462f8f309ed9e5d4f550cd0ef18738f2ceac9c2c77bf3ab54338e2` |
| pulse-dark-results | dark, results | 820×760 | `captures/pulse-dark-results.png` | `e405355ac31c2dea1c1b1230e7f33bccc23d56b48e8b59c46ba67159efea9774` |
| pulse-light-error | light, stale results and error | 600×560 | `captures/pulse-light-error.png` | `ccd78fb30632348bb834544a87b5cc984ac16cc2aa9537ea8ef3065defe3b594` |
| workbench-light-shell | light, shell | 1280×800 | `captures/workbench-light-shell.png` | `a7220353dbd38223c18eb0ebfe89fe629f32ecbdabce8ab72df998129f3a799e` |
| workbench-dark-palette | dark, palette | 960×680 | `captures/workbench-dark-palette.png` | `309fc3044f9b8e37ad996d7289f6dae7dd4e321189e18558d35db429ba10315b` |

All commands use `--quality-capture <profile>` from the example README. The
profiles still exercise normal application rendering and existing minimum-size,
focus, loading, error, and palette paths. Logical-size evidence is the executable
gate. The user approved physical 200% Windows display scaling as a manual
follow-up; this record does not claim a 200% pass.

## Boundaries

Fluent and AvaloniaEdit templates remain native. The Lucent mark remains the
window icon only. No utility catalog, compatibility resource aliases, runtime
CSS, or Workbench project/compiler integration was added.
