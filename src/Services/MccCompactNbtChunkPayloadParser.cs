using System.Buffers.Binary;
using fNbt;

namespace Console2Lce;

/// <summary>
/// Parses and converts MccCompactNbt chunk payloads (MCC/Xbox 360 chunk format with RLE compression).
/// 
/// MccCompactNbt is a hybrid format that combines:
/// - NBT-like headers and structure for most fields
/// - RLE-encoded byte arrays for Blocks, Data, SkyLight, BlockLight
/// - Standard NBT compound wrapping for dynamic fields (Entities, TileEntities)
/// 
/// The decoded structure resembles 4J's CompressedChunkStorage format.
/// </summary>
public sealed class MccCompactNbtChunkPayloadParser
{
    private const int ExpectedBlocksSize = 32768; // 16*16*128
    private const int ExpectedNibbleSize = 16384;  // 16*16*128/2

    private readonly byte[] _data;
    private int _offset;

    public MccCompactNbtChunkPayloadParser(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        _offset = 0;
    }

    /// <summary>
    /// Attempts to parse an MccCompactNbt payload and convert it to 4J LegacyNbt format.
    /// Returns true if successful and writes the LegacyNbt to legacyNbt parameter.
    /// </summary>
    public bool TryParseAndConvertToLegacyNbt(out byte[] legacyNbt, out string? error)
    {
        legacyNbt = Array.Empty<byte>();
        error = null;

        try
        {
            // Parse the MccCompactNbt structure manually (not using standard NBT parser)
            // because we need to handle RLE-compressed fields

            // Root compound tag + name
            if (!SkipCompoundHeader("root"))
            {
                error = "Invalid root compound header";
                return false;
            }

            // Expect Level compound
            byte levelTagType = ReadByte();
            if (levelTagType != 0x0A) // Compound
            {
                error = $"Expected Compound tag for Level, got 0x{levelTagType:X2}";
                return false;
            }

            string levelName = ReadNbtString();
            if (levelName != "Level")
            {
                error = $"Expected 'Level' compound, got '{levelName}'";
                return false;
            }

            // Now we're inside the Level compound
            // Read all the RLE-encoded arrays and accumulate the chunk data

            var level = new NbtCompound("Level");

            // Blocks array (RLE-encoded)
            byte[] blocks = ReadRleByteArray("Blocks", ExpectedBlocksSize);
            if (blocks != null)
            {
                level.Add(new NbtByteArray("Blocks", blocks));
            }

            // Data array (RLE-encoded, nibbles)
            byte[] data = ReadRleByteArray("Data", ExpectedNibbleSize);
            if (data != null)
            {
                level.Add(new NbtByteArray("Data", data));
            }

            // SkyLight array (RLE-encoded, nibbles)
            byte[] skyLight = ReadRleByteArray("SkyLight", ExpectedNibbleSize);
            if (skyLight != null)
            {
                level.Add(new NbtByteArray("SkyLight", skyLight));
            }

            // BlockLight array (RLE-encoded, nibbles)
            byte[] blockLight = ReadRleByteArray("BlockLight", ExpectedNibbleSize);
            if (blockLight != null)
            {
                level.Add(new NbtByteArray("BlockLight", blockLight));
            }

            // HeightMap (standard NBT byte array)
            byte[] heightMap = ReadStandardNbtByteArray("HeightMap");
            if (heightMap != null)
            {
                level.Add(new NbtByteArray("HeightMap", heightMap));
            }

            // From this point on, we should be able to use standard NBT parsing for dynamic content
            // Try to read as standard NBT
            var file = new NbtFile();
            file.LoadFromBuffer(_data, _offset, _data.Length - _offset, NbtCompression.None);

            // Merge the dynamic content into our level compound
            foreach (NbtTag tag in file.RootTag)
            {
                if (tag.Name != "Level") // Avoid duplicate
                {
                    level.Add((NbtTag)tag.Clone());
                }
                else if (tag is NbtCompound levelCompound)
                {
                    foreach (NbtTag innerTag in levelCompound)
                    {
                        if (!level.Contains(innerTag.Name))
                        {
                            level.Add((NbtTag)innerTag.Clone());
                        }
                    }
                }
            }

            // Encode as legacy NBT
            var root = new NbtCompound(string.Empty) { level };
            using var ms = new MemoryStream();
            new NbtFile(root).SaveToStream(ms, NbtCompression.None);
            legacyNbt = ms.ToArray();
            return true;
        }
        catch (Exception ex)
        {
            error = $"Parse error: {ex.Message}";
            return false;
        }
    }

    private bool SkipCompoundHeader(string expectedName)
    {
        if (_offset + 1 > _data.Length)
            return false;

        byte tagType = _data[_offset];
        if (tagType != 0x0A) // Compound
            return false;
        _offset++;

        string name = ReadNbtString();
        return true;
    }

    private byte ReadByte()
    {
        if (_offset >= _data.Length)
            throw new InvalidOperationException("Attempted to read past end of stream");
        return _data[_offset++];
    }

    private string ReadNbtString()
    {
        if (_offset + 2 > _data.Length)
            throw new InvalidOperationException("Not enough data for string length");

        ushort len = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(_offset, 2));
        _offset += 2;

        if (_offset + len > _data.Length)
            throw new InvalidOperationException("Not enough data for string content");

        string result = System.Text.Encoding.UTF8.GetString(_data.AsSpan(_offset, len));
        _offset += len;
        return result;
    }

    private byte[]? ReadRleByteArray(string expectedName, int expectedSize)
    {
        if (_offset >= _data.Length)
            return null;

        byte tagType = _data[_offset];
        if (tagType == 0x00) // TAG_End
            return null;

        if (tagType != 0x07) // TAG_Byte_Array expected for RLE fields
            return null;

        _offset++;

        string name = ReadNbtString();
        if (name != expectedName)
        {
            // Not the field we expected; rewind conceptually (can't actually, so skip this)
            return null;
        }

        // Read array length (declared length is not used for RLE-compressed arrays)
        if (_offset + 4 > _data.Length)
            return null;

        int declaredLength = BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(_offset, 4));
        _offset += 4;

        // Find where the RLE data for this array ends
        // This is tricky: we need to find the next Valid NBT tag
        byte[] rleData = ExtractRleDataUntilNextTag();
        if (rleData.Length == 0)
            return null;

        // Decode the RLE data
        try
        {
            return SavegameRleCodec.Decode(rleData, expectedSize);
        }
        catch
        {
            return null;
        }
    }

    private byte[]? ReadStandardNbtByteArray(string expectedName)
    {
        if (_offset >= _data.Length)
            return null;

        byte tagType = _data[_offset];
        if (tagType != 0x07) // TAG_Byte_Array
            return null;

        _offset++;

        string name = ReadNbtString();
        if (name != expectedName)
        {
            // Not what we expected
            return null;
        }

        // Read array length
        if (_offset + 4 > _data.Length)
            return null;

        int length = BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(_offset, 4));
        _offset += 4;

        if (_offset + length > _data.Length)
            return null;

        byte[] result = _data.AsSpan(_offset, length).ToArray();
        _offset += length;
        return result;
    }

    private byte[] ExtractRleDataUntilNextTag()
    {
        int startOffset = _offset;

        // Scan forward looking for the next NBT tag (0x00-0x0A) that looks like a tag header
        // This is heuristic: look for a byte that could be a tag type, followed by a reasonable name length

        while (_offset < _data.Length)
        {
            byte b = _data[_offset];

            // Check if this could be a tag type
            if (b >= 0x00 && b <= 0x0A)
            {
                // Could be a tag; check if followed by reasonable name length
                if (_offset + 3 < _data.Length)
                {
                    ushort nameLen = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(_offset + 1, 2));
                    if (nameLen < 100 && _offset + 3 + nameLen < _data.Length)
                    {
                        // Looks like a valid tag header; stop here
                        break;
                    }
                }
            }

            _offset++;
        }

        return _data.AsSpan(startOffset, _offset - startOffset).ToArray();
    }
}
