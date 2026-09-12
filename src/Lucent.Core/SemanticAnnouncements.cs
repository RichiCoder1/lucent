namespace Lucent.Core;

/// <summary>Controls whether changes to semantic text are announced by platform accessibility adapters.</summary>
public enum SemanticAnnouncement
{
    /// <summary>Does not request an accessibility announcement.</summary>
    None,

    /// <summary>Requests a noninterrupting announcement whose newest value supersedes older pending values.</summary>
    Polite,
}
