namespace Console2Lce;

public sealed record SavegameEnvelopeCandidate(
    string Name,
    string Description,
    string Endianness,
    int CompressionFlag,
    int ExpectedDecompressedSize,
    int PayloadOffset,
    int PayloadLength,
    bool IsPlausible);
