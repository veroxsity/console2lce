using System.Buffers.Binary;
using System.Text;
using fNbt;

namespace Console2Lce.Tests;

public sealed class MccCompactNbtChunkPayloadParserTests
{
    [Fact]
    public void TryParseToLegacyNbt_DecodesByteArraysUsingDeclaredEncodedLength()
    {
        byte[] blocksDecoded = CreateFilled(32768, 1);
        byte[] dataDecoded = CreateFilled(16384, 2);
        byte[] skyDecoded = CreateFilled(16384, 0xFF);
        byte[] blockLightDecoded = CreateFilled(16384, 0);
        byte[] heightMapDecoded = CreateFilled(256, 64);

        byte[] payload = BuildCompactPayload(
            blocksDecoded,
            dataDecoded,
            skyDecoded,
            blockLightDecoded,
            heightMapDecoded,
            chunkX: 12,
            chunkZ: -7);

        bool parsed = MccCompactNbtChunkPayloadParser.TryParseToLegacyNbt(payload, out byte[] legacyNbt);

        Assert.True(parsed);

        NbtFile nbt = new();
        nbt.LoadFromBuffer(legacyNbt, 0, legacyNbt.Length, NbtCompression.None);
        NbtCompound? level = nbt.RootTag.Get<NbtCompound>("Level");
        Assert.NotNull(level);

        Assert.Equal(12, level!.Get<NbtInt>("xPos")!.IntValue);
        Assert.Equal(-7, level.Get<NbtInt>("zPos")!.IntValue);
        Assert.Equal(blocksDecoded, level.Get<NbtByteArray>("Blocks")!.Value);
        Assert.Equal(dataDecoded, level.Get<NbtByteArray>("Data")!.Value);
        Assert.Equal(skyDecoded, level.Get<NbtByteArray>("SkyLight")!.Value);
        Assert.Equal(blockLightDecoded, level.Get<NbtByteArray>("BlockLight")!.Value);
        Assert.Equal(heightMapDecoded, level.Get<NbtByteArray>("HeightMap")!.Value);
    }

    private static byte[] BuildCompactPayload(
        byte[] blocks,
        byte[] data,
        byte[] sky,
        byte[] blockLight,
        byte[] heightMap,
        int chunkX,
        int chunkZ)
    {
        using MemoryStream ms = new();

        WriteTagHeader(ms, NbtTagType.Compound, string.Empty);
        WriteTagHeader(ms, NbtTagType.Compound, "Level");

        WriteCompactByteArrayTag(ms, "Blocks", blocks);
        WriteIntTag(ms, "xPos", chunkX);
        WriteCompactByteArrayTag(ms, "Data", data);
        WriteIntTag(ms, "zPos", chunkZ);
        WriteCompactByteArrayTag(ms, "BlockLight", blockLight);
        WriteCompactByteArrayTag(ms, "SkyLight", sky);
        WriteCompactByteArrayTag(ms, "HeightMap", heightMap);

        ms.WriteByte((byte)NbtTagType.End);
        ms.WriteByte((byte)NbtTagType.End);

        return ms.ToArray();
    }

    private static void WriteCompactByteArrayTag(MemoryStream ms, string name, byte[] decoded)
    {
        WriteTagHeader(ms, NbtTagType.ByteArray, name);
        byte[] encoded = EncodeRepeated(decoded);
        WriteInt32BigEndian(ms, encoded.Length);
        ms.Write(encoded, 0, encoded.Length);
    }

    private static void WriteIntTag(MemoryStream ms, string name, int value)
    {
        WriteTagHeader(ms, NbtTagType.Int, name);
        WriteInt32BigEndian(ms, value);
    }

    private static void WriteTagHeader(MemoryStream ms, NbtTagType type, string name)
    {
        ms.WriteByte((byte)type);
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);

        Span<byte> lenBytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lenBytes, (ushort)nameBytes.Length);
        ms.Write(lenBytes);
        ms.Write(nameBytes, 0, nameBytes.Length);
    }

    private static void WriteInt32BigEndian(MemoryStream ms, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        ms.Write(bytes);
    }

    private static byte[] EncodeRepeated(byte[] decoded)
    {
        List<byte> encoded = new();
        int offset = 0;

        while (offset < decoded.Length)
        {
            byte value = decoded[offset];
            int runLength = 1;
            while (offset + runLength < decoded.Length && decoded[offset + runLength] == value && runLength < 256)
            {
                runLength++;
            }

            if (runLength >= 4)
            {
                encoded.Add(0xFF);
                encoded.Add((byte)(runLength - 1));
                encoded.Add(value);
                offset += runLength;
                continue;
            }

            encoded.Add(value);
            offset++;
        }

        return encoded.ToArray();
    }

    private static byte[] CreateFilled(int length, byte value)
    {
        byte[] data = new byte[length];
        Array.Fill(data, value);
        return data;
    }
}
