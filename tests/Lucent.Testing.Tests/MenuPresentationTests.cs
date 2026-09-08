using Lucent.Core;

namespace Lucent.Testing.Tests;

[TestClass]
public sealed class MenuPresentationTests
{
    [TestMethod]
    public async Task StandardCommandInvokesBeforePopupLayoutIsInstalled()
    {
        var invoked = false;
        ContextMenuRequest? request = null;
        await using var application = await HeadlessApplication.StartAsync(
            HeadlessFixtures.Components.MenuSurface(
                () => invoked = true,
                static () => { },
                static () => true,
                static () => false,
                static () => false,
                static () => { },
                static _ => { }
            )
        );
        await application.InvokeAsync(context =>
        {
            context.Input.ContextMenuRequested += value => request = value;
            return 0;
        });
        await application.KeyAsync(new(KeyCommandKind.Down, Key.Tab));
        await application.KeyAsync(new(KeyCommandKind.Down, Key.ContextMenu));

        var result = await application.InvokeAsync(_ =>
        {
            request!.CreateComposition().Flush();
            var command = request
                .StandardMenu!.Entries.Single(entry => entry.Label == "Run")
                .Identity!.Value;
            return request.InvokeStandardCommand(command);
        });

        Assert.AreEqual(SemanticCommandResult.Applied, result);
        Assert.IsTrue(invoked);
    }

    [TestMethod]
    public async Task CompiledLuiMenuProjectsFlatDescriptorAndRechecksSemanticState()
    {
        Signal<bool>? enabled = null;
        ContextMenuRequest? request = null;
        await using var application = await HeadlessApplication.StartAsync(context =>
        {
            enabled = context.Composition.Root.Scope.Signal(true, "standard-menu-enabled");
            return HeadlessFixtures.Components.MenuSurface(
                static () => { },
                static () => { },
                () => enabled.Value,
                static () => false,
                static () => false,
                static () => { },
                static _ => { }
            );
        });
        await application.InvokeAsync(context =>
        {
            context.Input.ContextMenuRequested += value => request = value;
            return 0;
        });
        await application.KeyAsync(new(KeyCommandKind.Down, Key.Tab));
        await application.KeyAsync(new(KeyCommandKind.Down, Key.ContextMenu));

        var descriptor = await application.InvokeAsync(_ => request!.StandardMenu);

        Assert.IsNotNull(
            descriptor,
            await application.InvokeAsync(_ => request!.CreateComposition().Dump())
        );
        Assert.AreEqual(4, descriptor.Entries.Count);
        AssertEntry(descriptor.Entries[0], StandardMenuEntryKind.Command, "Disabled", false);
        AssertEntry(descriptor.Entries[1], StandardMenuEntryKind.Command, "Run", true);
        AssertEntry(descriptor.Entries[2], StandardMenuEntryKind.Separator, null, false);
        AssertEntry(descriptor.Entries[3], StandardMenuEntryKind.Command, "Other", true);
        Assert.AreEqual(
            SemanticCommandResult.Disabled,
            await application.InvokeAsync(_ =>
                request!.InvokeStandardCommand(descriptor.Entries[0].Identity!.Value)
            )
        );

        await application.InvokeAsync(_ =>
        {
            enabled!.Value = false;
            return 0;
        });
        Assert.AreEqual(
            SemanticCommandResult.Stale,
            await application.InvokeAsync(_ =>
                request!.InvokeStandardCommand(descriptor.Entries[1].Identity!.Value)
            )
        );
        var current = await application.InvokeAsync(_ => request!.StandardMenu);
        Assert.IsNotNull(current);
        Assert.IsFalse(current.Entries[1].Enabled);
        Assert.AreEqual(
            SemanticCommandResult.Disabled,
            await application.InvokeAsync(_ =>
                request!.InvokeStandardCommand(current.Entries[1].Identity!.Value)
            )
        );
    }

    [TestMethod]
    public async Task CustomMenuRootLayoutOrExplicitStyleUsesRenderedFallback()
    {
        await AssertFallback(Components.Column([Components.MenuItem("Run", static () => { })]));
        await AssertFallback(
            Components.Menu([Components.MenuItem("Run", static () => { }, style: Style.Empty)])
        );
        await AssertFallback(
            Components.Menu([Components.MenuItem("Run", static () => { })], Style.Empty)
        );
    }

    [TestMethod]
    public async Task StandardMenuStylesResolveForLightDarkAndHighContrastThemes()
    {
        var palettes = new[]
        {
            new Palette(
                ControlThemes.Light,
                "#FFFFFFFF",
                "#0F172AFF",
                "#DBEAFEFF",
                "#FFFF00FF",
                "#000000FF",
                "#94A3B8FF"
            ),
            new Palette(
                ControlThemes.Dark,
                "#111827FF",
                "#F8FAFCFF",
                "#1E3A5FFF",
                "#FACC15FF",
                "#000000FF",
                "#64748BFF"
            ),
            new Palette(
                ControlThemes.HighContrast,
                "#000000FF",
                "#FFFFFFFF",
                "#0000FFFF",
                "#FFFF00FF",
                "#000000FF",
                "#808080FF"
            ),
        };

        foreach (var palette in palettes)
        {
            await using var application = await HeadlessApplication.StartAsync(
                Components.Menu([
                    Components.MenuItem("Run", static () => { }),
                    Components.MenuSeparator(),
                ]),
                new HeadlessApplicationOptions { ThemeFactory = _ => palette.Theme }
            );

            var resolved = await application.InvokeAsync(context =>
            {
                var menu = context.Composition.Root.Children.Single();
                var item = menu.Children[0];
                var separator = menu.Children[1];
                var normal = item.Resolve(TypographyProperties.TextColor).Value;
                item.SetVariants(VariantState.Hover);
                var hover = item.Resolve(VisualProperties.Background).Value;
                item.SetVariants(VariantState.FocusVisible);
                var focus = item.Resolve(VisualProperties.Background).Value;
                var focusText = item.Resolve(TypographyProperties.TextColor).Value;
                item.SetVariants(VariantState.Disabled);
                return new ResolvedMenu(
                    menu.Resolve(VisualProperties.Background).Value,
                    menu.Resolve(VisualProperties.Border).Value,
                    menu.Resolve(VisualProperties.CornerRadius).Value,
                    menu.Resolve(LayoutProperties.Padding).Value,
                    normal,
                    hover,
                    focus,
                    focusText,
                    item.Resolve(VisualProperties.Opacity).Value,
                    separator.Children.Single().Resolve(VisualProperties.Background).Value,
                    separator.Resolve(LayoutProperties.Padding).Value
                );
            });

            Assert.AreEqual(palette.Surface, resolved.Surface.Color?.ToString());
            Assert.AreEqual(palette.Foreground, resolved.Foreground.ToString());
            Assert.AreEqual(palette.Selected, resolved.Hover.Color?.ToString());
            Assert.AreEqual(palette.Selected, resolved.Focus.Color?.ToString());
            Assert.AreEqual(palette.Foreground, resolved.FocusForeground.ToString());
            Assert.AreEqual(palette.Disabled, resolved.Border.Brush?.Color?.ToString());
            Assert.AreEqual(palette.Disabled, resolved.Separator.Color?.ToString());
            Assert.AreEqual(Insets.Symmetric(0, 4), resolved.SeparatorPadding);
            Assert.IsTrue(resolved.Border.IsHairline);
            Assert.AreEqual(8f, resolved.CornerRadius);
            Assert.AreEqual(Insets.Uniform(6), resolved.Padding);
            Assert.AreEqual(0.55f, resolved.DisabledOpacity);
        }
    }

    [TestMethod]
    public async Task PointerMoveTransfersTheSingleMenuHighlight()
    {
        await using var application = await HeadlessApplication.StartAsync(
            Components.Menu([
                Components.MenuItem("First", static () => { }),
                Components.MenuItem("Second", static () => { }),
            ])
        );
        await application.KeyAsync(new(KeyCommandKind.Down, Key.Tab));

        await application.PointerAsync(new(PointerCommandKind.Move, 1, 12, 50));

        var state = await application.InvokeAsync(context =>
        {
            var items = context.Composition.Root.Children.Single().Children;
            return (
                FocusedSecond: context.Input.FocusedElement?.ElementId == items[1].Id,
                First: items[0].Resolve(VisualProperties.Background).Value,
                Second: items[1].Resolve(VisualProperties.Background).Value
            );
        });
        Assert.IsTrue(state.FocusedSecond);
        Assert.AreEqual("#FFFFFFFF", state.First.Color?.ToString());
        Assert.AreEqual("#DBEAFEFF", state.Second.Color?.ToString());
    }

    [TestMethod]
    public async Task HostPaddingKeepsAConstrainedMenuInsideItsInnerViewport()
    {
        var recipe = ComponentRecipe.Create(
            "popup-host",
            (context, root) =>
            {
                root.Present(context.Theme, author: Style.Empty.Padding(Insets.Uniform(16)));
                context.Mount(
                    root,
                    Components.Menu([Components.MenuItem("Run", static () => { })])
                );
            }
        );
        await using var application = await HeadlessApplication.StartAsync(
            recipe,
            new HeadlessApplicationOptions { Viewport = new(180, 80, 1.5f) }
        );

        var bounds = await application.InvokeAsync(context =>
        {
            var menu = context.Composition.Root.Children.Single().Children.Single();
            return context.Scene.Boxes.Single(box => box.Identity.ElementId == menu.Id).Bounds;
        });

        Assert.AreEqual(16f, bounds.X);
        Assert.AreEqual(16f, bounds.Y);
        Assert.IsTrue(bounds.X + bounds.Width <= 164f);
        Assert.IsTrue(bounds.Y + bounds.Height <= 64f);
    }

    private static async Task AssertFallback(ComponentRecipe menu)
    {
        ContextMenuRequest? request = null;
        await using var application = await HeadlessApplication.StartAsync(
            Components.ContextMenu(
                [Components.Button("Target", static () => { }, Style.Empty.Height(40))],
                () => menu
            )
        );
        await application.InvokeAsync(context =>
        {
            context.Input.ContextMenuRequested += value => request = value;
            return 0;
        });
        await application.KeyAsync(new(KeyCommandKind.Down, Key.Tab));
        await application.KeyAsync(new(KeyCommandKind.Down, Key.ContextMenu));

        Assert.IsNull(await application.InvokeAsync(_ => request!.StandardMenu));
    }

    private static void AssertEntry(
        StandardMenuEntry entry,
        StandardMenuEntryKind kind,
        string? label,
        bool enabled
    )
    {
        Assert.AreEqual(kind, entry.Kind);
        Assert.AreEqual(label, entry.Label);
        Assert.AreEqual(enabled, entry.Enabled);
        Assert.AreEqual(kind == StandardMenuEntryKind.Command, entry.Identity.HasValue);
    }

    private sealed record Palette(
        Theme Theme,
        string Surface,
        string Foreground,
        string Selected,
        string Focus,
        string FocusForeground,
        string Disabled
    );

    private sealed record ResolvedMenu(
        Brush Surface,
        Border Border,
        float CornerRadius,
        Insets Padding,
        Color Foreground,
        Brush Hover,
        Brush Focus,
        Color FocusForeground,
        float DisabledOpacity,
        Brush Separator,
        Insets SeparatorPadding
    );
}
