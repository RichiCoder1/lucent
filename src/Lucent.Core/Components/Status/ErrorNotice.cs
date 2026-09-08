namespace Lucent.Core;

public static partial class Components
{
    /// <summary>Displays a live recoverable-error message and an optional retry action.</summary>
    /// <param name="message">Reads the current message through ordinary reactive dependency tracking.</param>
    /// <param name="retry">Optional action shown as a Retry button.</param>
    /// <param name="style">Optional author style appended after the stock notice layout.</param>
    [LucentComponent]
    public static ComponentRecipe ErrorNotice(
        Func<string> message,
        Action? retry = null,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(message);
        return ErrorNoticeContent(message, retry, style);
    }
}
