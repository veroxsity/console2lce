using Console2Lce;
using System.Text.Json;

namespace Console2Lce.Cli;

internal static class InspectCommandRunner
{
    public static int Run(CommandLineOptions options)
    {
        string inputPath = Path.GetFullPath(options.InputPath!);
        string outputPath = Path.GetFullPath(options.OutputPath!);

        byte[] packageBytes = File.ReadAllBytes(inputPath);
        StfsPackageMetadata metadata = StfsPackageDescriptorReader.Read(packageBytes);
        var reader = new StfsReader();
        IReadOnlyList<StfsFileEntry> entries = reader.EnumerateEntries(packageBytes);

        Directory.CreateDirectory(outputPath);
        string jsonPath = Path.Combine(outputPath, "stfs-files.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"Package: {metadata.PackageType}");
        Console.WriteLine($"Input:   {inputPath}");
        Console.WriteLine($"Output:  {outputPath}");
        Console.WriteLine($"Header:  0x{metadata.HeaderSize:X} (aligned 0x{metadata.HeaderAlignedSize:X})");
        Console.WriteLine($"FT blk:  0x{metadata.FileTableBlockNumber:X} x {metadata.FileTableBlockCount}");
        Console.WriteLine($"Entries: {entries.Count}");

        foreach (StfsFileEntry entry in entries)
        {
            Console.WriteLine($"- {entry.Name} size={entry.Size} start=0x{entry.StartingBlock:X}");
        }

        Console.WriteLine();
        Console.WriteLine($"Wrote {jsonPath}");
        return 0;
    }
}
