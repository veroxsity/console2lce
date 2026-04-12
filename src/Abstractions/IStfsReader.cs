namespace Console2Lce;

public interface IStfsReader
{
    IReadOnlyList<StfsFileEntry> EnumerateEntries(ReadOnlyMemory<byte> packageBytes);

    byte[] ReadFile(ReadOnlyMemory<byte> packageBytes, string entryName);
}
