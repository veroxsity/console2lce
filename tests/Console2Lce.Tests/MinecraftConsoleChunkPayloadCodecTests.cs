using fNbt;

namespace Console2Lce.Tests;

public sealed class MinecraftConsoleChunkPayloadCodecTests
{
    [Fact]
    public void TryReadChunkCoordinates_ReadsLegacyNbtChunkCoordinates()
    {
        byte[] payload = BuildLegacyChunkNbt(12, -4);

        bool success = MinecraftConsoleChunkPayloadCodec.TryReadChunkCoordinates(payload, out int chunkX, out int chunkZ, out bool hasLevelWrapper);

        Assert.True(success);
        Assert.Equal(12, chunkX);
        Assert.Equal(-4, chunkZ);
        Assert.True(hasLevelWrapper);
    }

    [Fact]
    public void TryDecodeToLegacyNbt_RoundTripsLegacyNbt()
    {
        byte[] payload = BuildLegacyChunkNbt(1, 2);

        bool success = MinecraftConsoleChunkPayloadCodec.TryDecodeToLegacyNbt(payload, out byte[] legacyNbt);

        Assert.True(success);
        var file = new NbtFile();
        file.LoadFromBuffer(legacyNbt, 0, legacyNbt.Length, NbtCompression.None);
        NbtCompound level = file.RootTag.Get<NbtCompound>("Level")!;
        Assert.Equal(1, level.Get<NbtInt>("xPos")!.Value);
        Assert.Equal(2, level.Get<NbtInt>("zPos")!.Value);
    }

    [Fact]
    public void TryReadPayloadInfo_RecognizesMccCompactNbtShape()
    {
        byte[] payload =
        [
            0x0A, 0x00, 0x00,
            0x0A, 0x00, 0x05, (byte)'L', (byte)'e', (byte)'v', (byte)'e', (byte)'l',
            0x07, 0x00, 0x06, (byte)'B', (byte)'l', (byte)'o', (byte)'c', (byte)'k', (byte)'s',
            0x00, 0x00, 0x80, 0x00,
            0x01, 0x02, 0x03, 0x04,
        ];

        bool success = MinecraftConsoleChunkPayloadCodec.TryReadPayloadInfo(
            payload,
            out string payloadKind,
            out int? chunkX,
            out int? chunkZ,
            out bool? hasLevelWrapper);

        Assert.True(success);
        Assert.Equal("MccCompactNbt", payloadKind);
        Assert.Null(chunkX);
        Assert.Null(chunkZ);
        Assert.True(hasLevelWrapper);
    }

    private static byte[] BuildLegacyChunkNbt(int chunkX, int chunkZ)
    {
        var root = new NbtCompound(string.Empty)
        {
            new NbtCompound("Level")
            {
                new NbtInt("xPos", chunkX),
                new NbtInt("zPos", chunkZ),
                new NbtByteArray("Blocks", new byte[32768]),
                new NbtByteArray("Data", new byte[16384]),
                new NbtByteArray("SkyLight", new byte[16384]),
                new NbtByteArray("BlockLight", new byte[16384]),
                new NbtByteArray("HeightMap", new byte[256]),
                new NbtList("Entities", NbtTagType.Compound),
                new NbtList("TileEntities", NbtTagType.Compound),
            }
        };

        using var ms = new MemoryStream();
        new NbtFile(root).SaveToStream(ms, NbtCompression.None);
        return ms.ToArray();
    }
}
