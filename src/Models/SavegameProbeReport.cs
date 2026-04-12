namespace Console2Lce;

public sealed record SavegameProbeReport(
    int SavegameLength,
    string? LeadingPrefixHex,
    IReadOnlyList<string> Findings,
    IReadOnlyList<SavegameEnvelopeCandidate> Envelopes,
    IReadOnlyList<SavegameProbeAttempt> Attempts,
    bool HasSuccessfulDecompression,
    string? RecommendedEnvelope,
    string? RecommendedDecoder);
