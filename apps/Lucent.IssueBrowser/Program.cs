using Lucent.Core;
using Lucent.Platform.Windows;
using Lucent.IssueBrowser;

try
{
    var graph = new ReactiveGraph();
    using var composition = IssueBrowserStructure.Create(graph, out var theme);
    return WindowsBootstrap.Run("Lucent Issue Browser", composition, theme);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Lucent M0 startup failed: {exception.Message}");
    return 1;
}
