# Repository guidance

Lucent is a Windows-first, NativeAOT-compatible desktop UI stack. Keep portable runtime concepts independent from Windows hosting, input, IME, accessibility, and presentation adapters.

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

For pre-release work, choose checks by affected behavior and risk. Read docs/TESTING.md for repository test scope and docs/agents/verification.md for the selection policy; release checks are reserved for release decisions and risks that focused checks cannot contain.
