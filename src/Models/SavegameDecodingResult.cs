namespace Console2Lce;

public sealed record SavegameDecodingResult(
    SavegameProbeResult ProbeResult,
    byte[]? DecompressedBytes,
    string DecoderSummary,
    string? FallbackFailure);
