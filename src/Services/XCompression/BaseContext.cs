namespace XCompression;

public abstract class BaseContext : IDisposable
{
    internal IntPtr XnaNativeHandle;
    internal Delegates.CreateCompressionContext? NativeCreateCompressionContext;
    internal Delegates.Compress? NativeCompress;
    internal Delegates.DestroyCompressionContext? NativeDestroyCompressionContext;
    internal Delegates.CreateDecompressionContext? NativeCreateDecompressionContext;
    internal Delegates.Decompress? NativeDecompress;
    internal Delegates.DestroyDecompressionContext? NativeDestroyDecompressionContext;

    internal BaseContext()
    {
        if (XnaNative.Load(this) == false)
        {
            throw new InvalidOperationException("Failed to load XnaNative.dll. XNA Game Studio 4.0 must be installed.");
        }
    }

    ~BaseContext()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (XnaNativeHandle != IntPtr.Zero)
        {
            NativeCreateCompressionContext = null;
            NativeCompress = null;
            NativeDestroyCompressionContext = null;
            NativeCreateDecompressionContext = null;
            NativeDecompress = null;
            NativeDestroyDecompressionContext = null;
            XnaNativeHandle = IntPtr.Zero;
        }
    }

    internal ErrorCode CreateDecompressionContext(
        int type,
        CompressionSettings settings,
        int flags,
        out IntPtr context)
    {
        if (XnaNativeHandle == IntPtr.Zero)
            throw new InvalidOperationException("XnaNative is not loaded");

        context = IntPtr.Zero;
        return (ErrorCode)(NativeCreateDecompressionContext ?? throw new InvalidOperationException("XnaNative is not loaded"))(type, ref settings, flags, ref context);
    }

    internal ErrorCode Decompress(
        IntPtr context,
        IntPtr output,
        ref int outputSize,
        IntPtr input,
        ref int inputSize)
    {
        if (XnaNativeHandle == IntPtr.Zero)
            throw new InvalidOperationException("XnaNative is not loaded");

        return (ErrorCode)(NativeDecompress ?? throw new InvalidOperationException("XnaNative is not loaded"))(context, output, ref outputSize, input, ref inputSize);
    }

    internal void DestroyDecompressionContext(IntPtr context)
    {
        if (XnaNativeHandle == IntPtr.Zero)
            throw new InvalidOperationException("XnaNative is not loaded");

        (NativeDestroyDecompressionContext ?? throw new InvalidOperationException("XnaNative is not loaded"))(context);
    }
}
