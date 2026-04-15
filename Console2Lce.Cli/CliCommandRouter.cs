namespace Console2Lce.Cli;

internal static class CliCommandRouter
{
    public static int Run(string[] args)
    {
        if (!CommandLineOptionsParser.TryParse(args, out CommandLineOptions? options, out string? error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            PrintUsage();
            return 1;
        }

        if (options is null || options.Command == CliCommand.Help)
        {
            PrintUsage();
            return 0;
        }

        return options.Command switch
        {
            CliCommand.Inspect => InspectCommandRunner.Run(options),
            CliCommand.Extract => ExtractCommandRunner.Run(options),
            CliCommand.Convert => ConvertCommandRunner.Run(options),
            _ => 1,
        };
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Console2Lce");
        Console.WriteLine("Xbox 360 Minecraft save tooling for inspection, extraction, and LCE conversion.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  Console2Lce inspect <path-to-save.bin-or-savegame.dat> --out <debug-dir>");
        Console.WriteLine("  Console2Lce extract <path-to-save.bin-or-savegame.dat> --out <extract-dir>");
        Console.WriteLine("  Console2Lce convert <path-to-save.bin> --out <lce-output-dir>");
    }

}
