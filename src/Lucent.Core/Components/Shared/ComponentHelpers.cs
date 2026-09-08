namespace Lucent.Core;

public static partial class Components
{
    private static ComponentContent Content(ComponentContent content) =>
        content ?? throw new ArgumentNullException(nameof(content));

    private static string Required(string value, string parameter) =>
        ControlState.Required(value, parameter);

    private static float RowHeight(Func<float> read)
    {
        var value = read();
        if (!float.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(nameof(read));
        return value;
    }
}
