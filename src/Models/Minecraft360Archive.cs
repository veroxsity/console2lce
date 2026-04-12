using System.Collections.ObjectModel;

namespace Console2Lce;

public sealed class Minecraft360Archive
{
    public Minecraft360Archive(
        Minecraft360ArchiveHeader header,
        IEnumerable<Minecraft360ArchiveEntry> entries,
        IReadOnlyDictionary<string, byte[]> files)
    {
        if (header.DecompressedSize < 0)
        {
            throw new InvalidMinecraft360ArchiveHeaderException("Decompressed size cannot be negative.");
        }

        Header = header;

        List<Minecraft360ArchiveEntry> entryList = entries.ToList();
        foreach (Minecraft360ArchiveEntry entry in entryList)
        {
            ArchiveRangeValidator.EnsureWithinBounds(entry.Offset, entry.Length, header.DecompressedSize, entry.Name);
        }

        Entries = new ReadOnlyCollection<Minecraft360ArchiveEntry>(entryList);
        Files = new ReadOnlyDictionary<string, byte[]>(
            new Dictionary<string, byte[]>(files, StringComparer.OrdinalIgnoreCase));
    }

    public Minecraft360ArchiveHeader Header { get; }

    public IReadOnlyList<Minecraft360ArchiveEntry> Entries { get; }

    public IReadOnlyDictionary<string, byte[]> Files { get; }

    public bool TryGetFile(string name, out byte[]? bytes)
    {
        return Files.TryGetValue(name, out bytes);
    }
}
