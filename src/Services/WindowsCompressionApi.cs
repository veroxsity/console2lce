using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Console2Lce;

internal static class WindowsCompressionApi
{
    public const uint CompressAlgorithmNull = 1;
    public const uint CompressAlgorithmMsZip = 2;
    public const uint CompressAlgorithmXpress = 3;
    public const uint CompressAlgorithmXpressHuff = 4;
    public const uint CompressAlgorithmLzms = 5;
    public const uint CompressRaw = 1u << 29;

    public static byte[] Decompress(byte[] compressedBytes, int expectedDecompressedSize, uint algorithm)
    {
        ArgumentNullException.ThrowIfNull(compressedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedDecompressedSize);

        IntPtr handle = IntPtr.Zero;
        if (!CreateDecompressor(algorithm, IntPtr.Zero, out handle))
        {
            throw CreateWin32Exception("CreateDecompressor");
        }

        try
        {
            byte[] output = new byte[expectedDecompressedSize];
            if (!Decompress(
                handle,
                compressedBytes,
                (nuint)compressedBytes.Length,
                output,
                (nuint)output.Length,
                out nuint decompressedSize))
            {
                throw CreateWin32Exception("Decompress");
            }

            if ((int)decompressedSize != expectedDecompressedSize)
            {
                throw new SavegameDatDecompressionFailedException(
                    $"Windows Compression API produced {decompressedSize} bytes, expected {expectedDecompressedSize}.");
            }

            return output;
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                CloseDecompressor(handle);
            }
        }
    }

    public static byte[] Compress(byte[] uncompressedBytes, uint algorithm)
    {
        ArgumentNullException.ThrowIfNull(uncompressedBytes);

        IntPtr handle = IntPtr.Zero;
        if (!CreateCompressor(algorithm, IntPtr.Zero, out handle))
        {
            throw CreateWin32Exception("CreateCompressor");
        }

        try
        {
            if (!Compress(
                handle,
                uncompressedBytes,
                (nuint)uncompressedBytes.Length,
                null,
                0,
                out nuint compressedSize))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 122)
                {
                    throw new Win32Exception(error, "Compress size query failed.");
                }
            }

            byte[] output = new byte[(int)compressedSize];
            if (!Compress(
                handle,
                uncompressedBytes,
                (nuint)uncompressedBytes.Length,
                output,
                (nuint)output.Length,
                out compressedSize))
            {
                throw CreateWin32Exception("Compress");
            }

            if ((int)compressedSize == output.Length)
            {
                return output;
            }

            return output[..(int)compressedSize];
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                CloseCompressor(handle);
            }
        }
    }

    private static Exception CreateWin32Exception(string operation)
    {
        int error = Marshal.GetLastWin32Error();
        return new SavegameDatDecompressionFailedException(
            $"{operation} failed with Win32 error {error}: {new Win32Exception(error).Message}");
    }

    [DllImport("cabinet.dll", SetLastError = true, ExactSpelling = true)]
    private static extern bool CreateDecompressor(
        uint algorithm,
        IntPtr allocationRoutines,
        out IntPtr decompressorHandle);

    [DllImport("cabinet.dll", SetLastError = true, ExactSpelling = true)]
    private static extern bool CloseDecompressor(IntPtr decompressorHandle);

    [DllImport("cabinet.dll", SetLastError = true, ExactSpelling = true)]
    private static extern bool Decompress(
        IntPtr decompressorHandle,
        byte[] compressedData,
        nuint compressedDataSize,
        byte[] uncompressedBuffer,
        nuint uncompressedBufferSize,
        out nuint uncompressedDataSize);

    [DllImport("cabinet.dll", SetLastError = true, ExactSpelling = true)]
    private static extern bool CreateCompressor(
        uint algorithm,
        IntPtr allocationRoutines,
        out IntPtr compressorHandle);

    [DllImport("cabinet.dll", SetLastError = true, ExactSpelling = true)]
    private static extern bool CloseCompressor(IntPtr compressorHandle);

    [DllImport("cabinet.dll", SetLastError = true, ExactSpelling = true)]
    private static extern bool Compress(
        IntPtr compressorHandle,
        byte[] uncompressedData,
        nuint uncompressedDataSize,
        byte[]? compressedBuffer,
        nuint compressedBufferSize,
        out nuint compressedDataSize);
}
