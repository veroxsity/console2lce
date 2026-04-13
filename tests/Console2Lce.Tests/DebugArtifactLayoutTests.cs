namespace Console2Lce.Tests;

public sealed class DebugArtifactLayoutTests
{
    [Fact]
    public void Constructor_NormalizesOutputPaths()
    {
        string root = Path.Combine(Path.GetTempPath(), "console2lce-tests", "inspect");
        var layout = new DebugArtifactLayout(root);

        Assert.Equal(Path.Combine(layout.OutputDirectory, "stfs-files.json"), layout.StfsFilesJsonPath);
        Assert.Equal(Path.Combine(layout.OutputDirectory, "savegame.dat"), layout.SavegameDatPath);
        Assert.Equal(Path.Combine(layout.OutputDirectory, "savegame-probe.json"), layout.SavegameProbeJsonPath);
        Assert.Equal(Path.Combine(layout.OutputDirectory, "savegame.decompressed.bin"), layout.SavegameDecompressedPath);
        Assert.Equal(Path.Combine(layout.OutputDirectory, "archive-index.json"), layout.ArchiveIndexJsonPath);
        Assert.Equal(Path.Combine(layout.OutputDirectory, "archive"), layout.ArchiveDirectoryPath);
        Assert.Equal(Path.Combine(layout.OutputDirectory, "region-analysis.json"), layout.RegionAnalysisJsonPath);
    }
}
