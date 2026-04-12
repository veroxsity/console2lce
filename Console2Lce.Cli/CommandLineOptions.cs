namespace Console2Lce.Cli;

internal sealed record CommandLineOptions(
    CliCommand Command,
    string? InputPath,
    string? OutputPath);
