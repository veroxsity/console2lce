using System.Runtime.InteropServices;

namespace XCompression;

public sealed class DecompressionContext : BaseContext
{
    private IntPtr _handle;
    public readonly uint WindowSize;
    public readonly uint ChunkSize;

    public DecompressionContext()
        : this((uint)Constants.DefaultWindowSize, (uint)Constants.DefaultChunkSize)
    {
    }

    public DecompressionContext(uint windowSize, uint chunkSize)
        : this(codecType: 1, windowSize, chunkSize, contextFlags: 1)
    {
    }

    public DecompressionContext(int codecType, uint windowSize, uint chunkSize, int contextFlags)
    {
        CompressionSettings settings;
        settings.Flags = 0;
        settings.WindowSize = windowSize;
        settings.ChunkSize = chunkSize;

        var result = CreateDecompressionContext(codecType, settings, contextFlags, out _handle);
        if (result != ErrorCode.None)
            throw new InvalidOperationException($"XMemCreateDecompressionContext failed: {result:X8}");

        WindowSize = windowSize;
        ChunkSize = chunkSize;
    }

    public ErrorCode Decompress(
        byte[] inputBytes,
        int inputOffset,
        ref int inputCount,
        byte[] outputBytes,
        int outputOffset,
        ref int outputCount)
    {
        if (inputBytes == null)
            throw new ArgumentNullException(nameof(inputBytes));
        if (inputOffset < 0 || inputOffset >= inputBytes.Length)
            throw new ArgumentOutOfRangeException(nameof(inputOffset));
        if (inputCount <= 0 || inputOffset + inputCount > inputBytes.Length)
            throw new ArgumentOutOfRangeException(nameof(inputCount));
        if (outputBytes == null)
            throw new ArgumentNullException(nameof(outputBytes));
        if (outputOffset < 0 || outputOffset >= outputBytes.Length)
            throw new ArgumentOutOfRangeException(nameof(outputOffset));
        if (outputCount <= 0 || outputOffset + outputCount > outputBytes.Length)
            throw new ArgumentOutOfRangeException(nameof(outputCount));

        var outputHandle = GCHandle.Alloc(outputBytes, GCHandleType.Pinned);
        var inputHandle = GCHandle.Alloc(inputBytes, GCHandleType.Pinned);
        var result = Decompress(
            _handle,
            outputHandle.AddrOfPinnedObject() + outputOffset,
            ref outputCount,
            inputHandle.AddrOfPinnedObject() + inputOffset,
            ref inputCount);
        inputHandle.Free();
        outputHandle.Free();
        return result;
    }

    ~DecompressionContext()
    {
        Dispose(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (_handle != IntPtr.Zero)
        {
            DestroyDecompressionContext(_handle);
            _handle = IntPtr.Zero;
        }

        base.Dispose(disposing);
    }
}
