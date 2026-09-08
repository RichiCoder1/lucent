# Core components

The public stock recipes live on `Lucent.Core.Components`. The type remains a
single partial class so C# and `.lui` authoring resolve the same component
symbols; the files are grouped by the component family that owns each recipe.

| Family | Source location | Use it for |
| --- | --- | --- |
| Layout | `src/Lucent.Core/Components/Layout` | `Layout`, `Row`, `Column`, and responsive containers |
| Commands | `src/Lucent.Core/Components/Commands` | `CommandScope` and application key bindings |
| Text | `src/Lucent.Core/Components/Text` | Static and live text content |
| Buttons | `src/Lucent.Core/Components/Buttons` | `Button` and `Selectable` recipes and their stateful presentation |
| Text fields | `src/Lucent.Core/Components/TextField` | `TextField`, `TextArea`, editor state, pointer editing, IME and placeholder projection |
| Scrolling | `src/Lucent.Core/Components/Scrolling` | Scroll viewports, virtualized lists and scrollbar presentation |
| Status | `src/Lucent.Core/Components/Status` | Status and progress recipes, plus `.lui` composites such as `ErrorNotice` |
| Shared | `src/Lucent.Core/Components/Shared` | Mount configuration, validation and state used by more than one family |
| Presentation | `src/Lucent.Core/Presentation` | Shared `ControlThemes` tokens and stock theme values |

The public recipe is the boundary for ordinary application composition. Keep
editor sessions, input routing, reactive ownership, layout, composition and
platform adapters in their own modules even when a component consumes them.
Family implementation files use the internal partial `Controls` type for
mounting and projection details; application code should use the public recipe
or an ordinary `Style` instead of reaching into that helper.

Use C# when a component owns state, editing, input, virtualization, resource
lifetime or a low-level semantic contract. Use `.lui` when the composition is
usefully expressed from existing stock recipes, styles and typed callbacks.
The `Components/Status/ErrorNotice.lui` composite is the reference shape: its
C# adapter exposes the message reader and retry callback, while the markup owns
the layout, status content and conditional retry button.

When adding a stock component, keep all overloads for one public recipe in its
family directory, keep its internal presentation and mount code nearby, and
preserve the `Lucent.Core` namespace, public `Components` type and metadata. A folder move is
an organization change, not a new public namespace or assembly.
