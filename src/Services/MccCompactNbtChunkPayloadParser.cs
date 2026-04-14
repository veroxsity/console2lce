using System.Buffers.Binary;
using fNbt;

namespace Console2Lce;

public sealed class MccCompactNbtChunkPayloadParser
{
    private static readonly byte[] RootLevelPrefix =
    [
        (byte)NbtTagType.Compound, 0x00, 0x00,
        (byte)NbtTagType.Compound, 0x00, 0x05, (byte)'L', (byte)'e', (byte)'v', (byte)'e', (byte)'l',
    ];

    private const int ExpectedBlocksSize = 32768;
    private const int ExpectedNibbleSize = ExpectedBlocksSize / 2;
    private const int ExpectedHeightMapSize = 256;

    private readonly byte[] _payload;
    private int _offset;

    private static readonly byte[][] KnownTagMarkers =
    [
        BuildTagMarker(NbtTagType.ByteArray, "Blocks"),
        BuildTagMarker(NbtTagType.ByteArray, "Data"),
        BuildTagMarker(NbtTagType.ByteArray, "SkyLight"),
        BuildTagMarker(NbtTagType.ByteArray, "BlockLight"),
        BuildTagMarker(NbtTagType.ByteArray, "HeightMap"),
        BuildTagMarker(NbtTagType.ByteArray, "Biomes"),
        BuildTagMarker(NbtTagType.Int, "xPos"),
        BuildTagMarker(NbtTagType.Int, "zPos"),
        BuildTagMarker(NbtTagType.Long, "LastUpdate"),
        BuildTagMarker(NbtTagType.Long, "InhabitedTime"),
        BuildTagMarker(NbtTagType.Short, "TerrainPopulatedFlags"),
        BuildTagMarker(NbtTagType.List, "Entities"),
        BuildTagMarker(NbtTagType.List, "TileEntities"),
        BuildTagMarker(NbtTagType.List, "TileTicks"),
    ];

    private static readonly byte[] BlocksTagMarker = BuildTagMarker(NbtTagType.ByteArray, "Blocks");
    private static readonly byte[] DataTagMarker = BuildTagMarker(NbtTagType.ByteArray, "Data");
    private static readonly byte[] SkyLightTagMarker = BuildTagMarker(NbtTagType.ByteArray, "SkyLight");
    private static readonly byte[] BlockLightTagMarker = BuildTagMarker(NbtTagType.ByteArray, "BlockLight");
    private static readonly byte[] HeightMapTagMarker = BuildTagMarker(NbtTagType.ByteArray, "HeightMap");
    private static readonly byte[] XPosTagMarker = BuildTagMarker(NbtTagType.Int, "xPos");
    private static readonly byte[] ZPosTagMarker = BuildTagMarker(NbtTagType.Int, "zPos");
    private static readonly byte[] LastUpdateTagMarker = BuildTagMarker(NbtTagType.Long, "LastUpdate");
    private static readonly byte[] TerrainPopulatedTagMarker = BuildTagMarker(NbtTagType.Byte, "TerrainPopulated");

    private MccCompactNbtChunkPayloadParser(byte[] payload)
    {
        _payload = payload;
    }

    public static bool TryParseToLegacyNbt(byte[] payload, out byte[] legacyNbt)
    {
        legacyNbt = Array.Empty<byte>();
        ArgumentNullException.ThrowIfNull(payload);

        var parser = new MccCompactNbtChunkPayloadParser(payload);
        return parser.TryParseToLegacyNbtInternal(out legacyNbt);
    }

    private bool TryParseToLegacyNbtInternal(out byte[] legacyNbt)
    {
        legacyNbt = Array.Empty<byte>();

        try
        {
            RequireByte((byte)NbtTagType.Compound);
            _ = ReadString(); // Root name (usually empty)

            RequireByte((byte)NbtTagType.Compound);
            if (!string.Equals(ReadString(), "Level", StringComparison.Ordinal))
            {
                return false;
            }

            var level = new NbtCompound("Level");
            ParseLevelTags(level);

            // Compact payload tails are noisy; prefer explicit marker reads when available.
            int? markerX = TryReadIntTag("xPos");
            int? markerZ = TryReadIntTag("zPos");
            if (markerX is not null)
            {
                Upsert(level, new NbtInt("xPos", markerX.Value));
            }

            if (markerZ is not null)
            {
                Upsert(level, new NbtInt("zPos", markerZ.Value));
            }

            if (!level.Contains("Blocks")
                || !level.Contains("Data")
                || !level.Contains("SkyLight")
                || !level.Contains("BlockLight")
                || !level.Contains("HeightMap"))
            {
                if (TryParseKnownCompactLayout(out legacyNbt))
                {
                    return true;
                }

                if (TryParseMinimalByKnownMarkers(out legacyNbt))
                {
                    return true;
                }

                TryWriteParseDebug(new InvalidDataException(
                    $"Missing required tags: Blocks={level.Contains("Blocks")}, Data={level.Contains("Data")}, SkyLight={level.Contains("SkyLight")}, BlockLight={level.Contains("BlockLight")}, HeightMap={level.Contains("HeightMap")}."));
                return false;
            }

            if (!level.Contains("Entities"))
            {
                level.Add(new NbtList("Entities", NbtTagType.Compound));
            }

            if (!level.Contains("TileEntities"))
            {
                level.Add(new NbtList("TileEntities", NbtTagType.Compound));
            }

            var root = new NbtCompound(string.Empty) { level };
            using var ms = new MemoryStream();
            new NbtFile(root).SaveToStream(ms, NbtCompression.None);
            legacyNbt = ms.ToArray();
            return true;
        }
        catch (Exception exception)
        {
            if (TryParseKnownCompactLayout(out legacyNbt))
            {
                return true;
            }

            if (TryParseMinimalByKnownMarkers(out legacyNbt))
            {
                return true;
            }

            TryWriteParseDebug(exception);
            return false;
        }
    }

    private bool TryParseKnownCompactLayout(out byte[] legacyNbt)
    {
        legacyNbt = Array.Empty<byte>();

        try
        {
            int blocksMarker = FindMarker(BlocksTagMarker, 0);
            int lastUpdateMarker = FindMarker(LastUpdateTagMarker, blocksMarker + BlocksTagMarker.Length);
            int xPosMarker = FindMarker(XPosTagMarker, lastUpdateMarker + LastUpdateTagMarker.Length);
            int dataMarker = FindMarker(DataTagMarker, xPosMarker + XPosTagMarker.Length);
            int zPosMarker = FindMarker(ZPosTagMarker, dataMarker + DataTagMarker.Length);

            int terrainPopulatedMarker = -1;
            try
            {
                terrainPopulatedMarker = FindMarker(TerrainPopulatedTagMarker, zPosMarker + ZPosTagMarker.Length);
            }
            catch
            {
                terrainPopulatedMarker = -1;
            }

            int blockLightSearchStart = terrainPopulatedMarker >= 0
                ? terrainPopulatedMarker + TerrainPopulatedTagMarker.Length
                : zPosMarker + ZPosTagMarker.Length;
            int blockLightMarker = FindMarker(BlockLightTagMarker, blockLightSearchStart);
            int skyLightMarker = FindMarker(SkyLightTagMarker, blockLightMarker + BlockLightTagMarker.Length);
            int heightMapMarker = FindMarker(HeightMapTagMarker, skyLightMarker + SkyLightTagMarker.Length);

            int entitiesMarker;
            try
            {
                entitiesMarker = FindMarker(BuildTagMarker(NbtTagType.List, "Entities"), heightMapMarker + HeightMapTagMarker.Length);
            }
            catch
            {
                entitiesMarker = _payload.Length;
            }

            byte[] blocks = TryDecodeFieldUsingBoundarySearch(blocksMarker, "Blocks", ExpectedBlocksSize)
                ?? DecodeSegmentByMarkers(blocksMarker, "Blocks", lastUpdateMarker, ExpectedBlocksSize);

            byte[] data = TryDecodeFieldUsingBoundarySearch(dataMarker, "Data", ExpectedNibbleSize)
                ?? TryDecodeSegmentByMarkers(dataMarker, "Data", zPosMarker, ExpectedNibbleSize)
                ?? new byte[ExpectedNibbleSize];

            byte[] blockLight = TryDecodeFieldUsingBoundarySearch(blockLightMarker, "BlockLight", ExpectedNibbleSize)
                ?? TryDecodeSegmentByMarkers(blockLightMarker, "BlockLight", skyLightMarker, ExpectedNibbleSize)
                ?? new byte[ExpectedNibbleSize];

            byte[] skyLight = TryDecodeFieldUsingBoundarySearch(skyLightMarker, "SkyLight", ExpectedNibbleSize)
                ?? TryDecodeSegmentByMarkers(skyLightMarker, "SkyLight", heightMapMarker, ExpectedNibbleSize)
                ?? CreateFilledArray(ExpectedNibbleSize, 0xFF);

            byte[] heightMap = TryDecodeFieldUsingBoundarySearch(heightMapMarker, "HeightMap", ExpectedHeightMapSize)
                ?? TryDecodeSegmentByMarkers(heightMapMarker, "HeightMap", entitiesMarker, ExpectedHeightMapSize)
                ?? new byte[ExpectedHeightMapSize];

            int chunkX = BinaryPrimitives.ReadInt32BigEndian(_payload.AsSpan(xPosMarker + XPosTagMarker.Length, sizeof(int)));
            int chunkZ = BinaryPrimitives.ReadInt32BigEndian(_payload.AsSpan(zPosMarker + ZPosTagMarker.Length, sizeof(int)));

            var level = new NbtCompound("Level")
            {
                new NbtInt("xPos", chunkX),
                new NbtInt("zPos", chunkZ),
                new NbtByteArray("Blocks", blocks),
                new NbtByteArray("Data", data),
                new NbtByteArray("SkyLight", skyLight),
                new NbtByteArray("BlockLight", blockLight),
                new NbtByteArray("HeightMap", heightMap),
                new NbtList("Entities", NbtTagType.Compound),
                new NbtList("TileEntities", NbtTagType.Compound),
            };

            var root = new NbtCompound(string.Empty) { level };
            using var ms = new MemoryStream();
            new NbtFile(root).SaveToStream(ms, NbtCompression.None);
            legacyNbt = ms.ToArray();
            return true;
        }
        catch (Exception exception)
        {
            TryAppendParseDebug("KnownLayout", exception);
            legacyNbt = Array.Empty<byte>();
            return false;
        }
    }

    private byte[]? TryDecodeSegmentByMarkers(int markerOffset, string fieldName, int nextMarkerOffset, int expectedSize)
    {
        try
        {
            return DecodeSegmentByMarkers(markerOffset, fieldName, nextMarkerOffset, expectedSize);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] CreateFilledArray(int length, byte value)
    {
        var result = new byte[length];
        if (value != 0)
        {
            Array.Fill(result, value);
        }

        return result;
    }

    private bool TryParseMinimalByKnownMarkers(out byte[] legacyNbt)
    {
        legacyNbt = Array.Empty<byte>();

        try
        {
            if (!_payload.AsSpan().StartsWith(RootLevelPrefix))
            {
                return false;
            }

            int blocksMarker = FindMarker(BlocksTagMarker, 0);
            int dataMarker = FindMarker(DataTagMarker, blocksMarker + BlocksTagMarker.Length + sizeof(int));
            int skyLightMarker = FindMarker(SkyLightTagMarker, dataMarker + DataTagMarker.Length + sizeof(int));
            int blockLightMarker = FindMarker(BlockLightTagMarker, skyLightMarker + SkyLightTagMarker.Length + sizeof(int));
            int heightMapMarker = FindMarker(HeightMapTagMarker, blockLightMarker + BlockLightTagMarker.Length + sizeof(int));

            (string Name, int Offset)[] nibbleFields =
            [
                ("Data", dataMarker),
                ("SkyLight", skyLightMarker),
                ("BlockLight", blockLightMarker),
            ];

            Array.Sort(nibbleFields, static (left, right) => left.Offset.CompareTo(right.Offset));

            byte[] blocks = DecodeSegmentByMarkers(blocksMarker, "Blocks", nibbleFields[0].Offset, ExpectedBlocksSize);
            var nibbleDecoded = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            for (int index = 0; index < nibbleFields.Length; index++)
            {
                int nextMarker = index + 1 < nibbleFields.Length
                    ? nibbleFields[index + 1].Offset
                    : heightMapMarker;

                nibbleDecoded[nibbleFields[index].Name] = DecodeSegmentByMarkers(
                    nibbleFields[index].Offset,
                    nibbleFields[index].Name,
                    nextMarker,
                    ExpectedNibbleSize);
            }

            if (!nibbleDecoded.TryGetValue("Data", out byte[]? data)
                || !nibbleDecoded.TryGetValue("SkyLight", out byte[]? skyLight)
                || !nibbleDecoded.TryGetValue("BlockLight", out byte[]? blockLight))
            {
                throw new InvalidDataException("Missing one or more nibble fields in compact payload.");
            }

            byte[] heightMap = ReadHeightMap(heightMapMarker);

            int? chunkX = TryReadIntTag("xPos");
            int? chunkZ = TryReadIntTag("zPos");

            var level = new NbtCompound("Level")
            {
                new NbtByteArray("Blocks", blocks),
                new NbtByteArray("Data", data),
                new NbtByteArray("SkyLight", skyLight),
                new NbtByteArray("BlockLight", blockLight),
                new NbtByteArray("HeightMap", heightMap),
                new NbtList("Entities", NbtTagType.Compound),
                new NbtList("TileEntities", NbtTagType.Compound),
            };

            if (chunkX is not null)
            {
                level.Add(new NbtInt("xPos", chunkX.Value));
            }

            if (chunkZ is not null)
            {
                level.Add(new NbtInt("zPos", chunkZ.Value));
            }

            var root = new NbtCompound(string.Empty) { level };
            using var ms = new MemoryStream();
            new NbtFile(root).SaveToStream(ms, NbtCompression.None);
            legacyNbt = ms.ToArray();
            return true;
        }
        catch (Exception exception)
        {
            TryAppendParseDebug("MinimalMarkers", exception);
            legacyNbt = Array.Empty<byte>();
            return false;
        }
    }

    private void TryAppendParseDebug(string stage, Exception exception)
    {
        string? debugPath = Environment.GetEnvironmentVariable("CONSOLE2LCE_MCC_PARSE_DEBUG");
        if (string.IsNullOrWhiteSpace(debugPath))
        {
            return;
        }

        string fullPath = Path.GetFullPath(debugPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");
        File.AppendAllText(
            fullPath,
            $"[{stage}] Offset={_offset} PayloadLength={_payload.Length}{Environment.NewLine}{exception}{Environment.NewLine}");
    }

    private int FindMarker(byte[] marker, int start)
    {
        if ((uint)start >= (uint)_payload.Length)
        {
            throw new EndOfStreamException();
        }

        int index = _payload.AsSpan(start).IndexOf(marker);
        if (index < 0)
        {
            throw new InvalidDataException("Expected compact NBT marker was not found.");
        }

        return start + index;
    }

    private byte[] DecodeSegmentByMarkers(int markerOffset, string fieldName, int nextMarkerOffset, int expectedSize)
    {
        byte[] marker = BuildTagMarker(NbtTagType.ByteArray, fieldName);
        int bodyStart = markerOffset + marker.Length;
        int declaredLength = BinaryPrimitives.ReadInt32BigEndian(_payload.AsSpan(bodyStart, sizeof(int)));
        bodyStart += sizeof(int);

        if (declaredLength == expectedSize && bodyStart + declaredLength == nextMarkerOffset)
        {
            return _payload.AsSpan(bodyStart, declaredLength).ToArray();
        }

        int encodedLength = nextMarkerOffset - bodyStart;
        if (encodedLength <= 0)
        {
            throw new InvalidDataException("Invalid compact field marker ordering.");
        }

        return SavegameRleCodec.Decode(_payload.AsSpan(bodyStart, encodedLength), expectedSize);
    }

    private byte[] DecodeFieldUsingBoundarySearch(int markerOffset, string fieldName, int expectedSize)
    {
        byte[] marker = BuildTagMarker(NbtTagType.ByteArray, fieldName);
        int fieldBodyOffset = markerOffset + marker.Length;
        int savedOffset = _offset;
        try
        {
            _offset = fieldBodyOffset;
            return ReadCompactOrRawByteArray(fieldName, expectedSize);
        }
        finally
        {
            _offset = savedOffset;
        }
    }

    private byte[]? TryDecodeFieldUsingBoundarySearch(int markerOffset, string fieldName, int expectedSize)
    {
        try
        {
            return DecodeFieldUsingBoundarySearch(markerOffset, fieldName, expectedSize);
        }
        catch
        {
            return null;
        }
    }

    private byte[] ReadHeightMap(int markerOffset)
    {
        byte[] marker = BuildTagMarker(NbtTagType.ByteArray, "HeightMap");
        int start = markerOffset + marker.Length;
        int declaredLength = BinaryPrimitives.ReadInt32BigEndian(_payload.AsSpan(start, sizeof(int)));
        if (declaredLength != ExpectedHeightMapSize)
        {
            throw new InvalidDataException($"Unexpected HeightMap length {declaredLength}.");
        }

        start += sizeof(int);
        if (start + declaredLength > _payload.Length)
        {
            throw new EndOfStreamException();
        }

        return _payload.AsSpan(start, declaredLength).ToArray();
    }

    private int? TryReadIntTag(string name)
    {
        byte[] marker = BuildTagMarker(NbtTagType.Int, name);
        int markerOffset = _payload.AsSpan().IndexOf(marker);
        if (markerOffset < 0)
        {
            return null;
        }

        int valueOffset = markerOffset + marker.Length;
        if (valueOffset + sizeof(int) > _payload.Length)
        {
            return null;
        }

        return BinaryPrimitives.ReadInt32BigEndian(_payload.AsSpan(valueOffset, sizeof(int)));
    }

    private static void Upsert(NbtCompound compound, NbtTag tag)
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

    private void TryWriteParseDebug(Exception exception)
    {
        string? debugPath = Environment.GetEnvironmentVariable("CONSOLE2LCE_MCC_PARSE_DEBUG");
        if (string.IsNullOrWhiteSpace(debugPath))
        {
            return;
        }

        string fullPath = Path.GetFullPath(debugPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");
        if (File.Exists(fullPath) && File.Exists(fullPath + ".bin"))
        {
            return;
        }

        File.WriteAllText(
            fullPath,
            $"Offset={_offset}{Environment.NewLine}PayloadLength={_payload.Length}{Environment.NewLine}{exception}{Environment.NewLine}");
        File.WriteAllBytes(fullPath + ".bin", _payload);
    }

    private void ParseLevelTags(NbtCompound level)
    {
        while (_offset < _payload.Length)
        {
            NbtTagType type;
            try
            {
                type = (NbtTagType)ReadByte();
            }
            catch (EndOfStreamException)
            {
                // Some compact payload tails are truncated/non-canonical after core chunk tags.
                // Keep already-parsed fields instead of forcing fallback reconstruction.
                return;
            }

            if (type == NbtTagType.End)
            {
                // End of Level compound. Root end may still exist, which we ignore.
                return;
            }

            string name;
            try
            {
                name = ReadString();
            }
            catch (EndOfStreamException)
            {
                return;
            }

            NbtTag value;
            try
            {
                if (type == NbtTagType.ByteArray && string.Equals(name, "Blocks", StringComparison.Ordinal))
                {
                    value = new NbtByteArray(name, ReadCompactOrRawByteArray(name, ExpectedBlocksSize));
                }
                else if (type == NbtTagType.ByteArray && IsNibbleField(name))
                {
                    value = new NbtByteArray(name, ReadCompactOrRawByteArray(name, ExpectedNibbleSize));
                }
                else if (type == NbtTagType.ByteArray && string.Equals(name, "HeightMap", StringComparison.Ordinal))
                {
                    value = new NbtByteArray(name, ReadCompactOrRawByteArray(name, ExpectedHeightMapSize));
                }
                else
                {
                    value = ReadTagPayload(type, name);
                }
            }
            catch (EndOfStreamException)
            {
                return;
            }
            catch (InvalidDataException)
            {
                // Stop on malformed tail tags; caller validates required core arrays exist.
                return;
            }

            if (level.Contains(name))
            {
                continue;
            }

            level.Add(value);
        }
    }

    private static bool IsNibbleField(string name)
    {
        return string.Equals(name, "Data", StringComparison.Ordinal)
            || string.Equals(name, "SkyLight", StringComparison.Ordinal)
            || string.Equals(name, "BlockLight", StringComparison.Ordinal);
    }

    private static byte[] BuildTagMarker(NbtTagType type, string name)
    {
        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
        var marker = new byte[1 + 2 + nameBytes.Length];
        marker[0] = (byte)type;
        BinaryPrimitives.WriteUInt16BigEndian(marker.AsSpan(1, 2), (ushort)nameBytes.Length);
        nameBytes.CopyTo(marker.AsSpan(3));
        return marker;
    }

    private NbtTag ReadTagPayload(NbtTagType type, string name)
    {
        return type switch
        {
            NbtTagType.Byte => new NbtByte(name, ReadByte()),
            NbtTagType.Short => new NbtShort(name, ReadInt16()),
            NbtTagType.Int => new NbtInt(name, ReadInt32()),
            NbtTagType.Long => new NbtLong(name, ReadInt64()),
            NbtTagType.Float => new NbtFloat(name, ReadSingle()),
            NbtTagType.Double => new NbtDouble(name, ReadDouble()),
            NbtTagType.String => new NbtString(name, ReadString()),
            NbtTagType.ByteArray => new NbtByteArray(name, ReadSizedBytes(ReadInt32())),
            NbtTagType.IntArray => new NbtIntArray(name, ReadIntArray()),
            NbtTagType.List => ReadList(name),
            NbtTagType.Compound => ReadCompound(name),
            _ => throw new InvalidDataException($"Unsupported NBT tag type: {type}"),
        };
    }

    private NbtCompound ReadCompound(string name)
    {
        var compound = new NbtCompound(name);
        while (true)
        {
            NbtTagType childType = (NbtTagType)ReadByte();
            if (childType == NbtTagType.End)
            {
                return compound;
            }

            string childName = ReadString();
            compound.Add(ReadTagPayload(childType, childName));
        }
    }

    private NbtList ReadList(string name)
    {
        NbtTagType elementType = (NbtTagType)ReadByte();
        int count = ReadInt32();
        if (count < 0)
        {
            throw new InvalidDataException("Negative NBT list length.");
        }

        var list = new NbtList(name, elementType);
        for (int i = 0; i < count; i++)
        {
            list.Add(ReadListElement(elementType));
        }

        return list;
    }

    private NbtTag ReadListElement(NbtTagType type)
    {
        return type switch
        {
            NbtTagType.Byte => new NbtByte(string.Empty, ReadByte()),
            NbtTagType.Short => new NbtShort(string.Empty, ReadInt16()),
            NbtTagType.Int => new NbtInt(string.Empty, ReadInt32()),
            NbtTagType.Long => new NbtLong(string.Empty, ReadInt64()),
            NbtTagType.Float => new NbtFloat(string.Empty, ReadSingle()),
            NbtTagType.Double => new NbtDouble(string.Empty, ReadDouble()),
            NbtTagType.String => new NbtString(string.Empty, ReadString()),
            NbtTagType.ByteArray => new NbtByteArray(string.Empty, ReadSizedBytes(ReadInt32())),
            NbtTagType.IntArray => new NbtIntArray(string.Empty, ReadIntArray()),
            NbtTagType.List => ReadList(string.Empty),
            NbtTagType.Compound => ReadCompound(string.Empty),
            _ => throw new InvalidDataException($"Unsupported NBT list element type: {type}"),
        };
    }

    private byte[] ReadCompactOrRawByteArray(string fieldName, int expectedDecodedSize)
    {
        int declaredLength = ReadInt32();

        // Some payloads can already store raw bytes for this field.
        if (declaredLength == expectedDecodedSize
            && _offset + declaredLength <= _payload.Length
            && IsAcceptedBoundary(fieldName, _offset + declaredLength))
        {
            return ReadSizedBytes(declaredLength);
        }

        // MCC compact payloads usually store the encoded byte-count in this field.
        // Decode within the declared slice first and consume the full declared field.
        if (declaredLength > 0 && _offset + declaredLength <= _payload.Length)
        {
            ReadOnlySpan<byte> declaredSlice = _payload.AsSpan(_offset, declaredLength);
            if (SavegameRleCodec.TryDecodeExact(declaredSlice, expectedDecodedSize, out byte[] declaredDecoded)
                && IsAcceptedBoundary(fieldName, _offset + declaredLength))
            {
                _offset += declaredLength;
                return declaredDecoded;
            }

            if (SavegameRleCodec.TryDecodePrefix(declaredSlice, expectedDecodedSize, out byte[] declaredPrefixDecoded, out int declaredConsumed)
                && declaredConsumed > 0
                && IsAcceptedBoundary(fieldName, _offset + declaredLength))
            {
                _offset += declaredLength;
                return declaredPrefixDecoded;
            }
        }

        int bodyStart = _offset;
        if (SavegameRleCodec.TryDecodePrefix(_payload.AsSpan(bodyStart), expectedDecodedSize, out byte[] decoded, out int consumed)
            && consumed > 0)
        {
            int tentativeEnd = bodyStart + consumed;
            if (IsAcceptedBoundary(fieldName, tentativeEnd))
            {
                _offset = tentativeEnd;
                return decoded;
            }

            if (TryFindNearbyBoundary(fieldName, bodyStart, expectedDecodedSize, tentativeEnd, out int adjustedEnd, out byte[] adjustedDecoded))
            {
                _offset = adjustedEnd;
                return adjustedDecoded;
            }
        }

        (int end, decoded) = FindRleBoundary(fieldName, bodyStart, expectedDecodedSize);
        _offset = end;
        return decoded;
    }

    private (int end, byte[] decoded) FindRleBoundary(string fieldName, int bodyStart, int expectedDecodedSize)
    {
        foreach (int candidateEnd in EnumerateLikelyBoundaries(bodyStart))
        {
            if (!IsAcceptedBoundary(fieldName, candidateEnd))
            {
                continue;
            }

            byte[] encoded = _payload.AsSpan(bodyStart, candidateEnd - bodyStart).ToArray();
            if (SavegameRleCodec.TryDecodeExact(encoded, expectedDecodedSize, out byte[] decoded))
            {
                return (candidateEnd, decoded);
            }
        }

        throw new InvalidDataException("Failed to find a valid RLE boundary.");
    }

    private bool TryFindNearbyBoundary(string fieldName, int bodyStart, int expectedDecodedSize, int tentativeEnd, out int boundaryEnd, out byte[] decoded)
    {
        const int maxDelta = 96;

        for (int delta = 1; delta <= maxDelta; delta++)
        {
            int candidateEnd = tentativeEnd - delta;
            if (candidateEnd <= bodyStart)
            {
                break;
            }

            if (!IsAcceptedBoundary(fieldName, candidateEnd))
            {
                continue;
            }

            byte[] encoded = _payload.AsSpan(bodyStart, candidateEnd - bodyStart).ToArray();
            try
            {
                decoded = SavegameRleCodec.Decode(encoded, expectedDecodedSize);
                boundaryEnd = candidateEnd;
                return true;
            }
            catch (SavegameDatDecompressionFailedException)
            {
                // Keep searching nearby boundaries.
            }
        }

        for (int delta = 1; delta <= maxDelta; delta++)
        {
            int candidateEnd = tentativeEnd + delta;
            if (candidateEnd > _payload.Length)
            {
                break;
            }

            if (!IsAcceptedBoundary(fieldName, candidateEnd))
            {
                continue;
            }

            byte[] encoded = _payload.AsSpan(bodyStart, candidateEnd - bodyStart).ToArray();
            try
            {
                decoded = SavegameRleCodec.Decode(encoded, expectedDecodedSize);
                boundaryEnd = candidateEnd;
                return true;
            }
            catch (SavegameDatDecompressionFailedException)
            {
                // Keep searching nearby boundaries.
            }
        }

        boundaryEnd = 0;
        decoded = Array.Empty<byte>();
        return false;
    }

    private bool IsAcceptedBoundary(string fieldName, int position)
    {
        return IsKnownTagBoundary(position);
    }

    private bool LooksLikeValidPostBlocksBoundary(int position)
    {
        try
        {
            var probe = new MccCompactNbtChunkPayloadParser(_payload)
            {
                _offset = position,
            };

            var level = new NbtCompound("Level");
            probe.ParseLevelTags(level);

            return level.Contains("Data")
                && level.Contains("SkyLight")
                && level.Contains("BlockLight")
                && level.Contains("HeightMap");
        }
        catch
        {
            return false;
        }
    }

    private IEnumerable<int> EnumerateLikelyBoundaries(int bodyStart)
    {
        var boundaries = new SortedSet<int>();

        foreach (byte[] marker in KnownTagMarkers)
        {
            int searchStart = bodyStart + 1;
            while (searchStart < _payload.Length)
            {
                int relative = _payload.AsSpan(searchStart).IndexOf(marker);
                if (relative < 0)
                {
                    break;
                }

                int foundAt = searchStart + relative;
                if (foundAt > bodyStart)
                {
                    boundaries.Add(foundAt);
                }

                searchStart = foundAt + 1;
            }
        }

        for (int i = bodyStart + 1; i < _payload.Length; i++)
        {
            if (_payload[i] == (byte)NbtTagType.End)
            {
                boundaries.Add(i);
            }
        }

        boundaries.Add(_payload.Length);
        return boundaries;
    }

    private bool IsLikelyTagBoundary(int position)
    {
        if (position == _payload.Length)
        {
            return true;
        }

        if (position > _payload.Length)
        {
            return false;
        }

        byte typeByte = _payload[position];
        if (typeByte == (byte)NbtTagType.End)
        {
            return true;
        }

        if (typeByte < (byte)NbtTagType.Byte || typeByte > (byte)NbtTagType.IntArray)
        {
            return false;
        }

        if (position + 3 > _payload.Length)
        {
            return false;
        }

        ushort nameLength = BinaryPrimitives.ReadUInt16BigEndian(_payload.AsSpan(position + 1, 2));
        int nameStart = position + 3;
        return nameStart + nameLength <= _payload.Length;
    }

    private bool IsKnownTagBoundary(int position)
    {
        if (!IsLikelyTagBoundary(position))
        {
            return false;
        }

        if (position == _payload.Length || _payload[position] == (byte)NbtTagType.End)
        {
            return true;
        }

        foreach (byte[] marker in KnownTagMarkers)
        {
            if (_payload.AsSpan(position).StartsWith(marker))
            {
                return true;
            }
        }

        return false;
    }

    private byte[] ReadRawByteArray(string fieldName, int expectedLength)
    {
        RequireByte((byte)NbtTagType.ByteArray);
        if (!string.Equals(ReadString(), fieldName, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Expected field '{fieldName}'.");
        }

        int length = ReadInt32();
        if (length != expectedLength)
        {
            throw new InvalidDataException($"Unexpected {fieldName} length: {length}.");
        }

        return ReadSizedBytes(length);
    }

    private void RequireByte(byte expected)
    {
        byte value = ReadByte();
        if (value != expected)
        {
            throw new InvalidDataException($"Expected 0x{expected:X2}, got 0x{value:X2}.");
        }
    }

    private byte ReadByte()
    {
        if (_offset >= _payload.Length)
        {
            throw new EndOfStreamException();
        }

        return _payload[_offset++];
    }

    private short ReadInt16()
    {
        if (_offset + sizeof(short) > _payload.Length)
        {
            throw new EndOfStreamException();
        }

        short value = BinaryPrimitives.ReadInt16BigEndian(_payload.AsSpan(_offset, sizeof(short)));
        _offset += sizeof(short);
        return value;
    }

    private int ReadInt32()
    {
        if (_offset + sizeof(int) > _payload.Length)
        {
            throw new EndOfStreamException();
        }

        int value = BinaryPrimitives.ReadInt32BigEndian(_payload.AsSpan(_offset, sizeof(int)));
        _offset += sizeof(int);
        return value;
    }

    private long ReadInt64()
    {
        if (_offset + sizeof(long) > _payload.Length)
        {
            throw new EndOfStreamException();
        }

        long value = BinaryPrimitives.ReadInt64BigEndian(_payload.AsSpan(_offset, sizeof(long)));
        _offset += sizeof(long);
        return value;
    }

    private float ReadSingle()
    {
        if (_offset + sizeof(int) > _payload.Length)
        {
            throw new EndOfStreamException();
        }

        int bits = BinaryPrimitives.ReadInt32BigEndian(_payload.AsSpan(_offset, sizeof(int)));
        _offset += sizeof(int);
        return BitConverter.Int32BitsToSingle(bits);
    }

    private double ReadDouble()
    {
        if (_offset + sizeof(long) > _payload.Length)
        {
            throw new EndOfStreamException();
        }

        long bits = BinaryPrimitives.ReadInt64BigEndian(_payload.AsSpan(_offset, sizeof(long)));
        _offset += sizeof(long);
        return BitConverter.Int64BitsToDouble(bits);
    }

    private string ReadString()
    {
        int length = ReadInt16();
        if (length < 0)
        {
            throw new InvalidDataException("Negative NBT string length.");
        }

        byte[] bytes = ReadSizedBytes(length);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private int[] ReadIntArray()
    {
        int count = ReadInt32();
        if (count < 0)
        {
            throw new InvalidDataException("Negative NBT int array length.");
        }

        var values = new int[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = ReadInt32();
        }

        return values;
    }

    private byte[] ReadSizedBytes(int count)
    {
        if (count < 0)
        {
            throw new InvalidDataException("Negative byte length.");
        }

        if (_offset + count > _payload.Length)
        {
            throw new EndOfStreamException();
        }

        byte[] bytes = _payload.AsSpan(_offset, count).ToArray();
        _offset += count;
        return bytes;
    }
}
