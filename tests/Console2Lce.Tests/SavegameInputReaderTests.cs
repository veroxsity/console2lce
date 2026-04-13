namespace Console2Lce.Tests;

public sealed class SavegameInputReaderTests
{
    [Fact]
    public void Read_TreatsUnknownInputAsRawSavegameDat()
    {
        byte[] inputBytes = [0x00, 0x85, 0x7A, 0xC0, 0x00, 0x00, 0x00, 0x00];

        SavegameInputData input = SavegameInputReader.Read(inputBytes);

        Assert.False(input.IsStfsPackage);
        Assert.Null(input.Metadata);
        Assert.Empty(input.Entries);
        Assert.Null(input.FirstBlockOffset);
        Assert.Empty(input.LeadingPrefixBytes);
        Assert.Equal(inputBytes, input.SavegameBytes);
    }

    [Fact]
    public void Read_ExtractsSavegameDatFromStfsPackage()
    {
        byte[] packageBytes = BuildSingleFilePackage("savegame.dat", [0xDE, 0xAD, 0xBE, 0xEF], out int firstBlockOffset);

        SavegameInputData input = SavegameInputReader.Read(packageBytes);

        Assert.True(input.IsStfsPackage);
        Assert.NotNull(input.Metadata);
        Assert.Single(input.Entries);
        Assert.Equal(firstBlockOffset, input.FirstBlockOffset);
        Assert.Equal([0x00, 0x00, 0x00, 0x00], input.LeadingPrefixBytes);
        Assert.Equal([0xDE, 0xAD, 0xBE, 0xEF], input.SavegameBytes);
    }

    private static byte[] BuildSingleFilePackage(string fileName, byte[] fileBytes, out int firstBlockOffset)
    {
        const int tableBlock = 0x212;
        const int dataBlock = 0x213;
        const int fileTableOffset = 0x226000;
        const int dataBlockOffset = 0x227000;

        byte[] packageBytes = new byte[dataBlockOffset + 0x1000];
        "CON "u8.CopyTo(packageBytes);

        WriteInt32BigEndian(packageBytes, 0x340, 0x971A);
        packageBytes[0x37B] = 2;
        WriteInt16LittleEndian(packageBytes, 0x37C, 1);
        WriteInt24LittleEndian(packageBytes.AsSpan(), 0x37E, tableBlock);

        WriteDirectoryEntry(packageBytes.AsSpan(fileTableOffset, 0x40), fileName, fileBytes.Length, dataBlock);

        firstBlockOffset = dataBlockOffset;
        fileBytes.CopyTo(packageBytes, firstBlockOffset);
        return packageBytes;
    }

    private static void WriteDirectoryEntry(Span<byte> entry, string fileName, int fileSize, int startingBlock)
    {
        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(fileName);
        nameBytes.CopyTo(entry);
        entry[0x28] = (byte)(0x40 | nameBytes.Length);
        WriteInt24LittleEndian(entry, 0x29, 1);
        WriteInt24LittleEndian(entry, 0x2C, 1);
        WriteInt24LittleEndian(entry, 0x2F, startingBlock);
        entry[0x32] = 0xFF;
        entry[0x33] = 0xFF;
        WriteInt32BigEndian(entry, 0x34, fileSize);
    }

    private static void WriteInt16LittleEndian(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteInt32BigEndian(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }

    private static void WriteInt32BigEndian(Span<byte> bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }

    private static void WriteInt24LittleEndian(Span<byte> bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
    }
}
