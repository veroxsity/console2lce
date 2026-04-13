using System.Buffers.Binary;
using Xunit.Abstractions;

namespace Console2Lce.Tests;

public sealed class MccCompactNbtRawAnalysisTest
{
    private readonly ITestOutputHelper _output;

    public MccCompactNbtRawAnalysisTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void AnalyzeRawStructure()
    {
        // Load the decoded chunk
        string chunkPath = @"c:\Users\Dan\Documents\Programming\.MinecraftLegacyEdition\console2lce\.local_testing\debug\chunk-0-decoded.bin";
        byte[] data = File.ReadAllBytes(chunkPath);

        _output.WriteLine($"Total chunk size: {data.Length} bytes\n");

        // Structure:
        // 0x00: Compound tag
        // 0x01-0x02: Name length (net16)
        // 0x03: Next tag
        // Eventually should see: 0x00 (end of root compound)

        // Skip the root compound header
        int offset = 0;
        _output.WriteLine($"0x{offset:X4}: {data[offset]:X2} - Compound tag");
        offset++;

        ushort rootNameLen = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
        _output.WriteLine($"0x{offset:X4}: {rootNameLen:X4} - Root name length: {rootNameLen} bytes");
        offset += 2;

        // Root name bytes
        if (rootNameLen > 0)
        {
            string rootName = System.Text.Encoding.ASCII.GetString(data.AsSpan(offset, rootNameLen));
            _output.WriteLine($"0x{offset:X4}: '{rootName}'");
            offset += rootNameLen;
        }

        // Now we should be at the first tag inside the root
        _output.WriteLine($"\nParsing tags starting at 0x{offset:X4}:\n");

        while (offset < data.Length)
        {
            byte tagType = data[offset];
            offset++;

            if (tagType == 0x00)
            {
                _output.WriteLine($"0x{offset - 1:X4}: 0x{tagType:X2} - END_TAG (end of compound)");
                break;
            }

            if (offset + 2 > data.Length)
            {
                _output.WriteLine($"0x{offset - 1:X4}: Not enough data for tag name");
                break;
            }

            ushort nameLen = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
            offset += 2;

            string name = System.Text.Encoding.ASCII.GetString(data.AsSpan(offset, nameLen));
            offset += nameLen;

            _output.WriteLine($"0x{offset - 2 - nameLen:X4}: Tag type 0x{tagType:X2}, name='{name}' ({nameLen} chars)");

            // Based on tag type, skip the value
            switch (tagType)
            {
                case 0x07: // Byte Array
                    if (offset + 4 > data.Length)
                    {
                        _output.WriteLine($"  Not enough data for byte array length");
                        break;
                    }
                    int arrayLen = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));
                    offset += 4;
                    _output.WriteLine($"  Byte Array length: {arrayLen} bytes (0x{arrayLen:X} / 0x{offset + arrayLen:X})");
                    offset += arrayLen;
                    break;

                case 0x0A: // Compound
                    _output.WriteLine($"  Compound (would recurse...)");
                    // Don't try to recurse; just show we found it
                    break;

                case 0x09: // List
                    if (offset + 5 > data.Length)
                        break;
                    byte listItemType = data[offset];
                    offset++;
                    int listLen = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));
                    offset += 4;
                    _output.WriteLine($"  List of {listItemType:X2}, length: {listLen} items");
                    // Skip list content
                    break;

                case 0x01: // Byte
                    _output.WriteLine($"  Byte value: 0x{data[offset]:X2}");
                    offset += 1;
                    break;

                case 0x02: // Short
                    short shortVal = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset, 2));
                    offset += 2;
                    _output.WriteLine($"  Short value: {shortVal}");
                    break;

                case 0x03: // Int
                    int intVal = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));
                    offset += 4;
                    _output.WriteLine($"  Int value: {intVal}");
                    break;

                case 0x04: // Long
                    long longVal = BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(offset, 8));
                    offset += 8;
                    _output.WriteLine($"  Long value: {longVal}");
                    break;

                case 0x08: // String
                    ushort strLen = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
                    offset += 2;
                    string strValue = System.Text.Encoding.UTF8.GetString(data.AsSpan(offset, strLen));
                    offset += strLen;
                    _output.WriteLine($"  String: '{strValue}'");
                    break;

                default:
                    _output.WriteLine($"  Unknown or complex tag type");
                    break;
            }

            if (offset >= data.Length)
            {
                _output.WriteLine($"\nReached end of data at 0x{offset:X4}");
                break;
            }
        }

        _output.WriteLine($"\nFinal offset: 0x{offset:X4} (chunk is {data.Length} bytes)");
        if (offset < data.Length)
        {
            _output.WriteLine($"Remaining bytes: {data.Length - offset}");
            // Show the remaining bytes
            byte[] remaining = data.AsSpan(offset, Math.Min(128, data.Length - offset)).ToArray();
            _output.WriteLine($"Remaining data (hex): {BitConverter.ToString(remaining).Replace("-", " ")}");
        }
    }
}
