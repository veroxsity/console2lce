using System.Buffers.Binary;
using fNbt;

namespace Console2Lce;

public sealed class MccCompactNbtChunkPayloadParser
{
    private const int ExpectedBlocksSize = 32768;
    private const int ExpectedNibbleSize = ExpectedBlocksSize / 2;
    private const int ExpectedHeightMapSize = 256;

    private readonly byte[] _payload;
    private int _offset;

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

            byte[] blocks = ReadRleByteArrayBefore("Blocks", "Data", ExpectedBlocksSize);
            byte[] data = ReadRleByteArrayBefore("Data", "SkyLight", ExpectedNibbleSize);
            byte[] skyLight = ReadRleByteArrayBefore("SkyLight", "BlockLight", ExpectedNibbleSize);
            byte[] blockLight = ReadRleByteArrayBefore("BlockLight", "HeightMap", ExpectedNibbleSize);
            byte[] heightMap = ReadRawByteArray("HeightMap", ExpectedHeightMapSize);

            var level = new NbtCompound("Level")
            {
                new NbtByteArray("Blocks", blocks),
                new NbtByteArray("Data", data),
                new NbtByteArray("SkyLight", skyLight),
                new NbtByteArray("BlockLight", blockLight),
                new NbtByteArray("HeightMap", heightMap),
            };

            ParseLevelTailTags(level);

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
        catch
        {
            return false;
        }
    }

    private void ParseLevelTailTags(NbtCompound level)
    {
        while (_offset < _payload.Length)
        {
            NbtTagType type = (NbtTagType)ReadByte();
            if (type == NbtTagType.End)
            {
                // End of Level compound. Root end may still exist, which we ignore.
                return;
            }

            string name = ReadString();
            NbtTag value = ReadTagPayload(type, name);

            if (level.Contains(name))
            {
                continue;
            }

            level.Add(value);
        }
    }

    private NbtTag ReadTagPayload(NbtTagType type, string name)
    {
        return type switch
        {
            NbtTagType.Byte => new NbtByte(name, ReadByte()),
            NbtTagType.Short => new NbtShort(name, ReadInt16()),
            NbtTagType.Int => new NbtInt(name, ReadInt32()),
            NbtTagType.Long => new NbtLong(name, ReadInt64()),
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
            NbtTagType.String => new NbtString(string.Empty, ReadString()),
            NbtTagType.ByteArray => new NbtByteArray(string.Empty, ReadSizedBytes(ReadInt32())),
            NbtTagType.IntArray => new NbtIntArray(string.Empty, ReadIntArray()),
            NbtTagType.List => ReadList(string.Empty),
            NbtTagType.Compound => ReadCompound(string.Empty),
            _ => throw new InvalidDataException($"Unsupported NBT list element type: {type}"),
        };
    }

    private byte[] ReadRleByteArrayBefore(string fieldName, string nextFieldName, int expectedDecodedSize)
    {
        RequireByte((byte)NbtTagType.ByteArray);
        if (!string.Equals(ReadString(), fieldName, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Expected field '{fieldName}'.");
        }

        _ = ReadInt32(); // Declared logical length.

        int bodyStart = _offset;
        int end = FindRleBoundary(nextFieldName, bodyStart, expectedDecodedSize);
        byte[] encoded = _payload.AsSpan(bodyStart, end - bodyStart).ToArray();
        _offset = end;

        return SavegameRleCodec.Decode(encoded, expectedDecodedSize);
    }

    private int FindRleBoundary(string nextFieldName, int bodyStart, int expectedDecodedSize)
    {
        for (int candidateEnd = bodyStart + 1; candidateEnd < _payload.Length; candidateEnd++)
        {
            if (!IsNamedByteArrayTagAt(candidateEnd, nextFieldName))
            {
                continue;
            }

            byte[] encoded = _payload.AsSpan(bodyStart, candidateEnd - bodyStart).ToArray();
            try
            {
                _ = SavegameRleCodec.Decode(encoded, expectedDecodedSize);
                return candidateEnd;
            }
            catch (SavegameDatDecompressionFailedException)
            {
                // Keep searching until we hit a valid boundary.
            }
        }

        throw new InvalidDataException($"Failed to find RLE boundary before field '{nextFieldName}'.");
    }

    private bool IsNamedByteArrayTagAt(int position, string name)
    {
        if (position + 3 >= _payload.Length)
        {
            return false;
        }

        if (_payload[position] != (byte)NbtTagType.ByteArray)
        {
            return false;
        }

        ushort nameLength = BinaryPrimitives.ReadUInt16BigEndian(_payload.AsSpan(position + 1, 2));
        if (nameLength != name.Length)
        {
            return false;
        }

        int nameStart = position + 3;
        if (nameStart + nameLength > _payload.Length)
        {
            return false;
        }

        ReadOnlySpan<byte> expected = System.Text.Encoding.ASCII.GetBytes(name);
        return _payload.AsSpan(nameStart, nameLength).SequenceEqual(expected);
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
