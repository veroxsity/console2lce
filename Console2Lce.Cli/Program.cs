namespace Console2Lce.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        return CliCommandRouter.Run(args);
    }
}
