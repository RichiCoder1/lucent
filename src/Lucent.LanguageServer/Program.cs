namespace Lucent.LanguageServer;

internal static class Program
{
    public static async Task<int> Main()
    {
        return await LanguageServer.RunAsync(
            Console.OpenStandardInput(),
            Console.OpenStandardOutput());
    }
}
