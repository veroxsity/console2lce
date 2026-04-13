namespace Console2Lce;

public static class MinecraftXbox360RegionAnalyzer
{
    public static IReadOnlyList<MinecraftXbox360Region> Analyze(Minecraft360Archive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);

        var parser = new MinecraftXbox360RegionParser();
        var results = new List<MinecraftXbox360Region>();

        foreach ((string fileName, byte[] bytes) in archive.Files.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!fileName.EndsWith(".mcr", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            results.Add(parser.Parse(bytes, fileName));
        }

        return results;
    }
}
