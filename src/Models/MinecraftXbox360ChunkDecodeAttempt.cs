namespace Console2Lce;

public sealed record MinecraftXbox360ChunkDecodeAttempt(
    string Decoder,
    bool Success,
    int? DecodedLength,
    string? PayloadKind,
    int? ChunkX,
    int? ChunkZ,
    bool? HasLevelWrapper,
    string? Error);
