using Console2Lce;
using Xunit.Abstractions;

namespace Console2Lce.Tests;

public sealed class DumpChunkTest
{
    private readonly ITestOutputHelper _output;

    public DumpChunkTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void DumpDecodedChunk()
    {
        // Load the extracted archive to access region files
        string archivePath = @"c:\Users\Dan\Documents\Programming\.MinecraftLegacyEdition\console2lce\.local_testing\debug\savegame.decompressed.bin";
        string outputDir = @"c:\Users\Dan\Documents\Programming\.MinecraftLegacyEdition\console2lce\.local_testing\debug\";

        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"Archive not found: {archivePath}");
        }

        byte[] archiveBytes = File.ReadAllBytes(archivePath);
        var archiveParser = new Minecraft360ArchiveParser();
        Minecraft360Archive archive = archiveParser.Parse(archiveBytes);

        // Find a region file (r.0.0.mcr)
        string? regionFileName = archive.Files.Keys.FirstOrDefault(k => k.EndsWith("r.0.0.mcr"));
        if (regionFileName is null)
        {
            throw new InvalidOperationException("No r.0.0.mcr found in archive");
        }

        byte[] regionBytes = archive.Files[regionFileName];
        _output.WriteLine($"Found region file: {regionFileName} ({regionBytes.Length} bytes)");

        // Parse the region
        var parser = new MinecraftXbox360RegionParser();
        MinecraftXbox360Region region = parser.Parse(regionBytes, regionFileName);
        _output.WriteLine($"Region contains {region.Chunks.Count} chunks");

        // Get first chunk
        MinecraftXbox360RegionChunk chunk = region.Chunks.First();
        _output.WriteLine($"Chunk {chunk.Index}: stored={chunk.StoredLength} bytes, " +
                          $"decompressed={chunk.DecompressedLength} bytes, rle={chunk.UsesRleCompression}");

        // Decode it
        var decoder = new MinecraftXbox360ChunkDecoder();
        MinecraftXbox360ChunkDecodeReport report = decoder.DecodeSample(regionFileName, chunk, regionBytes);

        if (!report.Success)
        {
            throw new InvalidOperationException($"Decode failed: {report.Attempts.Last().Error}");
        }

        _output.WriteLine($"Decoded successfully: kind={report.PayloadKind}, length={report.DecodedLength}");

        // Extract the actual decoded bytes by decoding again
        byte[] compressedBytes = regionBytes.AsSpan()
            .Slice(chunk.PayloadOffset, chunk.StoredLength)
            .ToArray();

        var externalDecoder = new MccXboxSupportChunkExternalDecoder();
        if (!externalDecoder.TryDecode(compressedBytes, chunk.DecompressedLength, 
            out byte[] decodedBytes, out string? failure))
        {
            throw new InvalidOperationException($"Decode failed: {failure}");
        }

        // Save to file
        string outputFile = Path.Combine(outputDir, $"chunk-{chunk.Index}-decoded.bin");
        File.WriteAllBytes(outputFile, decodedBytes);
        _output.WriteLine($"Saved {decodedBytes.Length} bytes to {outputFile}");

        // Dump hex
        DumpHex(decodedBytes);
    }

    private void DumpHex(byte[] data)
    {
        const int bytesPerLine = 16;
        _output.WriteLine("\nFirst 512 bytes (hex dump):");
        
        for (int i = 0; i < Math.Min(512, data.Length); i += bytesPerLine)
        {
            int count = Math.Min(bytesPerLine, data.Length - i);
            string hex = string.Join(" ", data.Skip(i).Take(count).Select(b => b.ToString("X2")));
            string ascii = string.Concat(data.Skip(i).Take(count)
                .Select(b => b >= 32 && b < 127 ? (char)b : '.'));
            _output.WriteLine($"0x{i:X6}: {hex,-48} {ascii}");
        }
    }
}
