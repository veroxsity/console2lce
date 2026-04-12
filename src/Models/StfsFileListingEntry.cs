namespace Console2Lce;

internal sealed record StfsFileListingEntry(
    int Index,
    string Name,
    byte Flags,
    int BlockCount,
    int StartingBlock,
    short PathIndicator,
    int FileSize)
{
    public bool IsDirectory => (Flags & 0x80) == 0x80;

    public bool UsesConsecutiveBlocks => (Flags & 0x40) == 0x40;

    public byte NameLength => (byte)(Flags & 0x3F);
}
