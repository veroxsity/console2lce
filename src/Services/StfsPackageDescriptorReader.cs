using System.Buffers.Binary;

namespace Console2Lce;

public static class StfsPackageDescriptorReader
{
    private const int HeaderSizeOffset = 0x340;
    private const int VolumeDescriptorOffset = 0x379;

    public static StfsPackageMetadata Read(ReadOnlySpan<byte> packageBytes)
    {
        StfsPackageType packageType = StfsPackageMagicReader.ReadPackageType(packageBytes);
        EnsureLength(packageBytes, VolumeDescriptorOffset + 0x24);

        int headerSize = ReadInt32BigEndian(packageBytes, HeaderSizeOffset);
        int headerAlignedSize = AlignToBoundary(headerSize, 0x1000);
        int blockSeparation = packageBytes[VolumeDescriptorOffset + 2];
        int fileTableBlockCount = ReadInt16LittleEndian(packageBytes, VolumeDescriptorOffset + 3);
        int fileTableBlockNumber = ReadInt24LittleEndian(packageBytes, VolumeDescriptorOffset + 5);

        return new StfsPackageMetadata(
            packageType,
            headerSize,
            headerAlignedSize,
            blockSeparation,
            fileTableBlockCount,
            fileTableBlockNumber);
    }

    private static void EnsureLength(ReadOnlySpan<byte> bytes, int minimumLength)
    {
        if (bytes.Length < minimumLength)
        {
            throw new InvalidXboxPackageMagicException();
        }
    }

    private static int AlignToBoundary(int value, int boundary)
    {
        return (value + boundary - 1) & -boundary;
    }

    private static int ReadInt16LittleEndian(ReadOnlySpan<byte> bytes, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, sizeof(ushort)));
    }

    private static int ReadInt24LittleEndian(ReadOnlySpan<byte> bytes, int offset)
    {
        return bytes[offset]
            | (bytes[offset + 1] << 8)
            | (bytes[offset + 2] << 16);
    }

    private static int ReadInt32BigEndian(ReadOnlySpan<byte> bytes, int offset)
    {
        return BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(offset, sizeof(int)));
    }
}
