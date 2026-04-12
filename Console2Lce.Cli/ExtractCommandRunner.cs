using Console2Lce;

namespace Console2Lce.Cli;

internal static class ExtractCommandRunner
{
    public static int Run(CommandLineOptions options)
    {
        string inputPath = Path.GetFullPath(options.InputPath!);
        string outputPath = Path.GetFullPath(options.OutputPath!);

        Directory.CreateDirectory(outputPath);

        byte[] packageBytes = File.ReadAllBytes(inputPath);
        var reader = new StfsReader();
        byte[] savegameBytes = reader.ReadFile(packageBytes, "savegame.dat");
        string savegamePath = Path.Combine(outputPath, "savegame.dat");
        File.WriteAllBytes(savegamePath, savegameBytes);

        Console.WriteLine($"Extracted savegame.dat to {savegamePath}");
        Console.WriteLine($"Bytes: {savegameBytes.Length}");
        return 0;
    }
}
