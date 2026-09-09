using SDL3;

namespace Lucent.ComponentBrowser;

internal static class ExampleClipboard
{
    public static bool TryCopy(string text, out string message)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            if (SDL.SetClipboardText(text))
            {
                message = "Copied the compiled .lui source.";
                return true;
            }

            var error = SDL.GetError();
            message = string.IsNullOrWhiteSpace(error)
                ? "The Windows clipboard did not accept the source."
                : "Clipboard unavailable: " + error;
            return false;
        }
        catch (Exception exception)
        {
            message = "Clipboard unavailable: " + exception.Message;
            return false;
        }
    }
}
