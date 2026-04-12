namespace Console2Lce;

public sealed record SavegameProbeAttempt(
    string Envelope,
    string Decoder,
    bool Success,
    int? OutputLength,
    string? FirstBytesHex,
    bool HasPlausibleArchiveHeader,
    int? IndexOffset,
    int? FileCount,
    string? Failure);
