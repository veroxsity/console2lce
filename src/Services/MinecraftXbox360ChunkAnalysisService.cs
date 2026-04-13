namespace Console2Lce;

public static class MinecraftXbox360ChunkAnalysisService
{
    public static IReadOnlyList<MinecraftXbox360ChunkDecodeReport> Analyze(Minecraft360Archive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);

        IReadOnlyList<MinecraftXbox360Region> regions = MinecraftXbox360RegionAnalyzer.Analyze(archive);
        var decoder = new MinecraftXbox360ChunkDecoder();
        var reports = new List<MinecraftXbox360ChunkDecodeReport>();

        foreach (MinecraftXbox360Region region in regions)
        {
            MinecraftXbox360RegionChunk? sampleChunk = region.Chunks
                .OrderBy(chunk => chunk.Index)
                .FirstOrDefault();

            if (sampleChunk is null)
            {
                continue;
            }

            byte[] regionBytes = archive.Files[region.FileName];
            reports.Add(decoder.DecodeSample(region.FileName, sampleChunk, regionBytes));
        }

        return reports;
    }
}
