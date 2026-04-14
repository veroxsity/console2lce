using fNbt;
using LceWorldConverter;
using System.Text.RegularExpressions;

namespace Console2Lce.Cli;

internal static class ConvertCommandRunner
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
            container.WriteToFile(container.CreateFile("level.dat"), ConvertLevelDat(levelDatBytes));
        }

        var regionWriterCache = new Dictionary<string, LceRegionFile>(StringComparer.OrdinalIgnoreCase);
        var chunkDecoder = new MinecraftXbox360ChunkDecoder();
        int totalRegionFiles = 0;
        int totalChunksSeen = 0;
        int chunksDecoded = 0;
        int chunksLegacyNbt = 0;
        int chunksWritten = 0;

        foreach ((string fileName, byte[] regionBytes) in archive.Files.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!fileName.EndsWith(".mcr", StringComparison.OrdinalIgnoreCase))
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
                if (!chunkDecoder.TryDecodeChunkPayload(compressedBytes, chunk.DecompressedLength, chunk.UsesRleCompression, out byte[] decodedPayload, out _, out _, null))
                {
                    continue;
                }
                chunksDecoded++;

                if (!MinecraftConsoleChunkPayloadCodec.TryDecodeToLegacyNbt(decodedPayload, out byte[] legacyChunkNbt))
                {
                    continue;
                }
                chunksLegacyNbt++;

                NbtFile file = new();
                file.LoadFromBuffer(legacyChunkNbt, 0, legacyChunkNbt.Length, NbtCompression.None);
                NbtCompound root = file.RootTag;
                NbtCompound level = root.Get<NbtCompound>("Level") ?? root;

                RepairLikelySwappedNibbles(level);

                int worldChunkX = regionX * 32 + chunk.X;
                int worldChunkZ = regionZ * 32 + chunk.Z;
                byte[] lceChunkNbt = ChunkConverter.ConvertChunk(level, worldChunkX, worldChunkZ);
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
        Console.WriteLine($"Chunks to legacy NBT:   {chunksLegacyNbt}");
        Console.WriteLine($"Chunks written:         {chunksWritten}");
        return 0;
    }

    private static string ToLceRegionEntryName(string xboxRegionName)
    {
        string normalized = xboxRegionName.Replace('\\', '/');
        if (normalized.StartsWith("DIM-1r.", StringComparison.OrdinalIgnoreCase))
        {
            return "DIM-1/" + normalized[5..];
        }

        if (normalized.StartsWith("DIM1r.", StringComparison.OrdinalIgnoreCase))
        {
            return "DIM1/" + normalized[4..];
        }

        return normalized;
    }

    private static bool TryParseRegionCoordinates(string regionFileName, out int regionX, out int regionZ)
    {
        regionX = 0;
        regionZ = 0;

        Match match = RegionCoordinatesPattern.Match(regionFileName.Replace('\\', '/'));
        if (!match.Success)
        {
            return false;
        }

        return int.TryParse(match.Groups["x"].Value, out regionX)
            && int.TryParse(match.Groups["z"].Value, out regionZ);
    }

    private static byte[] ConvertLevelDat(byte[] levelDatBytes)
    {
        byte[] candidate = levelDatBytes;

        try
        {
            var file = new NbtFile();
            file.LoadFromBuffer(levelDatBytes, 0, levelDatBytes.Length, NbtCompression.AutoDetect);

            // Keep the source coordinates intact, but add the LCE-specific fields that the game expects.
            // The world chunks themselves are written at their original coordinates.
            candidate = LevelDatConverter.Convert(file.RootTag, 0, 0, 320, false);
        }
        catch
        {
            // Preserve the original bytes as a fallback; we still attempt to sanitize below.
        }

        return NormalizeLevelDatForLce(candidate);
    }

    private static void RepairLikelySwappedNibbles(NbtCompound level)
    {
        byte[]? blocks = level.Get<NbtByteArray>("Blocks")?.Value;
        byte[]? data = level.Get<NbtByteArray>("Data")?.Value;

        if (blocks is null || data is null || data.Length == 0)
        {
            return;
        }

        NormalizeLikelyXboxTallGrassMetadata(blocks, data);
    }

    private static void NormalizeLikelyXboxTallGrassMetadata(byte[] blocks, byte[] data)
    {
        int max = Math.Min(blocks.Length, 32768);
        int id31Count = 0;
        int id31Meta0 = 0;
        int id31Healthy = 0;

        for (int index = 0; index < max; index++)
        {
            if (blocks[index] != 31)
            {
                continue;
            }

            id31Count++;
            byte meta = GetNibble(data, index);
            if (meta == 0)
            {
                id31Meta0++;
            }
            else if (meta is 1 or 2)
            {
                id31Healthy++;
            }
        }

        // Some Xbox compact payloads surface short grass as id31/meta0.
        // If no healthy id31 variants are present, normalize 0 -> 1 for this chunk.
        if (id31Count == 0 || id31Meta0 == 0 || id31Healthy > 0)
        {
            return;
        }

        for (int index = 0; index < max; index++)
        {
            if (blocks[index] == 31 && GetNibble(data, index) == 0)
            {
                SetNibble(data, index, 1);
            }
        }
    }

    private static byte GetNibble(byte[] data, int index)
    {
        int packed = data[index >> 1];
        return (byte)(((index & 1) == 0) ? (packed & 0x0F) : ((packed >> 4) & 0x0F));
    }

    private static void SetNibble(byte[] data, int index, byte value)
    {
        int byteIndex = index >> 1;
        byte nibble = (byte)(value & 0x0F);
        if ((index & 1) == 0)
        {
            data[byteIndex] = (byte)((data[byteIndex] & 0xF0) | nibble);
        }
        else
        {
            data[byteIndex] = (byte)((data[byteIndex] & 0x0F) | (nibble << 4));
        }
    }

    private static byte[] NormalizeLevelDatForLce(byte[] levelDatBytes)
    {
        try
        {
            var file = new NbtFile();
            file.LoadFromBuffer(levelDatBytes, 0, levelDatBytes.Length, NbtCompression.AutoDetect);

            NbtCompound root = file.RootTag;
            NbtCompound? data = root.Get<NbtCompound>("Data");
            if (data is null)
            {
                return levelDatBytes;
            }

            static void Upsert(NbtCompound compound, NbtTag tag)
            {
                if (string.IsNullOrEmpty(tag.Name))
                {
                    return;
                }

                if (compound.Contains(tag.Name))
                {
                    compound.Remove(tag.Name);
                }

                compound.Add(tag);
            }

            string generator = (data.Get<NbtString>("generatorName")?.Value ?? "default").Trim().ToLowerInvariant();
            if (generator != "default" && generator != "flat")
            {
                generator = "default";
            }

            int spawnY = data.Get<NbtInt>("SpawnY")?.Value ?? 64;
            spawnY = Math.Clamp(spawnY, 1, 127);

            Upsert(data, new NbtString("generatorName", generator));
            Upsert(data, new NbtInt("generatorVersion", generator == "flat" ? 0 : 1));
            Upsert(data, new NbtString("generatorOptions", generator == "flat" ? "2;7,2x3,2;1;" : ""));
            Upsert(data, new NbtInt("XZSize", 320));
            Upsert(data, new NbtInt("HellScale", 3));
            Upsert(data, new NbtByte("newSeaLevel", 1));
            Upsert(data, new NbtInt("version", 19133));
            Upsert(data, new NbtInt("SpawnY", spawnY));

            using var ms = new MemoryStream();
            file.SaveToStream(ms, NbtCompression.None);
            return ms.ToArray();
        }
        catch
        {
            return levelDatBytes;
        }
    }
}