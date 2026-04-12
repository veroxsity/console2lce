using System.Buffers.Binary;
using System.Text;

namespace Console2Lce;

public sealed class StfsReader : IStfsReader
{
    private const int FileListingEntrySize = 0x40;
    private const int FileListingNameSize = 0x28;
    private const int StfsBlockSize = 0x1000;
    private const int HashRecordSize = 0x18;
    private const int BlocksPerHashTable = 0xAA;
    private const int EndOfChain = 0xFFFFFF;

    public IReadOnlyList<StfsFileEntry> EnumerateEntries(ReadOnlyMemory<byte> packageBytes)
    {
        StfsPackageMetadata metadata = StfsPackageDescriptorReader.Read(packageBytes.Span);
        IReadOnlyList<StfsFileListingEntry> entries = ParseFileListingEntries(packageBytes.Span, metadata);

        return entries
            .Where(entry => !entry.IsDirectory)
            .Select(entry => new StfsFileEntry(
                ResolvePath(entry, entries),
                entry.FileSize,
                entry.StartingBlock))
            .ToList();
    }

    public byte[] ReadFile(ReadOnlyMemory<byte> packageBytes, string entryName)
    {
        return ReadFileWithContext(packageBytes, entryName).Bytes;
    }

    public StfsExtractedFile ReadFileWithContext(ReadOnlyMemory<byte> packageBytes, string entryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);

        ReadOnlySpan<byte> bytes = packageBytes.Span;
        StfsPackageMetadata metadata = StfsPackageDescriptorReader.Read(bytes);
        IReadOnlyList<StfsFileListingEntry> entries = ParseFileListingEntries(bytes, metadata);

        StfsFileListingEntry? entry = entries
            .Where(candidate => !candidate.IsDirectory)
            .FirstOrDefault(candidate => ResolvePath(candidate, entries).Equals(entryName, StringComparison.OrdinalIgnoreCase)
                || candidate.Name.Equals(entryName, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            throw new SavegameDatNotFoundException();
        }

        int firstBlockOffset = ComputeDataBlockOffset(metadata, entry.StartingBlock);
        byte[] fileBytes = ReadFileBytes(bytes, metadata, entry);
        return new StfsExtractedFile(
            ResolvePath(entry, entries),
            entry.FileSize,
            entry.StartingBlock,
            firstBlockOffset,
            fileBytes);
    }

    private static IReadOnlyList<StfsFileListingEntry> ParseFileListingEntries(ReadOnlySpan<byte> packageBytes, StfsPackageMetadata metadata)
    {
        byte[] tableBytes = ReadFileTable(packageBytes, metadata);
        var entries = new List<StfsFileListingEntry>();

        for (int offset = 0; offset + FileListingEntrySize <= tableBytes.Length; offset += FileListingEntrySize)
        {
            ReadOnlySpan<byte> entryBytes = tableBytes.AsSpan(offset, FileListingEntrySize);
            if (entryBytes.IndexOfAnyExcept((byte)0) < 0)
            {
                break;
            }

            string name = ReadNullPaddedAscii(entryBytes.Slice(0, FileListingNameSize));
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            byte flags = entryBytes[0x28];
            int blockCount = ReadInt24LittleEndian(entryBytes, 0x29);
            int blockCountCopy = ReadInt24LittleEndian(entryBytes, 0x2C);
            int startingBlock = ReadInt24LittleEndian(entryBytes, 0x2F);
            short pathIndicator = BinaryPrimitives.ReadInt16BigEndian(entryBytes.Slice(0x32, sizeof(short)));
            int fileSize = BinaryPrimitives.ReadInt32BigEndian(entryBytes.Slice(0x34, sizeof(int)));

            if (blockCount != blockCountCopy)
            {
                continue;
            }

            entries.Add(new StfsFileListingEntry(
                entries.Count,
                name,
                flags,
                blockCount,
                startingBlock,
                pathIndicator,
                fileSize));
        }

        return entries;
    }

    private static byte[] ReadFileTable(ReadOnlySpan<byte> packageBytes, StfsPackageMetadata metadata)
    {
        byte[] tableBytes = new byte[metadata.FileTableBlockCount * StfsBlockSize];
        for (int index = 0; index < metadata.FileTableBlockCount; index++)
        {
            int blockNumber = metadata.FileTableBlockNumber + index;
            int sourceOffset = ComputeDataBlockOffset(metadata, blockNumber);
            packageBytes.Slice(sourceOffset, StfsBlockSize).CopyTo(tableBytes.AsSpan(index * StfsBlockSize, StfsBlockSize));
        }

        return tableBytes;
    }

    private static byte[] ReadFileBytes(ReadOnlySpan<byte> packageBytes, StfsPackageMetadata metadata, StfsFileListingEntry entry)
    {
        if (entry.FileSize == 0)
        {
            return Array.Empty<byte>();
        }

        var output = new byte[entry.FileSize];
        int written = 0;
        int currentBlock = entry.StartingBlock;

        for (int blockIndex = 0; blockIndex < entry.BlockCount && written < output.Length; blockIndex++)
        {
            int sourceOffset = ComputeDataBlockOffset(metadata, currentBlock);
            int bytesToCopy = Math.Min(StfsBlockSize, output.Length - written);
            packageBytes.Slice(sourceOffset, bytesToCopy).CopyTo(output.AsSpan(written, bytesToCopy));
            written += bytesToCopy;

            if (blockIndex == entry.BlockCount - 1)
            {
                break;
            }

            currentBlock = entry.UsesConsecutiveBlocks
                ? currentBlock + 1
                : ReadNextBlockNumber(packageBytes, metadata, currentBlock);
        }

        return output;
    }

    private static int ReadNextBlockNumber(ReadOnlySpan<byte> packageBytes, StfsPackageMetadata metadata, int currentBlock)
    {
        int recordIndex = currentBlock % BlocksPerHashTable;
        int primaryHashOffset = ComputeHashTableOffset(metadata, currentBlock);
        int? secondaryHashOffset = metadata.PackageType == StfsPackageType.Con
            ? primaryHashOffset + StfsBlockSize
            : null;

        HashRecord primary = ReadHashRecord(packageBytes, primaryHashOffset, recordIndex);
        if (IsUsedHashRecord(primary))
        {
            return primary.NextBlock;
        }

        if (secondaryHashOffset.HasValue)
        {
            HashRecord secondary = ReadHashRecord(packageBytes, secondaryHashOffset.Value, recordIndex);
            if (IsUsedHashRecord(secondary))
            {
                return secondary.NextBlock;
            }
        }

        if (primary.NextBlock == EndOfChain)
        {
            return EndOfChain;
        }

        throw new ArchiveEntryOutOfBoundsException($"Could not resolve next STFS block after 0x{currentBlock:X}.");
    }

    private static bool IsUsedHashRecord(HashRecord record)
    {
        return record.Status is 0x80 or 0xC0;
    }

    private static HashRecord ReadHashRecord(ReadOnlySpan<byte> packageBytes, int hashTableOffset, int recordIndex)
    {
        int recordOffset = hashTableOffset + (recordIndex * HashRecordSize);
        ReadOnlySpan<byte> recordBytes = packageBytes.Slice(recordOffset, HashRecordSize);
        byte status = recordBytes[0x14];
        int nextBlock = ReadInt24BigEndian(recordBytes, 0x15);
        return new HashRecord(status, nextBlock);
    }

    private static int ComputeHashTableOffset(StfsPackageMetadata metadata, int blockNumber)
    {
        int hashBlockNumber = ComputeLevelZeroHashBlockNumber(metadata, blockNumber);
        return metadata.HeaderAlignedSize + (hashBlockNumber * StfsBlockSize);
    }

    private static int ComputeLevelZeroHashBlockNumber(StfsPackageMetadata metadata, int blockNumber)
    {
        int blockShift = ComputeBlockShift(metadata);
        int step = (blockNumber / BlocksPerHashTable) * 0xAC;

        if (blockNumber / BlocksPerHashTable != 0)
        {
            int higherLevelBase = (blockNumber / 0x70E4) + 1;
            if (metadata.PackageType == StfsPackageType.Con)
            {
                step += higherLevelBase << blockShift;
            }
            else
            {
                step += higherLevelBase;
            }

            if (blockNumber / 0x70E4 != 0)
            {
                return metadata.PackageType == StfsPackageType.Con
                    ? step + (1 << blockShift)
                    : step + 1;
            }
        }

        return step;
    }

    private static int ComputeDataBlockOffset(StfsPackageMetadata metadata, int logicalBlock)
    {
        int adjustedBlockNumber = ComputeDataBlockNumber(metadata, logicalBlock);
        return metadata.HeaderAlignedSize + (adjustedBlockNumber * StfsBlockSize);
    }

    private static int ComputeDataBlockNumber(StfsPackageMetadata metadata, int logicalBlock)
    {
        int blockShift = ComputeBlockShift(metadata);
        int baseCount = (logicalBlock + BlocksPerHashTable) / BlocksPerHashTable;
        if (metadata.PackageType == StfsPackageType.Con)
        {
            baseCount <<= blockShift;
        }

        int result = baseCount + logicalBlock;
        if (logicalBlock > BlocksPerHashTable)
        {
            int higherLevelBase = (logicalBlock + 0x70E4) / 0x70E4;
            if (metadata.PackageType == StfsPackageType.Con)
            {
                higherLevelBase <<= blockShift;
            }

            result += higherLevelBase;
            if (logicalBlock > 0x70E4)
            {
                int topLevelBase = (logicalBlock + 0x4AF768) / 0x4AF768;
                if (metadata.PackageType == StfsPackageType.Con)
                {
                    topLevelBase <<= 1;
                }

                result += topLevelBase;
            }
        }

        return result;
    }

    private static int ComputeBlockShift(StfsPackageMetadata metadata)
    {
        if (metadata.HeaderAlignedSize == 0xB000)
        {
            return 1;
        }

        return (metadata.BlockSeparation & 1) == 1 ? 0 : 1;
    }

    private static string ResolvePath(StfsFileListingEntry entry, IReadOnlyList<StfsFileListingEntry> entries)
    {
        if (entry.PathIndicator < 0)
        {
            return entry.Name;
        }

        if (entry.PathIndicator >= entries.Count)
        {
            return entry.Name;
        }

        StfsFileListingEntry parent = entries[entry.PathIndicator];
        return $"{ResolvePath(parent, entries)}/{entry.Name}";
    }

    private static string ReadNullPaddedAscii(ReadOnlySpan<byte> bytes)
    {
        int terminator = bytes.IndexOf((byte)0);
        if (terminator < 0)
        {
            terminator = bytes.Length;
        }

        return Encoding.ASCII.GetString(bytes.Slice(0, terminator));
    }

    private static int ReadInt24LittleEndian(ReadOnlySpan<byte> bytes, int offset)
    {
        return bytes[offset]
            | (bytes[offset + 1] << 8)
            | (bytes[offset + 2] << 16);
    }

    private static int ReadInt24BigEndian(ReadOnlySpan<byte> bytes, int offset)
    {
        return (bytes[offset] << 16)
            | (bytes[offset + 1] << 8)
            | bytes[offset + 2];
    }

    private readonly record struct HashRecord(byte Status, int NextBlock);
}
