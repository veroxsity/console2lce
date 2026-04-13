using System.Reflection;
using System.Runtime.InteropServices;

namespace Console2Lce;

public sealed class MccXboxSupportChunkExternalDecoder : IMinecraftXbox360ChunkExternalDecoder
{
    private const string LibraryFileName = "XBOXSupport64.dll";
    private const string DecompressEntryPointName = "Decompress";
    private const string ReleaseEntryPointName = "Release";
    private static readonly Lazy<NativeLibraryState> State = new(LoadLibrary);

    public string DecoderName => "MccXBOXSupport64";

    public bool TryDecode(ReadOnlySpan<byte> compressedBytes, int expectedDecompressedSize, out byte[] decodedBytes, out string? failure)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedDecompressedSize);

        if (!OperatingSystem.IsWindows())
        {
            decodedBytes = Array.Empty<byte>();
            failure = "MCC XBOXSupport64.dll decoding is currently implemented for Windows only.";
            return false;
        }

        NativeLibraryState state = State.Value;
        if (state.Decompress is null || state.Release is null)
        {
            decodedBytes = Array.Empty<byte>();
            failure = state.Failure ?? "MCC XBOXSupport64.dll is not available.";
            return false;
        }

        IntPtr inputBuffer = IntPtr.Zero;
        IntPtr outputBuffer = IntPtr.Zero;

        try
        {
            byte[] input = compressedBytes.ToArray();
            inputBuffer = Marshal.AllocHGlobal(input.Length);
            Marshal.Copy(input, 0, inputBuffer, input.Length);

            int outputLength = expectedDecompressedSize;
            state.Decompress(inputBuffer, input.Length, ref outputBuffer, ref outputLength);

            if (outputBuffer == IntPtr.Zero || outputLength <= 0)
            {
                decodedBytes = Array.Empty<byte>();
                failure = "MCC XBOXSupport64.dll returned no decoded bytes.";
                return false;
            }

            decodedBytes = new byte[outputLength];
            Marshal.Copy(outputBuffer, decodedBytes, 0, outputLength);
            failure = null;
            return true;
        }
        catch (Exception exception)
        {
            decodedBytes = Array.Empty<byte>();
            failure = $"MCC XBOXSupport64.dll decoding failed: {exception.Message}";
            return false;
        }
        finally
        {
            if (outputBuffer != IntPtr.Zero && State.Value.Release is not null)
            {
                try
                {
                    State.Value.Release(ref outputBuffer);
                }
                catch
                {
                    outputBuffer = IntPtr.Zero;
                }
            }

            if (inputBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(inputBuffer);
            }
        }
    }

    private static NativeLibraryState LoadLibrary()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new NativeLibraryState(IntPtr.Zero, null, null, null, "MCC XBOXSupport64.dll is only supported on Windows.");
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

            if (!NativeLibrary.TryGetExport(handle, DecompressEntryPointName, out IntPtr decompressEntryPoint)
                || !NativeLibrary.TryGetExport(handle, ReleaseEntryPointName, out IntPtr releaseEntryPoint))
            {
                NativeLibrary.Free(handle);
                continue;
            }

            var decompress = Marshal.GetDelegateForFunctionPointer<DecompressDelegate>(decompressEntryPoint);
            var release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(releaseEntryPoint);
            return new NativeLibraryState(handle, decompress, release, path, null);
        }

        return new NativeLibraryState(
            IntPtr.Zero,
            null,
            null,
            null,
            "MCC XBOXSupport64.dll was not found. Set CONSOLE2LCE_XBOX_SUPPORT_PATH or install MCC ToolChest.");
    }

    private static IEnumerable<string> GetCandidatePaths()
    {
        string? overridePath = Environment.GetEnvironmentVariable("CONSOLE2LCE_XBOX_SUPPORT_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            yield return Path.GetFullPath(overridePath);
        }

        string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "MCCToolChest", LibraryFileName);
        }

        string? programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "MCCToolChest", LibraryFileName);
        }

        foreach (string directory in EnumerateParentDirectories(AppContext.BaseDirectory))
        {
            yield return Path.Combine(directory, LibraryFileName);
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

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DecompressDelegate(IntPtr input, int inputLength, ref IntPtr output, ref int outputLength);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ReleaseDelegate(ref IntPtr output);

    private sealed record NativeLibraryState(
        IntPtr Handle,
        DecompressDelegate? Decompress,
        ReleaseDelegate? Release,
        string? Path,
        string? Failure);
}
