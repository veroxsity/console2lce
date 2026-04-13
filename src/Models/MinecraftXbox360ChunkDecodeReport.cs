namespace Console2Lce;

public sealed record MinecraftXbox360ChunkDecodeReport(
    string RegionFileName,
    int ChunkIndex,
    int LocalX,
    int LocalZ,
    bool Success,
    string? Decoder,
    int? DecodedLength,
    string? PayloadKind,
    int? ChunkX,
    int? ChunkZ,
    bool? HasLevelWrapper,
    IReadOnlyList<MinecraftXbox360ChunkDecodeAttempt> Attempts);
