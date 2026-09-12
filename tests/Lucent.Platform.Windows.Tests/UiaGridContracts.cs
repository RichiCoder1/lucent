using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;

namespace Lucent.Platform.Windows.Tests;

public sealed unsafe partial class UiaLifecycleContracts
{
    [TestMethod]
    public void PublicTableRealizesOffscreenRowsAndKeepsNativeSelectionAndStaleProvidersCorrect()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(UIA table) failed.");
        var window = CreateWindow("Lucent UIA table");
        try
        {
            using var composition = new Composition(new ReactiveGraph(), "uia-public-table");
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            var items = composition.Root.Scope.Signal(
                Enumerable.Range(0, 10_000).ToArray(),
                "items"
            );
            var selected = composition.Root.Scope.Signal(SelectedKey.Some(0), "selected");
            composition.Mount(
                composition.Root,
                theme,
                Components.TableView(
                    "Records",
                    () => items.Value,
                    item => item,
                    new TableColumn<int>[]
                    {
                        new(
                            "id",
                            "Identifier",
                            item => item.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        ),
                    },
                    () => selected.Value,
                    key => selected.Value = SelectedKey.Some(key),
                    style: Style.Empty.Width(240).Height(160)
                )
            );
            using var renderer = new SkiaSceneRenderer();
            using var dispatcher = new WindowsUiaDispatcher();
            using var provider = new WindowsUiaProvider(
                Hwnd(window),
                composition,
                dispatcher,
                "Records"
            );
            RetainedScene? scene = null;
            var refreshes = 0;
            void Refresh()
            {
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    composition.Flush();
                    var next = SceneLayout.Project(composition, new(240, 160, 1), renderer);
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
                throw new InvalidOperationException("Table UIA realization did not converge.");
            }
            provider.SetRealizationRefresh(Refresh);
            nint grid = 0,
                container = 0,
                selection = 0,
                first = 0,
                last = 0;
            try
            {
                Refresh();
                grid = FindPattern(provider.InterfacePointer, 10006);
                container = FindPattern(provider.InterfacePointer, 10019);
                selection = FindPattern(provider.InterfacePointer, 10001);
                Assert(
                    grid != 0 && container != 0 && selection != 0,
                    "Public TableView omitted native capabilities."
                );
                nint selectedRows = 0;
                Assert(
                    Table(selection, 3, &selectedRows) == WindowsUiaProvider.Ok
                        && SafeArrayLength(selectedRows) == 1,
                    "Selection must find a selected row inside the nested scroll viewport."
                );
                _ = SafeArrayDestroy(selectedRows);
                Assert(
                    Grid(grid, 3, 0, 0, &first) == WindowsUiaProvider.Ok && first != 0,
                    "First cell unavailable."
                );
                Assert(
                    Grid(grid, 3, 9999, 0, &last) == WindowsUiaProvider.Ok && last != 0,
                    "Offscreen cell was not realized."
                );
                Assert(
                    refreshes == 2,
                    "One offscreen request must require one bounded host refresh."
                );
                Assert(
                    provider.CacheCount < 40,
                    "Virtualization allocated providers for offscreen rows."
                );
                var staleGridItem = Query(first, UiaWrappers.GridItemProvider);
                Assert(staleGridItem != 0, "Retained cell lost its stable COM transport.");
                try
                {
                    var staleRow = -1;
                    Assert(
                        GridItem(staleGridItem, 3, &staleRow) == WindowsUiaProvider.NotAvailable,
                        "An unrealized retained cell provider remained live."
                    );
                }
                finally
                {
                    Release(staleGridItem);
                }
                selectedRows = 0;
                Assert(
                    Table(selection, 3, &selectedRows) == WindowsUiaProvider.Ok
                        && SafeArrayLength(selectedRows) == 1,
                    "Selection must realize the selected logical row after it scrolls offscreen."
                );
                _ = SafeArrayDestroy(selectedRows);
                Assert(
                    refreshes == 3 && provider.CacheCount < 40,
                    "Offscreen selection must use one bounded realization refresh."
                );
                var empty = default(WindowsUiaProvider.RawVariant);
                nint rowProvider = 0;
                Assert(
                    ItemContainer(container, 3, 0, 0, empty, &rowProvider) == WindowsUiaProvider.Ok
                        && rowProvider != 0,
                    "Indexed enumeration did not return the first public row."
                );
                nint nextRow = 0;
                Assert(
                    ItemContainer(container, 3, rowProvider, 0, empty, &nextRow)
                        == WindowsUiaProvider.Ok
                        && nextRow != 0,
                    "Indexed enumeration did not accept its previous provider."
                );
                Release(rowProvider);
                Release(nextRow);
                items.Value = [];
                Refresh();
                nint missing = 1;
                Assert(
                    Grid(grid, 3, 0, 0, &missing) == WindowsUiaProvider.InvalidArgument
                        && missing == 0,
                    "A filtered-out row was returned from the old grid dimensions."
                );
                provider.Dispose();
                var count = -1;
                Assert(
                    Grid(grid, 4, &count) == WindowsUiaProvider.NotAvailable,
                    "Disposed table provider remained live."
                );
            }
            finally
            {
                Release(first);
                Release(last);
                Release(grid);
                Release(container);
                Release(selection);
                scene?.Dispose();
            }
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    [TestMethod]
    public void GridTableAndItemContainerUseNativeProviderSlots()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(UIA grid) failed.");
        var window = CreateWindow("Lucent UIA grid");
        try
        {
            using var composition = new Composition(new ReactiveGraph(), "uia-grid");
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            ElementIdentity gridIdentity = default;
            ElementIdentity? headerIdentity = null;
            var grid = ComponentRecipe.Create(
                "grid",
                (context, root) =>
                {
                    root.Present(theme, Style.Empty.Width(240).Height(80));
                    gridIdentity = new(composition.Epoch, root.Id);
                    context.Mount(
                        root,
                        ComponentContent.Create(
                            new ContentRecipe[]
                            {
                                Header(identity => headerIdentity = identity),
                                Cell(gridIdentity, 0, 0),
                            }
                        )
                    );
                    root.AttachBehaviors(
                        new GridProbe(() =>
                            headerIdentity is { } identity
                                ? new[] { identity }
                                : Array.Empty<ElementIdentity>()
                        )
                    );
                }
            );
            composition.Mount(composition.Root, theme, grid);
            using var renderer = new SkiaSceneRenderer();
            using var scene = SceneLayout.Project(composition, new(240, 100, 1), renderer);
            Assert(composition.Input.SetScene(scene), "Grid scene rejected.");
            using var dispatcher = new WindowsUiaDispatcher();
            using var provider = new WindowsUiaProvider(
                Hwnd(window),
                composition,
                dispatcher,
                "Grid"
            );
            provider.Refresh(scene);
            provider.SetRealizationRefresh(() => provider.Refresh(scene));

            nint gridPattern = 0,
                tablePattern = 0,
                itemContainer = 0,
                cell = 0,
                tableItem = 0;
            try
            {
                gridPattern = FindPattern(provider.InterfacePointer, 10006);
                Assert(gridPattern != 0, "IGridProvider was not advertised.");
                tablePattern = FindPattern(provider.InterfacePointer, 10012);
                Assert(tablePattern != 0, "ITableProvider was not advertised.");
                itemContainer = FindPattern(provider.InterfacePointer, 10019);
                Assert(itemContainer != 0, "IItemContainerProvider was not advertised.");
                cell = FindPattern(provider.InterfacePointer, 10007);
                Assert(cell != 0, "IGridItemProvider was not advertised.");
                tableItem = FindPattern(provider.InterfacePointer, 10013);
                Assert(tableItem != 0, "ITableItemProvider was not advertised.");

                var rows = -1;
                var columns = -1;
                Assert(
                    Grid(gridPattern, 4, &rows) == WindowsUiaProvider.Ok && rows == 1,
                    "IGridProvider.get_RowCount used the wrong native slot."
                );
                Assert(
                    Grid(gridPattern, 5, &columns) == WindowsUiaProvider.Ok && columns == 1,
                    "IGridProvider.get_ColumnCount used the wrong native slot."
                );

                nint cellProvider = 0;
                Assert(
                    Grid(gridPattern, 3, 0, 0, &cellProvider) == WindowsUiaProvider.Ok
                        && cellProvider != 0,
                    "IGridProvider.GetItem did not return the realized cell provider."
                );
                Release(cellProvider);

                var row = -1;
                var column = -1;
                var rowSpan = -1;
                var columnSpan = -1;
                Assert(
                    GridItem(cell, 3, &row) == WindowsUiaProvider.Ok && row == 0,
                    "IGridItemProvider.Row used the wrong native slot."
                );
                Assert(
                    GridItem(cell, 4, &column) == WindowsUiaProvider.Ok && column == 0,
                    "IGridItemProvider.Column used the wrong native slot."
                );
                Assert(
                    GridItem(cell, 5, &rowSpan) == WindowsUiaProvider.Ok && rowSpan == 1,
                    "IGridItemProvider.RowSpan used the wrong native slot."
                );
                Assert(
                    GridItem(cell, 6, &columnSpan) == WindowsUiaProvider.Ok && columnSpan == 1,
                    "IGridItemProvider.ColumnSpan used the wrong native slot."
                );
                nint containingGrid = 0;
                Assert(
                    GridItem(cell, 7, &containingGrid) == WindowsUiaProvider.Ok
                        && containingGrid != 0,
                    "IGridItemProvider.ContainingGrid used the wrong native slot."
                );
                Release(containingGrid);

                nint headers = 0;
                Assert(
                    Table(tablePattern, 3, &headers) == WindowsUiaProvider.Ok,
                    "ITableProvider.GetRowHeaders did not return an HRESULT."
                );
                Assert(
                    SafeArrayLength(headers) == 0,
                    "The first table contract unexpectedly exposed row headers."
                );
                _ = SafeArrayDestroy(headers);
                headers = 0;
                Assert(
                    Table(tablePattern, 4, &headers) == WindowsUiaProvider.Ok,
                    "ITableProvider.GetColumnHeaders did not return an HRESULT."
                );
                Assert(
                    SafeArrayLength(headers) == 1,
                    "ITableProvider omitted the retained column header."
                );
                var headerIndex = 0;
                nint header = 0;
                Assert(
                    SafeArrayGetElement(headers, &headerIndex, &header) >= 0 && header != 0,
                    "Column header SAFEARRAY did not contain a provider."
                );
                try
                {
                    var controlType = default(WindowsUiaProvider.RawVariant);
                    Assert(
                        Simple(header, 5, 30003, &controlType) == WindowsUiaProvider.Ok
                            && controlType.Type == 3
                            && controlType.Value == 50035,
                        "Column headings must expose HeaderItem, not the Header container control type."
                    );
                }
                finally
                {
                    Release(header);
                }
                _ = SafeArrayDestroy(headers);
                var major = -1;
                Assert(
                    Table(tablePattern, 5, &major) == WindowsUiaProvider.Ok && major == 0,
                    "ITableProvider.RowOrColumnMajor did not report row-major order."
                );

                headers = 0;
                Assert(
                    Table(tableItem, 3, &headers) == WindowsUiaProvider.Ok,
                    "ITableItemProvider.GetRowHeaderItems did not return an HRESULT."
                );
                _ = SafeArrayDestroy(headers);
                headers = 0;
                Assert(
                    Table(tableItem, 4, &headers) == WindowsUiaProvider.Ok,
                    "ITableItemProvider.GetColumnHeaderItems did not return an HRESULT."
                );
                Assert(
                    SafeArrayLength(headers) == 1,
                    "ITableItemProvider omitted the retained column header."
                );
                _ = SafeArrayDestroy(headers);

                var invalid = new WindowsUiaProvider.RawVariant { Type = 3, Value = 1 };
                nint found = 1;
                Assert(
                    ItemContainer(itemContainer, 3, 0, 0, invalid, &found)
                        == WindowsUiaProvider.InvalidArgument
                        && found == 0,
                    "IItemContainerProvider accepted a non-empty property search variant."
                );
                invalid = default;
                found = 0;
                Assert(
                    ItemContainer(itemContainer, 3, 0, 0, invalid, &found) == WindowsUiaProvider.Ok
                        && found != 0,
                    "IItemContainerProvider did not realize and return the requested first item."
                );
                Release(found);
            }
            finally
            {
                Release(gridPattern);
                Release(tablePattern);
                Release(itemContainer);
                Release(cell);
                Release(tableItem);
            }
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }

    private static ComponentRecipe Cell(ElementIdentity grid, int row, int column) =>
        ComponentRecipe.Create(
            "grid-cell",
            (context, root) =>
            {
                root.Present(context.Theme, Style.Empty.Width(120).Height(32));
                root.AttachBehaviors(new GridCellProbe(grid, row, column));
            }
        );

    private static ComponentRecipe Header(Action<ElementIdentity> register) =>
        ComponentRecipe.Create(
            "grid-header",
            (context, root) =>
            {
                root.Present(context.Theme, Style.Empty.Width(120).Height(32));
                register(new(root.Composition.Epoch, root.Id));
                root.AttachBehaviors(new GridHeaderProbe());
            }
        );

    private static int Grid(nint pointer, int slot, int* value) =>
        ((delegate* unmanaged[Stdcall]<nint, int*, int>)(*(nint**)pointer)[slot])(pointer, value);

    private static int Grid(nint pointer, int slot, int row, int column, nint* value) =>
        ((delegate* unmanaged[Stdcall]<nint, int, int, nint*, int>)(*(nint**)pointer)[slot])(
            pointer,
            row,
            column,
            value
        );

    private static int GridItem(nint pointer, int slot, int* value) =>
        ((delegate* unmanaged[Stdcall]<nint, int*, int>)(*(nint**)pointer)[slot])(pointer, value);

    private static int GridItem(nint pointer, int slot, nint* value) =>
        ((delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)pointer)[slot])(pointer, value);

    private static int Table(nint pointer, int slot, nint* value) =>
        ((delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)pointer)[slot])(pointer, value);

    private static int Table(nint pointer, int slot, int* value) =>
        ((delegate* unmanaged[Stdcall]<nint, int*, int>)(*(nint**)pointer)[slot])(pointer, value);

    private static int ItemContainer(
        nint pointer,
        int slot,
        nint startAfter,
        int propertyId,
        WindowsUiaProvider.RawVariant propertyValue,
        nint* value
    ) =>
        (
            (delegate* unmanaged[Stdcall]<
                nint,
                nint,
                int,
                WindowsUiaProvider.RawVariant,
                nint*,
                int>)
                (*(nint**)pointer)[slot]
        )(pointer, startAfter, propertyId, propertyValue, value);

    private sealed class GridProbe(Func<IReadOnlyList<ElementIdentity>> headers) : Behavior
    {
        public override string Name => "grid-probe";
        public override BehaviorOwnership Ownership =>
            BehaviorOwnership.Action | BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            context.SetSemantics(
                new(
                    SemanticRole.Table,
                    "Orders",
                    actions: SemanticAction.RealizeItem,
                    selection: new(false, false),
                    collection: new(1),
                    grid: new(1, 1, headers())
                )
            );
            context.OnSemanticCommand(command => command.Kind == SemanticCommandKind.RealizeItem);
        }
    }

    private sealed class GridHeaderProbe : Behavior
    {
        public override string Name => "grid-header-probe";
        public override BehaviorOwnership Ownership => BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context) =>
            context.SetSemantics(new(SemanticRole.HeaderItem, "Order"));
    }

    private sealed class GridCellProbe(ElementIdentity grid, int row, int column) : Behavior
    {
        public override string Name => "grid-cell-probe";
        public override BehaviorOwnership Ownership => BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context) =>
            context.SetSemantics(
                new(
                    SemanticRole.DataItem,
                    "Order cell",
                    collectionIndex: 0,
                    gridItem: new(grid, row, column)
                )
            );
    }
}
