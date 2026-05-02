using System.Runtime.InteropServices;

namespace XCompression;

public static class Delegates
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int CreateCompressionContext(
        int type,
        ref CompressionSettings settings,
        int flags,
        ref IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int Compress(
        IntPtr context,
        IntPtr output,
        ref int outputSize,
        IntPtr input,
        ref int inputSize);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void DestroyCompressionContext(IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int CreateDecompressionContext(
        int type,
        ref CompressionSettings settings,
        int flags,
        ref IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int Decompress(
        IntPtr context,
        IntPtr output,
        ref int outputSize,
        IntPtr input,
        ref int inputSize);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void DestroyDecompressionContext(IntPtr context);
}
