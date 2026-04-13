namespace Console2Lce;

public sealed class DebugArtifactLayout
{
    public DebugArtifactLayout(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        OutputDirectory = Path.GetFullPath(outputDirectory);
    }

    public string OutputDirectory { get; }

    public string StfsFilesJsonPath => Path.Combine(OutputDirectory, "stfs-files.json");

    public string SavegameDatPath => Path.Combine(OutputDirectory, "savegame.dat");

    public string SavegameProbeJsonPath => Path.Combine(OutputDirectory, "savegame-probe.json");

    public string SavegameDecompressedPath => Path.Combine(OutputDirectory, "savegame.decompressed.bin");

    public string ArchiveIndexJsonPath => Path.Combine(OutputDirectory, "archive-index.json");

    public string ArchiveDirectoryPath => Path.Combine(OutputDirectory, "archive");

    public string RegionAnalysisJsonPath => Path.Combine(OutputDirectory, "region-analysis.json");

    public string ChunkAnalysisJsonPath => Path.Combine(OutputDirectory, "chunk-analysis.json");
}
