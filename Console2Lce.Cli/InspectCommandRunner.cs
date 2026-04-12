using Console2Lce;

namespace Console2Lce.Cli;

internal static class InspectCommandRunner
{
    public static int Run(CommandLineOptions options)
    {
        string inputPath = Path.GetFullPath(options.InputPath!);
        string outputPath = Path.GetFullPath(options.OutputPath!);

        byte[] packageBytes = File.ReadAllBytes(inputPath);
        StfsPackageType packageType = StfsPackageMagicReader.ReadPackageType(packageBytes);
        IReadOnlyList<StfsDirectoryEntry> entries = StfsDirectoryScanner.ScanHeuristicEntries(packageBytes);

        Console.WriteLine($"Package: {packageType}");
        Console.WriteLine($"Input:   {inputPath}");
        Console.WriteLine($"Output:  {outputPath}");
        Console.WriteLine($"Entries: {entries.Count}");

        foreach (StfsDirectoryEntry entry in entries)
        {
            Console.WriteLine($"- {entry.Name} @ 0x{entry.EntryOffset:X} size~{entry.FileSizeCandidate}");
        }

        Console.WriteLine();
        Console.WriteLine("This is a heuristic STFS directory probe based on the current sample.");
        Console.WriteLine("Next step is replacing it with a header-driven reader and real file block extraction.");
        return 0;
    }
}
