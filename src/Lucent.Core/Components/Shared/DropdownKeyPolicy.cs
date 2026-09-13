namespace Lucent.Core;

internal enum DropdownKeyAction
{
    None,
    Toggle,
    Open,
    Close,
}

internal static class DropdownKeyPolicy
{
    internal static bool IsTraversalDismissal(KeyCommand command) =>
        command is { Kind: KeyCommandKind.Down, IsRepeat: false, Key: Key.Tab }
        && (command.Modifiers & ~KeyModifiers.Shift) == 0;

    internal static DropdownKeyAction Classify(KeyCommand command)
    {
        if (command.Kind != KeyCommandKind.Down || command.IsRepeat)
            return DropdownKeyAction.None;
        if (command is { Key: Key.F4, Modifiers: KeyModifiers.None })
            return DropdownKeyAction.Toggle;
        if (command is { Key: Key.Down, Modifiers: KeyModifiers.Alt })
            return DropdownKeyAction.Open;
        if (command is { Key: Key.Up, Modifiers: KeyModifiers.Alt })
            return DropdownKeyAction.Close;
        return DropdownKeyAction.None;
    }

    internal static bool Apply(
        KeyCommand command,
        bool expanded,
        Func<bool> open,
        Func<bool> close
    ) =>
        Classify(command) switch
        {
            DropdownKeyAction.Toggle => expanded ? close() : open(),
            DropdownKeyAction.Open => expanded || open(),
            DropdownKeyAction.Close => expanded && close(),
            _ => false,
        };
}
