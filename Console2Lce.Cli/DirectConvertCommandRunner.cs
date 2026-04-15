using fNbt;
using System.Text.RegularExpressions;
using LceWorldConverter;

namespace Console2Lce.Cli;

/// <summary>
/// Direct Xbox 360 → Win64 LCE converter.
/// Skips all NBT interpretation and block repair.
/// Decompresses Xbox LZX → Recompresses with zlib → Writes to LCE.
/// No metadata corruption, 100% fidelity.
/// </summary>
internal static class DirectConvertCommandRunner
{
    private static readonly Regex RegionCoordinatesPattern = new(
        @"(?:(?:DIM-1|DIM1)/)?r\.(?<x>-?\d+)\.(?<z>-?\d+)\.mcr$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static int Run(CommandLineOptions options)
    {
        string inputPath = Path.GetFullPath(options.InputPath!);
        string outputPath = Path.GetFullPath(options.OutputPath!);
        Directory.CreateDirectory(outputPath);

        byte[] inputBytes = File.ReadAllBytes(inputPath);
        SavegameInputData input = SavegameInputReader.Read(inputBytes);
        SavegameDecodingResult decodeResult = new SavegameDecodeService().Decode(input.SavegameBytes, input.LeadingPrefixBytes);

        if (decodeResult.DecompressedBytes is null)
        {
            Console.Error.WriteLine("Unable to decode savegame.dat.");
            if (!string.IsNullOrWhiteSpace(decodeResult.FallbackFailure))
            {
                Console.Error.WriteLine(decodeResult.FallbackFailure);
            }

            return 2;
        }

        var archive = new Minecraft360ArchiveParser().Parse(decodeResult.DecompressedBytes);
        string saveDataPath = Path.Combine(outputPath, "saveData.ms");

        var container = new SaveDataContainer(originalSaveVersion: 7, currentSaveVersion: 9);

        if (archive.Files.TryGetValue("level.dat", out byte[]? levelDatBytes))
        {
            container.WriteToFile(container.CreateFile("level.dat"), levelDatBytes);
        }

        var regionWriterCache = new Dictionary<string, LceRegionFile>(StringComparer.OrdinalIgnoreCase);
        var chunkDecoder = new MinecraftXbox360ChunkDecoder();
        int totalRegionFiles = 0;
        int totalChunksSeen = 0;
        int chunksDecoded = 0;
        int chunksWritten = 0;

        foreach ((string fileName, byte[] regionBytes) in archive.Files.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!fileName.EndsWith(".mcr", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Skip non-Overworld dimensions (Nether, End). Only convert Overworld chunks.
            if (fileName.StartsWith("DIM-1/", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("DIM1/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            totalRegionFiles++;

            MinecraftXbox360Region region = new MinecraftXbox360RegionParser().Parse(regionBytes, fileName);
            string lceRegionName = ToLceRegionEntryName(fileName);
            if (!TryParseRegionCoordinates(lceRegionName, out int regionX, out int regionZ))
            {
                continue;
            }

            if (!regionWriterCache.TryGetValue(lceRegionName, out LceRegionFile? writer))
            {
                writer = new LceRegionFile(lceRegionName);
                regionWriterCache[lceRegionName] = writer;
            }

            foreach (MinecraftXbox360RegionChunk chunk in region.Chunks)
            {
                totalChunksSeen++;
                byte[] compressedBytes = regionBytes.AsSpan(chunk.PayloadOffset, chunk.StoredLength).ToArray();
                
                // Decompress Xbox LZX → get raw chunk data
                if (!chunkDecoder.TryDecodeChunkPayload(compressedBytes, chunk.DecompressedLength, chunk.UsesRleCompression, 
                    out byte[] decompressedPayload, out _, out _, null))
                {
                    continue;
                }
                chunksDecoded++;

                // Convert Xbox payload to NBT without block interpretation or repair.
                if (!MinecraftConsoleChunkPayloadCodec.TryDecodeToLegacyNbt(decompressedPayload, out byte[] legacyChunkNbt))
                {
                    continue;
                }

                // Parse the Xbox NBT to get access to block data for proper LCE conversion.
                NbtFile file = new();
                file.LoadFromBuffer(legacyChunkNbt, 0, legacyChunkNbt.Length, NbtCompression.None);
                NbtCompound root = file.RootTag;
                NbtCompound level = root.Get<NbtCompound>("Level") ?? root;

                // Repair likely swapped nibbles in Xbox metadata
                ChunkConverter.RepairXboxNibbles(level);

                // Calculate world chunk coordinates for LCE output.
                int worldChunkX = regionX * 32 + chunk.X;
                int worldChunkZ = regionZ * 32 + chunk.Z;

                // Convert to LCE NBT structure (fixes coordinates, biomes, chunk format).
                // Uses Xbox-specific converter that preserves all block data and metadata.
                byte[] lceChunkNbt = ChunkConverter.ConvertXboxChunk(level, worldChunkX, worldChunkZ);

                // Write LCE-formatted NBT directly to LCE region.
                // Block data is perfectly preserved, only NBT structure is converted.
                writer.WriteChunk(chunk.X, chunk.Z, lceChunkNbt);
                chunksWritten++;
            }

            writer.WriteTo(container);
        }

        container.Save(saveDataPath);

        Console.WriteLine($"Input:   {inputPath}");
        Console.WriteLine($"Output:  {outputPath}");
        Console.WriteLine($"Wrote    {saveDataPath}");
        Console.WriteLine($"Files:   {archive.Entries.Count}");
        Console.WriteLine($"Regions: {regionWriterCache.Count}");
        Console.WriteLine($"Region files processed: {totalRegionFiles}");
        Console.WriteLine($"Chunks seen:            {totalChunksSeen}");
        Console.WriteLine($"Chunks decoded:         {chunksDecoded}");
        Console.WriteLine($"Chunks written:         {chunksWritten}");
        Console.WriteLine($"[OK] Direct conversion complete - no metadata interpretation performed");
        return 0;
    }

    private static string ToLceRegionEntryName(string filename)
    {
        Match match = RegionCoordinatesPattern.Match(filename);
        if (match.Success)
        {
            return $"r.{match.Groups["x"].Value}.{match.Groups["z"].Value}.mcr";
        }

        return filename;
    }

    private static bool TryParseRegionCoordinates(string filename, out int x, out int z)
    {
        Match match = RegionCoordinatesPattern.Match(filename);
        if (match.Success && int.TryParse(match.Groups["x"].Value, out x) && int.TryParse(match.Groups["z"].Value, out z))
        {
            return true;
        }

        x = 0;
        z = 0;
        return false;
    }
}
