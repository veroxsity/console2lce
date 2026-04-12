namespace Console2Lce;

public interface ISavegameDecompressor
{
    byte[] Decompress(ReadOnlyMemory<byte> savegameBytes);
}
