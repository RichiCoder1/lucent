# Repository guidance

Lucent is being rebuilt as a Windows-first, NativeAOT-compatible desktop UI stack. Keep portable runtime concepts independent from Windows hosting, input, IME, accessibility, and presentation adapters.

The prior Avalonia implementation and Native validation spike are preserved on `archive/avalonia-final`. Do not add compatibility paths for that unreleased implementation.

Record architectural inspirations and dependencies in `CREDITS.md` before adopting them. Prefer executable, fail-closed evidence over prose-only claims.

## Agent skills

### Issue tracker

Issues live in `RichiCoder1/lucent` GitHub Issues and execution work is organized in [Lucent Native Project 4](https://github.com/users/RichiCoder1/projects/4/views/1). See `docs/agents/issue-tracker.md`.

### Triage labels

Use the default canonical triage labels. See `docs/agents/triage-labels.md`.

### Domain docs

Use a single-context glossary at `CONTEXT.md` and decisions under `docs/adr/`. See `docs/agents/domain.md`.

### Verification

For pre-release work after M7, choose checks by affected behavior and risk. See `docs/agents/verification.md`; full milestone gates are not required for every issue.
