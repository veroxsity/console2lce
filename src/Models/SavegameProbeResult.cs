namespace Console2Lce;

public sealed record SavegameProbeResult(
    SavegameProbeReport Report,
    byte[]? DecompressedBytes);
