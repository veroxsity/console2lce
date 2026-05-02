using LceWorldConverter;

namespace Console2Lce.Tests;

public sealed class Minecraft360ArchiveFileCopyServiceTests
{
    [Fact]
    public void CopyAuxiliaryFiles_CopiesPlayersAndDataButSkipsLevelAndRegions()
    {
        var archive = new Minecraft360Archive(
            new Minecraft360ArchiveHeader(0, 6, 7, 9, 17),
            [
                new Minecraft360ArchiveEntry("level.dat", 0, 3),
                new Minecraft360ArchiveEntry("r.0.0.mcr", 3, 4),
                new Minecraft360ArchiveEntry("DIM-1r.0.0.mcr", 7, 4),
                new Minecraft360ArchiveEntry("players/123.dat", 11, 3),
                new Minecraft360ArchiveEntry("data/map_0.dat", 14, 2),
                new Minecraft360ArchiveEntry("requiredGameRules.grf", 16, 1),
            ],
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["level.dat"] = [1, 2, 3],
                ["r.0.0.mcr"] = [4, 5, 6, 7],
                ["DIM-1r.0.0.mcr"] = [8, 9, 10, 11],
                ["players/123.dat"] = [12, 13, 14],
                ["data/map_0.dat"] = [15, 16],
                ["requiredGameRules.grf"] = [17],
            });

        var container = new SaveDataContainer(originalSaveVersion: 7, currentSaveVersion: 9);

        Minecraft360ArchiveFileCopyResult result =
            new Minecraft360ArchiveFileCopyService().CopyAuxiliaryFiles(archive, container);

        string outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "saveData.ms");
        container.Save(outputPath);
        var reader = new LceSaveDataReader(outputPath);

        Assert.Equal(3, result.CopiedFiles);
        Assert.Equal(1, result.CopiedPlayerFiles);
        Assert.Equal(1, result.RemappedPrimaryPlayerFiles);
        Assert.True(reader.TryGetFileBytes(Minecraft360ArchiveFileCopyService.Windows64LegacyHostPlayerEntryName, out byte[] player));
        Assert.Equal([12, 13, 14], player);
        Assert.False(reader.TryGetFileBytes("players/123.dat", out _));
        Assert.True(reader.TryGetFileBytes("data/map_0.dat", out byte[] map));
        Assert.Equal([15, 16], map);
        Assert.True(reader.TryGetFileBytes("requiredGameRules.grf", out byte[] gameRules));
        Assert.Equal([17], gameRules);
        Assert.False(reader.TryGetFileBytes("level.dat", out _));
        Assert.False(reader.TryGetFileBytes("r.0.0.mcr", out _));
        Assert.False(reader.TryGetFileBytes("DIM-1r.0.0.mcr", out _));
    }

    [Theory]
    [InlineData("level.dat", false)]
    [InlineData("r.-1.0.mcr", false)]
    [InlineData("DIM1/r.0.0.mcr", false)]
    [InlineData("players/123.dat", true)]
    [InlineData("data/mapDataMappings.dat", true)]
    [InlineData("requiredGameRules.grf", true)]
    public void ShouldCopyAuxiliaryFile_FiltersExpectedArchiveEntries(string entryName, bool expected)
    {
        Assert.Equal(expected, Minecraft360ArchiveFileCopyService.ShouldCopyAuxiliaryFile(entryName));
    }

    [Fact]
    public void CopyAuxiliaryFiles_PreservesAdditionalPlayerFilesAfterPrimaryHostSlot()
    {
        var archive = new Minecraft360Archive(
            new Minecraft360ArchiveHeader(0, 3, 7, 9, 6),
            [
                new Minecraft360ArchiveEntry("players/111.dat", 0, 3),
                new Minecraft360ArchiveEntry("players/222.dat", 3, 3),
            ],
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["players/111.dat"] = [1, 2, 3],
                ["players/222.dat"] = [4, 5, 6],
            });

        var container = new SaveDataContainer(originalSaveVersion: 7, currentSaveVersion: 9);

        Minecraft360ArchiveFileCopyResult result =
            new Minecraft360ArchiveFileCopyService().CopyAuxiliaryFiles(archive, container);

        string outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "saveData.ms");
        container.Save(outputPath);
        var reader = new LceSaveDataReader(outputPath);

        Assert.Equal(2, result.CopiedFiles);
        Assert.Equal(2, result.CopiedPlayerFiles);
        Assert.Equal(1, result.RemappedPrimaryPlayerFiles);
        Assert.True(reader.TryGetFileBytes(Minecraft360ArchiveFileCopyService.Windows64LegacyHostPlayerEntryName, out byte[] primary));
        Assert.Equal([1, 2, 3], primary);
        Assert.True(reader.TryGetFileBytes("players/222.dat", out byte[] secondary));
        Assert.Equal([4, 5, 6], secondary);
    }
}
