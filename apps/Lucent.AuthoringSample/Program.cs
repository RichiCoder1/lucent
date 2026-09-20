using Lucent.Platform.Windows;

namespace Lucent.AuthoringSample;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var smoke = args.Length == 1 && args[0] == "--smoke";
        if (args.Length > 0 && !smoke)
        {
            Console.Error.WriteLine("Usage: Lucent.AuthoringSample [--smoke]");
            return 2;
        }

        try
        {
            return LucentApplication
                .CreateBuilder()
                .UseWindows(
                    new WindowsWindowOptions
                    {
                        Width = 1120,
                        Height = 760,
                        MinimumWidth = 720,
                        MinimumHeight = 520,
                    }
                )
                .SetTitle("Lucent Authoring Sample")
                .OnStart(start =>
                {
                    start.ProvideRootContext(new AuthoringHostContext("Windows"));
                    return ValueTask.CompletedTask;
                })
                .ConfigureRoot(
                    static (_, recipe) =>
                        ComponentRecipe.Create(
                            "authoring-sample-window-root",
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
                .OnMounted(session =>
                {
                    if (smoke)
                    {
                        AuthoringSampleSmoke.Verify(session);
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(250));
                            session.RequestClose();
                        });
                    }

                    return ValueTask.CompletedTask;
                })
                .OnDispose(_ =>
                {
                    if (smoke)
                        Console.WriteLine("AUTHORING SAMPLE CLEANUP");
                    return ValueTask.CompletedTask;
                })
                .Build(Application.Create)
                .Run();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Lucent Authoring Sample failed: {error}");
            return 1;
        }
    }
}
