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
            return 2;
        }

        var archive = new Minecraft360ArchiveParser().Parse(decodeResult.DecompressedBytes);
        string saveDataPath = Path.Combine(outputPath, "saveData.ms");

        var container = new SaveDataContainer(originalSaveVersion: 7, currentSaveVersion: 9);

        if (archive.Files.TryGetValue("level.dat", out byte[]? levelDatBytes))
        {
            container.WriteToFile(container.CreateFile("level.dat"), ConvertLevelDat(levelDatBytes));
        }

        Minecraft360ArchiveFileCopyResult auxiliaryCopyResult =
            new Minecraft360ArchiveFileCopyService().CopyAuxiliaryFiles(archive, container);

        var regionWriterCache = new Dictionary<string, LceRegionFile>(StringComparer.OrdinalIgnoreCase);
        var chunkDecoder = new MinecraftXbox360ChunkDecoder();
        var chunkConversionContext = new ChunkConversionContext(preserveDynamicChunkData: true);
        int totalRegionFiles = 0;
        int skippedRegionFiles = 0;
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

            MinecraftXbox360Region region;
            try
            {
                region = new MinecraftXbox360RegionParser().Parse(regionBytes, fileName);
            }
            catch (InvalidMinecraftXbox360RegionException exception)
            {
                skippedRegionFiles++;
                Console.Error.WriteLine($"Skipping region '{fileName}': {exception.Message}");
                continue;
            }

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
                byte[] lceChunkNbt = ChunkConverter.ConvertChunk(level, worldChunkX, worldChunkZ, chunkConversionContext);

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
        Console.WriteLine($"Auxiliary files copied: {auxiliaryCopyResult.CopiedFiles}");
        Console.WriteLine($"Player files copied:    {auxiliaryCopyResult.CopiedPlayerFiles}");
        Console.WriteLine($"Primary player remapped:{auxiliaryCopyResult.RemappedPrimaryPlayerFiles}");
        Console.WriteLine($"Regions: {regionWriterCache.Count}");
        Console.WriteLine($"Region files processed: {totalRegionFiles}");
        Console.WriteLine($"Region files skipped:   {skippedRegionFiles}");
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
        Console.WriteLine($"Stage [{stageName}] bed heads S/W/N/E:      {metrics.BedHeadsSouth}/{metrics.BedHeadsWest}/{metrics.BedHeadsNorth}/{metrics.BedHeadsEast}");
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
                // Standing torches may use 0 or 5; only treat out-of-range as invalid.
                if (meta > 5)
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
                if (isHead)
                {
                    switch (direction)
                    {
                        case 0: metrics.BedHeadsSouth++; break;
                        case 1: metrics.BedHeadsWest++; break;
                        case 2: metrics.BedHeadsNorth++; break;
                        case 3: metrics.BedHeadsEast++; break;
                    }
                }

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
        public int BedHeadsSouth;
        public int BedHeadsWest;
        public int BedHeadsNorth;
        public int BedHeadsEast;
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
                BedHeadsSouth = BedHeadsSouth + other.BedHeadsSouth,
                BedHeadsWest = BedHeadsWest + other.BedHeadsWest,
                BedHeadsNorth = BedHeadsNorth + other.BedHeadsNorth,
                BedHeadsEast = BedHeadsEast + other.BedHeadsEast,
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
            candidate = LevelDatConverter.Convert(file.RootTag, 0, 0, ReadSourceXzSize(file.RootTag), false);
        }
        catch
        {
            // Preserve the original bytes as a fallback; we still attempt to sanitize below.
        }

        return NormalizeLevelDatForLce(candidate);
    }

    private static int ReadSourceXzSize(NbtCompound root)
    {
        NbtCompound data = root.Get<NbtCompound>("Data") ?? root;
        int xzSize = data.Get<NbtInt>("XZSize")?.Value ?? 320;
        return xzSize > 0 ? xzSize : 320;
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
        NormalizeLikelyDirectionEncodedMetadata(blocks, data, max);
        RepairDoorPairs(blocks, data, max);
        RepairBedPairs(blocks, data, max);
        RepairStairsFacingBySupport(blocks, data, max);
        RepairLadderAndTorchAttachments(blocks, data, max);
    }

    private static void NormalizeLikelyDirectionEncodedMetadata(byte[] blocks, byte[] data, int max)
    {
        for (int index = 0; index < max; index++)
        {
            byte id = blocks[index];
            byte meta = GetNibble(data, index);

            if (id is 61 or 62)
            {
                // Some source payloads appear to encode furnace facing as Direction enum 0..3.
                // Convert Direction(south/west/north/east) to Facing(3/4/2/5).
                byte converted = meta switch
                {
                    0 => 3,
                    1 => 4,
                    2 => 2,
                    3 => 5,
                    _ => meta,
                };

                if (converted != meta)
                {
                    SetNibble(data, index, converted);
                }

                continue;
            }

            // Stairs are handled by structure-aware repair below.
        }
    }

    private static bool IsStairsId(byte id)
    {
        return id is 53 or 67 or 108 or 109 or 114 or 128 or 134 or 135 or 136 or 156 or 163 or 164;
    }

    private static void RepairStairsFacingBySupport(byte[] blocks, byte[] data, int max)
    {
        for (int index = 0; index < max; index++)
        {
            if (!IsStairsId(blocks[index]))
            {
                continue;
            }

            int y = index % 128;
            int column = index / 128;
            int z = column % 16;
            int x = column / 16;

            byte meta = GetNibble(data, index);
            byte currentDir = (byte)(meta & 0x03);
            int bestScore = int.MinValue;
            byte bestDir = currentDir;

            for (byte candidate = 0; candidate < 4; candidate++)
            {
                (int dx, int dz) = candidate switch
                {
                    // Legacy stair direction enum appears inverted by 180 degrees
                    // relative to the support-vs-front heuristic vectors.
                    0 => (-1, 0),
                    1 => (1, 0),
                    2 => (0, -1),
                    3 => (0, 1),
                    _ => (1, 0),
                };

                bool hasBehindSupport = HasAttachSupport(blocks, max, x - dx, y, z - dz);
                bool hasFrontSupport = HasAttachSupport(blocks, max, x + dx, y, z + dz);

                int score = 0;
                if (hasBehindSupport) score += 3;
                if (!hasFrontSupport) score += 2;
                if (candidate == currentDir) score += 1;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestDir = candidate;
                }
            }

            // Reset half-bits (bits 2-3) to 0 (bottom stairs) to avoid corrupted half values.
            // Preserve only the direction, removing any invalid half-block markings.
            SetNibble(data, index, bestDir);
        }
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

            byte dirA = (byte)(currentMeta & 0x03);
            byte dirB = (byte)(partnerMeta & 0x03);
            bool aIsHead = (currentMeta & 0x8) != 0;
            bool bIsHead = (partnerMeta & 0x8) != 0;

            bool aCanBeFoot = DirectionMatchesOffset(dirA, dx, dz);
            bool bCanBeFoot = DirectionMatchesOffset(dirB, -dx, -dz);

            bool indexIsFoot;
            if (aIsHead != bIsHead)
            {
                indexIsFoot = !aIsHead;
            }
            else if (aCanBeFoot != bCanBeFoot)
            {
                indexIsFoot = aCanBeFoot;
            }
            else
            {
                // Preserve direction bits when both part flags are ambiguous.
                indexIsFoot = dirA == DirectionFromOffset(dx, dz);
            }

            int footToHeadDx = indexIsFoot ? dx : -dx;
            int footToHeadDz = indexIsFoot ? dz : -dz;

            byte canonicalDirection;
            if (dirA == dirB && DirectionMatchesOffset(dirA, footToHeadDx, footToHeadDz))
            {
                canonicalDirection = dirA;
            }
            else if (indexIsFoot && aCanBeFoot)
            {
                canonicalDirection = dirA;
            }
            else if (!indexIsFoot && bCanBeFoot)
            {
                canonicalDirection = dirB;
            }
            else
            {
                canonicalDirection = DirectionFromOffset(footToHeadDx, footToHeadDz);
            }

            byte indexOccupiedBit = (byte)(currentMeta & 0x4);
            byte partnerOccupiedBit = (byte)(partnerMeta & 0x4);

            if (indexIsFoot)
            {
                SetNibble(data, index, (byte)(indexOccupiedBit | canonicalDirection));
                SetNibble(data, partner, (byte)(partnerOccupiedBit | canonicalDirection | 0x8));
            }
            else
            {
                SetNibble(data, index, (byte)(indexOccupiedBit | canonicalDirection | 0x8));
                SetNibble(data, partner, (byte)(partnerOccupiedBit | canonicalDirection));
            }

            visited[index] = true;
            visited[partner] = true;
        }
    }

    private static void RepairLadderAndTorchAttachments(byte[] blocks, byte[] data, int max)
    {
        for (int index = 0; index < max; index++)
        {
            byte id = blocks[index];
            if (id != 65 && id != 50 && id != 75 && id != 76)
            {
                continue;
            }

            int y = index % 128;
            int column = index / 128;
            int z = column % 16;
            int x = column / 16;

            byte meta = GetNibble(data, index);

            if (id == 65)
            {
                byte repaired = RepairLadderMeta(blocks, data, max, x, y, z, meta);
                if (repaired != meta)
                {
                    SetNibble(data, index, repaired);
                }

                continue;
            }

            byte torchRepaired = RepairTorchMeta(blocks, max, x, y, z, meta);
            if (torchRepaired != meta)
            {
                SetNibble(data, index, torchRepaired);
            }
        }
    }

    private static byte RepairLadderMeta(byte[] blocks, byte[] data, int max, int x, int y, int z, byte current)
    {
        bool hasNorth = HasAttachSupport(blocks, max, x, y, z - 1);
        bool hasSouth = HasAttachSupport(blocks, max, x, y, z + 1);
        bool hasWest = HasAttachSupport(blocks, max, x - 1, y, z);
        bool hasEast = HasAttachSupport(blocks, max, x + 1, y, z);

        static bool HasSupportForMeta(byte meta, bool north, bool south, bool west, bool east)
        {
            return meta switch
            {
                2 => south,
                3 => north,
                4 => east,
                5 => west,
                _ => false,
            };
        }

        byte? GetNeighborLadderMeta(int ny)
        {
            if ((uint)ny >= 128u)
            {
                return null;
            }

            int neighbor = ((x * 16) + z) * 128 + ny;
            if (neighbor < 0 || neighbor >= max || blocks[neighbor] != 65)
            {
                return null;
            }

            byte neighborMeta = GetNibble(data, neighbor);
            return neighborMeta is >= 2 and <= 5 ? neighborMeta : null;
        }

        bool currentValid = current is >= 2 and <= 5;
        if (currentValid)
        {
            bool currentHasSupport = HasSupportForMeta(current, hasNorth, hasSouth, hasWest, hasEast);

            if (currentHasSupport)
            {
                return current;
            }
        }

        // Keep ladder columns coherent: prefer the rung below/above orientation when it has support.
        byte? belowMeta = GetNeighborLadderMeta(y - 1);
        if (belowMeta is byte below && HasSupportForMeta(below, hasNorth, hasSouth, hasWest, hasEast))
        {
            return below;
        }

        byte? aboveMeta = GetNeighborLadderMeta(y + 1);
        if (aboveMeta is byte above && HasSupportForMeta(above, hasNorth, hasSouth, hasWest, hasEast))
        {
            return above;
        }

        if (hasSouth) return 2;
        if (hasNorth) return 3;
        if (hasEast) return 4;
        if (hasWest) return 5;
        return (byte)(currentValid ? current : 2);
    }

    private static byte RepairTorchMeta(byte[] blocks, int max, int x, int y, int z, byte current)
    {
        bool hasEast = HasAttachSupport(blocks, max, x + 1, y, z);
        bool hasWest = HasAttachSupport(blocks, max, x - 1, y, z);
        bool hasSouth = HasAttachSupport(blocks, max, x, y, z + 1);
        bool hasNorth = HasAttachSupport(blocks, max, x, y, z - 1);
        bool hasFloor = HasAttachSupport(blocks, max, x, y - 1, z);

        bool currentValid = current is >= 1 and <= 5;
        if (currentValid)
        {
            bool currentHasSupport = current switch
            {
                // Wall torches point away from the supporting wall block.
                1 => hasWest,
                2 => hasEast,
                3 => hasNorth,
                4 => hasSouth,
                5 => hasFloor,
                _ => false,
            };

            if (currentHasSupport)
            {
                return current;
            }
        }

        if (hasWest) return 1;
        if (hasEast) return 2;
        if (hasNorth) return 3;
        if (hasSouth) return 4;
        if (hasFloor) return 5;
        return (byte)(currentValid ? current : 5);
    }

    private static bool HasAttachSupport(byte[] blocks, int max, int x, int y, int z)
    {
        if ((uint)x >= 16u || (uint)z >= 16u || (uint)y >= 128u)
        {
            return false;
        }

        int index = ((x * 16) + z) * 128 + y;
        if (index < 0 || index >= max)
        {
            return false;
        }

        byte id = blocks[index];
        if (id == 0)
        {
            return false;
        }

        // Ignore common non-solid/mountable blocks as support candidates.
        return id is not (50 or 63 or 65 or 68 or 69 or 75 or 76 or 77 or 106);
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

    private static bool DirectionMatchesOffset(byte direction, int dx, int dz)
    {
        return direction switch
        {
            0 => dx == 0 && dz == 1,
            1 => dx == -1 && dz == 0,
            2 => dx == 0 && dz == -1,
            3 => dx == 1 && dz == 0,
            _ => false,
        };
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

            int xzSize = data.Get<NbtInt>("XZSize")?.Value ?? 320;
            if (xzSize <= 0)
            {
                xzSize = 320;
            }

            int spawnY = data.Get<NbtInt>("SpawnY")?.Value ?? 64;
            spawnY = Math.Clamp(spawnY, 1, 127);

            Upsert(data, new NbtString("generatorName", generator));
            Upsert(data, new NbtInt("generatorVersion", generator == "flat" ? 0 : 1));
            Upsert(data, new NbtString("generatorOptions", generator == "flat" ? "2;7,2x3,2;1;" : ""));
            Upsert(data, new NbtInt("XZSize", xzSize));
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
