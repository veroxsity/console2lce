using Console2Lce;
using fNbt;
using Xunit.Abstractions;

namespace Console2Lce.Tests;

public sealed class MccCompactNbtFullAnalysisTest
{
    private readonly ITestOutputHelper _output;

    public MccCompactNbtFullAnalysisTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ParseCompleteNbtStructure()
    {
        // Load the decoded chunk
        string chunkPath = @"c:\Users\Dan\Documents\Programming\.MinecraftLegacyEdition\console2lce\.local_testing\debug\chunk-0-decoded.bin";
        byte[] chunkData = File.ReadAllBytes(chunkPath);

        _output.WriteLine($"Chunk size: {chunkData.Length} bytes\n");

        // Try to parse as NBT
        try
        {
            var file = new NbtFile();
            file.LoadFromBuffer(chunkData, 0, chunkData.Length, NbtCompression.None);

            _output.WriteLine("✓ Successfully parsed as NBT!");
            _output.WriteLine($"Root tag name: '{file.RootTag.Name}'\n");

            PrintNbtStructure(file.RootTag, 0);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"✗ NBT parse failed: {ex.Message}");
            throw;
        }
    }

    private void PrintNbtStructure(NbtTag tag, int indent)
    {
        string indentStr = new string(' ', indent * 2);

        if (tag is NbtCompound compound)
        {
            _output.WriteLine($"{indentStr}Compound '{tag.Name}' with {compound.Count} tags:");
            foreach (NbtTag child in compound)
            {
                PrintNbtStructure(child, indent + 1);
            }
        }
        else if (tag is NbtList list)
        {
            _output.WriteLine($"{indentStr}List '{tag.Name}' of type {list.ListType} with {list.Count} items");
            if (list.Count <= 5)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    PrintNbtStructure(list[i], indent + 1);
                }
            }
        }
        else if (tag is NbtByteArray arr)
        {
            _output.WriteLine($"{indentStr}ByteArray '{tag.Name}': {arr.Value.Length} bytes");
        }
        else if (tag is NbtIntArray intArr)
        {
            _output.WriteLine($"{indentStr}IntArray '{tag.Name}': {intArr.Value.Length} ints");
        }
        else if (tag is NbtInt intTag)
        {
            _output.WriteLine($"{indentStr}Int '{tag.Name}' = {intTag.Value}");
        }
        else if (tag is NbtShort shortTag)
        {
            _output.WriteLine($"{indentStr}Short '{tag.Name}' = {shortTag.Value}");
        }
        else if (tag is NbtByte byteTag)
        {
            _output.WriteLine($"{indentStr}Byte '{tag.Name}' = 0x{byteTag.Value:X2}");
        }
        else if (tag is NbtLong longTag)
        {
            _output.WriteLine($"{indentStr}Long '{tag.Name}' = {longTag.Value}");
        }
        else if (tag is NbtString strTag)
        {
            _output.WriteLine($"{indentStr}String '{tag.Name}' = '{strTag.Value}'");
        }
        else
        {
            _output.WriteLine($"{indentStr}{tag.TagType} '{tag.Name}'");
        }
    }
}
