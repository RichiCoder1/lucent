using IntegratedFixture;
using Lucent.Core;

Func<ComponentRecipe> rootFactory = CompanionApp.Create;
using var composition = new Composition(new ReactiveGraph(), "companion-integrated");
using var theme = new ThemeContext(composition.Root.Scope, new Theme("integrated"));
using (composition.Mount(composition.Root, theme, rootFactory()))
using (composition.Mount(composition.Root, theme, rootFactory()))
{
    var names = Flatten(composition.SemanticSnapshot()!).Select(static node => node.Name).ToArray();
    if (
        MountRegistry.Distinct != 2
        || ((CompanionApp)MountRegistry.Last!).Organization != "companion"
        || ((CompanionApp)MountRegistry.Last!).CompanionCount != 3
        || CompanionApp.SetupCalls != 2
        || names.Count(static name => name == "companion|3|{\"Name\":\"Ada\"}|/home") != 2
        || names.Count(static name => name == "real markup") != 2
    )
        return 1;
}
if (CompanionApp.CleanupCalls != 2)
    return 1;
Console.WriteLine(
    "COMPANION PASS: named partial method-group root, distinct mounts, JSON, route, and real markup."
);
return 0;

static IEnumerable<SemanticSnapshot> Flatten(SemanticSnapshot snapshot) =>
    new[] { snapshot }.Concat(snapshot.Children.SelectMany(Flatten));
