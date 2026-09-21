using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class RouteMatchingContracts
{
    [TestMethod]
    public void LiteralConstrainedAndStringPrecedenceIgnoreAuthoredOrder()
    {
        var literal = Pattern("literal", Literal("issues"), Literal("new"));
        var integer = Pattern(
            "integer",
            Literal("issues"),
            Parameter("number", 0, RouteValueShape.Signed32)
        );
        var text = Pattern("text", Literal("issues"), Parameter("slug", 0, RouteValueShape.Text));
        var first = RouteTable.Create([text, integer, literal]);
        var second = RouteTable.Create([literal, integer, text]);

        Assert.AreEqual("literal", Match(first, "/issues/new").DefinitionId.Value);
        Assert.AreEqual("integer", Match(first, "/issues/42").DefinitionId.Value);
        Assert.AreEqual(42, Match(first, "/issues/42").GetValue(0).Signed32);
        Assert.AreEqual("text", Match(first, "/issues/readme").DefinitionId.Value);
        Assert.AreEqual(first.Dump(), second.Dump());
    }

    [TestMethod]
    public void BuiltInCodecsAcceptOnlyCanonicalSpellings()
    {
        var guid = Guid.Parse("8f14e45f-ea4d-4c4b-9b8b-5e42f563d934");
        var cases = new[]
        {
            (RouteValueShape.Signed32, "-2147483648", true),
            (RouteValueShape.Signed32, "2147483647", true),
            (RouteValueShape.Signed32, "2147483648", false),
            (RouteValueShape.Signed32, "01", false),
            (RouteValueShape.Signed32, "+1", false),
            (RouteValueShape.Signed32, "-0", false),
            (RouteValueShape.Signed64, "9223372036854775807", true),
            (RouteValueShape.Signed64, "9223372036854775808", false),
            (RouteValueShape.Uuid, guid.ToString("D"), true),
            (RouteValueShape.Uuid, guid.ToString("D").ToUpperInvariant(), false),
            (RouteValueShape.Boolean, "true", true),
            (RouteValueShape.Boolean, "True", false),
        };
        foreach (var (shape, value, accepted) in cases)
        {
            var table = RouteTable.Create([Pattern("value", Parameter("value", 0, shape))]);
            Assert.AreEqual(
                accepted ? RouteMatchStatus.Matched : RouteMatchStatus.NotFound,
                table.Match(RouteLocation.Parse("/" + value).Location!).Status,
                value
            );
        }
    }

    [TestMethod]
    public void RejectsOverlappingConstrainedShapesAndAcceptsProvenDisjointShapes()
    {
        var integerConflict = Assert.ThrowsExactly<RouteTableConfigurationException>(() =>
            RouteTable.Create([
                Pattern("int32", Parameter("value", 0, RouteValueShape.Signed32)),
                Pattern("int64", Parameter("value", 0, RouteValueShape.Signed64)),
            ])
        );
        Assert.AreEqual(
            RouteTableConfigurationErrorKind.AmbiguousPath,
            integerConflict.Errors[0].Kind
        );

        var boolEnumConflict = Assert.ThrowsExactly<RouteTableConfigurationException>(() =>
            RouteTable.Create([
                Pattern("bool", Parameter("value", 0, RouteValueShape.Boolean)),
                Pattern("enum", Parameter("value", 0, RouteValueShape.Enum("true", "other"))),
            ])
        );
        Assert.AreEqual(
            RouteTableConfigurationErrorKind.AmbiguousPath,
            boolEnumConflict.Errors[0].Kind
        );

        var disjoint = RouteTable.Create([
            Pattern("bool", Parameter("value", 0, RouteValueShape.Boolean)),
            Pattern("enum", Parameter("value", 0, RouteValueShape.Enum("open", "closed"))),
            Pattern("guid", Parameter("value", 0, RouteValueShape.Uuid)),
        ]);
        Assert.AreEqual("enum", Match(disjoint, "/open").DefinitionId.Value);
        Assert.AreEqual("bool", Match(disjoint, "/false").DefinitionId.Value);
    }

    [TestMethod]
    public void QuerySchemaCannotResolveAPathTie()
    {
        var first = Pattern(
            "first",
            [Literal("issues")],
            [RouteQueryPattern.Required("view", "view", 0, RouteValueShape.Text)]
        );
        var second = Pattern(
            "second",
            [Literal("issues")],
            [RouteQueryPattern.Required("tab", "tab", 0, RouteValueShape.Text)]
        );

        var error = Assert.ThrowsExactly<RouteTableConfigurationException>(() =>
            RouteTable.Create([first, second])
        );
        Assert.AreEqual(RouteTableConfigurationErrorKind.AmbiguousPath, error.Errors.Single().Kind);
    }

    [TestMethod]
    public void QueryMatchingUsesDeclaredSlotsDefaultsAndStrictScalarRules()
    {
        var pattern = Pattern(
            "issue",
            [Literal("issues")],
            [
                RouteQueryPattern.Required("number", "number", 0, RouteValueShape.Signed32),
                RouteQueryPattern.Optional(
                    "view",
                    "view",
                    1,
                    RouteValueShape.Text,
                    RouteValue.FromText("summary")
                ),
            ]
        );
        var table = RouteTable.Create([pattern]);

        var match = Match(table, "/issues?view=activity&number=42");
        Assert.AreEqual(42, match.GetValue(0).Signed32);
        Assert.AreEqual("activity", match.GetValue(1).Text);
        Assert.AreEqual("summary", Match(table, "/issues?number=42").GetValue(1).Text);

        AssertQueryError(table, "/issues?number=42&extra=x", RouteMatchErrorKind.UnknownQueryKey);
        AssertQueryError(
            table,
            "/issues?number=42&number=43",
            RouteMatchErrorKind.DuplicateQueryValue
        );
        AssertQueryError(table, "/issues?number", RouteMatchErrorKind.QueryValueRequired);
        AssertQueryError(table, "/issues?number=042", RouteMatchErrorKind.InvalidQueryValue);
        AssertQueryError(table, "/issues?view=activity", RouteMatchErrorKind.MissingRequiredQuery);
    }

    [TestMethod]
    public void BareAndExplicitEmptyStringQueryValuesRemainDistinctLocations()
    {
        var table = RouteTable.Create([
            Pattern(
                "search",
                [Literal("search")],
                [RouteQueryPattern.Required("q", "query", 0, RouteValueShape.Text)]
            ),
        ]);

        var bare = Match(table, "/search?q");
        var explicitEmpty = Match(table, "/search?q=");
        Assert.AreEqual(string.Empty, bare.GetValue(0).Text);
        Assert.AreEqual(string.Empty, explicitEmpty.GetValue(0).Text);
        Assert.AreNotEqual(bare.Location, explicitEmpty.Location);
    }

    [TestMethod]
    public void GeneratedFormattingUsesSlotsEscapingDefaultsAndDeclarationOrder()
    {
        var pattern = Pattern(
            "issue",
            [Literal("issues"), Parameter("slug", 0, RouteValueShape.Text)],
            [
                RouteQueryPattern.Optional(
                    "view",
                    "view",
                    1,
                    RouteValueShape.Text,
                    RouteValue.FromText("summary")
                ),
                RouteQueryPattern.Required("page", "page", 2, RouteValueShape.Signed32),
            ]
        );

        var compact = pattern.Format([
            RouteValue.FromText("café/notes"),
            RouteValue.FromText("summary"),
            RouteValue.FromSigned32(2),
        ]);
        Assert.AreEqual("/issues/caf%C3%A9%2Fnotes?page=2", compact.CanonicalText);

        var expanded = pattern.Format([
            RouteValue.FromText("item"),
            RouteValue.FromText("activity"),
            RouteValue.FromSigned32(2),
        ]);
        Assert.AreEqual("/issues/item?view=activity&page=2", expanded.CanonicalText);
        Assert.AreEqual(
            2,
            Match(RouteTable.Create([pattern]), expanded.CanonicalText).GetValue(2).Signed32
        );
    }

    [TestMethod]
    public void GeneratedFormattingFailuresAreStructuredAndRedacted()
    {
        var pattern = Pattern("item", Parameter("id", 0, RouteValueShape.Signed32));
        var wrongShape = Assert.ThrowsExactly<RouteFormatException>(() =>
            pattern.Format([RouteValue.FromText("secret")])
        );
        Assert.AreEqual(RouteFormatErrorKind.ValueShapeMismatch, wrongShape.Kind);
        Assert.AreEqual(0, wrongShape.CaptureSlot);
        Assert.IsFalse(wrongShape.ToString().Contains("secret", StringComparison.Ordinal));

        var tight = new RouteLocationLimits(4, 2, 8, 2, 8, 8);
        var bounded = Assert.ThrowsExactly<RouteFormatException>(() =>
            Pattern("text", Parameter("text", 0, RouteValueShape.Text))
                .Format([RouteValue.FromText("long")], tight)
        );
        Assert.AreEqual(RouteFormatErrorKind.LocationRejected, bounded.Kind);
    }

    [TestMethod]
    public void GeneratedFormattingPreflightsHugeValuesBeforeAllocatingCanonicalOutput()
    {
        var pattern = Pattern("text", Parameter("text", 0, RouteValueShape.Text));
        var huge = new string('x', 1_000_000);
        var before = GC.GetAllocatedBytesForCurrentThread();

        var error = Assert.ThrowsExactly<RouteFormatException>(() =>
            pattern.Format([RouteValue.FromText(huge)])
        );
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(RouteFormatErrorKind.LocationRejected, error.Kind);
        Assert.IsTrue(allocated < 100_000, $"Rejected formatting allocated {allocated} bytes.");
    }

    [TestMethod]
    public void MatchReappliesTheAuthoritativeTableLimits()
    {
        var loose = new RouteLocationLimits(32, 4, 32, 4, 8, 8);
        var strict = new RouteLocationLimits(4, 4, 32, 4, 8, 8);
        var location = RouteLocation.Parse("/abcdef", loose).Location!;
        var table = RouteTable.Create(
            [Pattern("text", Parameter("text", 0, RouteValueShape.Text))],
            strict
        );

        var result = table.Match(location);

        Assert.AreEqual(RouteMatchStatus.RejectedLocation, result.Status);
        Assert.AreEqual(RouteLocationErrorKind.InputTooLong, result.LocationError.Kind);
        Assert.IsNull(result.Match);
        Assert.AreEqual(0, result.Candidates.Count);
    }

    [TestMethod]
    public void TableRejectsAuthoredShapesThatCannotFitItsLimits()
    {
        var limits = new RouteLocationLimits(2048, 1, 2, 1, 2, 8);
        var error = Assert.ThrowsExactly<RouteTableConfigurationException>(() =>
            RouteTable.Create([Pattern("long", Literal("long"), Literal("path"))], limits)
        );

        Assert.AreEqual(
            RouteTableConfigurationErrorKind.PatternExceedsLimits,
            error.Errors.Single().Kind
        );
        Assert.AreEqual("long", error.Errors.Single().First.Value);
    }

    [TestMethod]
    public void PatternAndTableDefensivelyCopyAuthoredCollections()
    {
        RouteSegmentPattern[] segments = [Literal("before")];
        var pattern = Pattern("stable", segments);
        segments[0] = Literal("after");
        RoutePattern[] patterns = [pattern];
        var table = RouteTable.Create(patterns);
        patterns[0] = Pattern("replacement", Literal("replacement"));

        Assert.AreEqual("stable", Match(table, "/before").DefinitionId.Value);
        Assert.AreEqual(RouteMatchStatus.NotFound, table.Match(Location("/after")).Status);
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<RoutePattern>)table.Patterns)[0] = pattern
        );
    }

    [TestMethod]
    public void DumpsAreDeterministicAndNeverContainApplicationValues()
    {
        var first = RouteTable.Create([
            Pattern("text", Literal("items"), Parameter("value", 0, RouteValueShape.Text)),
            Pattern("integer", Literal("items"), Parameter("value", 0, RouteValueShape.Signed32)),
        ]);
        var second = RouteTable.Create(first.Patterns.Reverse().ToArray());
        var result = first.Match(Location("/items/secret?credential=value"));

        Assert.AreEqual(first.Dump(), second.Dump());
        Assert.AreEqual(
            result.Dump(),
            first.Match(Location("/items/secret?credential=value")).Dump()
        );
        Assert.IsFalse(result.Dump().Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(result.Dump().Contains("credential", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(result.ToString().Contains("value", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void GeneratedReferencesAlwaysFormatThroughTheirExactPattern()
    {
        var pattern = Pattern(
            "issue",
            Literal("issues"),
            Parameter("number", 0, RouteValueShape.Signed32)
        );
        var reference = RouteReference.Create(pattern, [RouteValue.FromSigned32(42)]);

        Assert.AreSame(pattern, reference.Pattern);
        Assert.AreEqual("/issues/42", reference.Location.CanonicalText);
        Assert.ThrowsExactly<RouteFormatException>(() =>
            RouteReference.Create(pattern, [RouteValue.FromText("42")])
        );
        Assert.IsFalse(reference.ToString().Contains("42", StringComparison.Ordinal));
    }

    [TestMethod]
    public void GeneratedBranchesMustOwnEveryCaptureExactlyOnce()
    {
        var pattern = Pattern(
            "issue",
            Literal("projects"),
            Parameter("project", 0, RouteValueShape.Signed32),
            Parameter("issue", 1, RouteValueShape.Signed32)
        );
        var source = new RouteDeclarationSource("Routes.cs", 1, 1);
        RouteLevelDescriptor Level(string id, params int[] slots) =>
            new(new(id), slots, source, static (_, _, content) => content);

        Assert.ThrowsExactly<ArgumentException>(() =>
            new RouteDefinitionDescriptor(pattern, [Level("project", 0), Level("issue", 0)])
        );
        Assert.ThrowsExactly<ArgumentException>(() =>
            new RouteDefinitionDescriptor(pattern, [Level("issue", 1)])
        );
        var descriptor = new RouteDefinitionDescriptor(
            pattern,
            [Level("project", 0), Level("issue", 1)]
        );
        Assert.AreEqual("project", descriptor.Branch[0].Id.Value);
    }

    [TestMethod]
    public void DescriptorSetRequiresOneToOnePatternReferencePairing()
    {
        var pattern = Pattern("home", Literal("home"));
        var level = new RouteLevelDescriptor(
            new("home"),
            [],
            new("Routes.cs", 1, 1),
            static (_, _, content) => content
        );
        var definition = new RouteDefinitionDescriptor(pattern, [level]);
        var module = new RouteModuleDescriptor(
            "Routes",
            RouteFallbackPolicy.Reject,
            new("Routes.cs", 1, 1),
            [definition]
        );
        var table = RouteTable.Create(module.Patterns);
        var set = RouteDescriptorSet.Create(table, [module]);

        Assert.AreSame(definition, set.GetDefinition(Match(table, "/home")));

        var equalButDistinct = Pattern("home", Literal("home"));
        var otherTable = RouteTable.Create([equalButDistinct]);
        Assert.ThrowsExactly<ArgumentException>(() =>
            RouteDescriptorSet.Create(otherTable, [module])
        );
    }

    [TestMethod]
    public void LiveDescriptorProvidesTypedContextThroughStandaloneOverload()
    {
        var pattern = Pattern(
            "issue",
            Literal("issues"),
            Parameter("number", 0, RouteValueShape.Signed32)
        );
        var table = RouteTable.Create([pattern]);
        var match = Match(table, "/issues/42");
        RouteContext<RouteParameters>? supplied = null;
        var level = new RouteLevelDescriptor(
            new("issue"),
            [0],
            new("Routes.cs", 1, 1),
            (definition, matched, content, live) =>
            {
                supplied = new RouteContext<RouteParameters>(
                    definition,
                    new RouteParameters(matched.GetValue(0).Signed32),
                    live
                );
                return content;
            }
        );

        _ = level.ProvideContext(match, ComponentRecipe.Create("content", static (_, _) => { }));

        Assert.IsNotNull(supplied);
        Assert.AreEqual(42, supplied!.Parameters.Number);
        Assert.IsNull(supplied!.ActiveEntry);
        Assert.IsNull(supplied!.Child);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void GeneratedDescriptorSupportsExistingContextProviderOverloads(bool explicitLive)
    {
        var pattern = Pattern(
            "issue",
            Literal("issues"),
            Parameter("number", 0, RouteValueShape.Signed32)
        );
        var match = Match(RouteTable.Create([pattern]), "/issues/42");
        var live = new RouteContextLiveState();
        RouteContextLiveState? receivedLive = null;
        RouteContext<RouteParameters>? created = null;
        object? provided = null;
        var level = new RouteLevelDescriptor(
            new("issue"),
            [0],
            new("Routes.cs", 1, 1),
            (definition, matched, currentLive) =>
            {
                receivedLive = currentLive;
                return created = new RouteContext<RouteParameters>(
                    definition,
                    new RouteParameters(matched.GetValue(0).Signed32),
                    currentLive
                );
            },
            (context, content) =>
            {
                provided = context;
                return content;
            }
        );
        var content = ComponentRecipe.Create("content", static (_, _) => { });

        var result = explicitLive
            ? level.ProvideContext(match, content, live)
            : level.ProvideContext(match, content);

        Assert.AreSame(content, result);
        Assert.IsNotNull(created);
        Assert.AreEqual(42, created.Parameters.Number);
        Assert.AreSame(created, provided);
        Assert.IsNotNull(receivedLive);
        if (explicitLive)
            Assert.AreSame(live, receivedLive);
    }

    private static RouteSegmentPattern Literal(string value) =>
        RouteSegmentPattern.LiteralSegment(value);

    private static RouteSegmentPattern Parameter(string name, int slot, RouteValueShape shape) =>
        RouteSegmentPattern.Parameter(name, slot, shape);

    private static RoutePattern Pattern(
        string id,
        params ReadOnlySpan<RouteSegmentPattern> segments
    ) => Pattern(id, segments.ToArray(), []);

    private static RoutePattern Pattern(
        string id,
        IReadOnlyList<RouteSegmentPattern> segments,
        IReadOnlyList<RouteQueryPattern> query
    ) => RoutePattern.Create(new(id), segments, query);

    private static RouteLocation Location(string text) => RouteLocation.Parse(text).Location!;

    private static RouteMatch Match(RouteTable table, string text)
    {
        var result = table.Match(Location(text));
        Assert.AreEqual(RouteMatchStatus.Matched, result.Status, result.Dump());
        return result.Match!;
    }

    private static void AssertQueryError(RouteTable table, string text, RouteMatchErrorKind error)
    {
        var result = table.Match(Location(text));
        Assert.AreEqual(RouteMatchStatus.RejectedQuery, result.Status);
        Assert.AreEqual(error, result.Error.Kind);
    }

    private readonly record struct RouteParameters(int Number);
}
