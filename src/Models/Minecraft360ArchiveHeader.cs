namespace Console2Lce;

public readonly record struct Minecraft360ArchiveHeader(
    int HeaderOffset,
    int FileCount,
    short OriginalSaveVersion,
    short CurrentSaveVersion,
    int DecompressedSize);
