using System.Buffers.Binary;
using System.Text;

namespace Console2Lce;

public sealed class Minecraft360ArchiveParser : IMinecraft360ArchiveParser
{
    public const int SaveFileHeaderSize = 8;
    public const int FileEntrySize = 144;
    private const int FileNameBytes = 128;

    public Minecraft360Archive Parse(ReadOnlyMemory<byte> decompressedBytes)
    {
        if (!TryReadHeader(decompressedBytes.Span, out Minecraft360ArchiveHeader header))
        {
            throw new InvalidMinecraft360ArchiveHeaderException("Decoded bytes do not contain a valid Xbox 360 archive header.");
        }

        List<Minecraft360ArchiveEntry> entries = ParseEntries(decompressedBytes.Span, header);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (Minecraft360ArchiveEntry entry in entries)
        {
            byte[] fileBytes = decompressedBytes.Slice(entry.Offset, entry.Length).ToArray();
            files[entry.Name] = fileBytes;
        }

        return new Minecraft360Archive(header, entries, files);
    }

    public static bool TryReadHeader(ReadOnlySpan<byte> bytes, out Minecraft360ArchiveHeader header)
    {
        header = default;

        if (TryReadLegacySourceHeader(bytes, out header))
        {
            return true;
        }

        if (bytes.Length < SaveFileHeaderSize)
        {
            return false;
        }

        int headerOffset = BinaryPrimitives.ReadInt32BigEndian(bytes[..4]);
        int fileCount = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(4, 4));

        if (headerOffset < SaveFileHeaderSize || headerOffset > bytes.Length)
        {
            return false;
        }

        if (fileCount <= 0 || fileCount > 1_000_000)
        {
            return false;
        }

        long fileTableSize = (long)fileCount * FileEntrySize;
        if (headerOffset + fileTableSize > bytes.Length)
        {
            return false;
        }

        header = new Minecraft360ArchiveHeader(
            headerOffset,
            fileCount,
            0,
            0,
            bytes.Length);

        return true;
    }

    private static bool TryReadLegacySourceHeader(ReadOnlySpan<byte> bytes, out Minecraft360ArchiveHeader header)
    {
        header = default;

        if (bytes.Length < 12)
        {
            return false;
        }

        int headerOffset = BinaryPrimitives.ReadInt32BigEndian(bytes[..4]);
        int fileCount = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(4, 4));
        short originalSaveVersion = BinaryPrimitives.ReadInt16BigEndian(bytes.Slice(8, 2));
        short currentSaveVersion = BinaryPrimitives.ReadInt16BigEndian(bytes.Slice(10, 2));

        if (headerOffset < 12 || headerOffset > bytes.Length)
        {
            return false;
        }

        long fileTableSize = (long)fileCount * FileEntrySize;
        if (fileCount <= 0 || fileCount > 1_000_000 || headerOffset + fileTableSize > bytes.Length)
        {
            return false;
        }

        if (originalSaveVersion <= 0 || currentSaveVersion <= 0)
        {
            return false;
        }

        header = new Minecraft360ArchiveHeader(
            headerOffset,
            fileCount,
            originalSaveVersion,
            currentSaveVersion,
            bytes.Length);

        return true;
    }

    private static List<Minecraft360ArchiveEntry> ParseEntries(ReadOnlySpan<byte> bytes, Minecraft360ArchiveHeader header)
    {
        var entries = new List<Minecraft360ArchiveEntry>(header.FileCount);
        int position = header.HeaderOffset;

        for (int index = 0; index < header.FileCount; index++)
        {
            ReadOnlySpan<byte> entryBytes = bytes.Slice(position, FileEntrySize);
            position += FileEntrySize;

            string name = Encoding.BigEndianUnicode.GetString(entryBytes[..FileNameBytes]).TrimEnd('\0');
            int length = BinaryPrimitives.ReadInt32BigEndian(entryBytes.Slice(128, 4));
            int startOffset = BinaryPrimitives.ReadInt32BigEndian(entryBytes.Slice(132, 4));

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (length <= 0)
            {
                continue;
            }

            ArchiveRangeValidator.EnsureWithinBounds(startOffset, length, header.DecompressedSize, name);
            entries.Add(new Minecraft360ArchiveEntry(name, startOffset, length));
        }

        return entries;
    }
}
