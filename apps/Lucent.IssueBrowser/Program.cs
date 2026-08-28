using Lucent.Platform.Windows;

try
{
    return WindowsBootstrap.Run("Lucent Issue Browser — M0 (1.25x test presentation)");
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Lucent M0 startup failed: {exception.Message}");
    return 1;
}
