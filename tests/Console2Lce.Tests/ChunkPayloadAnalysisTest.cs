using Console2Lce;
using Xunit.Abstractions;

namespace Console2Lce.Tests;

public sealed class ChunkPayloadAnalysisTest
{
    private readonly ITestOutputHelper _output;

    public ChunkPayloadAnalysisTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void AnalyzeChunkBlockPayload()
    {
        // Load the decoded chunk
        string chunkPath = @"c:\Users\Dan\Documents\Programming\.MinecraftLegacyEdition\console2lce\.local_testing\debug\chunk-0-decoded.bin";
        byte[] chunkData = File.ReadAllBytes(chunkPath);

        // The structure should be:
        // 0x00: 0A (Compound tag)
        // 0x01: 00 00 (name length = 0)
        // 0x03: 0A (Compound tag for Level)
        // 0x04: 00 05 (name length = 5 for "Level")
        // 0x06: 4C 65 76 65 6C ("Level")
        // 0x0B: 07 (Byte Array tag for Blocks)
        // 0x0C: 00 06 (name length = 6)
        // 0x0E: 42 6C 6F 63 6B 73 ("Blocks")
        // 0x14: 00 00 80 00 (array length = 32768)
        // 0x18: [RLE-encoded block data]

        int blockDataOffset = 0x18;
        byte[] rleData = chunkData.AsSpan(blockDataOffset).ToArray();
        
        Assert.Equal(0x0A, chunkData[0]);
        Assert.Equal(0x4C, chunkData[6]); // 'L' in Level
        Assert.Equal(0x42, chunkData[14]); // 'B' in Blocks

        // Try to decode the RLE data
        try
        {
            byte[] decoded = SavegameRleCodec.Decode(rleData, 32768);
            Assert.NotNull(decoded);
            Assert.Equal(32768, decoded.Length);
            
            _output.WriteLine("✓ RLE decode successful!");
            _output.WriteLine($"Decoded {decoded.Length} bytes of block data");
            
            // Print some statistics
            var blockFrequency = new Dictionary<byte, int>();
            foreach (byte block in decoded)
            {
                if (!blockFrequency.ContainsKey(block))
                    blockFrequency[block] = 0;
                blockFrequency[block]++;
            }
            
            _output.WriteLine($"\nBlock type frequency (sampled) - {blockFrequency.Count} unique block types:");
            
            // Print the 10 most common blocks
            var topBlocks = blockFrequency.OrderByDescending(x => x.Value).Take(10);
            foreach (var (blockId, count) in topBlocks)
            {
                double percent = count * 100.0 / 32768;
                // Common block IDs: 0=air, 1=stone, 2=grass, 3=dirt, 7=bedrock, etc.
                _output.WriteLine($"  Block 0x{blockId:X2}: {count,5} times ({percent:F2}%)");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"✗ RLE decode failed: {ex.Message}");
            throw;
        }
    }
}
