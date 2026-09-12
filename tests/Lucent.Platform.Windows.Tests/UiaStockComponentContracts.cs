using System.Globalization;
using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

public sealed unsafe partial class UiaLifecycleContracts
{
    [TestMethod]
    public void PublicDatePickerExposesSpokenSelectedDayThroughNativeSelection()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(public date picker UIA) failed.");
        var window = CreateWindow("Lucent public date picker UIA");
        try
        {
            var graph = new ReactiveGraph();
            using var composition = new Composition(graph, "public-date-picker-uia");
            composition.ConfigureImages(new ImageCache(new ImmediateImagePreparer()));
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            var applied = graph.Signal<DateOnly?>(new(2024, 2, 29), "date.applied");
            composition.Mount(
                composition.Root,
                theme,
                Components.DatePicker(
                    "Due date",
                    () => applied.Value,
                    value => applied.Value = value,
                    new DatePickerOptions(
                        CultureInfo.GetCultureInfo("en-US"),
                        today: static () => new(2024, 2, 15)
                    )
                )
            );
            graph.Drain();

            using var renderer = new SkiaSceneRenderer();
            using var ownerScene = SceneLayout.Project(composition, new(360, 140, 1), renderer);
            Assert(composition.Input.SetScene(ownerScene), "Public DatePicker scene was rejected.");
            using var dispatcher = new WindowsUiaDispatcher();
            using var ownerProvider = new WindowsUiaProvider(
                Hwnd(window),
                composition,
                dispatcher,
                "Public date picker"
            );
            ownerProvider.Refresh(ownerScene);

            var openButton = FindStockProviderByName(
                ownerProvider.InterfacePointer,
                "Open calendar for Due date"
            );
            Assert(openButton != 0, "Public DatePicker omitted its native calendar action.");
            nint invoke = 0;
            try
            {
                invoke = Query(openButton, UiaWrappers.Invoke);
                Assert(
                    Fragment(invoke, 3) == WindowsUiaProvider.Ok,
                    "Public DatePicker calendar action did not open through native Invoke."
                );
            }
            finally
            {
                Release(invoke);
                Release(openButton);
            }

            graph.Drain();
            var request = composition.Input.ActiveSurface;
            Assert(request is not null, "Public DatePicker did not publish its calendar surface.");
            var popup = request!.CreateComposition();
            graph.Drain();
            using var popupScene = SceneLayout.Project(popup, new(360, 420, 1), renderer);
            Assert(
                popup.Input.SetScene(popupScene),
                "Public DatePicker calendar scene was rejected."
            );
            using var popupProvider = new WindowsUiaProvider(
                Hwnd(window),
                popup,
                dispatcher,
                "Public date picker calendar"
            );
            popupProvider.Refresh(popupScene);

            var calendarSelection = FindPattern(popupProvider.InterfacePointer, 10001);
            var selectedDay = FindStockProviderByName(
                popupProvider.InterfacePointer,
                "Thursday, February 29, 2024"
            );
            nint selectionItem = 0;
            try
            {
                Assert(
                    calendarSelection != 0,
                    "Public DatePicker calendar omitted SelectionPattern."
                );
                var required = 0;
                Assert(
                    Simple(calendarSelection, 5, &required) == WindowsUiaProvider.Ok
                        && required == 1,
                    "Public DatePicker calendar did not report its required day selection."
                );
                Assert(
                    selectedDay != 0,
                    "Public DatePicker did not expose the selected day with its full spoken date."
                );
                selectionItem = Query(selectedDay, UiaWrappers.SelectionItem);
                var selected = 0;
                Assert(
                    Simple(selectionItem, 6, &selected) == WindowsUiaProvider.Ok && selected == 1,
                    "Public DatePicker selected day was not selected through native UIA."
                );
                nint container = 0;
                try
                {
                    Assert(
                        Simple(selectionItem, 7, &container) == WindowsUiaProvider.Ok
                            && container != 0,
                        "Public DatePicker selected day omitted its native selection container."
                    );
                }
                finally
                {
                    Release(container);
                }
            }
            finally
            {
                Release(selectionItem);
                Release(selectedDay);
                Release(calendarSelection);
            }
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    [TestMethod]
    public void PublicComboBoxExposesNativeExpandCollapseAndPopupSelection()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(public combo box UIA) failed.");
        var window = CreateWindow("Lucent public combo box UIA");
        try
        {
            var graph = new ReactiveGraph();
            using var composition = new Composition(graph, "public-combo-box-uia");
            composition.ConfigureImages(new ImageCache(new ImmediateImagePreparer()));
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            var selected = graph.Signal<ComboBoxSelectedItem<int>?>(
                new(2, "Beta"),
                "combo.applied"
            );
            var choices = new[] { new ChoiceItem<int>(1, "Alpha"), new ChoiceItem<int>(2, "Beta") };
            var requests = new List<int>();
            composition.Mount(
                composition.Root,
                theme,
                Components.Field(
                    "Assignee",
                    field =>
                        Components.ComboBox(
                            field,
                            () => selected.Value,
                            key =>
                            {
                                requests.Add(key);
                                var choice = choices.Single(item => item.Key == key);
                                selected.Value = new(choice.Key, choice.Label);
                            },
                            (_, _) =>
                                ValueTask.FromResult(ComboBoxSuggestionResult.Success(choices)),
                            new ComboBoxOptions(debounce: TimeSpan.Zero)
                        )
                )
            );
            graph.Drain();

            using var renderer = new SkiaSceneRenderer();
            RetainedScene ownerScene = SceneLayout.Project(composition, new(360, 140, 1), renderer);
            Assert(composition.Input.SetScene(ownerScene), "Public ComboBox scene was rejected.");
            using var dispatcher = new WindowsUiaDispatcher();
            using var ownerProvider = new WindowsUiaProvider(
                Hwnd(window),
                composition,
                dispatcher,
                "Public combo box"
            );
            ownerProvider.Refresh(ownerScene);

            var expand = FindPattern(ownerProvider.InterfacePointer, 10005);
            try
            {
                Assert(expand != 0, "Public ComboBox omitted ExpandCollapsePattern.");
                Assert(
                    StockExpandState(expand) == 0,
                    "Public ComboBox did not initially report Collapsed."
                );
                Assert(
                    Fragment(expand, 3) == WindowsUiaProvider.Ok,
                    "Public ComboBox did not expand through its native pattern."
                );
                graph.Drain();
                var next = SceneLayout.Project(composition, new(360, 140, 1), renderer);
                Assert(
                    composition.Input.SetScene(next),
                    "Expanded public ComboBox scene was rejected."
                );
                ownerProvider.Refresh(next);
                ownerScene.Dispose();
                ownerScene = next;
                Assert(
                    StockExpandState(expand) == 1,
                    "Public ComboBox did not report Expanded after its native action."
                );
                Assert(
                    Fragment(expand, 4) == WindowsUiaProvider.Ok,
                    "Public ComboBox did not collapse through its native pattern."
                );
                graph.Drain();
                next = SceneLayout.Project(composition, new(360, 140, 1), renderer);
                Assert(
                    composition.Input.SetScene(next),
                    "Collapsed public ComboBox scene was rejected."
                );
                ownerProvider.Refresh(next);
                ownerScene.Dispose();
                ownerScene = next;
                Assert(
                    StockExpandState(expand) == 0,
                    "Public ComboBox did not report Collapsed after its native action."
                );
                Assert(
                    Fragment(expand, 3) == WindowsUiaProvider.Ok,
                    "Public ComboBox did not reopen through its native pattern."
                );
                graph.Drain();
                next = SceneLayout.Project(composition, new(360, 140, 1), renderer);
                Assert(
                    composition.Input.SetScene(next),
                    "Reopened public ComboBox scene was rejected."
                );
                ownerProvider.Refresh(next);
                ownerScene.Dispose();
                ownerScene = next;
                Assert(
                    StockExpandState(expand) == 1,
                    "Public ComboBox did not report Expanded after reopening."
                );

                var request = composition.Input.ActiveSurface;
                Assert(
                    request is not null,
                    "Public ComboBox did not publish its suggestion surface."
                );
                var popup = request!.CreateComposition();
                graph.Drain();
                using var popupScene = SceneLayout.Project(popup, new(320, 220, 1), renderer);
                Assert(
                    popup.Input.SetScene(popupScene),
                    "Public ComboBox popup scene was rejected."
                );
                using var popupProvider = new WindowsUiaProvider(
                    Hwnd(window),
                    popup,
                    dispatcher,
                    "Public combo box suggestions"
                );
                popupProvider.Refresh(popupScene);

                var popupSelection = FindPattern(popupProvider.InterfacePointer, 10001);
                var beta = FindStockProviderByName(popupProvider.InterfacePointer, "Beta");
                var alpha = FindStockProviderByName(popupProvider.InterfacePointer, "Alpha");
                nint betaSelection = 0;
                nint alphaSelection = 0;
                try
                {
                    Assert(popupSelection != 0, "Public ComboBox popup omitted SelectionPattern.");
                    var required = 0;
                    Assert(
                        Simple(popupSelection, 5, &required) == WindowsUiaProvider.Ok
                            && required == 1,
                        "Public ComboBox popup did not report required selection."
                    );
                    Assert(
                        beta != 0 && alpha != 0,
                        "Public ComboBox options were absent from UIA."
                    );
                    betaSelection = Query(beta, UiaWrappers.SelectionItem);
                    alphaSelection = Query(alpha, UiaWrappers.SelectionItem);
                    var isSelected = 0;
                    Assert(
                        Simple(betaSelection, 6, &isSelected) == WindowsUiaProvider.Ok
                            && isSelected == 1,
                        "Public ComboBox did not expose its applied option as selected."
                    );
                    var selectResult = Fragment(alphaSelection, 3);
                    Assert(
                        selectResult == WindowsUiaProvider.Ok,
                        $"Public ComboBox native selection returned 0x{selectResult:X8}."
                    );
                    Assert(
                        requests.SequenceEqual([1])
                            && selected.Value?.Key == 1
                            && selected.Value.Label == "Alpha",
                        "Native ComboBox selection did not reach the controlled public value."
                    );
                }
                finally
                {
                    Release(alphaSelection);
                    Release(betaSelection);
                    Release(alpha);
                    Release(beta);
                    Release(popupSelection);
                }
            }
            finally
            {
                Release(expand);
                ownerScene.Dispose();
            }
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    [TestMethod]
    public void PublicSliderExposesNativeRangeValueSlots()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(public slider UIA) failed.");
        var window = CreateWindow("Lucent public slider UIA");
        try
        {
            using var composition = new Composition(new ReactiveGraph(), "public-slider-uia");
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            var applied = composition.Root.Scope.Signal(4.5d, "slider.applied");
            var requests = new List<double>();
            composition.Mount(
                composition.Root,
                theme,
                Components.Slider(
                    "Volume",
                    () => applied.Value,
                    requests.Add,
                    new SliderOptions(0, 10, 0.5),
                    style: Style.Empty.Width(260)
                )
            );

            using var renderer = new SkiaSceneRenderer();
            using var scene = SceneLayout.Project(composition, new(300, 120, 1), renderer);
            Assert(composition.Input.SetScene(scene), "Public slider scene was rejected.");
            using var dispatcher = new WindowsUiaDispatcher();
            using var provider = new WindowsUiaProvider(
                Hwnd(window),
                composition,
                dispatcher,
                "Public slider"
            );
            provider.Refresh(scene);

            var range = FindPattern(provider.InterfacePointer, 10003);
            Assert(range != 0, "Public Slider did not expose RangeValuePattern.");
            try
            {
                Assert(
                    StockRangeDouble(range, 4) is 4.5,
                    "RangeValue.get_Value used the wrong native slot."
                );
                Assert(
                    StockRangeDouble(range, 7) is 0,
                    "RangeValue.get_Minimum used the wrong native slot."
                );
                Assert(
                    StockRangeDouble(range, 6) is 10,
                    "RangeValue.get_Maximum used the wrong native slot."
                );
                Assert(
                    StockRangeDouble(range, 9) is 0.5,
                    "RangeValue.get_SmallChange used the wrong native slot."
                );
                Assert(
                    StockRangeDouble(range, 8) is 5,
                    "RangeValue.get_LargeChange used the wrong native slot."
                );
                Assert(
                    StockRangeInt(range, 5) == 0,
                    "A writable public Slider was reported read-only."
                );
                Assert(
                    StockRangeSet(range, 3, 7.5) == WindowsUiaProvider.Ok,
                    "RangeValue.SetValue did not reach the public Slider."
                );
                Assert(
                    requests.Count == 1 && requests[0] is 7.5,
                    "Native RangeValue.SetValue did not issue the requested value."
                );
                Assert(
                    StockRangeSet(range, 3, 11) == WindowsUiaProvider.InvalidOperation,
                    "RangeValue accepted a value outside the public Slider range."
                );
                Assert(
                    StockRangeSet(range, 3, double.NaN) == WindowsUiaProvider.InvalidArgument,
                    "RangeValue accepted a non-finite public Slider value."
                );
            }
            finally
            {
                Release(range);
            }
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    [TestMethod]
    public void PublicTreeExposesNativeHierarchyMetadataAndRealizesOffscreenRows()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(public tree UIA) failed.");
        var window = CreateWindow("Lucent public tree UIA");
        try
        {
            using var composition = new Composition(new ReactiveGraph(), "public-tree-uia");
            composition.ConfigureImages(new ImageCache(new ImmediateImagePreparer()));
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            var roots = Enumerable
                .Range(0, 32)
                .Select(index => new StockTreeNode(
                    index,
                    "Root " + index,
                    index == 0 ? [new StockTreeNode(1000, "Child 0")] : []
                ))
                .ToArray();
            var selected = composition.Root.Scope.Signal(SelectedKey.Some(0), "tree.selected");
            var source = new TreeDataSource<int, StockTreeNode>(
                node => node.Key,
                node => node.Label,
                node => node.Children,
                node => node.Children.Count != 0
            );
            composition.Mount(
                composition.Root,
                theme,
                Components.TreeView(
                    "Files",
                    () => roots,
                    source,
                    () => selected.Value,
                    key => selected.Value = SelectedKey.Some(key),
                    key => key == 0,
                    (_, _) => { },
                    new TreeViewOptions(rowHeight: 36),
                    Style.Empty.Height(36).Width(280)
                )
            );

            using var renderer = new SkiaSceneRenderer();
            RetainedScene? scene = null;
            var refreshes = 0;
            using var dispatcher = new WindowsUiaDispatcher();
            using var provider = new WindowsUiaProvider(
                Hwnd(window),
                composition,
                dispatcher,
                "Public tree"
            );
            void Refresh()
            {
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    composition.Flush();
                    var next = SceneLayout.Project(composition, new(280, 36, 1), renderer);
                    if (!composition.Input.SetScene(next))
                    {
                        next.Dispose();
                        continue;
                    }
                    var previous = scene;
                    scene = next;
                    provider.Refresh(next);
                    previous?.Dispose();
                    refreshes++;
                    return;
                }
                throw new InvalidOperationException(
                    "Public Tree UIA realization did not converge."
                );
            }

            provider.SetRealizationRefresh(Refresh);
            nint treeSelection = 0;
            nint itemContainer = 0;
            nint prior = 0;
            nint current = 0;
            try
            {
                Refresh();
                treeSelection = FindPattern(provider.InterfacePointer, 10001);
                itemContainer = FindPattern(provider.InterfacePointer, 10019);
                Assert(treeSelection != 0, "Public Tree omitted SelectionPattern.");
                Assert(itemContainer != 0, "Public Tree omitted ItemContainerPattern.");

                var firstRefresh = refreshes;
                var rootPosition = 0;
                var rootSize = 0;
                var rootLevel = 0;
                var childLevel = 0;
                var childName = string.Empty;
                var offscreenName = string.Empty;
                for (var index = 0; index <= 21; index++)
                {
                    var empty = default(WindowsUiaProvider.RawVariant);
                    Assert(
                        ItemContainer(itemContainer, 3, prior, 0, empty, &current)
                            == WindowsUiaProvider.Ok
                            && current != 0,
                        "Public Tree native enumeration did not return the next row."
                    );
                    if (prior != 0)
                        Release(prior);
                    prior = current;
                    current = 0;

                    Assert(
                        ReadStockProperty(prior, 30003) == 50024,
                        "Public Tree enumeration returned a non-TreeItem control."
                    );
                    if (index == 0)
                    {
                        rootPosition = ReadStockProperty(prior, 30152);
                        rootSize = ReadStockProperty(prior, 30153);
                        rootLevel = ReadStockProperty(prior, 30154);
                    }
                    else if (index == 1)
                    {
                        childLevel = ReadStockProperty(prior, 30154);
                        childName = ReadFieldString(prior, 30005) ?? string.Empty;
                    }
                    else if (index == 21)
                        offscreenName = ReadFieldString(prior, 30005) ?? string.Empty;
                }

                Assert(
                    rootPosition == 1 && rootSize == 32 && rootLevel == 1,
                    "Public Tree root did not expose native set position, size, and level."
                );
                Assert(
                    childLevel == 2 && childName == "Child 0",
                    $"Public Tree child hierarchy was level {childLevel} named '{childName}'."
                );
                Assert(
                    refreshes > firstRefresh && offscreenName == "Root 20",
                    "Public Tree did not realize an offscreen row through its native container."
                );
                Assert(
                    provider.CacheCount < 40,
                    "Public Tree native realization allocated providers for the full collection."
                );
            }
            finally
            {
                Release(current);
                Release(prior);
                Release(itemContainer);
                Release(treeSelection);
                scene?.Dispose();
            }
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    private static double StockRangeDouble(nint pointer, int slot)
    {
        var value = 0d;
        Assert(
            ((delegate* unmanaged[Stdcall]<nint, double*, int>)(*(nint**)pointer)[slot])(
                pointer,
                &value
            ) == WindowsUiaProvider.Ok,
            "Native RangeValue getter failed."
        );
        return value;
    }

    private static int StockRangeInt(nint pointer, int slot)
    {
        var value = 0;
        Assert(
            ((delegate* unmanaged[Stdcall]<nint, int*, int>)(*(nint**)pointer)[slot])(
                pointer,
                &value
            ) == WindowsUiaProvider.Ok,
            "Native RangeValue read-only getter failed."
        );
        return value;
    }

    private static int StockRangeSet(nint pointer, int slot, double value) =>
        ((delegate* unmanaged[Stdcall]<nint, double, int>)(*(nint**)pointer)[slot])(pointer, value);

    private static int ReadStockProperty(nint provider, int property)
    {
        WindowsUiaProvider.RawVariant value = default;
        Assert(
            Simple(provider, 5, property, &value) == WindowsUiaProvider.Ok && value.Type == 3,
            $"Native UIA property {property} was not an I4."
        );
        return checked((int)value.Value);
    }

    private static int StockExpandState(nint pointer)
    {
        var value = -1;
        Assert(
            Simple(pointer, 5, &value) == WindowsUiaProvider.Ok,
            "Native ExpandCollapse state getter failed."
        );
        return value;
    }

    private static nint FindStockProviderByName(nint root, string name)
    {
        var fragment = Query(root, UiaWrappers.Fragment);
        try
        {
            return Find(fragment);
        }
        finally
        {
            Release(fragment);
        }

        nint Find(nint current)
        {
            var simple = Query(current, UiaWrappers.Simple);
            try
            {
                if (ReadFieldString(simple, 30005) == name)
                    return AddRef(current);
            }
            finally
            {
                Release(simple);
            }

            nint child = 0;
            if (Fragment(current, 3, 3, &child) != WindowsUiaProvider.Ok || child == 0)
                return 0;
            try
            {
                while (child != 0)
                {
                    var found = Find(child);
                    if (found != 0)
                        return found;
                    nint next = 0;
                    if (Fragment(child, 3, 1, &next) != WindowsUiaProvider.Ok || next == 0)
                        break;
                    Release(child);
                    child = next;
                }
                return 0;
            }
            finally
            {
                Release(child);
            }
        }
    }

    private sealed record StockTreeNode(
        int Key,
        string Label,
        IReadOnlyList<StockTreeNode> Children
    )
    {
        internal StockTreeNode(int key, string label)
            : this(key, label, Array.Empty<StockTreeNode>()) { }
    }
}
