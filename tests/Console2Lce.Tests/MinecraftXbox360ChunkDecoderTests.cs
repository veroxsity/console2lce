namespace Console2Lce.Tests;

public sealed class MinecraftXbox360ChunkDecoderTests
{
    [Fact]
    public void DecodeSample_UsesExternalDecoderFallbackWhenBuiltInAttemptsFail()
    {
        byte[] payload = BuildLegacyChunkPayload(3, -7);
        byte[] regionBytes = BuildRegionWithChunk(payload);
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

        var decoder = new MinecraftXbox360ChunkDecoder(new StubExternalChunkDecoder(payload));

        MinecraftXbox360ChunkDecodeReport report = decoder.DecodeSample("r.0.0.mcr", chunk, regionBytes);

        Assert.True(report.Success);
        Assert.Equal("MccXBOXSupport64", report.Decoder);
        Assert.Equal(payload.Length, report.DecodedLength);
        Assert.Equal("LegacyNbt", report.PayloadKind);
        Assert.Equal(3, report.ChunkX);
        Assert.Equal(-7, report.ChunkZ);
        Assert.True(report.HasLevelWrapper);
        Assert.Contains(report.Attempts, attempt => attempt.Decoder == "MccXBOXSupport64" && attempt.Success);
    }

    private static byte[] BuildLegacyChunkPayload(int chunkX, int chunkZ)
    {
        return MinecraftConsoleChunkPayloadCodecTestsShim.BuildLegacyChunkNbt(chunkX, chunkZ);
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

    private sealed class StubExternalChunkDecoder : IMinecraftXbox360ChunkExternalDecoder
    {
        private readonly byte[] _decodedBytes;

        public StubExternalChunkDecoder(byte[] decodedBytes)
        {
            _decodedBytes = decodedBytes;
        }

        public string DecoderName => "MccXBOXSupport64";

        public bool TryDecode(ReadOnlySpan<byte> compressedBytes, int expectedDecompressedSize, out byte[] decodedBytes, out string? failure)
        {
            decodedBytes = _decodedBytes;
            failure = null;
            return true;
        }
    }

    private static class MinecraftConsoleChunkPayloadCodecTestsShim
    {
        public static byte[] BuildLegacyChunkNbt(int chunkX, int chunkZ)
        {
            using var ms = new MemoryStream();
            var root = new fNbt.NbtCompound(string.Empty)
            {
                new fNbt.NbtCompound("Level")
                {
                    new fNbt.NbtInt("xPos", chunkX),
                    new fNbt.NbtInt("zPos", chunkZ),
                    new fNbt.NbtByteArray("Blocks", new byte[32768]),
                    new fNbt.NbtByteArray("Data", new byte[16384]),
                    new fNbt.NbtByteArray("SkyLight", new byte[16384]),
                    new fNbt.NbtByteArray("BlockLight", new byte[16384]),
                    new fNbt.NbtByteArray("HeightMap", new byte[256]),
                    new fNbt.NbtList("Entities", fNbt.NbtTagType.Compound),
                    new fNbt.NbtList("TileEntities", fNbt.NbtTagType.Compound),
                }
            };

            new fNbt.NbtFile(root).SaveToStream(ms, fNbt.NbtCompression.None);
            return ms.ToArray();
        }
    }
}
