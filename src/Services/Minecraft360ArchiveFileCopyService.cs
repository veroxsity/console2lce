using LceWorldConverter;

namespace Console2Lce;

public sealed class Minecraft360ArchiveFileCopyService
{
    public const ulong Windows64LegacyHostXuid = 0xe000d45248242f2eUL;
    public const string Windows64LegacyHostPlayerEntryName = "players/16141134514358595374.dat";

    public Minecraft360ArchiveFileCopyResult CopyAuxiliaryFiles(Minecraft360Archive archive, SaveDataContainer container)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(container);

        int copied = 0;
        int players = 0;
        int primaryPlayersRemapped = 0;
        bool primaryPlayerHandled = false;
        var copiedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Minecraft360ArchiveEntry archiveEntry in archive.Entries)
        {
            string entryName = NormalizeEntryName(archiveEntry.Name);
            if (!ShouldCopyAuxiliaryFile(entryName))
            {
                continue;
            }

            bool isPlayerFile = IsPlayerFile(entryName);
            string targetEntryName = entryName;
            if (isPlayerFile && !primaryPlayerHandled)
            {
                targetEntryName = Windows64LegacyHostPlayerEntryName;
                primaryPlayerHandled = true;
                if (!entryName.Equals(targetEntryName, StringComparison.OrdinalIgnoreCase))
                {
                    primaryPlayersRemapped++;
                }
            }

            if (!copiedNames.Add(targetEntryName))
            {
                continue;
            }

            if (!archive.Files.TryGetValue(archiveEntry.Name, out byte[]? bytes))
            {
                continue;
            }

            SaveFileEntry targetEntry = container.CreateFile(targetEntryName);
            container.WriteToFile(targetEntry, bytes);
            copied++;

            if (isPlayerFile)
            {
                players++;
            }
        }

        return new Minecraft360ArchiveFileCopyResult(copied, players, primaryPlayersRemapped);
    }

    public static bool ShouldCopyAuxiliaryFile(string entryName)
    {
        string normalized = NormalizeEntryName(entryName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.Equals("level.dat", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalized.EndsWith(".mcr", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsPlayerFile(string entryName)
    {
        string normalized = NormalizeEntryName(entryName);
        return normalized.StartsWith("players/", StringComparison.OrdinalIgnoreCase)
            && normalized.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEntryName(string entryName)
    {
        return entryName.Replace('\\', '/');
    }
}

public readonly record struct Minecraft360ArchiveFileCopyResult(
    int CopiedFiles,
    int CopiedPlayerFiles,
    int RemappedPrimaryPlayerFiles);
