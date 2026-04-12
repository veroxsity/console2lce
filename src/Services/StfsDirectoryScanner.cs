using System.Buffers.Binary;
using System.Text;

namespace Console2Lce;

public static class StfsDirectoryScanner
{
    public const int DefaultDirectoryOffset = 0xC000;
    public const int EntrySize = 0x40;
    public const int NameFieldSize = 0x28;
    public const int DefaultDirectorySpan = 0x1000;

    public static IReadOnlyList<StfsDirectoryEntry> ScanHeuristicEntries(ReadOnlyMemory<byte> packageBytes)
    {
        ReadOnlySpan<byte> bytes = packageBytes.Span;
        if (bytes.Length < DefaultDirectoryOffset + EntrySize)
        {
            return Array.Empty<StfsDirectoryEntry>();
        }

        int maxOffsetExclusive = Math.Min(bytes.Length, DefaultDirectoryOffset + DefaultDirectorySpan);
        var entries = new List<StfsDirectoryEntry>();

        for (int entryOffset = DefaultDirectoryOffset; entryOffset + EntrySize <= maxOffsetExclusive; entryOffset += EntrySize)
        {
            ReadOnlySpan<byte> entryBytes = bytes.Slice(entryOffset, EntrySize);
            if (entryBytes[0] == 0)
            {
                continue;
            }

            string? name = TryReadAsciiName(entryBytes.Slice(0, NameFieldSize));
            if (name is null)
            {
                continue;
            }

            int fileSizeCandidate = BinaryPrimitives.ReadInt32LittleEndian(entryBytes.Slice(NameFieldSize, sizeof(int)));
            if (fileSizeCandidate <= 0)
            {
                continue;
            }

            entries.Add(new StfsDirectoryEntry(name, entryOffset, fileSizeCandidate, entryBytes.ToArray()));
        }

        return entries;
    }

    private static string? TryReadAsciiName(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);
        if (end < 0)
        {
            end = bytes.Length;
        }

        if (end == 0)
        {
            return null;
        }

        ReadOnlySpan<byte> nameBytes = bytes.Slice(0, end);
        foreach (byte value in nameBytes)
        {
            if (value < 0x20 || value > 0x7E)
            {
                return null;
            }
        }

        return Encoding.ASCII.GetString(nameBytes);
    }
}
