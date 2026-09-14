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

**Mount requirement**:
A component's declared exact-type borrowed value, resolved at its mount position before its own state initializes and cached for that mount.
_Avoid_: Ambient lookup, injected property, reactive lookup

**Context provider**:
A transparent recipe contribution that supplies one stable exact-type value to its enclosed content without owning or disposing that value.
_Avoid_: Visual wrapper, service scope, global context

**Service binding**:
A lifecycle-owned bridge through which declared component requirements borrow application services. It controls admission and revocation without taking container ownership.
_Avoid_: Component container, route scope, service locator

**Route location**:
A bounded canonical root-relative path and ordered query data. Parsing and formatting preserve the same segment boundaries; displaying it is an explicit operation because it may contain user data.
_Avoid_: Arbitrary URI, route definition, diagnostic identifier

**Route table**:
The validated immutable authority for matching route locations against definitions and rejecting ambiguous patterns before navigation begins.
_Avoid_: Routing convention, generated matcher, component registry

**Style**:
An immutable set of typed assignments that determines an element's arrangement and visual representation. It does not own interaction, semantics, lifecycle, or content structure.
_Avoid_: CSS rule, property bag, modifier

**Target value**:
The authoritative winner of a typed property after ordinary style and control resolution. It expresses current intent independently of any visual transition.
_Avoid_: Animated value, sampled target

**Presented value**:
The value used to draw a property at a particular frame time. It can temporarily differ from its target without delaying interaction or changing application state.
_Avoid_: Override, logical value

**Motion track**:
One mounted element property's finite progression from a presented value toward its target. It belongs to that mount rather than to a reusable style or recipe.
_Avoid_: Animation timer, style state

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
A reusable declaration that creates and owns one stable retained root for each mount. Both authoring surfaces share this model; a recipe is distinct from its mounted state.
_Avoid_: Template, widget class, render function

**Authoring capability**:
A declared ability to style a recipe's target or supply its accessible metadata. It does not transfer ownership of interaction, state, or content to the authoring chain.
_Avoid_: Arbitrary restyling, control subclass, behavior capability

**Component context**:
Access to state and lifetime operations owned by one component mount. It belongs to that mount rather than to an ambient application environment.
_Avoid_: Current scope, service locator, rendering context

**Author accessibility metadata**:
An author's accessible name and description contributions, combined with the control's current semantics. The control continues to own its role, operations and applied state.
_Avoid_: Semantic replacement, accessibility behavior, role override

**Frozen drawing**:
An immutable recording of portable painting operations. Replaying it consumes captured values rather than reading live application state.
_Avoid_: Render callback, canvas component, chart engine

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
