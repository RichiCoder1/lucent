using Lucent.Core;

namespace Lucent.AuthoringSample;

public sealed partial class CounterPage
{
    private static readonly List<CounterPage> MountedPages = [];

    [State]
    public partial int Count { get; set; }

    public static int SetupCalls { get; private set; }

    public static int CleanupCalls { get; private set; }

    public void IncrementFromCompanion() => Count += 1;

    public void ResetFromCompanion() => Count = 0;

    partial void Setup(ComponentContext context)
    {
        SetupCalls++;
        MountedPages.Add(this);
        context.OnDispose(() =>
        {
            CleanupCalls++;
            MountedPages.Remove(this);
        });
    }

    public static void VerifyIndependentMounts()
    {
        var setupBefore = SetupCalls;
        var cleanupBefore = CleanupCalls;
        CounterPage first;
        CounterPage second;

        using (
            var firstComposition = new Composition(new ReactiveGraph(), "authoring-companion-first")
        )
        using (var firstTheme = new ThemeContext(firstComposition.Root.Scope, ControlThemes.Light))
        using (
            firstComposition.Mount(
                firstComposition.Root,
                firstTheme,
                Context.Provide(new AuthoringHostContext("Headless"), Application.Create())
            )
        )
        {
            first = MountedPages[^1];
            first.Count = 9;

            using var secondComposition = new Composition(
                new ReactiveGraph(),
                "authoring-companion-second"
            );
            using var secondTheme = new ThemeContext(
                secondComposition.Root.Scope,
                ControlThemes.Light
            );
            using (
                secondComposition.Mount(
                    secondComposition.Root,
                    secondTheme,
                    Context.Provide(new AuthoringHostContext("Headless"), Application.Create())
                )
            )
            {
                second = MountedPages[^1];
                AuthoringSampleChecks.Require(
                    !ReferenceEquals(first, second),
                    "Companion root factories reused one component instance."
                );
                AuthoringSampleChecks.Require(
                    first.Count == 9 && second.Count == 0,
                    "Companion component state leaked between independent mounts."
                );
            }
        }

        AuthoringSampleChecks.Require(
            SetupCalls == setupBefore + 2,
            "Companion component setup did not run once for each mount."
        );
        AuthoringSampleChecks.Require(
            CleanupCalls == cleanupBefore + 2,
            "Companion component cleanup did not run once for each disposed mount."
        );
        Console.WriteLine(
            "AUTHORING COMPANION PASS: partial state is isolated across two mounts and both setups clean up."
        );
    }
}

public static partial class AuthoringSampleCompanion
{
    static partial void VerifyCore()
    {
        CounterPage.VerifyIndependentMounts();
    }
}
