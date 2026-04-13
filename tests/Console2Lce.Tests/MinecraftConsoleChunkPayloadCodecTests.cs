using fNbt;
using System.Buffers.Binary;

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

    [Fact]
    public void TryDecodeToLegacyNbt_DecodesSyntheticMccCompactPayload()
    {
        byte[] payload = BuildMccCompactPayload(chunkX: 12, chunkZ: -7);

        bool success = MinecraftConsoleChunkPayloadCodec.TryDecodeToLegacyNbt(payload, out byte[] legacyNbt);

        Assert.True(success);

        var file = new NbtFile();
        file.LoadFromBuffer(legacyNbt, 0, legacyNbt.Length, NbtCompression.None);
        NbtCompound level = file.RootTag.Get<NbtCompound>("Level")!;
        Assert.Equal(12, level.Get<NbtInt>("xPos")!.Value);
        Assert.Equal(-7, level.Get<NbtInt>("zPos")!.Value);
        Assert.Equal(32768, level.Get<NbtByteArray>("Blocks")!.Value.Length);
        Assert.Equal(16384, level.Get<NbtByteArray>("Data")!.Value.Length);
        Assert.Equal(16384, level.Get<NbtByteArray>("SkyLight")!.Value.Length);
        Assert.Equal(16384, level.Get<NbtByteArray>("BlockLight")!.Value.Length);
        Assert.Equal(256, level.Get<NbtByteArray>("HeightMap")!.Value.Length);
    }

    [Fact]
    public void TryReadPayloadInfo_ReadsChunkCoordinatesFromMccCompactPayload()
    {
        byte[] payload = BuildMccCompactPayload(chunkX: 3, chunkZ: 9);

        bool success = MinecraftConsoleChunkPayloadCodec.TryReadPayloadInfo(
            payload,
            out string payloadKind,
            out int? chunkX,
            out int? chunkZ,
            out bool? hasLevelWrapper);

        Assert.True(success);
        Assert.Equal("MccCompactNbt", payloadKind);
        Assert.Equal(3, chunkX);
        Assert.Equal(9, chunkZ);
        Assert.True(hasLevelWrapper);
    }

    [Fact]
    public void ForceChunkCoordinates_PatchesLegacyNbtCoordinates()
    {
        byte[] payload = BuildLegacyChunkNbt(1, 2);

        byte[] patched = MinecraftConsoleChunkPayloadCodec.ForceChunkCoordinates(payload, -3, 11);

        Assert.NotEmpty(patched);
        bool success = MinecraftConsoleChunkPayloadCodec.TryReadChunkCoordinates(patched, out int chunkX, out int chunkZ, out bool hasLevelWrapper);
        Assert.True(success);
        Assert.Equal(-3, chunkX);
        Assert.Equal(11, chunkZ);
        Assert.True(hasLevelWrapper);
    }

    [Fact]
    public void ForceChunkCoordinates_PatchesMccCompactCoordinatesViaLegacyConversion()
    {
        byte[] payload = BuildMccCompactPayload(chunkX: 3, chunkZ: 9);

        byte[] patched = MinecraftConsoleChunkPayloadCodec.ForceChunkCoordinates(payload, -9, 0);

        Assert.NotEmpty(patched);
        bool success = MinecraftConsoleChunkPayloadCodec.TryReadChunkCoordinates(patched, out int chunkX, out int chunkZ, out bool hasLevelWrapper);
        Assert.True(success);
        Assert.Equal(-9, chunkX);
        Assert.Equal(0, chunkZ);
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

    private static byte[] BuildMccCompactPayload(int chunkX, int chunkZ)
    {
        byte[] blocks = new byte[32768];
        for (int index = 0; index < blocks.Length; index++)
        {
            blocks[index] = index < 1024 ? (byte)7 : (byte)1;
        }

        byte[] data = new byte[16384];
        byte[] skyLight = Enumerable.Repeat((byte)0xFF, 16384).ToArray();
        byte[] blockLight = new byte[16384];
        byte[] heightMap = Enumerable.Repeat((byte)64, 256).ToArray();

        using var ms = new MemoryStream();

        WriteByte(ms, (byte)NbtTagType.Compound);
        WriteString(ms, string.Empty);

        WriteByte(ms, (byte)NbtTagType.Compound);
        WriteString(ms, "Level");

        WriteRleByteArrayField(ms, "Blocks", 32768, blocks);
        WriteRleByteArrayField(ms, "Data", 16384, data);
        WriteRleByteArrayField(ms, "SkyLight", 16384, skyLight);
        WriteRleByteArrayField(ms, "BlockLight", 16384, blockLight);

        WriteByte(ms, (byte)NbtTagType.ByteArray);
        WriteString(ms, "HeightMap");
        WriteInt32(ms, heightMap.Length);
        ms.Write(heightMap, 0, heightMap.Length);

        WriteByte(ms, (byte)NbtTagType.Int);
        WriteString(ms, "xPos");
        WriteInt32(ms, chunkX);

        WriteByte(ms, (byte)NbtTagType.Int);
        WriteString(ms, "zPos");
        WriteInt32(ms, chunkZ);

        WriteByte(ms, (byte)NbtTagType.List);
        WriteString(ms, "Entities");
        WriteByte(ms, (byte)NbtTagType.Compound);
        WriteInt32(ms, 0);

        WriteByte(ms, (byte)NbtTagType.List);
        WriteString(ms, "TileEntities");
        WriteByte(ms, (byte)NbtTagType.Compound);
        WriteInt32(ms, 0);

        WriteByte(ms, (byte)NbtTagType.End); // End Level
        WriteByte(ms, (byte)NbtTagType.End); // End Root

        return ms.ToArray();
    }

    private static void WriteRleByteArrayField(Stream stream, string name, int declaredLength, byte[] decodedBytes)
    {
        WriteByte(stream, (byte)NbtTagType.ByteArray);
        WriteString(stream, name);
        WriteInt32(stream, declaredLength);
        byte[] encoded = EncodeRle(decodedBytes);
        stream.Write(encoded, 0, encoded.Length);
    }

    private static byte[] EncodeRle(byte[] data)
    {
        using var output = new MemoryStream();
        int index = 0;

        while (index < data.Length)
        {
            byte current = data[index++];
            int count = 1;

            while (index < data.Length && data[index] == current && count < 256)
            {
                index++;
                count++;
            }

            if (count <= 3)
            {
                if (current == 0xFF)
                {
                    output.WriteByte(0xFF);
                    output.WriteByte((byte)(count - 1));
                }
                else
                {
                    for (int run = 0; run < count; run++)
                    {
                        output.WriteByte(current);
                    }
                }

                continue;
            }

            output.WriteByte(0xFF);
            output.WriteByte((byte)(count - 1));
            output.WriteByte(current);
        }

        return output.ToArray();
    }

    private static void WriteByte(Stream stream, byte value) => stream.WriteByte(value);

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)bytes.Length));
        stream.Write(length);
        stream.Write(bytes, 0, bytes.Length);
    }
}
