# Component preview: authoring and edit-loop design

October 4, 2026. Proposed extension to the current implementation. The user has
approved the product direction: editor preview primarily previews components;
data and state variants extend that experience. This document supersedes the
earlier catalog-first discovery and mandatory development-project onboarding UX.
The [interaction guide](implementation-guide.md) still governs presentation,
input ownership, frame freshness, cleanup and failure behavior.

## Primary journey

Open `TaskCard.lui` → choose **Open Component Preview** from the editor title or
command palette → see `TaskCard` beside its source → edit → see the latest valid
result. No scenario ID, handwritten registry, preview executable, component-library
extraction or application startup should be required for an ordinary component.

The panel title/context is the component and its source. Its toolbar starts with
viewport, zoom and appearance. Add a **Variant** picker only when that component
has more than one available preview. A single authored variant needs an inert label,
not a chooser. “Scenario” remains a useful internal protocol/test term.

**Follow editor** is the default. **Pin preview** freezes the component target,
not its source snapshot: pinned components still update when their dependencies
change. Switching to another eligible `.lui` changes the unpinned target. Clicking
the panel, Output, a terminal or a non-component file does not clear it. A preview
companion file resolves to its explicitly named component. A type-only `.lui`
retains the last target with a quiet explanation; it does not choose an unrelated
component. With no previous target, show “Open a .lui component to preview.”

Resolve project/target framework from the language tooling's evaluated document
context. In linked files or multiple valid project/TFM contexts, prompt once using
native Quick Pick and remember the choice for that document/project context. Do
not guess a different app, select the first enumeration result or parse project
XML in the webview. Pin includes this project/TFM context.

Opening the preview explicitly starts work in an eligible trusted workspace.
Merely opening a source file when no preview is open starts no worker. An open,
following, running panel may track the editor without repeated confirmation.
Stop always remains explicit: changing files, variants, settings or pins while
stopped changes only the staged target. Start renders it with fresh state.

## Automatic default preview

For a component callable with no required arguments, the compiler/tooling supplies
a typed default recipe and stable source identity. Use the existing declared
defaults, empty default content where the existing language defines it, and the
preview host's ordinary theme, viewport and composition purpose.

```lui
component Greeting(string name = "World") {
    <Text>Hello, {name}</Text>
}
```

This needs no preview declaration. This is compiler-supported typed activation,
not assembly scanning or runtime constructor guessing. Do not synthesize null,
zero, empty strings, fake domain objects or no-op required callbacks to satisfy
required inputs. A required nullable argument is still required without a default.
Honor authored parameter accessibility, language mode and factory naming.

Declared required services/context that the preview cannot supply produce
**Configure preview** with their names. A missing nested requirement discovered
during mounting produces the same actionable state with its component origin;
static analysis must not claim to prove every transitive runtime dependency.
Do not recover by executing the application's service/bootstrap code.

`Design.IsDesignMode` is already a contextual read during preview composition.
It can choose design values in a component, but does not bypass parameter binding,
invent injected services, suppress exceptions or replace explicit input values.
Preview declarations do not need to set it themselves.

## Optional .lui declarations

Recommended new syntax, **not implemented syntax**:

```lui
preview Default for UserCard {
    readonly User user = new("Alex Example", "Designer");

    <UserCard user={user} />
}

preview LongName for UserCard {
    readonly User user = new("Alexandra Example-Simpson", "Design engineer");

    <UserCard user={user} />
}
```

`User` and `UserCard` here stand for the application's existing model/component.
These blocks can follow the component in `UserCard.lui`, or live in the optional
`UserCard.preview.lui` companion. The companion is a convention, not filename
inference: `for UserCard` binds the actual component symbol and is rename-aware.
The first delivery limits a preview-only companion to one target component.
No companion is required for the automatic path.

Each body uses normal `.lui` declarations and a single recipe, including ordinary
composition wrappers. It may contain inferred reactive state, `readonly` values,
methods, `Setup`, owned resources and ordinary typed context composition under
the same existing rules as components. It does not grant access to the target's
private mounted state. Represent loading/empty/error through its public props or
model; use interactions to reach internal states. Do not add a reflection-based
state/property injector or a separate expression language.

One component declaration per file remains the rule. Preview blocks are separate
development-only roots, not additional public components. Lower each to a hidden
owned recipe with independent state on each mount. Normal production generation
must exclude their bodies and development-only dependencies. No public preview
factory or `Lucent.Preview` reference is required in a shipping assembly.

Use the existing fixture setup/cleanup and explicit service-binding APIs for advanced
providers. The `.lui` wrapper is sufficient for ordinary props and typed context;
async service setup can use a development-only C# provider connected to the same
typed preview entry. Do not invent new `inject = value` syntax or imply that a
context provider automatically satisfies a service injection. Use existing binding
ownership; do not automatically merge independent service bindings. This escape
hatch must not make simple data previews require C#.

## Variant selection and lifetime

- Variant identity is evaluated project/TFM + component symbol + explicit variant
  identifier. File line numbers, display labels and array positions are not IDs.
- An authored `Default` replaces the automatic Default. Duplicate explicit IDs for
  the same component are diagnostics, never last-wins behavior.
- Initial choice: previous valid user choice, then Default, then the only variant.
  Multiple variants without either selection or Default show a component-scoped
  picker. Do not execute the first variant merely because it sorts first.
- Automatic Default can coexist with authored non-default variants when eligible.
  Never hide a required-input failure behind an invalid automatic variant.
- Removing the selected variant invalidates its frame immediately. Return to a
  valid Default when running, or ask for a choice if none exists; remain stopped
  when stopped. Say why the selected variant changed.
- Choosing a variant supplies its declared data and starting state, resets mounted
  state, and uses its presentation defaults. Retain validated per-component/variant
  presentation overrides within the open panel. Source edits preserve selection,
  zoom and overrides; they do not preserve runtime state yet.
- Unapplied settings belong to the current target/variant. A target change cancels
  that draft and announces it; it cannot leak into another component. Status-only
  updates preserve draft text and focus as in the interaction guide.

## Configure preview without an onboarding detour

Required input example: “UserCard needs `user: User`.” Primary action **Add preview
data** creates an editable declaration via the editor's native code-action/edit
flow, after the user invokes that action. Default destination is the companion;
also support adding the block in the component file. Show the exact proposed edit
through the normal editor experience and respect undo. Do not silently save files,
change component defaults, relax nullability or make required services optional.

The generated starter contains an explicit TODO/diagnostic for each missing value;
do not make placeholders look like a runnable successful preview. Reuse existing
symbols and signature completion. When those edits become valid, preview updates
without restarting the command. **Open preview definition** revisits authored data.
The HTML mockup demonstrates the missing-data screen and starter text only; it
does not implement a real editor edit.

Missing SDK/tools, unsupported target or trust restriction remain distinct from
missing component data. Do not label them “No scenarios.” For advanced callers an
explicit existing development catalog remains an option through host commands;
it is not the primary editor entry point or required setup for simple components.

## Fast loop with truthful execution

1. Resolve component identity from the evaluated tooling context without executing
   arbitrary fixture code to decide which component the file contains.
2. Invalidate currentness immediately on a relevant source/dependency edit. Keep
   same-component last-good pixels readable with their original revision and no
   input. On a different component, show its loading surface; never relabel the
   previous component's pixels as the new one.
3. Coalesce typing with a short quiet period (initial target 200ms, internal policy,
   not another user setting). Supersede pending generations and admit only the
   newest compatible snapshot. Do not wait for Save when the exact supported
   unsaved-source path is available. During rollout, label the fallback **On save**.
4. Compile the consistent dependency snapshot, then mount the new recipe. Reuse
   evaluated context and verified artifacts only when their identity is unchanged;
   do not skip freshness, original-path/configuration or cleanup checks for speed.
5. Accept complete current pixels atomically without stealing focus. Subsequent
   interactions in a live preview render without compiling source. Fit/zoom never
   builds. Source edits and explicit Reset may rebuild/remount and reset state;
   state-preserving Hot Reload is separate work.

Track user desired-running intent independently of phase and target. Switching
targets does not clear cleanup-blocked state or allow two live owners. Freeze the
pending target while cleanup completes; unconfirmed cleanup remains blocked.
Hidden running preview resumes at the latest followed eligible target after show;
hidden stopped preview stays stopped. Closing ends ownership.

Do not flicker through full-canvas spinners on every keystroke, steal editor focus,
save buffers automatically or announce every rendered frame. Keep a steady canvas
and concise Updating/error status. Editing incomplete syntax retains last-good
same-component pixels; diagnostics resolve naturally when the snapshot is valid.

Measure edit→invalidation, snapshot/build, first current frame, superseded work and
input→presented frame separately. Use representative small and multi-project cases,
cold and warm results, and p50/p95. The 200ms debounce is a design starting point,
not a measured end-to-end latency promise. No target is allowed to weaken fidelity.

## Compiler/build and host boundaries

Prefer a generated development host/catalog over a user-authored executable. It
uses the evaluated project configuration and existing supervised worker protocol.
Typed generated adapters call the actual recipe factories; the extension handles
IDs, descriptors and capability-checked commands, not arbitrary types or paths.

The adapter strategy must work for normal application projects and component
libraries without splitting the user's app. Prove that referencing/loading the
evaluated application output does not run its entry point, and preserve its assets,
generated code and dependency identity. Do not change OutputType, source locations
or symbols as an unverified workaround. If a target is not supported initially,
surface that precise limitation rather than call the authoring path complete.

Keep a preview compilation profile/output isolated from production. Normal builds
parse preview syntax for structural validity but exclude body binding/emission;
preview builds fully type-check it with mapped diagnostics. A companion's imports,
models and development dependencies stay in that profile. Co-located bodies may
use normal application imports; development-only namespaces must not be added as
ordinary application imports. Formatter/parser/editor source maps cover both forms.
Parse preview blocks as syntax, never remove arbitrary text with regex.

The stock compiler staging path has not established original-path/configuration
fidelity for unsaved snapshots. Use the verified compiler/tooling seam, and prove
source-generator ordering, additional inputs, generated declarations and exact
effective options before enabling this profile. Do not assume one source generator
can consume another generator's output in the same pass. Keep supported toolchain
and legacy/named component mode differences explicit in generated activation.

### Source-grounded implementation seams

Sol 6.1 xhigh checked these seams read-only at main `132fc85a` for this refinement:

| Seam | Implementation guidance |
| --- | --- |
| [Typed factories](../../../src/Lucent.Lui.Compiler/LuiCompiler.cs#L83) and [requirements](../../../src/Lucent.Lui.Compiler/LuiCompiler.Requirements.cs#L61) | Extend preparation metadata with identities, actual defaults and unsatisfied requirements. Internal components need a development-only public bridge returning Core recipe types. |
| [Preparation](../../../src/Lucent.Lui.Preparation/LuiPreparationEngine.cs#L103) and [prepared emitter](../../../src/Lucent.Lui.Preparation/LuiPreparedEmitterCompiler.cs#L166) | Reuse the existing staged pipeline: foreign generator results feed binding, with declarations and implementations emitted in their supported phases. Generate preview metadata/adapters here. |
| [Authored source projection](../../../src/Lucent.Lui.Compiler/LuiAuthoredSourceProjection.cs#L45) | Exclude production-ineligible preview bodies/imports/helpers before projection, foreign-generator inputs and binding. Skipping final emission alone is insufficient. Preserve authored offsets/source maps. |
| [Parser](../../../src/Lucent.Lui.Compiler/LuiParser.cs#L131) | Preview declarations are a new grammar construct. Synthetic wrapper roots do not relax the one-authored-component rule. |
| [Catalog](../../../src/Lucent.Preview/PreviewCatalog.cs#L12) and [setup context](../../../src/Lucent.Preview/PreviewSetupContext.cs#L33) | Keep typed setup, context/service bindings, fixture identity and existing teardown contracts. No reflection activation path is needed. |
| [Build engine](../../../src/Lucent.Preview.Build/BuildEngine.cs#L38) and [input isolation](../../../src/Lucent.Preview.Build/EvaluatedInputs.cs#L160) | Add a generated-host mode to the existing explicit-executable gate. Host source and output directories must be siblings: current isolation rejects outputs beneath any project directory. |

These are design seams, not executable proof. Stage typed metadata and activation,
then generated-host/internal-access proof, then preview exclusion/authoring. The
live host's independent implementation does not by itself establish these paths.

## Implementation increments

| Increment | Required observable result |
| --- | --- |
| 1. File association / follow / pin | Existing explicit entries can be filtered by verified component origins; no project-wide picker on the primary path. Saved-source image delivery remains accurately labeled. Missing automatic activation says unsupported/configure, never pretends to work. |
| 2. Typed default activation | Eligible components in a normal app/library preview with one command, zero handwritten registration, no production startup and no preview shipping dependency. |
| 3. Authored data and variants | Proposed `.lui` blocks/companion, native edit action, preview-only compile profile, mapped diagnostics and deterministic selection. Missing required inputs are actionable. |
| 4. Tight live loop | Consistent unsaved snapshot fidelity, bounded supersession, live input and measured warm path, each enabled only after its own proof. This may proceed in parallel with 2–3. |
| 5. Inspection | The separate #243 capability; no prerequisite for the above. |

This is a new authoring/compiler/tooling slice alongside existing #232/#233, not a
claim that a toolbar change finishes it. The previously prepared bounded panel
refinements remain useful and should not be discarded.

**Cheap initial priorities:** add the editor-title entry point and verified
active-document association, follow/pin, truthful source coverage, and filtering
existing entries to the current component. Those changes can use the existing
saved-image path and need not wait for the new declaration grammar. They are a
useful partial delivery; zero-registration activation still requires increment 2.
The owner accepted saved-source live/file preview first on October 4. #330 keeps
unsaved compiler fidelity open without blocking #232/#233. The eventual unsaved
fidelity gate is unchanged; the initial edit loop is explicitly **On save**.

## Additional acceptance scenarios

| ID | Action | Required result |
| --- | --- | --- |
| CP-01 | Open preview for a defaultable component in an ordinary app | One command, correct component, no registry/project extraction/production startup. |
| CP-02 | Switch TaskCard→Badge while following; then pin and switch back | Target follows initially; pinned Badge remains, but its own source/dependency changes still update it. |
| CP-03 | Focus Output/README/type-only `.lui` | No unrelated component or empty reset; retain prior target with an accurate follow indication. |
| CP-04 | Open component requiring a model or service | Name the missing requirement; Add preview data leads to a reversible authored edit, not fabricated runtime values. |
| CP-05 | Add Default and Empty variants | Picker contains only that component's entries; default identity and remembered selection are deterministic. |
| CP-06 | Stop, change file/data/settings, hide/show | No automatic execution; Start uses the staged target/variant. |
| CP-07 | Rapid edits and target changes, including a failing generation | No obsolete frame admission or wrong-component caption; no stale input; editor keeps focus. |
| CP-08 | Ordinary build/publish with development-only preview types present | No preview bodies/dependencies leak into shipping output; preview build separately diagnoses invalid preview code. |
| CP-09 | Linked file/multiple TFMs; rename component/provider | Explicit context resolution and symbol-aware target mapping; no heuristic filename activation. |
| CP-10 | Preview body has state, cleanup and context | Existing per-mount semantics hold; remount/reset cleans up; two components never share hidden preview state. |
| CP-11 | Remove selected variant or target during work | Invalidate immediately; explicit fallback/selection, bounded cleanup and truthful target identity. |
| CP-12 | Source-generator-heavy and unsaved multi-project sample | Exact build/source mapping and declared dependency semantics match the supported compiler path before automatic unsaved coverage is claimed. |

These checks extend UX-01–UX-14 from the interaction guide. Mockup interactions
illustrate the flow; they do not prove the compiler or native host contracts.
