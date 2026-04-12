namespace Console2Lce;

public sealed class Xbox360MinecraftArchiveExtractor
{
    private const string SavegameDatFileName = "savegame.dat";

    private readonly IStfsReader _stfsReader;
    private readonly ISavegameDecompressor _savegameDecompressor;
    private readonly IMinecraft360ArchiveParser _archiveParser;

    public Xbox360MinecraftArchiveExtractor(
        IStfsReader stfsReader,
        ISavegameDecompressor savegameDecompressor,
        IMinecraft360ArchiveParser archiveParser)
    {
        _stfsReader = stfsReader ?? throw new ArgumentNullException(nameof(stfsReader));
        _savegameDecompressor = savegameDecompressor ?? throw new ArgumentNullException(nameof(savegameDecompressor));
        _archiveParser = archiveParser ?? throw new ArgumentNullException(nameof(archiveParser));
    }

    public IReadOnlyList<StfsFileEntry> EnumerateStfsEntries(ReadOnlyMemory<byte> packageBytes)
    {
        return _stfsReader.EnumerateEntries(packageBytes);
    }

    public byte[] ExtractSavegameDat(ReadOnlyMemory<byte> packageBytes)
    {
        return _stfsReader.ReadFile(packageBytes, SavegameDatFileName);
    }

    public Minecraft360Archive ExtractArchive(ReadOnlyMemory<byte> packageBytes)
    {
        byte[] savegameBytes = ExtractSavegameDat(packageBytes);
        byte[] decompressedBytes = _savegameDecompressor.Decompress(savegameBytes);
        return _archiveParser.Parse(decompressedBytes);
    }
}
