namespace Console2Lce;

public sealed class SavegameDecodeService
{
    private readonly SavegameProbeService _probeService;

    public SavegameDecodeService()
        : this(new SavegameProbeService())
    {
    }

    public SavegameDecodeService(SavegameProbeService probeService)
    {
        _probeService = probeService ?? throw new ArgumentNullException(nameof(probeService));
    }

    public SavegameDecodingResult Decode(
        ReadOnlyMemory<byte> savegameBytes,
        ReadOnlyMemory<byte> leadingPrefixBytes = default)
    {
        SavegameProbeResult probeResult = _probeService.Probe(savegameBytes, leadingPrefixBytes);
        string decoderSummary = probeResult.Report.HasSuccessfulDecompression
            ? $"{probeResult.Report.RecommendedEnvelope} / {probeResult.Report.RecommendedDecoder}"
            : "unresolved";

        return new SavegameDecodingResult(
            probeResult,
            probeResult.DecompressedBytes,
            decoderSummary);
    }
}
