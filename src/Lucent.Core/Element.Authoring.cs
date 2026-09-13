namespace Lucent.Core;

public sealed partial class Element
{
    private Style? _recipeStyle;
    private bool _recipeAriaRegistered;
    private AriaMetadata? _recipeAria;

    // The declared target installs contributions before the existing control's first Present.
    // An inner same-root recipe contributes before the already registered outer recipe.
    internal void RegisterRecipeAuthoring(AuthorRecipeValues values)
    {
        RegisterRecipeStyleCore(values);
        if (values.MetadataReader is null)
            return;
        if (_recipeAriaRegistered)
            throw new InvalidOperationException(
                "A retained recipe can register accessibility metadata only once."
            );
        _recipeAriaRegistered = true;
        var read = values.MetadataReader;
        UpdateRecipeAria(read());
        _ = Scope.Effect(() => UpdateRecipeAria(read()), Name + ".author-aria");
    }

    internal void RegisterRecipeStyle(AuthorRecipeValues values)
    {
        if (values.MetadataReader is not null)
            throw new InvalidOperationException(
                "A style-only recipe target cannot accept accessibility metadata."
            );
        RegisterRecipeStyleCore(values);
    }

    private void RegisterRecipeStyleCore(AuthorRecipeValues values)
    {
        Composition.CheckThread();
        Composition.ValidateFactoryMutation(this);
        ThrowIfDisposed();
        if (_presentation is not null)
            throw new InvalidOperationException("Recipe contributions must precede presentation.");
        if (values.Style is not null)
            _recipeStyle = _recipeStyle is null
                ? values.Style
                : Style.Compose(values.Style, _recipeStyle);
    }

    private void UpdateRecipeAria(AriaMetadata? metadata)
    {
        Composition.CheckThread();
        if (Equals(_recipeAria, metadata))
            return;
        _recipeAria = metadata;
        if (_baseSemantics is not null)
            SetEffectiveSemantics(MergeAuthorSemantics(_baseSemantics));
    }

    private SemanticDeclaration MergeAuthorSemantics(SemanticDeclaration source)
    {
        if (_recipeAria is null)
            return source;
        return new SemanticDeclaration(
            source.Role,
            _recipeAria.Name ?? source.Name,
            source.Enabled,
            source.Focused,
            source.Selected,
            source.Actions,
            source.Value,
            source.Text,
            source.Expanded,
            source.Range,
            source.Relationships,
            source.ToggleState,
            source.Selection,
            _recipeAria.Description ?? source.Description,
            source.PositionInSet,
            source.SizeOfSet,
            source.IsPassword,
            source.Collection,
            source.Level,
            source.CollectionIndex,
            source.Grid,
            source.GridItem,
            source.Announcement
        );
    }

    private Style MergeRecipeStyle(Style? author) =>
        _recipeStyle is null ? author ?? Style.Empty
        : author is null ? _recipeStyle
        : Style.Compose(author, _recipeStyle);
}
