using Lucent.Core;

var projectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
var reference = AppRoutes.Issue(projectId, 42, IssueView.Activity);
if (
    reference.Location.CanonicalText
    != "/projects/11111111-1111-1111-1111-111111111111/issues/42?view=Activity"
)
    throw new InvalidOperationException("Generated route formatting was not canonical.");

var table = RouteTable.Create(AppRoutes.Module.Patterns);
var parsed = RouteLocation.Parse(reference.Location.CanonicalText, table.Limits);
if (!parsed.Succeeded)
    throw new InvalidOperationException("Canonical route parsing failed.");
var matched = table.Match(parsed.Location!);
if (
    matched.Status != RouteMatchStatus.Matched
    || matched.Match is null
    || !ReferenceEquals(matched.Match.Pattern, reference.Pattern)
)
    throw new InvalidOperationException("Typed route matching did not preserve pattern identity.");

var descriptors = RouteDescriptorSet.Create(table, [AppRoutes.Module]);
var definition = descriptors.GetDefinition(matched.Match);
if (definition.Branch.Count != 2)
    throw new InvalidOperationException("Generated route ancestry was incomplete.");

var sawTypedContexts = false;
var requirements = ComponentRequirements
    .Context<RouteContext<ProjectRoute>>(Source("project"))
    .AndContext<RouteContext<IssueRoute>>(Source("issue"));
var content = ComponentRecipe.Defer(
    "generated-route-context-consumer",
    requirements,
    (_, contexts) =>
    {
        if (
            contexts.Previous.Parameters.ProjectId != projectId
            || contexts.Previous.Parameters.View != IssueView.Activity
            || contexts.Value.Parameters.ProjectId != projectId
            || contexts.Value.Parameters.IssueId != 42
            || contexts.Value.Parameters.View != IssueView.Activity
            || contexts.Previous.Definition.Id.Value != "project"
            || contexts.Value.Definition.Id.Value != "issue"
        )
            throw new InvalidOperationException("Generated typed route contexts were incorrect.");
        sawTypedContexts = true;
        return ComponentRecipe.Create("route-leaf", static (_, _) => { });
    }
);
content = definition.Branch[1].ProvideContext(matched.Match, content);
content = definition.Branch[0].ProvideContext(matched.Match, content);

var graph = new ReactiveGraph();
using var composition = new Composition(graph, "generated-navigation-aot");
using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
_ = composition.Mount(composition.Root, theme, content);
if (!sawTypedContexts)
    throw new InvalidOperationException("Generated route contexts were not resolved during mount.");

var dump = matched.Dump();
if (
    dump.Contains("11111111", StringComparison.Ordinal)
    || dump.Contains("42", StringComparison.Ordinal)
    || dump.Contains("Activity", StringComparison.Ordinal)
)
    throw new InvalidOperationException("Route diagnostics exposed parameter values.");

Console.WriteLine("generated-navigation-native-aot=pass");

static ComponentRequirementSource Source(string member) =>
    new(member, "generated-route-context", "Program.cs", 1, 1);
