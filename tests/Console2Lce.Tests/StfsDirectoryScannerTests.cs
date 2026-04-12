using System.Text;

namespace Console2Lce.Tests;

public sealed class StfsDirectoryScannerTests
{
    [Fact]
    public void ScanHeuristicEntries_FindsSampleLikeEntry()
    {
        byte[] packageBytes = new byte[StfsDirectoryScanner.DefaultDirectoryOffset + StfsDirectoryScanner.DefaultDirectorySpan];
        int entryOffset = StfsDirectoryScanner.DefaultDirectoryOffset;

        byte[] nameBytes = Encoding.ASCII.GetBytes("savegame.dat");
        Array.Copy(nameBytes, 0, packageBytes, entryOffset, nameBytes.Length);
        BitConverter.GetBytes(542476).CopyTo(packageBytes, entryOffset + StfsDirectoryScanner.NameFieldSize);

        IReadOnlyList<StfsDirectoryEntry> entries = StfsDirectoryScanner.ScanHeuristicEntries(packageBytes);

        StfsDirectoryEntry entry = Assert.Single(entries);
        Assert.Equal("savegame.dat", entry.Name);
        Assert.Equal(542476, entry.FileSizeCandidate);
        Assert.Equal(entryOffset, entry.EntryOffset);
    }

    [Fact]
    public void ScanHeuristicEntries_IgnoresZeroedDirectoryEntries()
    {
        byte[] packageBytes = new byte[StfsDirectoryScanner.DefaultDirectoryOffset + StfsDirectoryScanner.DefaultDirectorySpan];

        IReadOnlyList<StfsDirectoryEntry> entries = StfsDirectoryScanner.ScanHeuristicEntries(packageBytes);

        Assert.Empty(entries);
    }
}
