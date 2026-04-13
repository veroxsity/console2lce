using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Console2Lce;

internal static class XboxLzxNativeDecoder
{
    private const string LibraryFileName = "console2lce_lzx_native.dll";
    private const string EntryPointName = "console2lce_lzx_decompress";
    private const int MinimumIntermediateBufferSize = 200 * 1024;
    private const int MaximumIntermediateBufferSize = 16 * 1024 * 1024;
    private const int ErrorBufferLength = 256;

    private static readonly Lazy<NativeLibraryState> State = new(LoadLibrary);

    public static string? AvailabilityFailure => State.Value.Failure;

    public static int GetRecommendedIntermediateBufferSize(int expectedDecompressedSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedDecompressedSize);

        int bufferSize = Math.Max(MinimumIntermediateBufferSize, expectedDecompressedSize);
        if (bufferSize > MaximumIntermediateBufferSize)
        {
            throw new SavegameDatDecompressionFailedException(
                $"Expected decompressed size {expectedDecompressedSize} exceeds the supported Xbox LZX intermediate buffer limit of {MaximumIntermediateBufferSize} bytes.");
        }

        return bufferSize;
    }

    public static byte[] Decompress(ReadOnlySpan<byte> compressedBytes, int outputBufferSize, int windowSize, int partitionSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputBufferSize);

        if (!OperatingSystem.IsWindows())
        {
            throw new SavegameDatDecompressionFailedException("Xbox LZX native decoding is currently implemented for Windows only.");
        }

        NativeLibraryState state = State.Value;
        if (state.Decompress is null)
        {
            throw new SavegameDatDecompressionFailedException(state.Failure ?? "Xbox LZX native helper is not available.");
        }

        byte[] input = compressedBytes.ToArray();
        byte[] output = new byte[outputBufferSize];
        var errorBuffer = new StringBuilder(ErrorBufferLength);
        int result = state.Decompress(input, input.Length, output, output.Length, windowSize, partitionSize, errorBuffer, errorBuffer.Capacity);

        if (result < 0)
        {
            string detail = errorBuffer.Length > 0 ? errorBuffer.ToString() : "Unknown native decoder failure.";
            throw new SavegameDatDecompressionFailedException(
                $"Xbox LZX native decoder failed for window={windowSize} partition={partitionSize}: {detail}");
        }

        if (result > output.Length)
        {
            throw new SavegameDatDecompressionFailedException(
                $"Xbox LZX native decoder reported {result} bytes, which exceeds the output buffer size {output.Length}.");
        }

        return output[..result];
    }

    private static NativeLibraryState LoadLibrary()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new NativeLibraryState(IntPtr.Zero, null, null, "Xbox LZX native helper is only supported on Windows.");
        }

        foreach (string path in GetCandidatePaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            if (!NativeLibrary.TryLoad(path, out IntPtr handle))
            {
                continue;
            }

            if (!NativeLibrary.TryGetExport(handle, EntryPointName, out IntPtr entryPoint))
            {
                NativeLibrary.Free(handle);
                continue;
            }

            var decompress = Marshal.GetDelegateForFunctionPointer<DecompressDelegate>(entryPoint);
            return new NativeLibraryState(handle, decompress, path, null);
        }

        return new NativeLibraryState(
            IntPtr.Zero,
            null,
            null,
            "Xbox LZX native helper was not found. Build it with `native/build-native.ps1` before running the Xbox LZX probe.");
    }

    private static IEnumerable<string> GetCandidatePaths()
    {
        string? overridePath = Environment.GetEnvironmentVariable("CONSOLE2LCE_LZX_NATIVE_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            yield return Path.GetFullPath(overridePath);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in EnumerateParentDirectories(AppContext.BaseDirectory))
        {
            foreach (string candidate in new[]
            {
                Path.Combine(directory, LibraryFileName),
                Path.Combine(directory, "runtimes", "win-x64", "native", LibraryFileName),
                Path.Combine(directory, "native", "build", "win-x64", "Debug", LibraryFileName),
                Path.Combine(directory, "native", "build", "win-x64", "Release", LibraryFileName),
                Path.Combine(directory, "native", "build", "win-x64", "bin", "Debug", LibraryFileName),
                Path.Combine(directory, "native", "build", "win-x64", "bin", "Release", LibraryFileName),
            })
            {
                if (seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateParentDirectories(string startDirectory)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory is not null)
        {
            yield return directory.FullName;
            directory = directory.Parent;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int DecompressDelegate(
        byte[] compressedBytes,
        int compressedLength,
        [Out] byte[] outputBytes,
        int outputLength,
        int windowSize,
        int partitionSize,
        StringBuilder errorBuffer,
        int errorBufferLength);

    private sealed record NativeLibraryState(
        IntPtr Handle,
        DecompressDelegate? Decompress,
        string? Path,
        string? Failure);
}
