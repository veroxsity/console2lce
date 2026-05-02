namespace Console2Lce.Tests;

public sealed class MinecraftXbox360ChunkDecoderTests
{
    [Fact]
    public void DecodeSample_ReportsFailureWhenNoDecoderSucceeds()
    {
        byte[] regionBytes = BuildRegionWithChunk([]);
        MinecraftXbox360RegionChunk chunk = new(
            Index: 0,
            X: 0,
            Z: 0,
            Timestamp: 0,
            SectorNumber: 2,
            SectorCount: 1,
            ChunkOffset: 8192,
            PayloadOffset: 8200,
            StoredLength: 12,
            DecompressedLength: 32768,
            UsesRleCompression: true);

        var decoder = new MinecraftXbox360ChunkDecoder();

        MinecraftXbox360ChunkDecodeReport report = decoder.DecodeSample("r.0.0.mcr", chunk, regionBytes);

        Assert.False(report.Success);
        Assert.Contains(report.Attempts, attempt => attempt.Decoder == "XMemLzx_128k_128k" && !attempt.Success);
    }

    private static byte[] BuildRegionWithChunk(byte[] payload)
    {
        byte[] region = new byte[MinecraftXbox360RegionParser.SectorBytes * 3];

        // Header table entry for chunk 0 -> sector 2, length 1 sector.
        region[0] = 0x00;
        region[1] = 0x00;
        region[2] = 0x02;
        region[3] = 0x01;

        int chunkOffset = MinecraftXbox360RegionParser.HeaderBytes;
        region[chunkOffset] = 0x80;
        region[chunkOffset + 1] = 0x00;
        region[chunkOffset + 2] = 0x00;
        region[chunkOffset + 3] = 0x0C;
        region[chunkOffset + 4] = 0x00;
        region[chunkOffset + 5] = 0x00;
        region[chunkOffset + 6] = 0x80;
        region[chunkOffset + 7] = 0x00;

        // Compressed payload bytes are intentionally invalid so built-in attempts fail.
        for (int index = 0; index < 12; index++)
        {
            region[chunkOffset + 8 + index] = (byte)(0xA0 + index);
        }

        return region;
    }

}
