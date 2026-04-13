using System.Buffers.Binary;
using System.Text;

namespace Console2Lce.Tests;

public sealed class Minecraft360ArchiveParserTests
{
    [Fact]
    public void Parse_ReadsBigEndianXboxArchiveEntries()
    {
        byte[] bytes = BuildArchive();

        Minecraft360Archive archive = new Minecraft360ArchiveParser().Parse(bytes);

        Assert.Equal(0x30, archive.Header.HeaderOffset);
        Assert.Equal(2, archive.Header.FileCount);
        Assert.Equal(2, archive.Header.OriginalSaveVersion);
        Assert.Equal(8, archive.Header.CurrentSaveVersion);
        Assert.True(archive.TryGetFile("level.dat", out byte[]? levelDat));
        Assert.Equal([0x0A, 0x00, 0x00, 0x00], levelDat);
        Assert.True(archive.TryGetFile("players/123.dat", out byte[]? playerDat));
        Assert.Equal([0x01, 0x02, 0x03], playerDat);
    }

    [Fact]
    public void TryReadHeader_RejectsOutOfBoundsFileTable()
    {
        byte[] bytes = new byte[32];
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0, 4), 24);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4, 4), 1);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(8, 2), 2);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(10, 2), 8);

        bool success = Minecraft360ArchiveParser.TryReadHeader(bytes, out _);

        Assert.False(success);
    }

    [Fact]
    public void Parse_ReadsObservedFooterLayout()
    {
        byte[] bytes = BuildObservedArchive();

        Minecraft360Archive archive = new Minecraft360ArchiveParser().Parse(bytes);

        Assert.Equal(0x40, archive.Header.HeaderOffset);
        Assert.Equal(2, archive.Header.FileCount);
        Assert.True(archive.TryGetFile("level.dat", out byte[]? levelDat));
        Assert.Equal([0x0A, 0x00, 0x00, 0x00], levelDat);
        Assert.True(archive.TryGetFile("r.0.0.mcr", out byte[]? region));
        Assert.Equal([0x01, 0x02, 0x03, 0x04], region);
    }

    private static byte[] BuildArchive()
    {
        const int headerOffset = 0x30;
        byte[] bytes = new byte[headerOffset + (Minecraft360ArchiveParser.FileEntrySize * 2)];

        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0, 4), headerOffset);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4, 4), 2);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(8, 2), 2);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(10, 2), 8);

        bytes[12] = 0x0A;
        bytes[13] = 0x00;
        bytes[14] = 0x00;
        bytes[15] = 0x00;

        bytes[16] = 0x01;
        bytes[17] = 0x02;
        bytes[18] = 0x03;

        WriteEntry(bytes.AsSpan(headerOffset, Minecraft360ArchiveParser.FileEntrySize), "level.dat", 12, 4, 111);
        WriteEntry(bytes.AsSpan(headerOffset + Minecraft360ArchiveParser.FileEntrySize, Minecraft360ArchiveParser.FileEntrySize), "players/123.dat", 16, 3, 222);
        return bytes;
    }

    private static byte[] BuildObservedArchive()
    {
        const int footerOffset = 0x40;
        byte[] bytes = new byte[footerOffset + (Minecraft360ArchiveParser.FileEntrySize * 2)];
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0, 4), footerOffset);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4, 4), 2);

        bytes[12] = 0x0A;
        bytes[13] = 0x00;
        bytes[14] = 0x00;
        bytes[15] = 0x00;

        bytes[16] = 0x01;
        bytes[17] = 0x02;
        bytes[18] = 0x03;
        bytes[19] = 0x04;

        WriteEntry(bytes.AsSpan(footerOffset, Minecraft360ArchiveParser.FileEntrySize), "level.dat", 12, 4, 111);
        WriteEntry(bytes.AsSpan(footerOffset + Minecraft360ArchiveParser.FileEntrySize, Minecraft360ArchiveParser.FileEntrySize), "r.0.0.mcr", 16, 4, 222);
        return bytes;
    }

    private static void WriteEntry(Span<byte> destination, string name, int startOffset, int length, long modifiedTime)
    {
        byte[] encoded = Encoding.BigEndianUnicode.GetBytes(name);
        encoded.CopyTo(destination);
        BinaryPrimitives.WriteInt32BigEndian(destination.Slice(128, 4), length);
        BinaryPrimitives.WriteInt32BigEndian(destination.Slice(132, 4), startOffset);
        BinaryPrimitives.WriteInt64BigEndian(destination.Slice(136, 8), modifiedTime);
    }
}
