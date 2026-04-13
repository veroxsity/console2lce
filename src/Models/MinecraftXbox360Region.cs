using System.Collections.ObjectModel;

namespace Console2Lce;

public sealed class MinecraftXbox360Region
{
    public MinecraftXbox360Region(
        string fileName,
        int length,
        IEnumerable<MinecraftXbox360RegionChunk> chunks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (length < 0)
        {
            throw new InvalidMinecraftXbox360RegionException("Region length cannot be negative.");
        }

        FileName = fileName;
        Length = length;
        Chunks = new ReadOnlyCollection<MinecraftXbox360RegionChunk>(chunks.ToList());
    }

    public string FileName { get; }

    public int Length { get; }

    public IReadOnlyList<MinecraftXbox360RegionChunk> Chunks { get; }

    public int PresentChunkCount => Chunks.Count;
}
