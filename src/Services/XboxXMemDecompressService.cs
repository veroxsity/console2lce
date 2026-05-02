using XCompression;

namespace Console2Lce;

public sealed class XboxXMemDecompressService : IDisposable
{
    private readonly DecompressionContext _context;

    public const int LceWindowSize = 128 * 1024;
    public const int LcePartitionSize = 128 * 1024;

    public XboxXMemDecompressService()
        : this(codecType: 1, windowSize: LceWindowSize, partitionSize: LcePartitionSize, contextFlags: 1)
    {
    }

    private XboxXMemDecompressService(int codecType, int windowSize, int partitionSize, int contextFlags)
    {
        _context = new DecompressionContext(codecType, (uint)windowSize, (uint)partitionSize, contextFlags);
    }

    public static string? AvailabilityFailure
    {
        get
        {
            try
            {
                using var ctx = new DecompressionContext(LceWindowSize, LcePartitionSize);
                return null;
            }
            catch (Exception ex)
            {
                return $"XMemDecompress not available: {ex.Message}";
            }
        }
    }

    public byte[] Decompress(ReadOnlySpan<byte> compressedBytes, int expectedDecompressedSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedDecompressedSize);

        byte[] output = new byte[expectedDecompressedSize];
        int inputCount = compressedBytes.Length;
        int outputCount = output.Length;

        ErrorCode result = _context.Decompress(
            compressedBytes.ToArray(), 0, ref inputCount,
            output, 0, ref outputCount);

        if (result != ErrorCode.None)
            throw new SavegameDatDecompressionFailedException($"XMemDecompress failed with error {result}.");

        if (outputCount != expectedDecompressedSize)
            throw new SavegameDatDecompressionFailedException($"XMemDecompress produced {outputCount} bytes, expected {expectedDecompressedSize}.");

        return output;
    }

    public byte[] DecompressUpTo(ReadOnlySpan<byte> compressedBytes, int maxDecompressedSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDecompressedSize);

        byte[] output = new byte[maxDecompressedSize];
        int inputCount = compressedBytes.Length;
        int outputCount = output.Length;

        ErrorCode result = _context.Decompress(
            compressedBytes.ToArray(), 0, ref inputCount,
            output, 0, ref outputCount);

        if (result != ErrorCode.None)
            throw new SavegameDatDecompressionFailedException($"XMemDecompress failed with error {result}.");

        Array.Resize(ref output, outputCount);
        return output;
    }

    public byte[] DecompressLzxRle(ReadOnlySpan<byte> compressedBytes, int expectedDecompressedSize)
    {
        int intermediateSize = Math.Max(expectedDecompressedSize, 200 * 1024);
        byte[] rleBytes = Decompress(compressedBytes, intermediateSize);
        return SavegameRleCodec.Decode(rleBytes, expectedDecompressedSize);
    }

    public static byte[] DecompressWithKnownContextVariants(
        ReadOnlySpan<byte> compressedBytes,
        int expectedDecompressedSize)
    {
        foreach (XMemContextVariant variant in GetKnownContextVariants())
        {
            try
            {
                using var service = new XboxXMemDecompressService(
                    variant.CodecType,
                    variant.WindowSize,
                    variant.PartitionSize,
                    variant.ContextFlags);
                return service.Decompress(compressedBytes, expectedDecompressedSize);
            }
            catch (SavegameDatDecompressionFailedException)
            {
            }
        }

        throw new SavegameDatDecompressionFailedException("XMemDecompress failed for all known Xbox LZX context variants.");
    }

    public static byte[] DecompressUpToWithKnownContextVariants(
        ReadOnlySpan<byte> compressedBytes,
        int maxDecompressedSize)
    {
        foreach (XMemContextVariant variant in GetKnownContextVariants())
        {
            try
            {
                using var service = new XboxXMemDecompressService(
                    variant.CodecType,
                    variant.WindowSize,
                    variant.PartitionSize,
                    variant.ContextFlags);
                return service.DecompressUpTo(compressedBytes, maxDecompressedSize);
            }
            catch (SavegameDatDecompressionFailedException)
            {
            }
        }

        throw new SavegameDatDecompressionFailedException("XMemDecompress failed for all known Xbox LZX context variants.");
    }

    public static byte[] DecompressLzxRleWithKnownContextVariants(
        ReadOnlySpan<byte> compressedBytes,
        int expectedDecompressedSize)
    {
        int maxRleBytes = checked(Math.Max(expectedDecompressedSize + 64 * 1024, expectedDecompressedSize * 2));
        byte[] rleBytes = DecompressUpToWithKnownContextVariants(compressedBytes, maxRleBytes);
        return SavegameRleCodec.Decode(rleBytes, expectedDecompressedSize);
    }

    private static IReadOnlyList<XMemContextVariant> GetKnownContextVariants()
    {
        return
        [
            new XMemContextVariant(0, 0, 0, 0),
            new XMemContextVariant(0, 128 * 1024, 512 * 1024, 0),
            new XMemContextVariant(0, 128 * 1024, 128 * 1024, 0),
            new XMemContextVariant(1, 128 * 1024, 512 * 1024, 0),
            new XMemContextVariant(1, 128 * 1024, 128 * 1024, 0),
            new XMemContextVariant(1, 128 * 1024, 128 * 1024, 1),
        ];
    }

    private readonly record struct XMemContextVariant(
        int CodecType,
        int WindowSize,
        int PartitionSize,
        int ContextFlags);

    public void Dispose()
    {
        _context.Dispose();
    }
}
