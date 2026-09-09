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
            "The menu target keeps its own accessible action. Menu entries use command names, availability is exposed, and Escape returns to the target."
        ),
        new(
            "surfaces",
            "Popover surfaces",
            "Surfaces",
            "Anchor a controlled interactive surface to a trigger while keeping open state in the application.",
            "PopoverExample.lui",
            "Use `Popover` for focused supporting content that needs its own controls. Keep `open` and `onOpenRequested` in application state, and let outside dismissal update that state.",
            "The trigger remains a normal tab stop. The popover is interactive, outside dismissal is explicit, and the close action provides a keyboard path back to the owner.",
            "The popup body is authored in the compiled companion file `Examples/PopoverExamplePopup.lui`."
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
