namespace Console2Lce;

public sealed record MinecraftXbox360RegionChunk(
    int Index,
    int X,
    int Z,
    int Timestamp,
    int SectorNumber,
    int SectorCount,
    int ChunkOffset,
    int PayloadOffset,
    int StoredLength,
    int DecompressedLength,
    bool UsesRleCompression);
