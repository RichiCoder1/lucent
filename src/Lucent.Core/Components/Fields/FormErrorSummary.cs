namespace Lucent.Core;

public static partial class Components
{
    /// <summary>Creates a focusable linked summary of current form errors after submission.</summary>
    [LucentComponent]
    public static ComponentRecipe FormErrorSummary(FormSession session, Style? style = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        return ComponentRecipe.Create(
            "form-error-summary",
            (context, root) =>
            {
                var summaryStyle = Style
                    .Empty.Participation(() =>
                        session.Errors.Count == 0
                            ? ElementParticipation.Collapsed
                            : ElementParticipation.Visible
                    )
                    .With(style ?? Style.Empty);
                Controls.Column(root, context.Theme, "Form errors", summaryStyle);
                _ = context.Mount(
                    root,
                    Status(() =>
                    {
                        var count = session.Errors.Count;
                        return count == 1
                            ? "1 field needs attention."
                            : count + " fields need attention.";
                    })
                );
                _ = context.ForEach(
                    root,
                    "form-error-links",
                    () => session.Errors,
                    error => error.FieldId,
                    (error, child) =>
                        Button(
                                () =>
                                    error.Value.FieldId
                                    + ": "
                                    + string.Join(" ", error.Value.Messages),
                                () => session.Focus(error.Value.FieldId)
                            )
                            .Mount(child)
                );
            }
        );
    }
}
