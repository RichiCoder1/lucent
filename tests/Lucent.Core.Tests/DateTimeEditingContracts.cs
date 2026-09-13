using System.Globalization;
using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class DateTimeEditingContracts
{
    [TestMethod]
    public void CalendarPopupKeepsCompactNavigationAndUniformDayGeometry()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "calendar-geometry");
        ConfigureImages(composition);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        composition.Mount(
            composition.Root,
            theme,
            Components.DatePicker(
                "Due date",
                static () => new DateOnly(2024, 2, 15),
                _ => { },
                new DatePickerOptions(
                    CultureInfo.GetCultureInfo("en-US"),
                    today: static () => new(2024, 2, 15)
                )
            )
        );
        using var ownerScene = Install(composition, graph);
        var open = Nodes(composition.SemanticSnapshot()!)
            .Single(node =>
                node.Role == SemanticRole.Button
                && node.Name.StartsWith("Open calendar", StringComparison.Ordinal)
            );
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(open.Identity, new(SemanticCommandKind.Invoke))
        );
        graph.Drain();

        var request = composition.Input.ActiveSurface!;
        var measured = request.Measure(new DateShaper(), new(600, 500, 1));
        Assert.IsTrue(
            measured.Width <= 280 && measured.Height <= 270,
            $"The stock calendar measured {measured.Width} by {measured.Height}."
        );
        var popup = request.CreateComposition();
        using var popupScene = Install(popup, graph);
        var semantics = Nodes(popup.SemanticSnapshot()!).ToArray();
        var previous = semantics.Single(node => node.Name == "Previous month");
        var next = semantics.Single(node => node.Name == "Next month");
        Assert.AreEqual(
            32f,
            popupScene
                .Boxes.Single(box => box.Identity.ElementId == previous.Identity.ElementId)
                .Bounds.Width
        );
        Assert.AreEqual(
            32f,
            popupScene
                .Boxes.Single(box => box.Identity.ElementId == next.Identity.ElementId)
                .Bounds.Width
        );

        var dayBounds = semantics
            .Where(node => node.Role == SemanticRole.ListItem)
            .Select(node =>
                popupScene
                    .Boxes.Single(box => box.Identity.ElementId == node.Identity.ElementId)
                    .Bounds
            )
            .ToArray();
        Assert.HasCount(42, dayBounds);
        Assert.IsTrue(
            dayBounds.All(bounds => bounds.Width == 32 && bounds.Height == 32),
            "Calendar days must use uniform compact cells."
        );

        var weekdayNames = Enumerable
            .Range(0, 7)
            .Select(index =>
                CultureInfo.GetCultureInfo("en-US").DateTimeFormat.AbbreviatedDayNames[index]
            )
            .ToArray();
        var weekdayText = SceneNodes(popupScene.Nodes)
            .OfType<TextSceneNode>()
            .Where(node =>
                node.Text.SourceText is { } text
                && weekdayNames.Contains(text, StringComparer.Ordinal)
            )
            .ToDictionary(node => node.Text.SourceText!, StringComparer.Ordinal);
        Assert.HasCount(7, weekdayText);
        for (var column = 0; column < 7; column++)
        {
            var header = weekdayText[weekdayNames[column]];
            var day = dayBounds[column];
            Assert.AreEqual(
                day.X + day.Width / 2,
                header.Bounds.X + header.Text.Width / 2,
                .001f,
                $"The {weekdayNames[column]} header was not centered over calendar column {column}."
            );
        }
    }

    [TestMethod]
    public void DatePickerCalendarKeyboardCommitsFocusedDateAndKeepsControlledLag()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "date-picker");
        ConfigureImages(composition);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var applied = graph.Signal<DateOnly?>(new(2024, 2, 29), "applied");
        DateOnly? requested = null;
        composition.Mount(
            composition.Root,
            theme,
            Components.DatePicker(
                "Due date",
                () => applied.Value,
                value => requested = value,
                new DatePickerOptions(
                    CultureInfo.GetCultureInfo("en-US"),
                    today: static () => new(2024, 2, 15)
                )
            )
        );
        using var ownerScene = Install(composition, graph);
        var open = Nodes(composition.SemanticSnapshot()!)
            .Single(node =>
                node.Role == SemanticRole.Button
                && node.Name.StartsWith("Open calendar", StringComparison.Ordinal)
            );
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(open.Identity, new(SemanticCommandKind.Invoke))
        );
        graph.Drain();
        var request = composition.Input.ActiveSurface!;
        var popup = request.CreateComposition();
        using var popupScene = Install(popup, graph);
        var calendar = Nodes(popup.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.Calendar);
        Assert.IsTrue(
            calendar.Description!.Contains("February 29, 2024", StringComparison.Ordinal)
        );
        Assert.IsTrue(calendar.Selection!.IsSelectionRequired);
        var marchFirst = Nodes(popup.SemanticSnapshot()!)
            .Single(node =>
                node.Role == SemanticRole.ListItem
                && node.Name.Contains("March 1", StringComparison.Ordinal)
            );
        var marchFirstBounds = popupScene
            .Boxes.Single(box => box.Identity.ElementId == marchFirst.Identity.ElementId)
            .Bounds;
        Assert.IsFalse(
            popup
                .Input.DispatchPointer(
                    new(PointerCommandKind.Up, 41, marchFirstBounds.X + 1, marchFirstBounds.Y + 1)
                )
                .Handled
        );
        Assert.IsNull(requested);
        Assert.IsTrue(
            popup
                .Input.DispatchPointer(
                    new(
                        PointerCommandKind.Down,
                        42,
                        marchFirstBounds.X + 1,
                        marchFirstBounds.Y + 1,
                        PointerButton.Primary
                    )
                )
                .Handled
        );
        Assert.IsTrue(
            popup
                .Input.DispatchPointer(
                    new(
                        PointerCommandKind.Cancel,
                        42,
                        marchFirstBounds.X + 1,
                        marchFirstBounds.Y + 1
                    )
                )
                .Handled
        );
        Assert.IsNull(requested);
        using var refreshedPopupScene = Install(popup, graph);
        Assert.IsTrue(popup.Input.MoveFocus(FocusTraversalDirection.Next));
        Assert.IsTrue(popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Right)).Handled);
        graph.Drain();
        using var movedPopupScene = Install(popup, graph);
        Assert.IsTrue(popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Enter)).Handled);
        graph.Drain();
        Assert.AreEqual(new DateOnly(2024, 3, 1), requested);
        Assert.AreEqual(new DateOnly(2024, 2, 29), applied.Value);
    }

    [TestMethod]
    public void DateDraftRejectsInvalidAndOutOfRangeWithoutChangingAppliedValue()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("date-draft");
        var applied = graph.Signal<DateOnly?>(new(2024, 2, 29), "applied");
        DateOnly? requested = null;
        var options = new DatePickerOptions(
            CultureInfo.GetCultureInfo("en-US"),
            minimum: new(2024, 1, 1),
            maximum: new(2024, 12, 31),
            today: static () => new(2024, 6, 15)
        );
        var session = new DateEditSession(
            scope,
            () => applied.Value,
            value => requested = value,
            options,
            "date"
        );

        session.Edit("2/29/2023");
        Assert.IsFalse(session.Commit());
        Assert.AreEqual(ValidationStatus.Invalid, session.Validation.Status);
        Assert.IsNull(requested);
        Assert.AreEqual(new DateOnly(2024, 2, 29), applied.Value);
        session.Edit("1/1/2025");
        Assert.IsFalse(session.Commit());
        Assert.IsNull(requested);
        session.Edit("2/29");
        Assert.IsFalse(session.Commit());
        Assert.AreEqual(ValidationStatus.Invalid, session.Validation.Status);
    }

    [TestMethod]
    public void DatePickerEscapeDismissesMovedCalendarWithoutRequestingAValue()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "date-picker-escape");
        ConfigureImages(composition);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        DateOnly? requested = null;
        composition.Mount(
            composition.Root,
            theme,
            Components.DatePicker(
                "Due date",
                static () => new DateOnly(2024, 2, 29),
                value => requested = value,
                new DatePickerOptions(
                    CultureInfo.GetCultureInfo("en-US"),
                    today: static () => new(2024, 2, 15)
                )
            )
        );
        using var ownerScene = Install(composition, graph);
        var open = Nodes(composition.SemanticSnapshot()!)
            .Single(node =>
                node.Role == SemanticRole.Button
                && node.Name.StartsWith("Open calendar", StringComparison.Ordinal)
            );
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            composition.ExecuteSemanticCommand(open.Identity, new(SemanticCommandKind.Invoke))
        );
        graph.Drain();
        var request = composition.Input.ActiveSurface!;
        var popup = request.CreateComposition();
        using var initialPopupScene = Install(popup, graph);
        var calendar = Nodes(popup.SemanticSnapshot()!)
            .Single(node => node.Role == SemanticRole.Calendar);
        Assert.AreEqual(
            SemanticCommandResult.Applied,
            popup.ExecuteSemanticCommand(calendar.Identity, new(SemanticCommandKind.Focus))
        );
        Assert.IsTrue(popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Right)).Handled);
        graph.Drain();
        using var movedPopupScene = Install(popup, graph);

        Assert.IsTrue(popup.Input.DispatchKey(new(KeyCommandKind.Down, Key.Escape)).Handled);
        Assert.IsNull(requested);
        Assert.IsNull(composition.Input.ActiveSurface);
    }

    [TestMethod]
    public void CalendarUsesCultureWeekStartAcrossLeapMonthKeyboardMovement()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("calendar");
        var options = new DatePickerOptions(
            CultureInfo.GetCultureInfo("en-GB"),
            today: static () => new(2024, 2, 29)
        );
        var calendar = new CalendarState(scope, options, new(2024, 2, 29), "calendar");
        var days = calendar.Days(new(2024, 2, 29));

        Assert.AreEqual(DayOfWeek.Monday, days[0].Date!.Value.DayOfWeek);
        Assert.IsTrue(days.Single(day => day.Date == new DateOnly(2024, 2, 29)).IsToday);
        Assert.IsTrue(calendar.MoveDays(1));
        Assert.AreEqual(new DateOnly(2024, 3, 1), calendar.Focused);
        Assert.IsTrue(calendar.MoveMonth(1));
        Assert.AreEqual(new DateOnly(2024, 4, 1), calendar.Focused);
        Assert.IsTrue(calendar.MoveWeekEdge(end: true));
        Assert.AreEqual(DayOfWeek.Sunday, calendar.Focused.DayOfWeek);
    }

    [TestMethod]
    public void CalendarBoundsNavigationAndSnapshotsTodayWithoutDuplicatingBoundaryDays()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("calendar-boundaries");
        var todayReads = 0;
        var options = new DatePickerOptions(
            CultureInfo.InvariantCulture,
            today: () =>
            {
                todayReads++;
                return DateOnly.MinValue;
            }
        );
        var minimum = new CalendarState(scope, options, DateOnly.MinValue, "minimum");
        var minimumDays = minimum.Days(DateOnly.MinValue);

        Assert.AreEqual(1, todayReads);
        Assert.IsFalse(minimum.MoveDays(-1));
        Assert.IsFalse(minimum.MoveMonth(-1));
        Assert.IsTrue(minimumDays.Any(day => day.Date is null && !day.IsEnabled));
        Assert.AreEqual(1, minimumDays.Count(day => day.Date == DateOnly.MinValue));

        var maximum = new CalendarState(scope, options, DateOnly.MaxValue, "maximum");
        var maximumDays = maximum.Days(DateOnly.MaxValue);
        Assert.AreEqual(2, todayReads);
        Assert.IsFalse(maximum.MoveDays(1));
        Assert.IsFalse(maximum.MoveMonth(1));
        Assert.IsTrue(maximumDays.Any(day => day.Date is null && !day.IsEnabled));
        Assert.AreEqual(1, maximumDays.Count(day => day.Date == DateOnly.MaxValue));
    }

    [TestMethod]
    public void TimeDraftUsesCultureAndBoundedStepWhileCallerStaysAuthoritative()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("time-draft");
        var applied = graph.Signal<TimeOnly?>(new(9, 0), "applied");
        TimeOnly? requested = null;
        var options = new TimePickerOptions(
            CultureInfo.GetCultureInfo("en-US"),
            minimum: new(8, 0),
            maximum: new(10, 0),
            step: TimeSpan.FromMinutes(30)
        );
        var session = new TimeEditSession(
            scope,
            () => applied.Value,
            value => requested = value,
            options,
            "time"
        );

        Assert.IsTrue(session.Step(1));
        Assert.AreEqual(new TimeOnly(9, 30), requested);
        Assert.AreEqual(new TimeOnly(9, 0), applied.Value);
        Assert.IsTrue(session.Draft.Contains("9:30", StringComparison.Ordinal));
        Assert.IsTrue(session.Step(1));
        Assert.AreEqual(new TimeOnly(10, 0), requested);
        Assert.IsTrue(session.Step(1));
        Assert.AreEqual(new TimeOnly(10, 0), requested);
        session.Edit("not a time");
        Assert.IsFalse(session.Commit());
        Assert.AreEqual(ValidationStatus.Invalid, session.Validation.Status);
        Assert.AreEqual(new TimeOnly(9, 0), applied.Value);
    }

    [TestMethod]
    public void TimeDraftPreservesUneditedSecondsAndRepeatedSubMinuteSteps()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("precise-time");
        var applied = new TimeOnly(13, 30, 5);
        var requests = new List<TimeOnly?>();
        var session = new TimeEditSession(
            scope,
            () => applied,
            requests.Add,
            new TimePickerOptions(
                CultureInfo.GetCultureInfo("en-US"),
                step: TimeSpan.FromMilliseconds(500)
            ),
            "precise-time"
        );

        Assert.IsTrue(session.Commit());
        Assert.AreEqual(applied, requests[0]);
        Assert.IsTrue(session.Step(1));
        Assert.AreEqual(new TimeOnly(13, 30, 5, 500), requests[1]);
        Assert.IsTrue(session.Step(1));
        Assert.AreEqual(new TimeOnly(13, 30, 6), requests[2]);
    }

    [TestMethod]
    public void NullableDateAndTimeClearingRequireExplicitPolicy()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("nullable-date-time");
        DateOnly? dateRequest = new(2024, 1, 1);
        var date = new DateEditSession(
            scope,
            static () => null,
            value => dateRequest = value,
            new DatePickerOptions(CultureInfo.InvariantCulture, allowNull: true),
            "date"
        );
        date.Edit("");
        Assert.IsTrue(date.Commit());
        Assert.IsNull(dateRequest);

        TimeOnly? timeRequest = new(9, 0);
        var time = new TimeEditSession(
            scope,
            static () => null,
            value => timeRequest = value,
            new TimePickerOptions(CultureInfo.InvariantCulture, allowNull: true),
            "time"
        );
        time.Edit("");
        Assert.IsTrue(time.Commit());
        Assert.IsNull(timeRequest);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new TimeEditSession(
                scope,
                static () => null,
                _ => { },
                new TimePickerOptions(),
                "required-time"
            )
        );
    }

    [TestMethod]
    public void TimePickerArrowKeysStepDraftWithoutAssumingCallerAcceptance()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "time-picker");
        ConfigureImages(composition);
        using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
        var applied = graph.Signal<TimeOnly?>(new(9, 0), "applied");
        TimeOnly? requested = null;
        composition.Mount(
            composition.Root,
            theme,
            Components.TimePicker(
                "Start time",
                () => applied.Value,
                value => requested = value,
                new TimePickerOptions(
                    CultureInfo.GetCultureInfo("en-US"),
                    step: TimeSpan.FromMinutes(30)
                )
            )
        );
        using var scene = Install(composition, graph);
        Assert.IsTrue(composition.Input.MoveFocus(FocusTraversalDirection.Next));
        Assert.IsTrue(composition.Input.DispatchKey(new(KeyCommandKind.Down, Key.Up)).Handled);
        graph.Drain();
        Assert.AreEqual(new TimeOnly(9, 30), requested);
        Assert.AreEqual(new TimeOnly(9, 0), applied.Value);
        Assert.IsTrue(
            Nodes(composition.SemanticSnapshot()!)
                .Single(node => node.Role == SemanticRole.Spinner)
                .Value!.Contains("9:30", StringComparison.Ordinal)
        );
    }

    private static RetainedScene Install(Composition composition, ReactiveGraph graph)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            graph.Drain();
            var scene = SceneLayout.Project(composition, new(600, 500, 1), new DateShaper());
            if (composition.Input.SetScene(scene))
                return scene;
            scene.Dispose();
        }
        throw new AssertFailedException("Could not install a stable date scene.");
    }

    private static void ConfigureImages(Composition composition) =>
        composition.ConfigureImages(new ImageCache(new ImmediatePreparer()));

    private static IEnumerable<SemanticSnapshot> Nodes(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Nodes(child))
            yield return descendant;
    }

    private static IEnumerable<SceneNode> SceneNodes(IEnumerable<SceneNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            var children = node switch
            {
                ClipSceneNode clip => clip.Children,
                OpacitySceneNode opacity => opacity.Children,
                _ => null,
            };
            if (children is not null)
                foreach (var child in SceneNodes(children))
                    yield return child;
        }
    }

    private sealed class DateShaper : ITextShaper
    {
        public ShapedText Shape(TextMeasureRequest request)
        {
            if (request.Text.Length == 0)
                return new("date-empty", 0, request.FontSize, []);
            var glyph = new ShapedGlyph(1, 0, 0, 0, request.Text.Length, 0, 0);
            var run = new ShapedRun(
                "date",
                "date",
                400,
                5,
                0,
                "date",
                0,
                "date#0",
                request.Direction,
                request.Language,
                request.FontSize,
                0,
                request.FontSize,
                -request.FontSize,
                0,
                request.Text.Length,
                [glyph]
            );
            return new("date", request.Text.Length, request.FontSize, [run]);
        }
    }

    private sealed class ImmediatePreparer : IImagePreparer
    {
        public ValueTask<PreparedImage> PrepareAsync(
            ImagePreparationRequest request,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult<PreparedImage>(new RasterImage(1, 1, [0, 0, 0, 255]));
    }
}
