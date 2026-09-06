# Stateful `.lui`: ownership and authoring discussion

Status: First implementation and focused trial complete, September 6, 2026. Q4 through Q11 are accepted. The compiler/runtime foundation is implemented; package consumers must use a release containing this change. Async syntax remains future work; mounting stays on the UI owner thread.

## Goal

Make components capable of owning their interaction state and handlers in `.lui`, while keeping Light Notes drafts, focus continuity, accepted saves, and responsive behavior reliable. Improve locality without making every component a miniature application controller.

Two questions must remain distinct: where behavior is authored, and which lifetime owns its state. A smaller C# model improves separation but does not by itself make `.lui` stateful. Moving a draft into a leaf improves locality only if that leaf lives long enough.

## Starting contracts

- [ADR 0002](../adr/0002-lui-authoring-surface.md) intentionally deferred local state. Generated methods currently construct reusable recipes; each mount owns one stable retained root. Updating a signal does not rerun the component method.
- [ReactiveScope](../../src/Lucent.Core/ReactiveScope.cs) already supplies signals, derived values, effects, asynchronous values, disposable ownership, and cleanup. Stateful authoring should expose this model rather than create another reactive engine.
- [ComponentRecipe](../../src/Lucent.Core/Recipes.cs) currently allocates its root before its build callback. The compiler returns the authored root recipe directly. A setup phase that returns another component's recipe needs a deliberate runtime seam; wrapping it in an extra element can change layout, focus, and semantics.
- [ADR 0003](../adr/0003-application-services-and-shutdown.md) keeps service composition explicit and accepted writes outside view cancellation. Component cleanup is not a save/shutdown protocol.
- The glossary already distinguishes editor sessions from mounted editors, and collapsed participation from unmounting. Those distinctions remain useful with component-local state.

## Agreed authoring direction (Q4 through Q8)

Component-level declarations have reactive semantics. Inferred derived declarations are read-only; `[Once]` marks writable state initialized once per mount from an expression. C# compile-time constant expressions create writable state; other unmarked initializers are derived. The compiler must not infer intent from guessed method purity or switch categories based on dependencies observed at runtime. The user also proposed `readonly` for write-once declarations: interpret this as a read-only snapshot initialized once per mount, distinct from `[Once]` writable initial copies and continuously updated derived values. Exact spelling remains subject to the final grammar.

Use ordinary C# component-level methods and assignment (`expanded = !expanded`). Component-level methods are visible to markup. Locals and local functions inside methods or `Setup` retain ordinary C# semantics and lexical visibility. Setup does not return an export object.

Lucent-created resources register with their component owner through supported owner-aware APIs. Arbitrary external subscriptions require explicit ownership, for example `owner.Own(source.Subscribe(OnChanged))`. An ambient scope does not automatically intercept external APIs. Borrowed resources are not disposed merely because they were passed to a component. Cleanup uses the existing scope contract.

Tooling must identify writable state, derived values and once-initialized state, and report assignments to derived values with an actionable diagnostic. These semantics extend the initially deferred authoring surface of ADR 0002; they do not introduce rerendering or a second reactive engine.

### Illustrative syntax (remaining details are provisional)

```csharp
public component Disclosure(string title, [DefaultContent] ComponentContent content) {
    bool expanded = false;
    string actionLabel = expanded ? "Collapse" : "Expand";

    [Once]
    string draft = title;

    void Toggle() => expanded = !expanded;

    Setup(owner) {
        // Explicitly own external subscriptions here.
        // Locals declared here stay private to setup and its callbacks.
    }

    <Column>
        <Button onInvoke={Toggle}>{actionLabel}</Button>
        if (expanded) {
            <Column>{content}</Column>
        }
    </Column>
}
```

This illustrates authoring semantics, not a complete accessible disclosure. Automatic live binding at supported value positions and the compile-time-constant initializer boundary are agreed. Snapshot-only inputs must not silently freeze reactive expressions; event inputs remain callbacks. Setup ordering is settled below. Resource-valued snapshots use explicit owner-aware construction; broader resource sugar remains deferred. Ordinary record declarations in `.lui` are a proposed convenience for data shapes, not implicitly reactive objects.

Complex draft transitions, async coordination, and persistence can remain separately testable C# models. Stateful `.lui` should support their construction and explicit dependencies as well as small inline handlers. The exact model-construction surface remains open.

## Light Notes ownership direction

`NoteWorkspace` currently creates the capture/search/title/URL/body editor sessions, focus targets, responsive constraints, list viewport, selection/filter/route state, and application commands. Much of that is connected by real workflows, not merely accidental centralization.

| State or behavior | Recommended owner | Reason |
| --- | --- | --- |
| Store, accepted operation queue, save-before-selection, retry, close preparation | Application/workspace coordinator | Must finish or expose failure independently of a leaf's lifetime. |
| Selected note and editable title/URL/body with dirty tracking | A cohesive editor model at a stable workspace/editor owner | Draft, undo and selection continuity must survive presentation changes. Selection cannot bypass saving the previous draft. |
| Capture draft and capture action | Capture model at a stable owner, wired to workspace operations | Local authoring is attractive, but global shortcuts and accepted capture completion still need deliberate coordination. |
| Search session, filtered list and viewport | Collection model at a stable collection owner | Query and scroll continuity have a useful longer lifetime than individual list rows. |
| Responsive constraints and collection/editor route | Shell or workspace presentation owner | Several sibling components depend on them. |
| Temporary expansion, local validation presentation, transient interaction state | Component mount | Disposal/reset on removal is usually appropriate; durable edits are a different category. |
| Selection and saved data displayed by a virtualized row | Inputs from a longer-lived owner | Row recycling must not own the authoritative selected note or saved record. |

Current responsive evidence matters: `light-notes/src/LightNotes/AppView.lui` mounts all panes unconditionally; `CollectionPane.lui`, `EditorPane.lui`, and `Navigation.lui` change participation. `light-notes/tests/LightNotes.Tests/ShellPresentationTests.cs` covers retained session and element identity, draft, caret, and scroll across wide/medium/compact transitions. Lucent's [participation tests](../../tests/Lucent.Core.Tests/ParticipationContracts.cs) establish that collapse retains ownership but clears focus. Replacing collapse with a conditional branch would change the lifetime contract.

`NoteWorkspace.BackToCollection` does not save. Search may hide the selected record without discarding its draft. Capture retry retains an attempt identity and clears input only after a durable write. These are explicit migration constraints, not reasons to keep every field in one class.

Narrow inputs and explicit actions should replace passing the entire workspace where practical. Share state intentionally; do not maintain a parent value and a child copy with effects that try to synchronize both directions.

## Lifecycle contracts and remaining details

1. **Isolation:** repeated mounts of one recipe cannot share local signals by accident. Ordinary external arguments can still intentionally share a session.
2. **Identity:** ordinary updates and same-key payload replacement preserve mounted local state; removal ends it. Virtualization may remove rows, so row-local state is not durable storage.
3. **Visibility:** collapse keeps ownership; removing a branch disposes it. Returning to a removed branch starts fresh unless its state was explicitly owned above it. This default is agreed in Q8. Explicit retention mechanisms, including virtualization use cases, may follow; retention is not the default.
4. **Construction failure:** setup resources and partially mounted descendants unwind together, including failures before a root exists. Arbitrary external side effects cannot be rolled back; setup should allocate and wire, not perform irreversible work.
5. **Root transparency:** setup must not insert an observable wrapper element or compromise naming, theme propagation, layout, focus, or accessibility. Nested authored components need clearly nested cleanup even if they forward one visual root.
6. **Reactive boundaries:** setup is not an effect. Derived state should calculate values; effects synchronize with external systems. A plain `item()` snapshot must not replace a live current-item reader in a retained row.
7. **Async ownership:** obsolete view reads can be cancelled and stale results suppressed. An accepted save continues under its application owner. Awaiting during setup, async-void handlers, cleanup errors, and UI-thread continuation rules need explicit support or clear rejection.
8. **Borrowed versus owned state:** passing in an existing session does not transfer disposal ownership. Locally created state belongs to its declared owner.
9. **Escaping closures:** content recipes and handlers can capture local state. Their consumers must not outlive that state owner; projected content and callbacks retained by an external service need an explicit ownership/unsubscription contract.
10. **Focus and commands:** global shortcuts must reach a stable command/focus capability without querying a mounted child object. Cleanup must remove subscriptions and bindings with their owner.

## Alternatives and trade-offs

| Approach | Strength | Main cost | Position |
| --- | --- | --- | --- |
| Smaller adjacent C# models only | Small framework change; straightforward testing | `.lui` remains visual and ownership stays awkward to author | Useful migration work, insufficient as the whole answer. |
| Per-mount C# setup with explicit reactive APIs only | Reuses Lucent semantics; local handlers and typed models compose naturally | More signal mechanics in simple component code | Considered; user prefers reactive component declarations plus C# setup. |
| Reactive field-shaped declarations plus C# setup/methods | Concise state and derived values; familiar handlers and escape hatch | Inference, source mapping and editor explanations must be precise | Agreed direction in Q4/Q5; detailed rules remain open. |
| Stateful component classes with lifecycle overrides | Familiar instance fields and methods | Changes recipe model, invites mounted handles and a second lifecycle abstraction | Not recommended for this need. |

Solid is a useful existing architectural reference: component initialization executes once, while reactive work has separate tracking and owner cleanup. This supports the proposed direction, but does not answer Lucent's .NET ownership, retained-root, or persistence contracts. See [component basics](https://docs.solidjs.com/concepts/components/basics), [reactivity](https://docs.solidjs.com/concepts/intro-to-reactivity), and [cleanup](https://docs.solidjs.com/reference/lifecycle/on-cleanup), consulted September 6, 2026 through Context7. Solid is already recorded in [CREDITS](../../CREDITS.md); no new package or copied source is proposed.

## Candidate delivery sequence

Subject to the interview, first specify and prove a transparent mount/setup ownership seam in typed C#. Then add `.lui` setup lowering with matching build diagnostics, completion, hover, navigation, formatting, semantic highlighting and incremental editor behavior. Local functions and captures must map back to authored code, including incomplete edits. Recent editor latency fixes make preservation of projection reuse and precise invalidation part of the scope.

Use one small interactive component plus a Light Notes capture/collection ownership slice to demonstrate the model. Follow with the coordinated editor model only after draft and save lifetimes are explicit. Avoid refactoring all of `NoteWorkspace` in one change.

Focused evidence should cover two mounts of the same recipe, cleanup after setup/root failure, nested setup without extra elements, same-key updates and branch removal, borrowed sessions, responsive draft continuity, and source-mapped locals/handlers. Reuse existing Core/compiler/LSP/app suites; no new milestone gates or broad desktop walkthroughs are implied.

## Stateful and stateless vocabulary seam

The user requested an explicit distinction and suggested future expression-bodied recipe composition. Agreed vocabulary (Q9): a **stateful component** owns local writable application/interaction state for each mount; a **stateless component** owns no such local writable state. Stateless does not mean nonreactive: it may observe external state, maintain framework binding machinery, and compose stateful descendants. Both produce component recipes; the recipe value itself is not a mounted state container. These definitions describe authored ownership, not a separate recipe type.

Future illustrative syntax:

```csharp
ComponentRecipe Example() => state.IsActive ? <SomeComponent /> : <OtherComponent />;
```

There are two distinct possible semantics: ordinary call-time recipe selection, or a live structural switch that replaces the mounted branch. Recommend ordinary C# call-time semantics for an ordinary helper, and an explicit reactive composition construct for the latter. Live root switching has identity/ownership consequences and is deferred with this syntax; current ADR 0002 still requires a stable retained root. Do not silently reinterpret arbitrary C# helper methods as reactive factories.

## Accepted decisions and follow-up

Agreed: Q4 read-only inferred derived declarations and `[Once]` writable initialization; Q5 component method visibility, no setup exports, owner-aware Lucent resources and explicit external subscription ownership; Q6 compile-time constant classification; Q7 automatic live markup in supported value positions; Q8 removal resets local state, with explicit retention a possible follow-up.

User addition: `readonly` write-once declarations, interpreted as read-only per-mount snapshots. Record declarations retain ordinary C# data semantics, subject to final surface design.

Further accepted decisions:

- Q9 accepted: stateful/stateless vocabulary, both on the same recipe contract. Expression-bodied markup and live root switching remain future work.
- Q10 accepted: synchronous per-mount initialization, declaration-order writable/readonly initialization, lazy derived evaluation with cycle diagnostics, then Setup before authored children mount. Methods are available throughout. No top-level await; asynchronous work uses owned operations/commands. Setup is not an after-layout/focus callback.
- Q11 accepted; implementation authorized: both a small stateful reusable component and a bounded collection extraction in Light Notes; coordinated editor drafts follow after those prove the contracts. The trial moves viewport ownership, derived presentation, and Clear Search into CollectionPane. Shell route/constraints stay coordinated with save-success and focus transitions.

Follow-up design work: async/error sugar, record declaration details, broader resource/model ergonomics, and shell command/focus extraction. The first slice uses readonly snapshots for owner-aware resources and existing Func<T> inputs for live values. Agreed durable decisions are recorded in ADR 0005.

## Explicit async direction

Q10 adds Solid as inspiration for explicit async state with concise authoring sugar. Keep component setup synchronous. Solid's [createResource](https://docs.solidjs.com/reference/basic-reactivity/create-resource) separates reactive sources and asynchronous fetchers and exposes loading/error/latest state (documentation consulted September 6, 2026 through Context7). Lucent already provides `AsyncValue<T>` with pending/error/value state, cancellation and generation handling. Reuse that runtime contract; do not infer asynchronous components from task-valued expressions.

Overlapping asynchronous loads and parallel/speculative component construction are different capabilities. Current mounting and reactive mutation belong to the UI owner thread. The user confirmed overlapping async loads with UI-thread mounting; parallel/speculative construction is not part of this work. Automatic resource declaration syntax, refresh ergonomics and async boundaries remain follow-up design work.

## First-slice result

Implemented per-mount declarations, ordinary methods, synchronous Setup, source mapping/editor information, and automatic live readers at compatible value inputs. Button now supports a live label that updates both rendered text and its accessible name. The runtime resolves deferred recipes without an extra visual root and rolls back owned resources on construction failure.

An isolated Light Notes trial moves collection viewport ownership, derived presentation, and Clear Search into `.lui`. A fresh offline restore, warning-clean build, and all 19 managed app/storage tests pass. The validated extraction is being adopted in [Light Notes](https://github.com/RichiCoder1/light-notes) through published package dependencies. Drafts, routing, persistence, and focus coordination remain workspace-owned.

Focused verification: Core 129/129; compiler 18/18; generator 13/13; language server 11/11 (ten in the full run, the environment-blocked real-project test passed after adding repository NuGet configuration); VS Code extension 11/11. The packed SDK consumer passed NativeAOT execution, per-mount state/cleanup, content forwarding, retained payloads, and runtime inventory checks. The later readonly-input diagnostic correction is covered by compiler and source-wired app tests and does not change emitted runtime code. Authored C# formatting and diff whitespace checks pass.

An additional Debug Core run overflowed the stack in the 200-level deep-layout stress test; the same test and full suite passed in the repository's normal Release configuration. This limitation remains open. No foreground desktop interaction or fresh manual appearance review was performed for this slice.

Recommended next steps:

1. Review the authored CollectionPane trial and the reusable state fixture before expanding the language. In particular, check whether the constant/derived/readonly distinction is clear during ordinary edits.
2. Keep the compiler, SDK, runtime, and installed language server aligned when adopting new syntax.
3. Add a supported source-consumer override to replace the trial's verbose project wiring. Keep package-only verification as a separate dependency-consumption check.
4. Design explicit async-resource sugar over AsyncValue and deliberate shell command/focus ownership. Keep accepted persistence work outside component cancellation.
5. Revisit record declarations and expression-bodied recipe composition after this smaller surface has been used. Parallel/speculative mounting remains out of scope.
