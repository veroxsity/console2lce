using Console2Lce;
using System.Text.Json;

namespace Console2Lce.Cli;

internal static class InspectCommandRunner
{
    public static int Run(CommandLineOptions options)
    {
        string inputPath = Path.GetFullPath(options.InputPath!);
        string outputPath = Path.GetFullPath(options.OutputPath!);
        var layout = new DebugArtifactLayout(outputPath);

        byte[] packageBytes = File.ReadAllBytes(inputPath);
        StfsPackageMetadata metadata = StfsPackageDescriptorReader.Read(packageBytes);
        var reader = new StfsReader();
        IReadOnlyList<StfsFileEntry> entries = reader.EnumerateEntries(packageBytes);
        byte[] savegameBytes = reader.ReadFile(packageBytes, "savegame.dat");
        var probeService = new SavegameProbeService();
        SavegameProbeResult probeResult = probeService.Probe(savegameBytes);

        Directory.CreateDirectory(layout.OutputDirectory);
        File.WriteAllText(layout.StfsFilesJsonPath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllBytes(layout.SavegameDatPath, savegameBytes);
        File.WriteAllText(layout.SavegameProbeJsonPath, JsonSerializer.Serialize(probeResult.Report, new JsonSerializerOptions { WriteIndented = true }));

        if (probeResult.DecompressedBytes is not null)
        {
            File.WriteAllBytes(layout.SavegameDecompressedPath, probeResult.DecompressedBytes);
        }

        Console.WriteLine($"Package: {metadata.PackageType}");
        Console.WriteLine($"Input:   {inputPath}");
        Console.WriteLine($"Output:  {layout.OutputDirectory}");
        Console.WriteLine($"Header:  0x{metadata.HeaderSize:X} (aligned 0x{metadata.HeaderAlignedSize:X})");
        Console.WriteLine($"FT blk:  0x{metadata.FileTableBlockNumber:X} x {metadata.FileTableBlockCount}");
        Console.WriteLine($"Entries: {entries.Count}");

        foreach (StfsFileEntry entry in entries)
        {
            Console.WriteLine($"- {entry.Name} size={entry.Size} start=0x{entry.StartingBlock:X}");
        }

        Console.WriteLine();
        Console.WriteLine($"savegame.dat: {savegameBytes.Length} bytes");
        Console.WriteLine($"Probe:        {(probeResult.Report.HasSuccessfulDecompression ? "success" : "no match")}");

        if (probeResult.Report.HasSuccessfulDecompression)
        {
            Console.WriteLine($"Decoder:      {probeResult.Report.RecommendedEnvelope} / {probeResult.Report.RecommendedDecoder}");
            Console.WriteLine($"Wrote         {layout.SavegameDecompressedPath}");
        }
        else
        {
            Console.WriteLine("Decoder:      unresolved");
        }

        Console.WriteLine();
        Console.WriteLine($"Wrote {layout.StfsFilesJsonPath}");
        Console.WriteLine($"Wrote {layout.SavegameDatPath}");
        Console.WriteLine($"Wrote {layout.SavegameProbeJsonPath}");
        return 0;
    }
}
