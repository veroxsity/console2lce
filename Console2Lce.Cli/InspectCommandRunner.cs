using System.Text.Json;

namespace Console2Lce.Cli;

internal static class InspectCommandRunner
{
    public static int Run(CommandLineOptions options)
    {
        string inputPath = Path.GetFullPath(options.InputPath!);
        string outputPath = Path.GetFullPath(options.OutputPath!);
        var layout = new DebugArtifactLayout(outputPath);

        byte[] inputBytes = File.ReadAllBytes(inputPath);
        SavegameInputData input = SavegameInputReader.Read(inputBytes);
        SavegameDecodingResult decodeResult = new SavegameDecodeService().Decode(input.SavegameBytes, input.LeadingPrefixBytes);

        Directory.CreateDirectory(layout.OutputDirectory);
        if (input.IsStfsPackage)
        {
            File.WriteAllText(layout.StfsFilesJsonPath, JsonSerializer.Serialize(input.Entries, new JsonSerializerOptions { WriteIndented = true }));
        }

        File.WriteAllBytes(layout.SavegameDatPath, input.SavegameBytes);
        File.WriteAllText(layout.SavegameProbeJsonPath, JsonSerializer.Serialize(decodeResult.ProbeResult.Report, new JsonSerializerOptions { WriteIndented = true }));

        if (decodeResult.DecompressedBytes is not null)
        {
            Minecraft360Archive archive = ArchiveArtifactWriter.Write(layout, decodeResult.DecompressedBytes);
            WriteRegionAnalysis(layout, archive);
        }

        Console.WriteLine($"Input:   {inputPath}");
        Console.WriteLine($"Output:  {layout.OutputDirectory}");
        Console.WriteLine($"Kind:    {(input.IsStfsPackage ? "STFS package" : "savegame.dat")}");

        if (input.Metadata is not null)
        {
            Console.WriteLine($"Package: {input.Metadata.PackageType}");
            Console.WriteLine($"Header:  0x{input.Metadata.HeaderSize:X} (aligned 0x{input.Metadata.HeaderAlignedSize:X})");
            Console.WriteLine($"FT blk:  0x{input.Metadata.FileTableBlockNumber:X} x {input.Metadata.FileTableBlockCount}");
            Console.WriteLine($"Entries: {input.Entries.Count}");

            foreach (StfsFileEntry entry in input.Entries)
            {
                Console.WriteLine($"- {entry.Name} size={entry.Size} start=0x{entry.StartingBlock:X}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"savegame.dat: {input.SavegameBytes.Length} bytes");
        if (input.FirstBlockOffset is not null)
        {
            Console.WriteLine($"First block:  0x{input.FirstBlockOffset:X}");
        }

        if (decodeResult.ProbeResult.Report.LeadingPrefixHex is not null)
        {
            Console.WriteLine($"Prefix:       {decodeResult.ProbeResult.Report.LeadingPrefixHex}");
        }
        Console.WriteLine($"Probe:        {(decodeResult.DecompressedBytes is not null ? "success" : "no match")}");

        if (decodeResult.DecompressedBytes is not null)
        {
            Console.WriteLine($"Decoder:      {decodeResult.DecoderSummary}");
            Console.WriteLine($"Wrote         {layout.SavegameDecompressedPath}");
            Console.WriteLine($"Wrote         {layout.ArchiveIndexJsonPath}");
            Console.WriteLine($"Wrote         {layout.ArchiveDirectoryPath}");
            Console.WriteLine($"Wrote         {layout.RegionAnalysisJsonPath}");
        }
        else
        {
            Console.WriteLine("Decoder:      unresolved");
            if (!string.IsNullOrWhiteSpace(decodeResult.FallbackFailure))
            {
                Console.WriteLine($"Fallback:     {decodeResult.FallbackFailure}");
            }
        }

        Console.WriteLine();
        if (input.IsStfsPackage)
        {
            Console.WriteLine($"Wrote {layout.StfsFilesJsonPath}");
        }

        Console.WriteLine($"Wrote {layout.SavegameDatPath}");
        Console.WriteLine($"Wrote {layout.SavegameProbeJsonPath}");
        return 0;
    }

    private static void WriteRegionAnalysis(DebugArtifactLayout layout, Minecraft360Archive archive)
    {
        IReadOnlyList<MinecraftXbox360Region> regionAnalysis = MinecraftXbox360RegionAnalyzer.Analyze(archive);
        File.WriteAllText(
            layout.RegionAnalysisJsonPath,
            JsonSerializer.Serialize(regionAnalysis, new JsonSerializerOptions { WriteIndented = true }));
    }
}
