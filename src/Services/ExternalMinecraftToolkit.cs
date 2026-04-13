using System.Diagnostics;

namespace Console2Lce;

public static class ExternalMinecraftToolkit
{
    private const string ExecutableName = "minecraft.exe";

    public static string? FindExecutablePath()
    {
        string? overridePath = Environment.GetEnvironmentVariable("CONSOLE2LCE_MINECRAFT_TOOLKIT_PATH");
        foreach (string candidate in EnumerateCandidates(overridePath))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static bool TryDecompress(ReadOnlyMemory<byte> savegameBytes, out byte[] decompressedBytes, out string? failure)
    {
        string? executablePath = FindExecutablePath();
        if (executablePath is null)
        {
            decompressedBytes = Array.Empty<byte>();
            failure = "External minecraft.exe toolkit was not found. Set CONSOLE2LCE_MINECRAFT_TOOLKIT_PATH or install MCC ToolChest.";
            return false;
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), "console2lce-toolkit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        string inputPath = Path.Combine(tempRoot, "savegame.dat");
        string outputPath = Path.Combine(tempRoot, "savegame.decompressed.bin");

        try
        {
            File.WriteAllBytes(inputPath, savegameBytes.ToArray());

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-d");
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add(outputPath);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                decompressedBytes = Array.Empty<byte>();
                failure = $"Failed to start external toolkit '{executablePath}'.";
                return false;
            }

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                decompressedBytes = Array.Empty<byte>();
                failure = string.IsNullOrWhiteSpace(stderr)
                    ? $"External toolkit exited with code {process.ExitCode}.{Environment.NewLine}{stdout}".Trim()
                    : stderr.Trim();
                return false;
            }

            if (!File.Exists(outputPath))
            {
                decompressedBytes = Array.Empty<byte>();
                failure = "External toolkit reported success but did not produce an output file.";
                return false;
            }

            decompressedBytes = File.ReadAllBytes(outputPath);
            failure = null;
            return true;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    private static IEnumerable<string> EnumerateCandidates(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            yield return Path.GetFullPath(overridePath);
        }

        string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "MCCToolChest", "support", ExecutableName);
        }

        string? programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "MCCToolChest", "support", ExecutableName);
        }
    }
}
