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
        int chunksMetadataNibbleSwapped = 0;
        int chunksWritten = 0;
        var sourceStageTotals = ChunkValidationMetrics.Empty;
        var interpretedStageTotals = ChunkValidationMetrics.Empty;
        var lceStageTotals = ChunkValidationMetrics.Empty;

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

                sourceStageTotals = sourceStageTotals.Add(AnalyzeChunk(level));

                if (RepairLikelySwappedNibbles(level))
                {
                    chunksMetadataNibbleSwapped++;
                }

                interpretedStageTotals = interpretedStageTotals.Add(AnalyzeChunk(level));

                int worldChunkX = regionX * 32 + chunk.X;
                int worldChunkZ = regionZ * 32 + chunk.Z;
                byte[] lceChunkNbt = ChunkConverter.ConvertChunk(level, worldChunkX, worldChunkZ);

                NbtFile lceFile = new();
                lceFile.LoadFromBuffer(lceChunkNbt, 0, lceChunkNbt.Length, NbtCompression.None);
                NbtCompound lceRoot = lceFile.RootTag;
                NbtCompound lceLevel = lceRoot.Get<NbtCompound>("Level") ?? lceRoot;
                lceStageTotals = lceStageTotals.Add(AnalyzeChunk(lceLevel));

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
        Console.WriteLine($"Chunks nibble-swapped:  {chunksMetadataNibbleSwapped}");
        Console.WriteLine($"Chunks written:         {chunksWritten}");
        PrintStageMetrics("source", sourceStageTotals);
        PrintStageMetrics("interpreted", interpretedStageTotals);
        PrintStageMetrics("lce", lceStageTotals);
        return 0;
    }

    private static void PrintStageMetrics(string stageName, ChunkValidationMetrics metrics)
    {
        Console.WriteLine($"Stage [{stageName}] invalid tile IDs:      {metrics.InvalidTileIds}");
        Console.WriteLine($"Stage [{stageName}] bed blocks:             {metrics.BedBlocks}");
        Console.WriteLine($"Stage [{stageName}] bed pair mismatches:    {metrics.BedPairMismatches}");
        Console.WriteLine($"Stage [{stageName}] door lower blocks:      {metrics.DoorLowerBlocks}");
        Console.WriteLine($"Stage [{stageName}] door pair mismatches:   {metrics.DoorPairMismatches}");
        Console.WriteLine($"Stage [{stageName}] furnace bad facing:     {metrics.FurnaceInvalidFacing}");
        Console.WriteLine($"Stage [{stageName}] torch bad facing:       {metrics.TorchInvalidFacing}");
        Console.WriteLine($"Stage [{stageName}] ladder bad facing:      {metrics.LadderInvalidFacing}");
        Console.WriteLine($"Stage [{stageName}] data==skyLight chunks:  {metrics.DataEqualsSkyLightChunks}");
        Console.WriteLine($"Stage [{stageName}] data==blockLight chunks:{metrics.DataEqualsBlockLightChunks}");
        Console.WriteLine($"Stage [{stageName}] all-zero data chunks:   {metrics.AllZeroDataChunks}");
    }

    private static ChunkValidationMetrics AnalyzeChunk(NbtCompound level)
    {
        byte[]? blocks = level.Get<NbtByteArray>("Blocks")?.Value;
        byte[]? data = level.Get<NbtByteArray>("Data")?.Value;
        byte[]? skyLight = level.Get<NbtByteArray>("SkyLight")?.Value;
        byte[]? blockLight = level.Get<NbtByteArray>("BlockLight")?.Value;
        if (blocks is null || data is null)
        {
            return ChunkValidationMetrics.Empty;
        }

        int max = Math.Min(blocks.Length, 32768);
        var metrics = ChunkValidationMetrics.Empty;

        bool dataAllZero = true;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] != 0)
            {
                dataAllZero = false;
                break;
            }
        }

        if (dataAllZero)
        {
            metrics.AllZeroDataChunks++;
        }

        if (skyLight is not null && skyLight.Length == data.Length && data.AsSpan().SequenceEqual(skyLight))
        {
            metrics.DataEqualsSkyLightChunks++;
        }

        if (blockLight is not null && blockLight.Length == data.Length && data.AsSpan().SequenceEqual(blockLight))
        {
            metrics.DataEqualsBlockLightChunks++;
        }

        for (int index = 0; index < max; index++)
        {
            byte id = blocks[index];
            byte meta = GetNibble(data, index);
            if (!IsValidLceTileId(id))
            {
                metrics.InvalidTileIds++;
            }

            if (id is 61 or 62)
            {
                if (meta is < 2 or > 5)
                {
                    metrics.FurnaceInvalidFacing++;
                }
            }
            else if (id is 50 or 75 or 76)
            {
                if (meta is < 1 or > 5)
                {
                    metrics.TorchInvalidFacing++;
                }
            }
            else if (id == 65)
            {
                if (meta is < 2 or > 5)
                {
                    metrics.LadderInvalidFacing++;
                }
            }
            else if (id is 64 or 71)
            {
                bool isUpper = (meta & 0x8) != 0;
                if (!isUpper)
                {
                    metrics.DoorLowerBlocks++;
                    int y = index % 128;
                    if (y < 127)
                    {
                        int above = index + 1;
                        if (above >= max || blocks[above] != id || (GetNibble(data, above) & 0x8) == 0)
                        {
                            metrics.DoorPairMismatches++;
                        }
                    }
                    else
                    {
                        metrics.DoorPairMismatches++;
                    }
                }
            }
            else if (id == 26)
            {
                metrics.BedBlocks++;
                byte direction = (byte)(meta & 0x03);
                bool isHead = (meta & 0x8) != 0;
                int y = index % 128;
                int column = index / 128;
                int z = column % 16;
                int x = column / 16;
                (int dx, int dz) = direction switch
                {
                    0 => (0, 1),
                    1 => (-1, 0),
                    2 => (0, -1),
                    3 => (1, 0),
                    _ => (0, 1),
                };

                int partnerX = isHead ? x - dx : x + dx;
                int partnerZ = isHead ? z - dz : z + dz;
                if ((uint)partnerX >= 16u || (uint)partnerZ >= 16u)
                {
                    metrics.BedPairMismatches++;
                    continue;
                }

                int partner = ((partnerX * 16) + partnerZ) * 128 + y;
                if (partner < 0 || partner >= max || blocks[partner] != 26)
                {
                    metrics.BedPairMismatches++;
                    continue;
                }

                byte partnerMeta = GetNibble(data, partner);
                bool partnerIsHead = (partnerMeta & 0x8) != 0;
                byte partnerDirection = (byte)(partnerMeta & 0x03);
                if (partnerIsHead == isHead || partnerDirection != direction)
                {
                    metrics.BedPairMismatches++;
                }
            }
        }

        return metrics;
    }

    private static bool IsValidLceTileId(byte id)
    {
        return id <= 160 || (id >= 170 && id <= 173);
    }

    private struct ChunkValidationMetrics
    {
        public static readonly ChunkValidationMetrics Empty = default;

        public int InvalidTileIds;
        public int BedBlocks;
        public int BedPairMismatches;
        public int DoorLowerBlocks;
        public int DoorPairMismatches;
        public int FurnaceInvalidFacing;
        public int TorchInvalidFacing;
        public int LadderInvalidFacing;
        public int DataEqualsSkyLightChunks;
        public int DataEqualsBlockLightChunks;
        public int AllZeroDataChunks;

        public readonly ChunkValidationMetrics Add(ChunkValidationMetrics other)
        {
            return new ChunkValidationMetrics
            {
                InvalidTileIds = InvalidTileIds + other.InvalidTileIds,
                BedBlocks = BedBlocks + other.BedBlocks,
                BedPairMismatches = BedPairMismatches + other.BedPairMismatches,
                DoorLowerBlocks = DoorLowerBlocks + other.DoorLowerBlocks,
                DoorPairMismatches = DoorPairMismatches + other.DoorPairMismatches,
                FurnaceInvalidFacing = FurnaceInvalidFacing + other.FurnaceInvalidFacing,
                TorchInvalidFacing = TorchInvalidFacing + other.TorchInvalidFacing,
                LadderInvalidFacing = LadderInvalidFacing + other.LadderInvalidFacing,
                DataEqualsSkyLightChunks = DataEqualsSkyLightChunks + other.DataEqualsSkyLightChunks,
                DataEqualsBlockLightChunks = DataEqualsBlockLightChunks + other.DataEqualsBlockLightChunks,
                AllZeroDataChunks = AllZeroDataChunks + other.AllZeroDataChunks,
            };
        }
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

    private static bool RepairLikelySwappedNibbles(NbtCompound level)
    {
        byte[]? blocks = level.Get<NbtByteArray>("Blocks")?.Value;
        byte[]? data = level.Get<NbtByteArray>("Data")?.Value;
        byte[]? skyLight = level.Get<NbtByteArray>("SkyLight")?.Value;
        byte[]? blockLight = level.Get<NbtByteArray>("BlockLight")?.Value;

        if (blocks is null || data is null || data.Length == 0)
        {
            return false;
        }

        int currentScore = ScoreMetadataCoherence(blocks, data);
        byte[] swappedData = SwapNibblePairs(data);
        int swappedScore = ScoreMetadataCoherence(blocks, swappedData);

        if (swappedScore > currentScore)
        {
            swappedData.CopyTo(data, 0);

            if (skyLight is not null && skyLight.Length == data.Length)
            {
                byte[] swappedSkyLight = SwapNibblePairs(skyLight);
                swappedSkyLight.CopyTo(skyLight, 0);
            }

            if (blockLight is not null && blockLight.Length == data.Length)
            {
                byte[] swappedBlockLight = SwapNibblePairs(blockLight);
                swappedBlockLight.CopyTo(blockLight, 0);
            }

            NormalizeStructuralMetadata(blocks, data);
            NormalizeLikelyXboxTallGrassMetadata(blocks, data);
            return true;
        }

        NormalizeStructuralMetadata(blocks, data);
        NormalizeLikelyXboxTallGrassMetadata(blocks, data);
        return false;
    }

    private static void NormalizeStructuralMetadata(byte[] blocks, byte[] data)
    {
        int max = Math.Min(blocks.Length, 32768);
        RepairDoorPairs(blocks, data, max);
        RepairBedPairs(blocks, data, max);
    }

    private static void RepairDoorPairs(byte[] blocks, byte[] data, int max)
    {
        for (int index = 0; index < max; index++)
        {
            byte id = blocks[index];
            if (id is not 64 and not 71)
            {
                continue;
            }

            int y = index % 128;
            byte meta = GetNibble(data, index);
            bool isUpper = (meta & 0x8) != 0;

            if (!isUpper && y < 127)
            {
                int above = index + 1;
                if (above < max && blocks[above] == id)
                {
                    byte aboveMeta = GetNibble(data, above);
                    SetNibble(data, above, (byte)(aboveMeta | 0x8));
                }
            }
            else if (isUpper && y > 0)
            {
                int below = index - 1;
                if (below >= 0 && blocks[below] == id)
                {
                    byte belowMeta = GetNibble(data, below);
                    SetNibble(data, below, (byte)(belowMeta & 0x7));
                }
            }
        }
    }

    private static void RepairBedPairs(byte[] blocks, byte[] data, int max)
    {
        var visited = new bool[max];
        for (int index = 0; index < max; index++)
        {
            if (visited[index] || blocks[index] != 26)
            {
                continue;
            }

            int y = index % 128;
            int column = index / 128;
            int z = column % 16;
            int x = column / 16;

            int partner = -1;
            int dx = 0;
            int dz = 0;

            // Prefer the partner implied by current facing first.
            byte currentMeta = GetNibble(data, index);
            byte currentDir = (byte)(currentMeta & 0x03);
            bool currentIsHead = (currentMeta & 0x8) != 0;
            (int impliedDx, int impliedDz) = currentDir switch
            {
                0 => (0, 1),
                1 => (-1, 0),
                2 => (0, -1),
                3 => (1, 0),
                _ => (0, 1),
            };

            int impliedX = currentIsHead ? x - impliedDx : x + impliedDx;
            int impliedZ = currentIsHead ? z - impliedDz : z + impliedDz;
            if ((uint)impliedX < 16u && (uint)impliedZ < 16u)
            {
                int impliedIndex = ((impliedX * 16) + impliedZ) * 128 + y;
                if (impliedIndex >= 0 && impliedIndex < max && blocks[impliedIndex] == 26)
                {
                    partner = impliedIndex;
                    dx = impliedX - x;
                    dz = impliedZ - z;
                }
            }

            if (partner < 0)
            {
                ReadOnlySpan<(int dx, int dz)> offsets = [
                    (1, 0), (-1, 0), (0, 1), (0, -1)
                ];

                foreach ((int candDx, int candDz) in offsets)
                {
                    int nx = x + candDx;
                    int nz = z + candDz;
                    if ((uint)nx >= 16u || (uint)nz >= 16u)
                    {
                        continue;
                    }

                    int candidate = ((nx * 16) + nz) * 128 + y;
                    if (blocks[candidate] == 26)
                    {
                        partner = candidate;
                        dx = candDx;
                        dz = candDz;
                        break;
                    }
                }
            }

            if (partner < 0 || visited[partner])
            {
                continue;
            }

            byte partnerMeta = GetNibble(data, partner);

            byte dirIfIndexIsFoot = DirectionFromOffset(dx, dz);
            byte dirIfPartnerIsFoot = DirectionFromOffset(-dx, -dz);

            byte indexAsFoot = (byte)(dirIfIndexIsFoot & 0x03);
            byte partnerAsHead = (byte)((dirIfIndexIsFoot & 0x03) | 0x8);
            byte indexAsHead = (byte)((dirIfPartnerIsFoot & 0x03) | 0x8);
            byte partnerAsFoot = (byte)(dirIfPartnerIsFoot & 0x03);

            int costIndexFoot = MetaChangeCost(currentMeta, indexAsFoot) + MetaChangeCost(partnerMeta, partnerAsHead);
            int costPartnerFoot = MetaChangeCost(currentMeta, indexAsHead) + MetaChangeCost(partnerMeta, partnerAsFoot);

            if (costIndexFoot <= costPartnerFoot)
            {
                SetNibble(data, index, indexAsFoot);
                SetNibble(data, partner, partnerAsHead);
            }
            else
            {
                SetNibble(data, index, indexAsHead);
                SetNibble(data, partner, partnerAsFoot);
            }

            visited[index] = true;
            visited[partner] = true;
        }
    }

    private static byte DirectionFromOffset(int dx, int dz)
    {
        if (dx == 0 && dz == 1)
        {
            return 0;
        }

        if (dx == -1 && dz == 0)
        {
            return 1;
        }

        if (dx == 0 && dz == -1)
        {
            return 2;
        }

        if (dx == 1 && dz == 0)
        {
            return 3;
        }

        return 0;
    }

    private static int MetaChangeCost(byte current, byte target)
    {
        byte diff = (byte)(current ^ target);
        int cost = 0;
        if ((diff & 0x1) != 0) cost++;
        if ((diff & 0x2) != 0) cost++;
        if ((diff & 0x4) != 0) cost++;
        if ((diff & 0x8) != 0) cost++;
        return cost;
    }

    private static int ScoreMetadataCoherence(byte[] blocks, byte[] data)
    {
        int score = 0;
        int max = Math.Min(blocks.Length, 32768);

        for (int index = 0; index < max; index++)
        {
            byte id = blocks[index];
            byte meta = GetNibble(data, index);

            if (id is 61 or 62)
            {
                score += meta is >= 2 and <= 5 ? 3 : -8;
            }
            else if (id is 64 or 71)
            {
                bool isUpper = (meta & 0x08) != 0;
                if (!isUpper)
                {
                    score += (meta & 0x03) <= 3 ? 2 : -8;
                    int y = index % 128;
                    if (y < 127)
                    {
                        int above = index + 1;
                        if (above < max && blocks[above] == id)
                        {
                            byte aboveMeta = GetNibble(data, above);
                            score += (aboveMeta & 0x08) != 0 ? 5 : -6;
                        }
                    }
                }
            }
            else if (id == 26)
            {
                byte direction = (byte)(meta & 0x03);
                bool isHead = (meta & 0x08) != 0;
                score += direction <= 3 ? 2 : -8;

                int y = index % 128;
                int column = index / 128;
                int z = column % 16;
                int x = column / 16;
                (int dx, int dz) = direction switch
                {
                    0 => (0, 1),
                    1 => (-1, 0),
                    2 => (0, -1),
                    3 => (1, 0),
                    _ => (0, 1),
                };

                int partnerX = isHead ? x - dx : x + dx;
                int partnerZ = isHead ? z - dz : z + dz;
                if ((uint)partnerX < 16u && (uint)partnerZ < 16u)
                {
                    int partner = ((partnerX * 16) + partnerZ) * 128 + y;
                    if (partner >= 0 && partner < max && blocks[partner] == 26)
                    {
                        byte partnerMeta = GetNibble(data, partner);
                        bool partnerIsHead = (partnerMeta & 0x08) != 0;
                        byte partnerDirection = (byte)(partnerMeta & 0x03);
                        score += partnerIsHead != isHead ? 5 : -6;
                        score += partnerDirection == direction ? 3 : -4;
                    }
                }
            }
            else if (id is 50 or 75 or 76)
            {
                score += meta is >= 1 and <= 5 ? 1 : -4;
            }
            else if (id == 65)
            {
                score += meta is >= 2 and <= 5 ? 1 : -4;
            }
        }

        return score;
    }

    private static byte[] SwapNibblePairs(byte[] source)
    {
        byte[] swapped = new byte[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            byte value = source[i];
            swapped[i] = (byte)(((value & 0x0F) << 4) | ((value >> 4) & 0x0F));
        }

        return swapped;
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
