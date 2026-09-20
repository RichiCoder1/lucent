using Lucent.Platform.Windows;

namespace Lucent.ComponentBrowser;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 0)
        {
            Console.Error.WriteLine("Usage: Lucent.ComponentBrowser");
            return 2;
        }

        try
        {
            return LucentApplication
                .CreateBuilder()
                .UseWindows(
                    new WindowsWindowOptions
                    {
                        Width = 1280,
                        Height = 840,
                        MinimumWidth = 760,
                        MinimumHeight = 520,
                    }
                )
                .SetTitle("Lucent Component Browser")
                .OnStart(start =>
                {
                    start
                        .ProvideRootContext<IFilePicker>(
                            new WindowsFilePicker(start.Session.Composition)
                        )
                        .ProvideRootContext<IUriLauncher>(
                            new WindowsUriLauncher(new UriLaunchPolicy(["https"]))
                        );
                    return ValueTask.CompletedTask;
                })
                .ConfigureRoot(
                    static (_, recipe) =>
                        ComponentRecipe.Create(
                            "component-browser-application",
                            (context, root) =>
                            {
                                root.Present(
                                    context.Theme,
                                    author: PresentationStyles.Surface.MainGrow(1).MainBasis(0)
                                );
                                context.Mount(root, recipe);
                            }
                        )
                )
                .Build(ComponentBrowserApplication.Create)
                .Run();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Lucent Component Browser failed: {exception}");
            return 1;
        }
    }
}
