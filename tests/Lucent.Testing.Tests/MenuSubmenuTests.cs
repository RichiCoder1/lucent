using Lucent.Core;

namespace Lucent.Testing.Tests;

[TestClass]
public sealed class MenuSubmenuTests
{
    [TestMethod]
    public async Task CompiledSubmenuIsLazyAndProjectsBoundedRecursiveDescriptor()
    {
        var constructions = 0;
        var invoked = 0;
        ContextMenuRequest? request = null;
        await using var app = await HeadlessApplication.StartAsync(
            Components.ContextMenu(
                [Components.Button("Target", static () => { })],
                () =>
                    HeadlessFixtures.Components.MenuSubmenuContent(
                        () =>
                        {
                            constructions++;
                            return Components.Menu([
                                Components.MenuItem("Leaf", () => invoked++),
                                Components.MenuSeparator(),
                                Components.MenuItem(
                                    "Unavailable",
                                    static () => { },
                                    static () => false
                                ),
                            ]);
                        },
                        static () => { }
                    )
            )
        );
        await app.InvokeAsync(context =>
        {
            context.Input.ContextMenuRequested += value => request = value;
            return context.Input.MoveFocus(FocusTraversalDirection.Next);
        });
        await app.KeyAsync(new(KeyCommandKind.Down, Key.ContextMenu));

        Assert.AreEqual(0, constructions);
        var result = await app.InvokeAsync(_ =>
        {
            var descriptor = request!.StandardMenu;
            Assert.IsNotNull(descriptor);
            var branch = descriptor.Entries[0];
            Assert.AreEqual(StandardMenuEntryKind.Submenu, branch.Kind);
            Assert.AreEqual("More", branch.Label);
            Assert.IsTrue(branch.Enabled);
            Assert.IsNotNull(branch.Submenu);
            Assert.AreEqual(3, branch.Submenu.Entries.Count);
            Assert.AreEqual(StandardMenuEntryKind.Separator, branch.Submenu.Entries[1].Kind);
            Assert.IsFalse(branch.Submenu.Entries[2].Enabled);
            return request.InvokeStandardCommand(branch.Submenu.Entries[0].Identity!.Value);
        });

        Assert.AreEqual(1, constructions);
        Assert.AreEqual(SemanticCommandResult.Applied, result);
        Assert.AreEqual(1, invoked);
        Assert.IsTrue(request!.IsDismissed);
    }

    [TestMethod]
    public async Task RightAndLeftOwnOneChainAndRestoreParentFocus()
    {
        ContextMenuRequest? request = null;
        await using var app = await HeadlessApplication.StartAsync(
            Components.ContextMenu(
                [Components.Button("Target", static () => { })],
                () =>
                    HeadlessFixtures.Components.MenuSubmenuContent(
                        () => Components.Menu([Components.MenuItem("Leaf", static () => { })]),
                        static () => { }
                    )
            )
        );
        await app.InvokeAsync(context =>
        {
            context.Input.ContextMenuRequested += value => request = value;
            return context.Input.MoveFocus(FocusTraversalDirection.Next);
        });
        await app.KeyAsync(new(KeyCommandKind.Down, Key.ContextMenu));

        await app.InvokeAsync(context =>
        {
            var root = request!.CreateComposition();
            Install(root, context.Viewport);
            Assert.IsTrue(root.Input.MoveFocus(FocusTraversalDirection.Next));
            var branch = Find(root.SemanticSnapshot(), "More");
            Assert.IsFalse(branch.Expanded!.Value);
            Assert.IsTrue(root.Input.DispatchKey(new(KeyCommandKind.Down, Key.Right)).Handled);
            Assert.AreEqual(2, request.ActiveLevels.Count);
            var child = request.ActiveLevels[1];
            Assert.IsTrue(child.FocusFirst);
            Install(root, context.Viewport);
            Install(child.Composition, context.Viewport);
            var changes = 0;
            request.ActiveLevelsChanged += () => changes++;
            var more = root.SemanticSnapshot()!;
            var generation = more.Identity;
            root.Input.DispatchPointer(new(PointerCommandKind.Move, 1, 12, 12));
            Install(root, context.Viewport);
            root.Input.DispatchPointer(new(PointerCommandKind.Move, 1, 14, 12));
            Assert.AreEqual(0, changes, "Moving within an open trigger rebuilt its branch.");
            Assert.AreEqual(generation, root.SemanticSnapshot()!.Identity);
            Assert.IsTrue(request.FocusFirst(child));
            Assert.AreEqual(
                InputModality.Keyboard,
                child.Composition.Input.Modality,
                "Keyboard-opened submenu focus must be visibly keyboard focus."
            );
            Assert.IsTrue(Find(child.Composition.SemanticSnapshot(), "Leaf").Focused);
            Assert.IsTrue(
                child.Composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Left)).Handled
            );
            Assert.AreEqual(1, request.ActiveLevels.Count);
            Assert.IsTrue(Find(root.SemanticSnapshot(), "More").Focused);
            Assert.IsFalse(Find(root.SemanticSnapshot(), "More").Expanded!.Value);
            Install(root, context.Viewport);
            Assert.IsTrue(root.Input.DispatchKey(new(KeyCommandKind.Down, Key.Right)).Handled);
            Install(root, context.Viewport);
            var reopened = request.ActiveLevels[1];
            Install(reopened.Composition, context.Viewport);
            Assert.IsTrue(request.FocusFirst(reopened));
            Assert.IsTrue(
                reopened.Composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Escape)).Handled
            );
            Assert.AreEqual(
                1,
                request.ActiveLevels.Count,
                "Escape must dismiss only the child level."
            );
            Assert.IsFalse(request.IsDismissed);
            Install(root, context.Viewport);
            Assert.IsTrue(root.Input.DispatchKey(new(KeyCommandKind.Down, Key.Right)).Handled);
            Install(root, context.Viewport);
            root.Input.DispatchPointer(new(PointerCommandKind.Move, 1, 12, 50));
            Assert.AreEqual(1, request.ActiveLevels.Count);
            return 0;
        });
    }

    [TestMethod]
    public async Task DisablingOpenSubmenuMovesFocusToNextEligibleParentItem()
    {
        Signal<bool>? enabled = null;
        ContextMenuRequest? request = null;
        await using var app = await HeadlessApplication.StartAsync(context =>
        {
            enabled = context.Composition.Root.Scope.Signal(true, "submenu-enabled");
            return Components.ContextMenu(
                [Components.Button("Target", static () => { })],
                () =>
                    Components.Menu([
                        Components.MenuSubmenu(
                            "More",
                            () => Components.Menu([Components.MenuItem("Leaf", static () => { })]),
                            () => enabled!.Value
                        ),
                        Components.MenuItem("After", static () => { }),
                    ])
            );
        });

        await app.InvokeAsync(context =>
        {
            context.Input.ContextMenuRequested += value => request = value;
            return context.Input.MoveFocus(FocusTraversalDirection.Next);
        });
        await app.KeyAsync(new(KeyCommandKind.Down, Key.ContextMenu));

        await app.InvokeAsync(context =>
        {
            var menu = request!.CreateComposition();
            Install(menu, context.Viewport);
            Assert.IsTrue(menu.Input.MoveFocus(FocusTraversalDirection.Next));
            Assert.IsTrue(
                menu.Input.DispatchKey(new(KeyCommandKind.Down, Key.Right)).Handled,
                "The enabled submenu did not open."
            );
            Assert.AreEqual(2, request.ActiveLevels.Count);
            Install(menu, context.Viewport);
            var child = request.ActiveLevels[1].Composition;
            Install(child, context.Viewport);
            Assert.IsTrue(request.FocusFirst(request.ActiveLevels[1]));
            Assert.IsTrue(Find(child.SemanticSnapshot(), "Leaf").Focused);
            return 0;
        });

        await app.InvokeAsync(_ =>
        {
            enabled!.Value = false;
            return 0;
        });

        await app.InvokeAsync(context =>
        {
            var menu = request!.ActiveLevels.Single().Composition;
            Install(menu, context.Viewport);
            var more = Find(menu.SemanticSnapshot(), "More");
            var after = Find(menu.SemanticSnapshot(), "After");
            Assert.IsFalse(more.Enabled);
            Assert.IsFalse(more.Focused, "A disabled submenu trigger must not retain focus.");
            Assert.IsTrue(after.Focused, "Focus did not move to the next eligible parent item.");
            Assert.AreEqual(after.Identity.ElementId, menu.Input.FocusedElement?.ElementId);
            request.Dispose();
            return 0;
        });
    }

    [TestMethod]
    public async Task EmptySubmenuDisablesButAllDisabledSubmenuRemainsInspectable()
    {
        var descriptor =
            (
                await DescriptorFor(
                    Components.Menu([
                        Components.MenuSubmenu("Empty", () => Components.Menu([])),
                        Components.MenuSubmenu(
                            "Inspect",
                            () =>
                                Components.Menu([
                                    Components.MenuItem(
                                        "Unavailable",
                                        static () => { },
                                        static () => false
                                    ),
                                ])
                        ),
                    ])
                )
            ) ?? throw new AssertFailedException("Expected a standard descriptor.");

        Assert.IsFalse(descriptor.Entries[0].Enabled);
        Assert.AreEqual(0, descriptor.Entries[0].Submenu!.Entries.Count);
        Assert.IsTrue(descriptor.Entries[1].Enabled);
        var inspect =
            descriptor.Entries[1].Submenu
            ?? throw new AssertFailedException("Expected nested descriptor.");
        Assert.AreEqual(1, inspect.Entries.Count);
        Assert.IsFalse(inspect.Entries[0].Enabled);
    }

    [TestMethod]
    public async Task StyledNestedEntryFailsTheWholeNativeProjectionClosed()
    {
        var descriptor = await DescriptorFor(
            Components.Menu([
                Components.MenuSubmenu(
                    "Custom",
                    () =>
                        Components.Menu([
                            Components.MenuItem("Styled", static () => { }, style: Style.Empty),
                        ])
                ),
            ])
        );
        Assert.IsNull(descriptor);
    }

    [TestMethod]
    public async Task NativeProjectionRejectsOverDepthAndEntryBudgets()
    {
        Assert.IsNull(await DescriptorFor(Nested(8)));
        Assert.IsNull(
            await DescriptorFor(
                Components.Menu(
                    ComponentContent.Create(
                        Enumerable
                            .Range(0, 513)
                            .Select(index =>
                                (ContentRecipe)
                                    Components.MenuItem($"Item {index}", static () => { })
                            )
                            .ToArray()
                    )
                )
            )
        );

        static ComponentRecipe Nested(int remaining) =>
            Components.Menu(
                remaining == 0
                    ? [Components.MenuItem("Leaf", static () => { })]
                    : [Components.MenuSubmenu("Next", () => Nested(remaining - 1))]
            );
    }

    [TestMethod]
    public void PointerIntentUsesPlacedDirectionAndCancelsPromptly()
    {
        var intent = new SubmenuPointerIntent(TimeSpan.FromMilliseconds(250));
        intent.Begin(100, 30, new(140, 10, 100, 100), TimeSpan.Zero);
        Assert.IsTrue(intent.ShouldDefer(120, 35, TimeSpan.FromMilliseconds(50)));
        Assert.IsFalse(intent.ShouldDefer(115, 35, TimeSpan.FromMilliseconds(60)));
        Assert.IsFalse(intent.IsActive);

        intent.Begin(200, 30, new(80, 10, 100, 100), TimeSpan.FromMilliseconds(100));
        Assert.IsTrue(intent.ShouldDefer(190, 35, TimeSpan.FromMilliseconds(150)));
        Assert.IsFalse(intent.ShouldDefer(170, 150, TimeSpan.FromMilliseconds(160)));

        intent.Begin(100, 30, new(140, 10, 100, 100), TimeSpan.FromMilliseconds(200));
        Assert.IsFalse(intent.ShouldDefer(150, 30, TimeSpan.FromMilliseconds(210)));
        Assert.IsFalse(intent.IsActive);
    }

    private static async Task<StandardMenuDescriptor?> DescriptorFor(ComponentRecipe menu)
    {
        ContextMenuRequest? request = null;
        await using var app = await HeadlessApplication.StartAsync(
            Components.ContextMenu([Components.Button("Target", static () => { })], () => menu)
        );
        await app.InvokeAsync(context =>
        {
            context.Input.ContextMenuRequested += value => request = value;
            return context.Input.MoveFocus(FocusTraversalDirection.Next);
        });
        await app.KeyAsync(new(KeyCommandKind.Down, Key.ContextMenu));
        return await app.InvokeAsync(_ => request!.StandardMenu);
    }

    private static void Install(Composition composition, LayoutViewport viewport)
    {
        composition.Flush();
        for (var attempt = 0; attempt < 3; attempt++)
            if (
                composition.Input.SetScene(
                    SceneLayout.Project(composition, viewport, new HeadlessTextShaper())
                )
            )
                return;
        Assert.Fail("Popup scene installation failed.");
    }

    private static SemanticSnapshot Find(SemanticSnapshot? root, string name) =>
        Flatten(root ?? throw new AssertFailedException("No semantic root."))
            .Single(node => node.Name == name);

    private static IEnumerable<SemanticSnapshot> Flatten(SemanticSnapshot root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var descendant in Flatten(child))
            yield return descendant;
    }
}
