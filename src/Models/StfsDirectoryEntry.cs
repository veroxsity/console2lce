namespace Console2Lce;

public sealed class StfsDirectoryEntry
{
    public StfsDirectoryEntry(string name, int entryOffset, int fileSizeCandidate, byte[] rawEntryBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(rawEntryBytes);

        Name = name;
        EntryOffset = entryOffset;
        FileSizeCandidate = fileSizeCandidate;
        RawEntryBytes = rawEntryBytes;
    }

    public string Name { get; }

    public int EntryOffset { get; }

    public int FileSizeCandidate { get; }

    public byte[] RawEntryBytes { get; }
}
