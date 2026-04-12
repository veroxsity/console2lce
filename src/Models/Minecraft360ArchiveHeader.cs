namespace Console2Lce;

public readonly record struct Minecraft360ArchiveHeader(
    int IndexOffset,
    int FileCount,
    int DecompressedSize);
