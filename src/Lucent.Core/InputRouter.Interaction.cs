namespace Lucent.Core;

public sealed partial class InputRouter
{
    internal void SuspendInteraction()
    {
        var errors = new List<Exception>();
        ReleaseAll(PointerCaptureLossReason.Disabled, errors);
        ClearHover(errors);
        RequestFocus(null, FocusChangeReason.Disabled, errors);
        Throw(errors);
    }
}
