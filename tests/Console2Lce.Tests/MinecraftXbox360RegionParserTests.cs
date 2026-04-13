using System.Buffers.Binary;

namespace Console2Lce.Tests;

public sealed class MinecraftXbox360RegionParserTests
{
    [Fact]
    public void Parse_ReadsBigEndianChunkMetadata()
    {
        byte[] bytes = BuildRegionFile(
            chunkIndex: 0,
            timestamp: 123456,
            sectorNumber: 2,
            sectorCount: 1,
            storedLengthWithFlags: 0x80000018u,
            decompressedLength: 0x1234);

        MinecraftXbox360Region region = new MinecraftXbox360RegionParser().Parse(bytes, "r.0.0.mcr");

        MinecraftXbox360RegionChunk chunk = Assert.Single(region.Chunks);
        Assert.Equal("r.0.0.mcr", region.FileName);
        Assert.Equal(0, chunk.Index);
        Assert.Equal(0, chunk.X);
        Assert.Equal(0, chunk.Z);
        Assert.Equal(123456, chunk.Timestamp);
        Assert.Equal(2, chunk.SectorNumber);
        Assert.Equal(1, chunk.SectorCount);
        Assert.Equal(8192, chunk.ChunkOffset);
        Assert.Equal(8200, chunk.PayloadOffset);
        Assert.Equal(0x18, chunk.StoredLength);
        Assert.Equal(0x1234, chunk.DecompressedLength);
        Assert.True(chunk.UsesRleCompression);
    }

    [Fact]
    public void Parse_IgnoresEmptyChunkSlots()
    {
        byte[] bytes = new byte[MinecraftXbox360RegionParser.HeaderBytes];

        MinecraftXbox360Region region = new MinecraftXbox360RegionParser().Parse(bytes, "r.0.0.mcr");

        Assert.Empty(region.Chunks);
    }

    [Fact]
    public void Parse_RejectsChunkThatOverrunsAllocatedSectors()
    {
        byte[] bytes = BuildRegionFile(
            chunkIndex: 0,
            timestamp: 1,
            sectorNumber: 2,
            sectorCount: 1,
            storedLengthWithFlags: 0x00001000u,
            decompressedLength: 0x20);

        InvalidMinecraftXbox360RegionException exception = Assert.Throws<InvalidMinecraftXbox360RegionException>(
            () => new MinecraftXbox360RegionParser().Parse(bytes, "r.0.0.mcr"));

        Assert.Contains("overruns its allocated sectors", exception.Message);
    }

    [Fact]
    public void Parse_RejectsChunkThatPointsBeyondFile()
    {
        byte[] bytes = BuildRegionFile(
            chunkIndex: 0,
            timestamp: 1,
            sectorNumber: 4,
            sectorCount: 1,
            storedLengthWithFlags: 0x00000008u,
            decompressedLength: 0x20);

        Array.Resize(ref bytes, MinecraftXbox360RegionParser.HeaderBytes + MinecraftXbox360RegionParser.SectorBytes);

        InvalidMinecraftXbox360RegionException exception = Assert.Throws<InvalidMinecraftXbox360RegionException>(
            () => new MinecraftXbox360RegionParser().Parse(bytes, "r.0.0.mcr"));

        Assert.Contains("extends beyond the region length", exception.Message);
    }

    private static byte[] BuildRegionFile(
        int chunkIndex,
        int timestamp,
        int sectorNumber,
        int sectorCount,
        uint storedLengthWithFlags,
        int decompressedLength)
    {
        int length = (sectorNumber + sectorCount) * MinecraftXbox360RegionParser.SectorBytes;
        byte[] bytes = new byte[length];

        BinaryPrimitives.WriteInt32BigEndian(
            bytes.AsSpan(chunkIndex * sizeof(int), sizeof(int)),
            (sectorNumber << 8) | sectorCount);
        BinaryPrimitives.WriteInt32BigEndian(
            bytes.AsSpan(MinecraftXbox360RegionParser.SectorBytes + (chunkIndex * sizeof(int)), sizeof(int)),
            timestamp);

        int chunkOffset = sectorNumber * MinecraftXbox360RegionParser.SectorBytes;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(chunkOffset, sizeof(uint)), storedLengthWithFlags);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(chunkOffset + sizeof(uint), sizeof(int)), decompressedLength);

        int storedLength = checked((int)(storedLengthWithFlags & 0x7FFFFFFFu));
        for (int index = 0; index < Math.Min(storedLength, length - (chunkOffset + MinecraftXbox360RegionParser.ChunkHeaderSize)); index++)
        {
            bytes[chunkOffset + MinecraftXbox360RegionParser.ChunkHeaderSize + index] = (byte)(index + 1);
        }

        return bytes;
    }
}
