namespace Console2Lce.Cli;

internal static class CommandLineOptionsParser
{
    public static bool TryParse(string[] args, out CommandLineOptions? options, out string? error)
    {
        options = null;
        error = null;

        if (args.Length == 0)
        {
            options = new CommandLineOptions(CliCommand.Help, null, null);
            return true;
        }

        if (IsHelpToken(args[0]))
        {
            options = new CommandLineOptions(CliCommand.Help, null, null);
            return true;
        }

        if (!TryParseCommand(args[0], out CliCommand command))
        {
            error = $"Unknown command '{args[0]}'.";
            return false;
        }

        if (args.Length < 2)
        {
            error = $"Expected an input path after '{args[0]}'.";
            return false;
        }

        string inputPath = args[1];
        string? outputPath = null;

        for (int index = 2; index < args.Length; index++)
        {
            string token = args[index];
            if (token.Equals("--out", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    error = "Expected a directory path after --out.";
                    return false;
                }

                outputPath = args[++index];
                continue;
            }

            error = $"Unknown argument '{token}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            error = "Expected --out <directory>.";
            return false;
        }

        options = new CommandLineOptions(command, inputPath, outputPath);
        return true;
    }

    private static bool IsHelpToken(string token)
    {
        return token.Equals("help", StringComparison.OrdinalIgnoreCase)
            || token.Equals("--help", StringComparison.OrdinalIgnoreCase)
            || token.Equals("-h", StringComparison.OrdinalIgnoreCase)
            || token.Equals("/?", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseCommand(string token, out CliCommand command)
    {
        if (token.Equals("inspect", StringComparison.OrdinalIgnoreCase))
        {
            command = CliCommand.Inspect;
            return true;
        }

        if (token.Equals("extract", StringComparison.OrdinalIgnoreCase))
        {
            command = CliCommand.Extract;
            return true;
        }

        if (token.Equals("convert", StringComparison.OrdinalIgnoreCase))
        {
            command = CliCommand.Convert;
            return true;
        }

        command = CliCommand.Help;
        return false;
    }
}
