namespace Lucent.Core;

/// <summary>How focus recovers after its retained owner is hidden or removed.</summary>
public enum FocusRecoveryPolicy
{
    /// <summary>Clear focus. A subsequent explicit request or Tab establishes a new owner.</summary>
    Clear,

    /// <summary>On the next fresh scene, prefer the surviving owner, then a focusable ancestor, then a nearby tab stop.</summary>
    NearestAvailable,
}

public sealed partial class InputRouter
{
    private FocusRecoveryPolicy _focusRecovery;
    private FocusState? _focusToRecover;
    private long _focusRequestSerial;

    /// <summary>
    /// Gets or sets recovery after responsive hiding, removal or scene invalidation.
    /// Recovery never revives a removed element or replays focus when a hidden pane returns.
    /// Explicit focus requests take priority. Disabled controls clear focus without recovery.
    /// </summary>
    public FocusRecoveryPolicy FocusRecovery
    {
        get
        {
            Check();
            return _focusRecovery;
        }
        set
        {
            Check();
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            _focusRecovery = value;
            if (value == FocusRecoveryPolicy.Clear)
                _focusToRecover = null;
        }
    }

    private void LoseSceneFocus(FocusChangeReason reason, List<Exception> errors)
    {
        var previous = _focused;
        var requestSerial = _focusRequestSerial;
        RequestFocus(null, reason, errors);
        if (
            _focusRecovery == FocusRecoveryPolicy.NearestAvailable
            && reason != FocusChangeReason.Disabled
            && _focused is null
            && _focusRequestSerial == requestSerial + 1
        )
            _focusToRecover = previous;
    }

    private void RecoverSceneFocus(List<Exception> errors)
    {
        var pending = _focusToRecover;
        _focusToRecover = null;
        if (pending is not { } previous || _focused is not null)
            return;

        // Walk from the old owner outwards. Recovery uses identities from the
        // old scene only as intent; every candidate must belong to the new scene.
        foreach (var identity in previous.Path.Reverse())
        {
            if (Eligible(identity) && _focusable.ContainsKey(identity.ElementId))
            {
                RequestFocus(identity, FocusChangeReason.Recovery, errors);
                return;
            }
        }

        var candidates = _scene!
            .Input.Where(item =>
                Eligible(item.Identity)
                && _focusable.TryGetValue(item.Identity.ElementId, out var focusable)
                && focusable.TabStop
                && (
                    !_scrollable.ContainsKey(item.Identity.ElementId)
                    || !HasFocusableDescendant(item.Identity)
                )
            )
            .OrderBy(item => item.Order)
            .Select(item => (RetainedInputElement?)item)
            .ToArray();
        var next =
            candidates.FirstOrDefault(item => item!.Value.Order >= previous.Order)
            ?? candidates.LastOrDefault();
        if (next is { } candidate)
            RequestFocus(candidate.Identity, FocusChangeReason.Recovery, errors);
    }
}
