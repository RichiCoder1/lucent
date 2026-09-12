using Lucent.Platform.Windows;

namespace Lucent.ComponentBrowser;

internal sealed class ComponentBrowserLifecycle : IApplicationLifecycle
{
    public ValueTask<ComponentRecipe> StartAsync(ApplicationSession session) =>
        ValueTask.FromResult<ComponentRecipe>(
            ComponentBrowserStructure.Create(
                new WindowsFilePicker(session.Composition),
                new WindowsUriLauncher(new UriLaunchPolicy(["https"]))
            )
        );

    public ValueTask<bool> PrepareCloseAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(true);

    public ValueTask StopAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
