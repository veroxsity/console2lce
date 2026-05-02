using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using MD5 = System.Security.Cryptography.MD5;

namespace XCompression;

internal static class XnaNative
{
    /// <summary>
    /// XnaNative.dll exports wrapper functions that don't allow specifying window and partition
    /// sizes, so we call the underlying internal functions directly via hardcoded RVA offsets
    /// identified by the DLL's MD5 hash.
    /// </summary>
    private static readonly NativeInfo[] NativeInfos =
    [
        new("3.0.11010.0", "08dde3aeaa90772e9bf841aa30ad409d", 0x1018D303, 0x1018D293, 0x1018D2DF, 0x1018D3DA, 0x1018D36A, 0x1018D3B6),
        new("3.1.10527.0", "fb193b2a3b5dc72d6f0ff6b86723c1ed", 0x101963F1, 0x1019633F, 0x101963CB, 0x101964DB, 0x1019645F, 0x101964B5),
        new("4.0.20823.0", "993d6b608c47e867bcf10a064ff2d61a", 0x10197933, 0x10197881, 0x1019790D, 0x10197A1D, 0x101979A1, 0x101979F7),
        new("4.0.30901.0", "cbffc669518ee511890f236fefffb4c1", 0x10197933, 0x10197881, 0x1019790D, 0x10197A1D, 0x101979A1, 0x101979F7),
    ];

    private static NativeInfo? FindAcceptableInfo(out string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            path = null!;
            return null;
        }

        var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using (baseKey)
        {
            if (baseKey == null)
            {
                path = null!;
                return null;
            }

            var versions = new[] { "v4.0", "v3.1", "v3.0" };
            foreach (var version in versions)
            {
                var subKey = baseKey.OpenSubKey($@"SOFTWARE\Microsoft\XNA\Framework\{version}");
                using (subKey)
                {
                    if (subKey == null)
                        continue;

                    var nativePath = subKey.GetValue("NativeLibraryPath", null) as string;
                    if (string.IsNullOrEmpty(nativePath))
                        continue;

                    path = Path.GetFullPath(Path.Combine(nativePath, "XnaNative.dll"));
                    if (!File.Exists(path))
                        continue;

                    string hash;
                    try
                    {
                        hash = ComputeMD5(path);
                    }
                    catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or NotSupportedException or UnauthorizedAccessException)
                    {
                        continue;
                    }

                    var info = NativeInfos.FirstOrDefault(i => i.Hash == hash);
                    if (info.Valid)
                        return info;
                }
            }
        }

        path = null!;
        return null;
    }

    private static string ComputeMD5(string path)
    {
        using var md5 = MD5.Create();
        using var input = File.OpenRead(path);
        var sb = new StringBuilder();
        foreach (var b in md5.ComputeHash(input))
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static NativeInfo? TryLoadFromAppDirectory(out string path)
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "XnaNative.dll"),
            Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x86", "native", "XnaNative.dll"),
        ];

        foreach (string candidate in candidates)
        {
            if (!File.Exists(candidate))
                continue;

            string hash;
            try
            {
                hash = ComputeMD5(candidate);
            }
            catch
            {
                continue;
            }

            var info = NativeInfos.FirstOrDefault(i => i.Hash == hash);
            if (info.Valid)
            {
                path = candidate;
                return info;
            }
        }

        path = null!;
        return null;
    }

    private static AcceptableInfo _acceptableVersion;

    internal static bool Load(BaseContext context)
    {
        if (context.XnaNativeHandle != IntPtr.Zero)
            return true;

        if (!_acceptableVersion.Valid)
        {
            var info = FindAcceptableInfo(out string path) ?? TryLoadFromAppDirectory(out path);
            if (info == null)
                throw new FileNotFoundException("Could not find an acceptable version of XnaNative.dll. Install XNA Game Studio 4.0 or place XnaNative.dll next to the executable.");

            _acceptableVersion = new AcceptableInfo(info.Value, path);
        }

        var library = Kernel32.LoadLibrary(_acceptableVersion.Path);
        if (library == IntPtr.Zero)
            throw new Win32Exception($"Could not load XnaNative {_acceptableVersion.Info.Version}");

        var process = Process.GetCurrentProcess();
        var module = process.Modules.Cast<ProcessModule>().FirstOrDefault(pm => pm.FileName == _acceptableVersion.Path);
        if (module == null)
        {
            Kernel32.FreeLibrary(library);
            throw new InvalidOperationException("Could not find loaded XnaNative module");
        }

        context.XnaNativeHandle = library;
        context.NativeCreateCompressionContext = GetFunction<Delegates.CreateCompressionContext>(module, _acceptableVersion.Info.CreateCompressionContextAddress);
        context.NativeCompress = GetFunction<Delegates.Compress>(module, _acceptableVersion.Info.CompressAddress);
        context.NativeDestroyCompressionContext = GetFunction<Delegates.DestroyCompressionContext>(module, _acceptableVersion.Info.DestroyCompressionContextAddress);
        context.NativeCreateDecompressionContext = GetFunction<Delegates.CreateDecompressionContext>(module, _acceptableVersion.Info.CreateDecompressionContextAddress);
        context.NativeDecompress = GetFunction<Delegates.Decompress>(module, _acceptableVersion.Info.DecompressAddress);
        context.NativeDestroyDecompressionContext = GetFunction<Delegates.DestroyDecompressionContext>(module, _acceptableVersion.Info.DestroyDecompressionContextAddress);
        return true;
    }

    private static TDelegate GetFunction<TDelegate>(ProcessModule module, IntPtr address)
        where TDelegate : class
    {
        if (module == null)
            throw new ArgumentNullException(nameof(module));
        if (address == IntPtr.Zero)
            throw new ArgumentNullException(nameof(address));

        address -= 0x10000000;
        address += module.BaseAddress.ToInt32();
        return (TDelegate)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(TDelegate));
    }

    private struct AcceptableInfo
    {
        public readonly bool Valid;
        public readonly NativeInfo Info;
        public readonly string Path;

        public AcceptableInfo(NativeInfo info, string path)
        {
            Valid = true;
            Info = info;
            Path = path;
        }
    }

    public struct NativeInfo
    {
        public readonly bool Valid;
        public readonly string Version;
        public readonly string Hash;
        public readonly IntPtr CreateCompressionContextAddress;
        public readonly IntPtr CompressAddress;
        public readonly IntPtr DestroyCompressionContextAddress;
        public readonly IntPtr CreateDecompressionContextAddress;
        public readonly IntPtr DecompressAddress;
        public readonly IntPtr DestroyDecompressionContextAddress;

        public NativeInfo(
            string version,
            string hash,
            int createCompressionContextAddress,
            int compressAddress,
            int destroyCompressionContextAddress,
            int createDecompressionContextAddress,
            int decompressAddress,
            int destroyDecompressionContextAddress)
        {
            Valid = true;
            Version = version;
            Hash = hash;
            CreateCompressionContextAddress = (IntPtr)createCompressionContextAddress;
            CompressAddress = (IntPtr)compressAddress;
            DestroyCompressionContextAddress = (IntPtr)destroyCompressionContextAddress;
            CreateDecompressionContextAddress = (IntPtr)createDecompressionContextAddress;
            DecompressAddress = (IntPtr)decompressAddress;
            DestroyDecompressionContextAddress = (IntPtr)destroyDecompressionContextAddress;
        }
    }
}
