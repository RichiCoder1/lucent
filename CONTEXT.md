# Lucent

Lucent defines the author-facing concepts of a native desktop UI stack for .NET.

## Language

**Reference application**:
The first complete application built with Lucent to discover and validate the framework surface; it is expected to mature into a maintained example.
_Avoid_: Spike app, demo harness, test app

**Framework surface**:
The coherent set of Lucent capabilities available to application authors, independent of any one authoring syntax.
_Avoid_: Library API, engine API

**Authoring surface**:
A supported way to declare Lucent UI. `.lui` is the primary application authoring surface over the same framework model used by typed C# composition.
_Avoid_: Frontend, binding syntax, wrapper

**Diagnostic dump**:
A complete deterministic snapshot of relevant Lucent state used to explain behavior and make debugging decisions. It is distinct from sampled operational telemetry.
_Avoid_: Trace, log bundle, telemetry export

**Style**:
An immutable set of typed assignments that determines an element's arrangement and visual representation. It does not own interaction, semantics, lifecycle, or content structure.
_Avoid_: CSS rule, property bag, modifier

**Layout container**:
A retained owner of child content whose arrangement is selected through style. Changing its arrangement does not change the ownership of those children.
_Avoid_: Responsive branch, layout instance

**Layout strategy**:
A measurement and arrangement policy that consumes available constraints and child layout contributions. It does not own child content or interactive behavior.
_Avoid_: Container, layout engine

**Window breakpoint**:
A named minimum logical window width used to select responsive style assignments. It is independent of device scale and of the space assigned to any nested container.
_Avoid_: Container query, responsive route

**Child layout contribution**:
Typed style information that a parent's layout strategy uses to size or place a child, such as a grid position or a growth weight.
_Avoid_: Parent mutation, attached-property bag

**Behavior**:
A reusable interaction contract that adds input, focus, semantics, and lifecycle ownership to an element without changing its content structure.
_Avoid_: Control subclass, event bundle, style

**Composition**:
The authored structure and ownership of elements and supplied content. Composition is distinct from both visual styling and interactive behavior.
_Avoid_: Template expansion, render tree, layout

**Component recipe**:
A reusable typed C# capability returned by a `[LucentComponent]` method. Each mount creates and owns exactly one stable retained root; the value is not a runtime template instance, virtual node, component object, or rerender function. `.lui` components lower to the same `ComponentRecipe` interface used by handwritten C#.
_Avoid_: Template, widget class, render function

**Stateful component**:
A component that owns local writable application or interaction state for each mount. The reusable recipe is distinct from that mounted state.
_Avoid_: Stateful recipe, rerender function

**Stateless component**:
A component that owns no local writable application or interaction state. It may observe changing inputs and compose stateful descendants.
_Avoid_: Static component, nonreactive component

**Content recipe**:
A typed capability that contributes zero or more retained components or structural regions below an existing component root. Immutable ordered `ComponentContent` groups content recipes transactionally; an ordinary component recipe converts safely to one content recipe.
_Avoid_: Fragment element, child template, virtual children

**Application session**:
The one-run owner of an application's startup, visible composition, close negotiation and final cleanup.
_Avoid_: Window scope, global service locator

**Close preparation**:
The retryable decision to stop accepting work and finish accepted operations before terminal application shutdown. A declined or failed preparation leaves the application available for recovery.
_Avoid_: Force close, service stop

**Editor session**:
The document draft and editing continuity owned independently of the view currently presenting it.
_Avoid_: Mounted editor, text-field instance

**Participation**:
Whether a retained subtree is visible, reserves space while hidden, or is collapsed without ending its ownership.
_Avoid_: Routing visibility, unmounting

**Default content**:
The single explicitly marked component parameter that receives its unnamed element body. Its type determines whether the body supplies one scalar value or an ordered group of content recipes; its name does not determine its role.
_Avoid_: Magic content parameter, implicit slot

**Current item**:
The latest payload associated with a mounted retained entry, available through a read-only capability. Replacing the payload preserves the entry's identity and local state; removing the entry ends that capability's lifetime.
_Avoid_: Captured item, mutable row model, replacement component

**Brush**:
An immutable box-local visual value. The initial closed set is solid color and bounded linear gradient. A brush does not imply image layers, borders, clipping, opacity, or layout.
_Avoid_: CSS background, renderer paint object, `IBrush`

**Asset identity**:
The stable domain and logical path identifying declared content, independent of its physical source or installed location. A content revision is distinct from that identity.
_Avoid_: File path, resource URL, cache key

**Asset reference**:
An immutable description of a declared content revision and the capability to obtain its bytes. Holding the reference does not load or prepare that content.
_Avoid_: Loaded image, open stream, native resource

**Image source**:
A reusable description of image content shared by image and icon presentation. The description is distinct from a mounted view or a prepared rendition.
_Avoid_: Icon source, image component, decoded bitmap

**Image rendition**:
A prepared representation of one content revision for particular rendering requirements. Different renditions may share the same asset identity and revision.
_Avoid_: New image identity, resized component

**Project context**:
The authoritative evaluated C# project inputs used consistently by build and editor tooling: compilation, references, options, global usings, and `.lui` documents.
_Avoid_: Workspace approximation, project manifest

**Source map**:
The deterministic bidirectional relationship between `.lui` spans and generated C# constructs used by build and editor tooling. It has no runtime role.
_Avoid_: Debug table, runtime metadata
