using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class TableContracts
{
    [TestMethod]
    public void LargeTableRealizesBoundedRowsAndPublishesLogicalCellCoordinates()
    {
        using var composition = new Composition(new ReactiveGraph(), "table-large");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var source = Enumerable
            .Range(0, 10_000)
            .Select(index => new Row(index, "Item " + index))
            .ToArray();
        var formats = 0;
        var selected = SelectedKey.Some(7);
        composition.Mount(
            composition.Root,
            theme,
            Components.TableView(
                "Records",
                () => source,
                row => row.Id,
                new TableColumn<Row>[]
                {
                    new(
                        "name",
                        "Name",
                        row =>
                        {
                            formats++;
                            return row.Title;
                        },
                        180
                    ),
                    new(
                        "id",
                        "Identifier",
                        row => row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        100
                    ),
                    new("empty", "Empty", _ => "", 100),
                },
                () => selected,
                _ => { },
                options: new(rowHeight: 32),
                style: Style.Empty.Width(400).Height(240)
            )
        );
        using var first = Install(composition);
        var nodes = Nodes(composition.SemanticSnapshot()!).ToArray();
        var table = nodes.Single(node => node.Role == SemanticRole.Table);
        Assert.AreEqual(10_000, table.Grid!.RowCount);
        Assert.AreEqual(3, table.Grid.ColumnCount);
        Assert.HasCount(3, table.Grid.ColumnHeaders);
        Assert.AreEqual(10_000, table.Collection!.ItemCount);
        Assert.IsTrue(nodes.Count(node => node.Role == SemanticRole.DataItem) < 20);
        Assert.IsTrue(formats < 200, "Offscreen row text must not be formatted.");
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(
                table.Identity,
                new(SemanticCommandKind.RealizeItem, ItemIndex: 9999)
            )
        );
        using var last = Install(composition);
        nodes = Nodes(composition.SemanticSnapshot()!).ToArray();
        Assert.IsTrue(nodes.Any(node => node.CollectionIndex == 9999));
        Assert.HasCount(3, nodes.Where(node => node.GridItem?.Row == 9999).ToArray());
        Assert.IsTrue(nodes.Count(node => node.Role == SemanticRole.DataItem) < 20);
        Assert.AreEqual(7, selected.Value);
    }

    [TestMethod]
    public void SortingAndFilteringKeepCallerSelectionAndColumnResizeAlignsCells()
    {
        using var composition = new Composition(new ReactiveGraph(), "table-controlled");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var rows = composition.Root.Scope.Signal(
            new[] { new Row(1, "Alpha"), new Row(2, "Beta"), new Row(3, "Gamma") },
            "rows"
        );
        var selected = composition.Root.Scope.Signal(SelectedKey.Some(2), "selected");
        var sort = composition.Root.Scope.Signal<TableSort?>(null, "sort");
        var requests = new List<int>();
        var sortRequests = new List<TableSort>();
        composition.Mount(
            composition.Root,
            theme,
            Components.TableView(
                "Items",
                () => rows.Value,
                row => row.Id,
                new TableColumn<Row>[]
                {
                    new("name", "Name", row => row.Title, 120, 64, 200),
                    new(
                        "id",
                        "Identifier",
                        row => row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        100
                    ),
                },
                () => selected.Value,
                requests.Add,
                () => sort.Value,
                sortRequests.Add,
                style: Style.Empty.Width(400).Height(240)
            )
        );
        using var initial = Install(composition);
        var nodes = Nodes(composition.SemanticSnapshot()!).ToArray();
        var third = nodes.Single(node =>
            node.Role == SemanticRole.DataItem && node.CollectionIndex == 2
        );
        Assert.AreEqual(
            SemanticCommandResult.Requested,
            composition.ExecuteSemanticCommand(third.Identity, new(SemanticCommandKind.Select))
        );
        Assert.AreEqual(3, requests[^1]);
        Assert.AreEqual(2, selected.Value.Value);
        selected.Value = SelectedKey.Some(3);
        var heading = nodes.Single(node =>
            node.Role == SemanticRole.HeaderItem && node.Name == "Name"
        );
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(heading.Identity, new(SemanticCommandKind.Invoke))
        );
        Assert.AreEqual(new TableSort("name", TableSortDirection.Ascending), sortRequests.Single());
        Assert.AreEqual(1, rows.Value[0].Id);
        rows.Value = rows.Value.Reverse().ToArray();
        sort.Value = sortRequests[0];
        using var sorted = Install(composition);
        nodes = Nodes(composition.SemanticSnapshot()!).ToArray();
        Assert.AreEqual(
            0,
            nodes
                .Single(node => node.Role == SemanticRole.DataItem && node.Selected)
                .CollectionIndex
        );
        var handle = nodes.Single(node =>
            node.Role == SemanticRole.Splitter && node.Name == "Name column width"
        );
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(
                handle.Identity,
                new(SemanticCommandKind.SetRangeValue, NumericValue: 200)
            )
        );
        using var resized = Install(composition);
        nodes = Nodes(composition.SemanticSnapshot()!).ToArray();
        var headers = nodes.Where(node => node.Role == SemanticRole.HeaderItem).ToArray();
        var cells = nodes
            .Where(node => node.GridItem?.Row == 0)
            .OrderBy(node => node.GridItem!.Column)
            .ToArray();
        float X(SemanticSnapshot node) =>
            resized.Boxes.Single(box => box.Identity.ElementId == node.Identity.ElementId).Bounds.X;
        Assert.AreEqual(200f, X(headers[1]) - X(headers[0]), .01f);
        Assert.AreEqual(200f, X(cells[1]) - X(cells[0]), .01f);
        Assert.AreEqual(X(headers[0]), X(cells[0]), .01f);
        rows.Value = [new Row(1, "Alpha")];
        using var filtered = Install(composition);
        Assert.AreEqual(3, selected.Value.Value);
        Assert.IsFalse(Nodes(composition.SemanticSnapshot()!).Any(node => node.Selected));
    }

    [TestMethod]
    public void HorizontalOverflowScrollsHeadersAndCellsTogether()
    {
        using var composition = new Composition(new ReactiveGraph(), "table-horizontal");
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Mount(
            composition.Root,
            theme,
            Components.TableView(
                "Items",
                () => new[] { new Row(1, "Alpha") },
                row => row.Id,
                new TableColumn<Row>[]
                {
                    new("name", "Name", row => row.Title, 180),
                    new("id", "Identifier", _ => "1", 160),
                },
                () => SelectedKey.None<int>(),
                _ => { },
                style: Style.Empty.Width(200).Height(160)
            )
        );
        using var initial = Install(composition);
        var nodes = Nodes(composition.SemanticSnapshot()!).ToArray();
        var scroll = nodes.Single(node => node.Name == "Table columns");
        var heading = nodes.Single(node =>
            node.Role == SemanticRole.HeaderItem && node.Name == "Identifier"
        );
        var cell = nodes.Single(node => node.GridItem is { Row: 0, Column: 1 });
        float X(RetainedScene scene, SemanticSnapshot node) =>
            scene.Boxes.Single(box => box.Identity.ElementId == node.Identity.ElementId).Bounds.X;
        var before = X(initial, heading);
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(
                scroll.Identity,
                new(SemanticCommandKind.Scroll, Horizontal: 100)
            )
        );
        using var shifted = Install(composition);
        Assert.AreEqual(before - 100, X(shifted, heading), .01f);
        Assert.AreEqual(X(shifted, heading), X(shifted, cell), .01f);
    }

    [TestMethod]
    public void ColumnContractsRejectAmbiguousKeysAndInvalidGeometry()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticCollectionSnapshot(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticCollectionSnapshot(1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TableColumn<Row>("name", "Name", row => row.Title, float.NaN)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() => new TableViewOptions(rowHeight: 0));
        Assert.Throws<ArgumentException>(() =>
            Components.TableView(
                "Items",
                () => Array.Empty<Row>(),
                row => row.Id,
                new TableColumn<Row>[]
                {
                    new("same", "A", row => row.Title),
                    new("same", "B", row => row.Title),
                },
                () => SelectedKey.None<int>(),
                _ => { }
            )
        );
    }

    private static RetainedScene Install(Composition composition)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            composition.Flush();
            var scene = SceneLayout.Project(composition, new(420, 280, 1), new EmptyShaper());
            if (composition.Input.SetScene(scene))
                return scene;
            scene.Dispose();
        }
        throw new InvalidOperationException("Table layout did not converge.");
    }

    private static IEnumerable<SemanticSnapshot> Nodes(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Nodes(child))
            yield return descendant;
    }

    private sealed record Row(int Id, string Title);

    private sealed class EmptyShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request) =>
            request.Text.Length == 0
                ? new("empty", 0, 0, [])
                : new(
                    "table",
                    request.Text.Length,
                    request.FontSize,
                    [
                        new ShapedRun(
                            "table",
                            "table",
                            400,
                            5,
                            0,
                            "table",
                            0,
                            "table#0",
                            request.Direction,
                            request.Language,
                            request.FontSize,
                            0,
                            request.FontSize,
                            -request.FontSize,
                            0,
                            request.Text.Length,
                            [new(1, 0, 0, 0, request.Text.Length, 0, 0)]
                        ),
                    ]
                );
    }
}
