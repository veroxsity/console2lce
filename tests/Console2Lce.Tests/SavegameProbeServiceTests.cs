using System.Buffers.Binary;
using System.IO.Compression;

namespace Console2Lce.Tests;

public sealed class SavegameProbeServiceTests
{
    [Fact]
    public void Probe_DetectsStoredPayloadWithPlausibleArchiveHeader()
    {
        byte[] archive = CreateArchive(indexOffset: 0x20, fileCount: 3, totalLength: 0x40);
        byte[] savegame = WrapWithLittleEndianEnvelope(archive);

        SavegameProbeResult result = new SavegameProbeService().Probe(savegame);

        Assert.True(result.Report.HasSuccessfulDecompression);
        Assert.Equal("HeaderAt0LittleEndian", result.Report.RecommendedEnvelope);
        Assert.Equal("StoredWithoutCompression", result.Report.RecommendedDecoder);
        Assert.Equal(archive, result.DecompressedBytes);
    }

    [Fact]
    public void Probe_DetectsZlibThenRlePayloadWithPlausibleArchiveHeader()
    {
        byte[] archive = CreateArchive(indexOffset: 0x28, fileCount: 5, totalLength: 0x60);
        byte[] encoded = EncodeRle(archive);
        byte[] compressed = CompressZlib(encoded);
        byte[] savegame = WrapWithLittleEndianEnvelope(compressed, archive.Length);

        SavegameProbeResult result = new SavegameProbeService().Probe(savegame);

        Assert.True(result.Report.HasSuccessfulDecompression);
        Assert.Equal("ZlibThenRle", result.Report.RecommendedDecoder);
        Assert.Equal(archive, result.DecompressedBytes);
    }

    [Fact]
    public void Probe_ReportsFailureWhenNoCandidateMatches()
    {
        byte[] invalid = new byte[32];

        SavegameProbeResult result = new SavegameProbeService().Probe(invalid);

        Assert.False(result.Report.HasSuccessfulDecompression);
        Assert.Null(result.DecompressedBytes);
        Assert.Contains(result.Report.Findings, finding => finding.Contains("not plausible", StringComparison.Ordinal));
        Assert.Contains(result.Report.Findings, finding => finding.Contains("LZXRLE/XMem", StringComparison.Ordinal));
    }

    [Fact]
    public void Probe_ReportsShortInputsClearly()
    {
        SavegameProbeResult result = new SavegameProbeService().Probe(Array.Empty<byte>());

        Assert.False(result.Report.HasSuccessfulDecompression);
        Assert.Empty(result.Report.Envelopes);
        Assert.Contains(result.Report.Findings, finding => finding.Contains("smaller than the expected 8-byte save envelope", StringComparison.Ordinal));
    }

    private static byte[] WrapWithLittleEndianEnvelope(byte[] payload, int? expectedSize = null)
    {
        byte[] wrapped = new byte[payload.Length + 8];
        BinaryPrimitives.WriteInt32LittleEndian(wrapped.AsSpan(0, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(wrapped.AsSpan(4, 4), expectedSize ?? payload.Length);
        payload.CopyTo(wrapped.AsSpan(8));
        return wrapped;
    }

    private static byte[] CreateArchive(int indexOffset, int fileCount, int totalLength)
    {
        byte[] bytes = new byte[totalLength];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), indexOffset);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), fileCount);
        return bytes;
    }

    private static byte[] EncodeRle(byte[] data)
    {
        using var output = new MemoryStream();
        int index = 0;

        while (index < data.Length)
        {
            byte current = data[index++];
            int count = 1;

            while (index < data.Length && data[index] == current && count < 256)
            {
                index++;
                count++;
            }

            if (count <= 3)
            {
                if (current == 0xFF)
                {
                    output.WriteByte(0xFF);
                    output.WriteByte((byte)(count - 1));
                }
                else
                {
                    for (int run = 0; run < count; run++)
                    {
                        output.WriteByte(current);
                    }
                }

                continue;
            }

            output.WriteByte(0xFF);
            output.WriteByte((byte)(count - 1));
            output.WriteByte(current);
        }

        return output.ToArray();
    }

    private static byte[] CompressZlib(byte[] data)
    {
        using var output = new MemoryStream();
        using var stream = new ZLibStream(output, CompressionLevel.SmallestSize);
        stream.Write(data, 0, data.Length);
        stream.Close();
        return output.ToArray();
    }
}
