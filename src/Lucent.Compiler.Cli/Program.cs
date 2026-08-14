namespace Lucent.Compiler.Cli;

internal static class Program
{
    public static int Main(string[] args) =>
        CompilerCli.Run(args, Console.Out, Console.Error);
}
