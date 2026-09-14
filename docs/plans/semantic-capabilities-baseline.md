# Semantic capabilities S1 baseline

Baseline: `910b2dc08588ccebbab3a0c039b84ba1d4a743e1` (`Record successful authoring desktop closeout [skip ci]`). Captured 2026-09-13 on MORO-DESKTOP, Windows 10.0.26200, x64, .NET 10.0.12, Release. This is characterization evidence, not a performance gate.

## Reproduction

```powershell
git rev-parse HEAD
dotnet build tests/Lucent.Performance.Verifier/Lucent.Performance.Verifier.csproj -c Release --no-restore
dotnet run --project tests/Lucent.Performance.Verifier/Lucent.Performance.Verifier.csproj -c Release --no-build -- --scene-projection
```

The build exited 0 with zero warnings and zero errors. The probe exited 0. Raw semantic results and the command record are in `artifacts/semantic-capabilities/baseline-910b2dc.json` and `baseline-910b2dc-commands.txt`.

## Measurements

Each scenario uses 10 warmups and 40 measured mutations. Update includes the mutation and reactive graph drain. Projection is `Composition.SemanticSnapshot()`. Allocations cover both phases on the current thread. `Snapshot builds` counts calls to `SemanticSnapshot`; `generation changes` compares every returned element identity with the prior build. The existing Windows `SemanticSnapshotBuilds` counter counts adapter refresh builds, while this headless probe records equivalent Core build calls without creating a native host.

| Scenario | Nodes / realized | Ancestors | Update p50 / p95 ms | Projection p50 / p95 ms | Allocated p50 / p95 B | Builds | Generation changes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Simple Button, CheckBox, Slider, TextField declarations | 5 / 0 | 0 | 0.0051 / 0.0078 | 0.0034 / 0.0058 | 7,688 / 7,688 | 40 | 160 (4/build) |
| Name and description change | 2 / 0 | 0 | 0.0010 / 0.0027 | 0.0011 / 0.0032 | 2,240 / 2,240 | 40 | 40 (1/build) |
| Text caret change over stable `ab` | 2 / 0 | 0 | 0.0009 / 0.0029 | 0.0011 / 0.0029 | 2,216 / 2,216 | 40 | 40 (1/build) |
| Metadata change below 128 structural ancestors | 2 / 0 | 128 | 0.0010 / 0.0033 | 0.0692 / 0.0832 | 32,960 / 32,960 | 40 | 40 (1/build) |
| Issue Browser virtualized list, unrelated input change | 54 / 12 | mixed | 0.0002 / 0.0003 | 0.1017 / 0.1037 | 41,128 / 41,128 | 40 | 0 |

The 128-level case quantifies the recursive structural walk and allocation but cannot isolate `InputAvailable` calls without adding a Core counter. `ReconcileSemanticState` calls recursive `InputAvailable` once for each semantic element; the deep case deliberately places one semantic leaf below 128 nonsemantic ancestors. The virtualized case is useful evidence for equality decisions: forty snapshots rebuilt the same 54-node semantic tree and allocated 41,128 bytes per update-plus-projection sample while changing no semantic generation.

## S4 comparison

The same command was rerun after the capability and snapshot migration in the working tree over `910b2dc`. An interim run identified a localized 200-byte caret regression consistent with rebuilding a structural-root declaration on every projection. The immutable structural payload was then retained per element, with supplemental-description changes refreshing that payload without suppressing snapshots or generations. The conclusive Release build exited 0 with zero warnings and zero errors, and the probe exited 0. Raw evidence is preserved in `artifacts/semantic-capabilities/baseline-s4-working-tree.json` for the interim run and `baseline-s4-final.json` for the conclusive run. Timings at this scale are sensitive to machine noise, so the allocation and generation columns carry more weight than sub-microsecond timing differences.

| Scenario | Allocation baseline → S4 | Allocation change | Projection p50 baseline → S4 | Generation changes baseline → S4 |
| --- | ---: | ---: | ---: | ---: |
| Simple controls | 7,688 → 7,408 B | -280 B (-3.6%) | 0.0034 → 0.0034 ms | 160 → 160 |
| Metadata change | 2,240 → 1,928 B | -312 B (-13.9%) | 0.0011 → 0.0011 ms | 40 → 40 |
| Text caret change | 2,216 → 2,120 B | -96 B (-4.3%) | 0.0011 → 0.0011 ms | 40 → 40 |
| 128 ancestors | 32,960 → 32,648 B | -312 B (-0.9%) | 0.0692 → 0.0679 ms | 40 → 40 |
| Virtualized list input change | 41,128 → 34,552 B | -6,576 B (-16.0%) | 0.1017 → 0.0964 ms | 0 → 0 |

The shared declaration payload and retained structural payload reduce allocation in all five focused scenarios. The conclusive run removes the interim caret regression without suppressing snapshots or generations. Projection medians are flat or modestly lower, and no scenario changes generation churn. Equality suppression remains a separate decision rather than part of this refactor.

## Ownership and publication

The inventory below describes the frozen `910b2dc` baseline. The S4 comparison above describes the migrated implementation; constructor and record-copy references below are historical.

`BehaviorContext.SetSemantics` or `BindSemantics` accepts the behavior-owned declaration only during attachment. A binding effect later calls `Element.UpdateControlSemantics`. `Element` stores `_baseSemantics` and `_semantics`; `_baseSemantics` is the behavior declaration and `_semantics` is the effective declaration after author metadata. Every effective replacement clears reconciled state, increments `_semanticGeneration`, and invalidates semantic projection, including freshly rebuilt equivalent declarations.

`Composition.SemanticSnapshot()` recursively builds visible children first. A semantic element becomes one immutable snapshot; a nonsemantic root becomes a structural Group snapshot; nonsemantic intermediate elements are elided. Supplemental tooltip descriptions are appended at projection and may rebuild the path to the selected description target with record `with` expressions. Interaction suspension separately rewrites every snapshot as disabled and unfocused. Windows flattens the immutable tree with scene geometry into its private `Node` dictionary, increments `SemanticSnapshotBuilds`, diffs old/new nodes for events, and keys providers by epoch and element. Command dispatch returns through `Composition.ExecuteSemanticCommand`, checks the current generation, enabled state and `Element.Allows`, then invokes the behavior-owned handler.

## Declaration and snapshot field map

All declaration fields are copied positionally into `SemanticSnapshot` today. Snapshot-only fields are `Identity`, reconciled `Enabled`/`Focused`/`Selected`, and `Children`. The destination column records the Windows read as well as portable readers.

| Field | Current owner/source | Validation at construction/publication | Command support | Principal destinations |
| --- | --- | --- | --- | --- |
| `Role` | Behavior producer; author metadata cannot change it | Defined enum; CheckBox/Switch need toggle; Splitter needs range; RadioGroup/TabList need selection; TreeItem needs level; password requires TextField | None by itself in Core | Windows control type; role-derived SelectionPattern and ComboBox ValuePattern; menu/list routing in Core |
| `Name` | Behavior base, optionally replaced by authored `AriaMetadata.Name` | Nonblank | None | Snapshot dumps, testing queries, Windows Name, context-menu base-name reader |
| `Enabled` | Behavior requested flag, reconciled with ancestor `InputAvailable` | Boolean; recomputed lazily | All non-Focus commands require effective enabled | Windows IsEnabled and command rejection |
| `Focused` | Behavior requested flag OR behavior focus state | Boolean; recomputed lazily | Focus is always permitted if current/enabled dispatch reaches it | Windows focus event/property |
| `Selected` | Applied behavior state, not requested state | Boolean; controlled selection acknowledgement remains separate | `Select` comes from Actions | Windows SelectionItem state/events; Core selection result checks |
| `Actions` | Behavior declaration; attachment also requires `BehaviorOwnership.Action` and a handler | Finite mask; paired constraints below | Single Core dispatch gate | Windows action-derived patterns/properties and command calls |
| `Value` | Producer-formatted display/current draft | No general validation; forbidden for password | SetValue only when action declared | Windows Value property/events and polite announcement text |
| `Text` | Text editor visible draft, caret, affinity, read-only flag | Nonnull text; endpoints in range and on grapheme boundaries; finite affinities; forbidden for password | Text/Value patterns require payload; mutation still requires action flags | Windows text ranges, caret, selection, geometry and text events |
| `Expanded` | Expansion behavior applied state | Presence exactly paired with ExpandCollapse action | Expand/Collapse when action declared | Windows ExpandCollapse pattern/property/event |
| `Range` | Slider/splitter applied numeric state | Finite ordered min/value/max; positive finite changes; Splitter requires it; writable state exactly paired with SetRangeValue | SetRangeValue when action declared | Windows RangeValue pattern/properties/events |
| `Relationships` | Field/control producer | Nondefault retained identities; distinct errors; nonblank help/error text; error data requires invalid state | None | Windows LabeledBy, DescribedBy, HelpText, error/full descriptions |
| `ToggleState` | CheckBox/Switch applied state | Defined enum; presence exactly paired with Toggle action; Switch rejects Indeterminate | Toggle when action declared | Windows Toggle pattern/property/event; Core requested-vs-applied result |
| `Selection` | Selection container producer | RadioGroup/TabList require it; booleans otherwise unconstrained | Does not itself grant Select | Windows SelectionPattern properties and selected-child discovery |
| `Description` | Behavior base, optionally replaced by authored description, then tooltip text appended at projection | Nonblank when present | None | Windows full/help description and property event; testing/dumps |
| `PositionInSet` | Logical membership producer | Paired with SizeOfSet; one based and not above size | None | Windows PositionInSet |
| `SizeOfSet` | Logical membership producer | Paired with PositionInSet | None | Windows SizeOfSet |
| `IsPassword` | Confidential editor producer | Requires TextField and null Value/Text | None | Windows password property; suppresses value events; input adapter classifies password input |
| `Collection` | Virtualized list/table container | ItemCount nonnegative; SelectedIndex in range | RealizeItem requires collection | Windows ItemContainer pattern and logical selected-index lookup |
| `Level` | Tree item producer | Positive; required for TreeItem | None | Windows Level |
| `CollectionIndex` | Realized logical collection member | Nonnegative | None | Windows item realization/lookup |
| `Grid` | Table container producer | Counts nonnegative; header identities valid/distinct and no more than columns | None | Windows Grid and Table patterns, dimensions and headers |
| `GridItem` | Realized table cell producer | Valid grid identity; row/column nonnegative | None | Windows GridItem and TableItem patterns/coordinates |
| `Announcement` | Status producer | Defined enum | None | Windows polite notification diff; text is Value then Name |
| `Identity` | Snapshot projection | Epoch/element from retained owner; generation increments on effective semantic/state changes | Required by every command freshness check | Windows provider key plus current-generation validation |
| `Children` | Snapshot projection | Visible semantic descendants, immutable/read-only by convention | None | Core dumps/testing; Windows fragment tree |

`SemanticSelectionSnapshot` itself is an immutable two-boolean record. Grid item bounds against the containing grid are not cross-validated in the constructor. Relationship identities are structurally valid at construction; attachment to the same live composition is resolved by readers later.

## Producers

The portable definition, binding and update seams are `Behavior.cs`, `Element.cs`, `Element.Authoring.cs`, `InputBehaviors.cs`, and `Components/Shared/Controls.Shared.cs`. Direct framework producers at this baseline are:

- Buttons/selectable rows and links: `Components/Buttons/ButtonControls.cs`, `Buttons.cs`, `IconButtons.cs`, and `Components/Links/Link.cs`.
- Selection/navigation: `Components/Selection/SelectionControls.cs`, `SelectionComponents.cs`, `Components/Lists/ListControls.cs`, `ComboBoxControls.cs`, `Components/Navigation/NavigationControls.cs`, and `Components/Trees/TreeControls.cs`.
- Editable/range/date: `Components/TextField/TextField.cs`, `TextFieldControls.cs`, `Components/Numeric/Slider.cs`, `SliderComponents.cs`, `NumberFieldComponents.cs`, `SplitPane.cs`, `Components/Dates/CalendarControls.cs`, and `DateTimePickerComponents.cs`.
- Tables/virtualization: `Components/Tables/TableControls.cs`, `TableComponents.cs`, and collection declarations in list/tree controls.
- Static/structural/status: `Components/Layout/LayoutControls.cs`, `Components/Text/Text.cs`, `TextControls.cs`, `Components/Fields/FieldControls.cs`, `FieldComponents.cs`, `Components/Images/Images.cs`, `Components/Status/StatusControls.cs`, `ProgressBar.cs`, `Gauge.cs`, dialog semantics, command scopes, scroll behavior, and context-menu Menu/MenuItem declarations.
- Compiler authoring reaches the same producers through generated component recipes; the executable package fixtures in `tests/Lucent.Lui.Sdk.Fixtures/Authoring`, `Content`, and `ContextMenu` consume the resulting snapshots. There is no separate compiler-owned semantic schema.

## Consumers and mutation sites

- `Element.Authoring.cs` is the only declaration-wide copier: it forwards all 23 fields to replace name/description. `Composition.Interaction.DisableInteraction` and supplemental-description projection are the snapshot `with` mutation sites that must migrate coherently.
- `Composition.cs` owns tree build, revision, freshness, command routing, requested/applied selection and toggle checks, List ancestor discovery, diagnostic serialization, and supplemental description targeting.
- `ContextMenus.cs` reads effective snapshots for popup behavior but `BehaviorContext.SemanticName` supplies the attachment-time behavior base name to submenu registration. `InputRouter.Menus.cs` reads the effective declared role directly from Element.
- `WindowsUiaProvider.cs` consumes every observable field through flattening, property/pattern projection, old/new event comparison, fragment navigation and command dispatch. `WindowsInputAdapter.cs` reads the effective snapshot to classify confidential text input.
- `Lucent.Testing.HeadlessSnapshot` exposes effective snapshots for role/name predicates, box lookup, scene-node lookup and dumps; `HeadlessContext` maps them back to retained elements. Issue Browser reaches semantics through the framework and its performance/accessibility tests rather than a separate application declaration type.
- Core, Windows, Testing and SDK fixture tests construct declarations/snapshots and read flat properties directly. Positional construction/public API and `with` call sites therefore require an atomic repository migration.

## Pattern versus action truth table

Pattern discovery and command eligibility are intentionally different at this baseline.

| Windows UIA pattern | Current discovery condition | Core command gate |
| --- | --- | --- |
| Invoke | `Actions.Invoke` | `Actions.Invoke` |
| Selection container | Role is List, RadioGroup, TabList, Tree, Calendar or Table | No container command; item Select uses its own action |
| Toggle | `Actions.Toggle` | `Actions.Toggle` |
| Value | `Actions.SetValue` OR Text payload present OR role is ComboBox | SetValue only with `Actions.SetValue` |
| Scroll | `Actions.Scroll` | `Actions.Scroll` |
| ExpandCollapse | `Actions.ExpandCollapse` | same action for Expand and Collapse |
| Grid / Table | Grid payload present | No direct command |
| GridItem / TableItem | GridItem payload present | No direct command |
| ItemContainer | Collection payload present | RealizeItem only with `Actions.RealizeItem` and declaration validation also requires Collection |
| RangeValue | Range payload present | SetRangeValue only with `Actions.SetRangeValue` |
| SelectionItem | `Actions.Select` | `Actions.Select` |
| Text / Text2 | Text payload present AND `Actions.SelectText` | SelectText and ScrollTextIntoView require their respective independent actions |

Windows also publishes action-availability properties from these same conditions. Its Value read-only result is true when Text says read-only or SetValue is absent. Range read-only comes from the range payload, whose constructor/declaration coupling currently makes writable range equivalent to SetRangeValue support.

## Base and effective metadata readers

| Reader | Classification | Reason |
| --- | --- | --- |
| `BehaviorContext.SemanticName` | Base | Reads the behavior context's `_semantic.Name`; context-menu/submenu registration uses the control's authored-by-behavior name during attachment. |
| `Element._baseSemantics` | Base storage | Replaced by behavior updates and retained so clearing author metadata restores behavior values. |
| `Element._semantics`, `DeclaredSemanticRole`, `SemanticEnabled`, `SemanticSelected`, `SemanticToggleState` | Effective | Contains author name/description overlay and reconciles live applied state. |
| `Element.CreateSemanticSnapshot` and presentation dump | Effective plus projection overlay | Copies effective declaration; merges tooltip supplemental description; reconciles enabled/focused/selected. |
| `Composition.SemanticSnapshot`, command routing and context-menu snapshot queries | Effective | Operate on current Element state/snapshot identity. |
| `WindowsUiaProvider`, `WindowsInputAdapter`, Lucent.Testing and repository consumers | Effective published snapshot | Observe authored metadata and supplemental description after publication. |

Metadata precedence is behavior base, then permitted author name/description replacement, then supplemental tooltip description appended during projection. Clearing authored metadata restores base values. The migration should preserve the base classification of `BehaviorContext.SemanticName` rather than globally redirecting it to effective metadata.
