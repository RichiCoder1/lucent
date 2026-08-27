# Milestone 2 authoring gate

Reviewer: `reviewer` using `openai-codex/gpt-5.6-terra:medium`  
Scored source: `3d0da02`  
Frozen contract: `GAUNTLET.md` SHA-256 `808268b07a0387c1ccb2eb95abc4685c3e0a2632d896279613b630c723f35a72`

| Category | Native | Avalonia | Evidence | Confound | Rationale |
| --- | ---: | ---: | --- | --- | --- |
| State / derived state | 2 | 1 | `NativeStackProbe/IssueBrowser.cs`; baseline `Program.cs` | low | Native uses signals/computed state but still reconciles the list; Avalonia clears/repopulates rows and raises notifications manually. |
| Structure | 1 | 1 | `IssueBrowser.cs`; `IssueBrowser.lui`; baseline `Program.cs` | high | Avalonia has a declarative tree but scattered imperative session plumbing; Native combines renderer, dispatch, semantics, and state in one app file. |
| Async flows | 2 | 1 | Native async computed; baseline `BeginSearch`/`CompleteSearch` | low | Native owns cancellation/latest generation; Avalonia simulates pending work and its generation counter is unused. |
| Styling | 1 | 3 | Native `BrowserRenderer`; `IssueBrowser.css` | high | Native hardcodes geometry and colors; Avalonia styling is declarative and centralized. |
| Accessibility | 1 | 1 | issue-12 semantic evidence | high | Native projects portable semantic snapshots; Avalonia evidence is an authored session array rather than mounted runtime semantics. |
| Lifecycle | 2 | 1 | Native `Dispose`; baseline session | low | Native explicitly owns/disposes graph and controls; baseline modeled async work has no cancellation or teardown. |
| Testing | 2 | 2 | issue-12 W1-W12 artifacts | none | Both have deterministic proof harnesses, but both remain application-authored rather than independent behavior suites. |
| Total integration | 1 | 1 | issue-12 sources and `change-task.json` | low | Both pass the walkthrough while retaining six imperative synchronization sites at baseline. |

Native scores higher in **3/8** categories. It is lower by **2** in styling. The assignee change is smaller for Native (+8 authored LOC versus +39), but the other two frozen conditions fail.

## Decision

**STOP before Milestone 3.** The runtime model is promising for state, async ownership, and lifecycle, but the current straight-C# rendering/structure/styling authoring model is not materially clearer under the predeclared threshold. Issues #14–#17 remain unstarted unless this decision is explicitly overridden or the experiment is reframed and re-registered.
