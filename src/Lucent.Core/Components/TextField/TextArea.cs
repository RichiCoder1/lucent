namespace Lucent.Core;

/// <summary>Scope-owned multiline Unicode text state backed by an editor session.</summary>
internal sealed class TextAreaState : TextFieldState
{
    internal TextAreaState(ReactiveScope scope, string name, EditorSession session)
        : base(scope, name, session)
    {
        if (!session.IsMultiline)
            throw new ArgumentException(
                "TextArea requires an editor session configured for multiline text.",
                nameof(session)
            );
    }
}
