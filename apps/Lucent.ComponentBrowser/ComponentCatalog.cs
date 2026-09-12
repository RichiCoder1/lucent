using System.Reflection;
using System.Text;

namespace Lucent.ComponentBrowser;

public static class ComponentCatalog
{
    public static IReadOnlyList<ComponentCatalogItem> Items { get; } =
    [
        new(
            "buttons",
            "Buttons",
            "Commands",
            "Invoke actions, expose icon-only commands, and keep selection visible.",
            "ButtonsExample.lui",
            "Use `Button` for a named action and `IconButton` only when the accessible label explains the command without visible text. Use `Selectable` for a choice that remains selected.",
            "Every action has a visible label or an explicit accessible label. Selected state and keyboard focus remain separate so a selected item is still understandable when focus moves."
        ),
        new(
            "fields",
            "Text fields",
            "Editing",
            "Retain single-line and multiline text through ordinary editor sessions.",
            "FieldsExample.lui",
            "Give each field a meaningful label, provide a placeholder only as a hint, and hoist an `EditorSession` when a draft must survive remounts.",
            "Labels are semantic names rather than visual decoration. The stock editor keeps caret, selection, undo, clipboard, and IME state in its session."
        ),
        new(
            "password",
            "Password field",
            "Editing",
            "Mask a controlled secret editor, keep reveal state local, and expose safe validation feedback.",
            "PasswordExample.lui",
            "Use `PasswordField` for confidential input. Keep the applied value in application state, avoid echoing it in status text, and choose a bounded history limit.",
            "The editor exposes a password semantic, suppresses plaintext projection and clipboard reads, remasks on focus loss, and keeps validation text separate from the secret."
        ),
        new(
            "combo-box",
            "Async ComboBox",
            "Navigation",
            "Combine controlled selection with generation-safe asynchronous suggestions.",
            "ComboBoxExample.lui",
            "Use `ComboBox` with a typed applied item, an explicit suggestion provider, and a selection policy that makes free text deliberate.",
            "The editor names its expanded state, the popup keeps selection controlled, stale suggestion generations cannot replace current results, and the example requires an enabled result."
        ),
        new(
            "selection",
            "Selection",
            "Commands",
            "Compose checkbox, switch, radio, and selectable states with clear application ownership.",
            "SelectionExample.lui",
            "Use `CheckBox` for a finite checked state, `Switch` for an immediate binary setting, and `RadioGroup` when one keyed option is selected. Keep the selection callback narrow and update state in the application layer.",
            "Each control exposes a stable accessible label and its applied state. Radio options share one roving tab stop, and the example keeps selection readable without relying on color alone."
        ),
        new(
            "feedback",
            "Feedback",
            "Status",
            "Communicate progress, loading, and recoverable failure with stock status recipes.",
            "FeedbackExample.lui",
            "Use `Status` for short noninteractive updates, `ProgressBar` for a bounded value, and `InlineNotice` when the user has a clear recovery action.",
            "Progress carries an accessible label. Error messages explain the problem and name the retry action without stealing focus from a usable control."
        ),
        new(
            "menus",
            "Menus",
            "Surfaces",
            "Keep secondary actions discoverable through a real context menu and nested command surface.",
            "MenusExample.lui",
            "Use `ContextMenu` to add secondary commands without replacing the target's primary action. Keep menu labels short and put related commands behind a submenu.",
            "The menu target keeps its own accessible action. Menu entries use command names, availability is exposed, and Escape returns to the target.",
            "The menu and submenu bodies are authored in the compiled companion files `Examples/MenusExampleMenu.lui` and `Examples/MenusExampleSubmenu.lui`."
        ),
        new(
            "surfaces",
            "Tooltips and popovers",
            "Surfaces",
            "Attach a noninteractive description or a controlled interactive surface to a trigger.",
            "PopoverExample.lui",
            "Use `Tooltip` for a short description on hover or keyboard focus, and `Popover` for supporting content that needs its own controls. Keep `open` and `onOpenRequested` in application state.",
            "The trigger remains a normal tab stop. The popover is interactive, outside dismissal is explicit, and the close action provides a keyboard path back to the owner.",
            "The popup body is authored in the compiled companion file `Examples/PopoverExamplePopup.lui`."
        ),
        new(
            "numeric",
            "Numeric input",
            "Editing",
            "Keep decimal drafts, bounded sliders, and commit policy in application-owned state.",
            "NumericExample.lui",
            "Use `NumberField` when a value needs culture-aware text editing and validation. Use `Slider` for a finite range with explicit preview and commit callbacks.",
            "Both controls expose their labels, current values, ranges, and disabled/read-only state to accessibility clients. The example keeps the applied value controlled by the browser model."
        ),
        new(
            "date-time",
            "Date and time",
            "Editing",
            "Keep bounded date and time drafts controlled while exposing calendar and stepping semantics.",
            "DateTimeExample.lui",
            "Use `DatePicker` and `TimePicker` with explicit culture, range, nullability, and step policies. Pass `DateTimeFieldOptions` for help, required state, validation, and form participation.",
            "The fields expose labels, required state, validation, calendar relationships, current values, and keyboard stepping. Draft parsing failures remain attached to the field without changing the applied value."
        ),
        new(
            "navigation",
            "Navigation and dialogs",
            "Navigation",
            "Compose keyed tabs, list selection, noneditable selects, disclosures, links, and a typed modal flow.",
            "NavigationExample.lui",
            "Use `ListBox` for a visible keyed collection, `Select` for a compact choice surface, and `Tabs` or `Disclosure` when content visibility follows a typed state policy. Keep dialog application writes behind a controller.",
            "Labels and relationships remain semantic: list choices expose position and selection, tabs retain their selected panel, links name their action, and the dialog owns focus while it is open."
        ),
        new(
            "tree",
            "Tree view",
            "Navigation",
            "Keep hierarchical rows, expansion, focus, and selected keys controlled by the application.",
            "TreeExample.lui",
            "Use `TreeView` with stable keys, explicit expansion readers and callbacks, and a `TreeDataSource` that keeps child discovery separate from row presentation.",
            "Tree semantics expose level, position, collection relationships, expansion, and selection. Collapsing a branch preserves a usable focus destination and does not silently change the applied key."
        ),
        new(
            "storage",
            "Native storage selection",
            "Platform services",
            "Select files, save destinations, and folders through the injected picker port without performing I/O.",
            "StorageExample.lui",
            "Inject `IFilePicker` from the application boundary, handle `Selected`, `Canceled`, `Unsupported`, and `Failed` as ordinary outcomes, and keep writing separate from destination selection.",
            "Each action has a named outcome surface. Selected names and locations are reported as content, while cancellation and unsupported hosts remain visible without throwing."
        ),
        new(
            "table",
            "Data table",
            "Navigation",
            "Keep keyed rows, column sorting, selection, and bounded realization in application state.",
            "TableExample.lui",
            "Use `TableView` for read-only tabular data with stable row keys, caller-owned sorting, and a controlled selected key. Provide concise column text and a bounded row viewport for large collections.",
            "The table exposes row and column relationships, position, selected state, sort direction, and a realization action for offscreen rows. Selection remains stable when sorting changes the visible order."
        ),
    ];

    public static ComponentCatalogItem Find(string id) =>
        Items.FirstOrDefault(item => item.Id == id)
        ?? throw new InvalidOperationException("Unknown component browser item: " + id);

    public static string ReadSource(string sourceFile)
    {
        var resourceName = "Lucent.ComponentBrowser.Examples." + sourceFile;
        using var stream = typeof(ComponentCatalog).Assembly.GetManifestResourceStream(
            resourceName
        );
        if (stream is null)
            throw new InvalidOperationException(
                "Embedded example source is missing: " + sourceFile
            );
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true
        );
        return reader.ReadToEnd();
    }
}
