using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class ThemeMutationContracts
{
    [TestMethod]
    public void ThemeSettersDoNotSubscribeTheWritingEffectToTheirPreviousValues()
    {
        var graph = new ReactiveGraph();
        using var scope = graph.CreateScope("theme-writer");
        using var theme = new ThemeContext(scope, ControlThemes.Light);
        var revision = scope.Signal(0, "revision");
        var runs = 0;
        using var effect = scope.Effect(
            () =>
            {
                runs++;
                theme.Theme = new Theme("application-" + revision.Value);
                theme.Appearance = ThemeAppearance.Light;
                theme.PresentationMode = ControlPresentationMode.Standard;
            },
            "theme-application"
        );
        graph.Drain();
        Assert.AreEqual(1, runs);
        theme.Theme = ControlThemes.Dark;
        theme.Appearance = new(ThemeColorScheme.Dark, ThemeContrast.High);
        theme.PresentationMode = ControlPresentationMode.Minimal;
        graph.Drain();
        Assert.AreEqual(1, runs, "Writes alone must not create reactive dependencies.");
        revision.Value = 1;
        graph.Drain();
        Assert.AreEqual(2, runs, "The explicitly read application signal must remain reactive.");
    }
}
