namespace Lucent.Core;

public sealed partial class InputRouter
{
    internal void CancelPointerCapture(int pointerId)
    {
        Check();
        var errors = new List<Exception>();
        Release(pointerId, PointerCaptureLossReason.Cancelled, errors);
        Throw(errors);
    }

    internal void SuspendInteraction()
    {
        var errors = new List<Exception>();
        ReleaseAll(PointerCaptureLossReason.Disabled, errors);
        ClearHover(errors);
        RequestFocus(null, FocusChangeReason.Disabled, errors);
        Throw(errors);
    }
}
