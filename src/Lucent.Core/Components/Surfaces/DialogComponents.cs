namespace Lucent.Core;

public static partial class Components
{
    /// <summary>
    /// Hosts a controller-owned modal dialog. The host element is collapsed; body and action
    /// content are retained in the modal surface composition while the controller is open.
    /// </summary>
    [LucentComponent]
    public static ComponentRecipe Dialog(
        DialogControllerBase controller,
        string title,
        [DefaultContent] ComponentContent content,
        ComponentContent? actions = null,
        Action? defaultAccept = null,
        bool destructive = false,
        Func<Composition, bool>? initialFocus = null,
        Func<ValueTask>? defaultAcceptAsync = null,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(controller);
        title = Required(title, nameof(title));
        content = Content(content);
        actions ??= ComponentContent.Empty;
        if (defaultAccept is not null && defaultAcceptAsync is not null)
            throw new ArgumentException("A dialog may have one default accept action.");

        return ComponentRecipe.Create(
            "dialog-host",
            (context, root) =>
            {
                root.Present(
                    context.Theme,
                    component: Style
                        .Empty.Set(LayoutProperties.Width, 0f)
                        .Set(LayoutProperties.Height, 0f)
                        .Set(InputProperties.Visible, false),
                    author: style
                );
                DialogSurfaceRequest? session = null;
                root.Scope.OnDispose(() =>
                {
                    session?.Dispose();
                    controller.OwnerDetached();
                });
                _ = root.Scope.Effect(
                    () =>
                    {
                        if (controller.IsOpen)
                        {
                            if (session is not null && !session.IsDismissed)
                                return;
                            session?.Dispose();
                            session = controller.CreateSurface(
                                root,
                                context.Theme,
                                title,
                                content,
                                actions,
                                destructive,
                                initialFocus,
                                defaultAccept,
                                defaultAcceptAsync,
                                style
                            );
                            try
                            {
                                root.Composition.Input.RequestSurface(session);
                            }
                            catch
                            {
                                session.Dispose();
                                session = null;
                                throw;
                            }
                        }
                        else
                        {
                            session?.Dispose();
                            session = null;
                        }
                    },
                    "dialog-open"
                );
            }
        );
    }

    /// <summary>Creates a conventional cancel action for a dialog action row.</summary>
    [LucentComponent]
    public static ComponentRecipe DialogCancel(
        [DefaultContent] string content = "Cancel",
        Action? onInvoke = null,
        Style? style = null
    )
    {
        content = Required(content, nameof(content));
        return Button(content, onInvoke, style);
    }

    [LucentComponent]
    internal static ComponentRecipe DialogFrame(
        DialogControllerBase controller,
        string title,
        bool destructive,
        Action? defaultAccept,
        Func<ValueTask>? defaultAcceptAsync,
        [DefaultContent] ComponentContent content,
        Style? style = null
    )
    {
        ArgumentNullException.ThrowIfNull(controller);
        title = Required(title, nameof(title));
        content = Content(content);
        return ComponentRecipe.Create(
            "dialog-frame",
            (context, root) =>
            {
                var component = Style
                    .Empty.Set(LayoutProperties.Axis, LayoutAxis.Column)
                    .Set(LayoutProperties.MinWidth, 280f)
                    .Set(LayoutProperties.MaxWidth, 640f)
                    .Set(LayoutProperties.MaxHeight, 760f)
                    .Set(LayoutProperties.Padding, Insets.Uniform(20))
                    .Set(LayoutProperties.Spacing, 12f)
                    .Set(LayoutProperties.Clip, true)
                    .Set(VisualProperties.Background, ControlThemes.Surface)
                    .Set(TypographyProperties.TextColor, ControlThemes.Foreground)
                    .Bind(
                        VisualProperties.Border,
                        () => Border.Hairline(context.Theme.Token(ControlThemes.Border))
                    )
                    .Set(VisualProperties.CornerRadius, 8f);
                root.Present(context.Theme, component, style);
                root.AttachBehaviors(
                    new DialogBehavior(controller, title, defaultAccept, defaultAcceptAsync)
                );
                context.Mount(root, content);
            }
        );
    }

    private sealed class DialogBehavior(
        DialogControllerBase controller,
        string title,
        Action? defaultAccept,
        Func<ValueTask>? defaultAcceptAsync
    ) : Behavior
    {
        public override string Name => "dialog";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            context.SetSemantics(new(SemanticRole.Group, title));
            context.OnKey(route =>
            {
                if (
                    route.Command is { Kind: KeyCommandKind.Down, Key: Key.Escape, IsRepeat: false }
                )
                {
                    // A modal surface consumes Escape while an accepted write is pending;
                    // cancellation never interrupts that application-owned write.
                    if (!context.CompositionInput().HasTextComposition)
                        _ = controller.TryCancel();
                    route.Handled = true;
                    return;
                }

                if (
                    route.Command is { Kind: KeyCommandKind.Down, Key: Key.Enter, IsRepeat: false }
                    && !context.CompositionInput().HasTextComposition
                    && (defaultAccept is not null || defaultAcceptAsync is not null)
                )
                {
                    route.Handled = true;
                    InvokeDefault(controller);
                }
            });
        }

        private void InvokeDefault(DialogControllerBase owner)
        {
            try
            {
                defaultAccept?.Invoke();
                if (defaultAcceptAsync is null)
                    return;
                var operation = defaultAcceptAsync();
                if (!operation.IsCompletedSuccessfully)
                    _ = ObserveDefault(owner, operation);
            }
            catch (Exception error)
            {
                owner.ReportFailure(error);
            }
        }

        private static async Task ObserveDefault(DialogControllerBase owner, ValueTask operation)
        {
            try
            {
                await operation.ConfigureAwait(false);
            }
            catch (Exception error)
            {
                try
                {
                    owner.ReportFailure(error);
                }
                catch (ObjectDisposedException)
                {
                    // The owner may be released while an explicit default action is finishing.
                }
            }
        }
    }
}
