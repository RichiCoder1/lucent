# Handoff: create `lucent-samples`

## Mission

Create [`RichiCoder1/lucent-samples`](https://github.com/RichiCoder1/lucent-samples) as the home for substantial Lucent applications and ports of open-source Avalonia, WinUI, or Uno applications.

Keep small, single-capability examples in Lucent's own `examples/` directory. Put an application in `lucent-samples` when it has independent assets, licensing, history, build time, or migration notes.

## Read first

Resolve the sample's pinned Lucent package version or commit, check out that exact revision from [`RichiCoder1/lucent`](https://github.com/RichiCoder1/lucent), and read:

- `AGENTS.md`
- `CONTEXT.md`
- `docs/ARCHITECTURE.md`
- `docs/ROADMAP.md`
- `docs/LUI-LANGUAGE.md`
- `docs/LUI-SDK-TOOLING.md`
- `CREDITS.md`

When linking these documents from `lucent-samples`, use URLs containing that tag or full commit SHA—not the moving `main` branch.

Lucent is experimental. Pin every sample to an explicit Lucent package version or commit. Upgrade that identity deliberately rather than tracking `main` implicitly.

## Deliverables

1. Create the public `RichiCoder1/lucent-samples` repository.
2. Confirm the repository license with the owner and add it before publication. Do not infer one from Lucent or an upstream sample.
3. Add the repository guidance and structure below.
4. Add one small first-party smoke sample that proves restore, build, launch, and NativeAOT publish without copying the Lucent Issue Browser.
5. Add a reusable validation script that discovers sample solution or project manifests rather than maintaining a second hand-written sample list.
6. Document how to add a first-party sample or a third-party port.
7. Configure CI for warning-clean restore/build and `win-x64` NativeAOT publish of supported samples.
8. Open separate issues for substantial ports. Do not import an upstream application until its license and asset terms are recorded.

The handoff is complete when a fresh checkout can run the documented validation command, the first sample publishes for `win-x64`, and repository guidance gives an agent an unambiguous path for adding the next port.

## Repository shape

Start with the minimum structure that the first sample needs:

```text
lucent-samples/
├── AGENTS.md
├── README.md
├── CREDITS.md
├── LICENSE
├── Directory.Build.props
├── global.json
├── samples/
│   └── HelloLucent/
│       ├── README.md
│       └── ... project files
└── tools/
    └── Verify-Samples.ps1
```

Add these only when their first consumer exists:

```text
docs/ports/<sample>.md        # migration notes for a substantial port
samples/<sample>/UPSTREAM.md  # exact upstream identity and imported-file ledger
samples/<sample>/LICENSES/    # required upstream and asset notices
```

Use one directory per independently buildable application. Do not create shared sample infrastructure until two applications need the same code. A port may preserve its upstream directory shape where doing so makes future comparison or attribution clearer.

## Root `AGENTS.md`

Use this concise root guidance. Keep detailed, branch-specific instructions in linked documents beside the relevant sample.

```markdown
# Repository guidance

This repository contains substantial Lucent samples and ports. Lucent itself lives at https://github.com/RichiCoder1/lucent and is the authority for framework architecture, terminology, and `.lui` semantics.

Pin each sample to an explicit Lucent version or commit. Treat framework limitations as evidence: reproduce them here, then report framework changes in `RichiCoder1/lucent` rather than adding sample-local compatibility layers.

Every sample must restore and build warning-clean. Runtime samples must publish NativeAOT for `win-x64` unless their README records a temporary, issue-linked exception.

## Adding or changing samples

Read the sample's README before editing it. For a third-party port, also read its `UPSTREAM.md` and license notices. Preserve upstream attribution and distinguish copied or translated source from conceptual inspiration.

Keep application policy in the sample and portable UI/runtime behavior in Lucent. Do not reach into Lucent renderer or Windows adapter internals from application code.

Run `pwsh tools/Verify-Samples.ps1` before reporting completion. Run the changed application's documented interactive smoke when behavior or presentation changes.

## Cross-repository work

Sample bugs and port progress belong in this repository. File a Lucent issue when the smallest reproduction demonstrates a framework or tooling defect; link both issues and record the exact Lucent identity used by the reproduction.
```

Add a nested `AGENTS.md` only when a port has enduring instructions that do not apply to its siblings—for example, an unusual upstream synchronization process. Put ordinary build commands and application behavior in that sample's README instead.

## Root documentation

### `README.md`

Include:

- the boundary between small framework examples and substantial samples;
- Lucent's experimental status and Windows-first current support;
- prerequisites derived from the pinned Lucent revision;
- the single repository validation command;
- a table of samples with origin, status, Lucent identity, and interactive smoke command;
- links to Lucent's architecture, language, SDK/tooling, and issue tracker.

Do not copy Lucent architecture or language documentation into this repository. Link to the pinned source revision so guidance cannot silently drift.

### `CREDITS.md`

Record conceptual references and repository-wide dependencies. For every port, link its local `UPSTREAM.md` and notices. State clearly whether code was copied, translated, or merely consulted.

### Per-sample `README.md`

Record:

- what the sample demonstrates;
- current implementation and migration status;
- exact Lucent identity;
- restore, run, NativeAOT publish, and interactive smoke commands;
- known framework gaps linked to issues;
- supported input, accessibility, scale, and theme scenarios;
- upstream attribution link when applicable.

### Per-port `UPSTREAM.md`

Before importing code or assets, record:

- upstream repository URL, commit or release, and retrieval date;
- upstream license and the location of retained notices;
- imported, translated, and omitted files or subsystems;
- asset/font/icon provenance and redistribution terms;
- intentional behavior or architecture deviations;
- a repeatable comparison or update procedure, if one is actually maintained.

If redistribution rights are unclear, stop before importing the material. Prefer a clean-room recreation from public behavior over uncertain source or asset reuse.

## Project defaults

Match the Lucent revision's supported SDK and quality baseline:

- pin the SDK in `global.json`;
- enable nullable reference types and implicit usings;
- treat warnings as errors;
- use the same recommended analyzer level and CSharpier version as Lucent;
- keep package lock files committed;
- publish runtime applications with NativeAOT for `win-x64`;
- use project references only for an intentional same-checkout development workflow; otherwise consume pinned packages.

Copy values from Lucent's checked-in configuration rather than duplicating them from this handoff. Record any justified divergence in the root README.

## Validation

`tools/Verify-Samples.ps1` should fail closed and:

1. verify the expected SDK is available;
2. restore locked dependencies;
3. check CSharpier formatting;
4. build every supported sample with warnings as errors;
5. run each sample's non-interactive contract checks;
6. publish runtime samples as `win-x64` NativeAOT;
7. verify declared publish files and required license notices.

Keep interactive visual, keyboard, accessibility, and IME checks in each sample README. Do not hide application behavior behind test-only production switches; use test-owned fixtures where external proof needs a controlled surface.

CI should run the same script used locally. Avoid a second CI-only orchestration path.

## Port workflow

For each substantial port:

1. **Qualify it.** Confirm the license permits the planned use and identify the user journeys that will exercise Lucent meaningfully.
2. **Freeze provenance.** Add `UPSTREAM.md` and notices before source or assets.
3. **Establish a baseline.** Capture a few observable upstream behaviors, screenshots, or accessibility expectations; do not attempt pixel-perfect parity by default.
4. **Port a vertical slice.** Reach one useful end-to-end workflow before broad component conversion.
5. **Separate defects.** Keep application policy in the sample. Reduce suspected framework defects and file them in Lucent with reciprocal links.
6. **Validate.** Run focused checks during development and the full repository verifier when the port milestone completes.
7. **Report honestly.** Update the sample status, known gaps, upstream identity, and Lucent identity.

The objective is to pressure-test Lucent with real applications, not to preserve every upstream framework abstraction. Prefer idiomatic Lucent code over compatibility wrappers unless the port is explicitly measuring migration compatibility.

## Issue ownership

- Track sample features, migration progress, and sample-only bugs in `lucent-samples`.
- Track reproducible framework, `.lui`, platform-adapter, or tooling defects in `RichiCoder1/lucent`.
- Link the issues in both directions and include the sample path, Lucent identity, upstream identity, and smallest reproduction.
- Record durable Lucent architecture decisions in Lucent, not in the samples repository.

## Initial sample selection

Keep `HelloLucent` first-party and intentionally small: it validates repository wiring, not framework breadth. Choose the first substantial OSS port separately using:

- permissive and unambiguous source and asset licensing;
- ordinary desktop interaction, text, scrolling, accessibility, and theming;
- enough layout variety to expose real gaps;
- bounded platform integrations;
- an active or historically stable upstream revision that can be pinned.

Do not begin multiple ports concurrently. Finish one representative vertical slice, feed concrete findings back into Lucent, and then decide whether the next port adds new evidence.
