namespace Console2Lce;

public sealed class SavegameDecompressor : ISavegameDecompressor
{
    private readonly SavegameDecodeService _decodeService;

    public SavegameDecompressor()
        : this(new SavegameDecodeService())
    {
    }

    public SavegameDecompressor(SavegameDecodeService decodeService)
    {
        _decodeService = decodeService ?? throw new ArgumentNullException(nameof(decodeService));
    }

    public byte[] Decompress(ReadOnlyMemory<byte> savegameBytes)
    {
        SavegameDecodingResult result = _decodeService.Decode(savegameBytes);
        return result.DecompressedBytes
            ?? throw new SavegameDatDecompressionFailedException("Unable to decode savegame.dat.");
    }
}
