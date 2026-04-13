using System.Buffers.Binary;
using System.IO.Compression;

namespace Console2Lce.Tests;

public sealed class SavegameProbeServiceTests
{
    [Fact]
    public void Probe_DetectsStoredPayloadWithPlausibleArchiveHeader()
    {
        byte[] archive = CreateArchive(originalSaveVersion: 2, currentSaveVersion: 8);
        byte[] savegame = WrapWithLittleEndianEnvelope(archive);

        SavegameProbeResult result = new SavegameProbeService().Probe(savegame);

        Assert.True(result.Report.HasSuccessfulDecompression);
        Assert.Equal("HeaderAt0LittleEndian", result.Report.RecommendedEnvelope);
        Assert.Equal("StoredWithoutCompression", result.Report.RecommendedDecoder);
        Assert.Equal(archive, result.DecompressedBytes);
        Assert.Contains(result.Report.Attempts, attempt => attempt.Decoder == "WindowsXpress");
        Assert.Contains(result.Report.Attempts, attempt => attempt.Decoder == "WindowsLzmsRaw");
        Assert.Contains(result.Report.Attempts, attempt => attempt.Decoder == "XboxLzxNativeThenRle128k128k");
    }

    [Fact]
    public void Probe_DetectsZlibThenRlePayloadWithPlausibleArchiveHeader()
    {
        byte[] archive = CreateArchive(originalSaveVersion: 2, currentSaveVersion: 8);
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
        Assert.Contains(result.Report.Findings, finding => finding.Contains("4J archive", StringComparison.Ordinal));
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

    private static byte[] CreateArchive(short originalSaveVersion, short currentSaveVersion)
    {
        const int headerOffset = 0x20;
        const int totalLength = headerOffset + Minecraft360ArchiveParser.FileEntrySize;
        byte[] bytes = new byte[totalLength];
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0, 4), headerOffset);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4, 4), 1);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(8, 2), originalSaveVersion);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(10, 2), currentSaveVersion);

        WriteUtf16BigEndian(bytes.AsSpan(headerOffset, 128), "level.dat");
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(headerOffset + 128, 4), 4);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(headerOffset + 132, 4), Minecraft360ArchiveParser.SaveFileHeaderSize);
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(headerOffset + 136, 8), 1234);

        bytes[Minecraft360ArchiveParser.SaveFileHeaderSize] = 0x0A;
        bytes[Minecraft360ArchiveParser.SaveFileHeaderSize + 1] = 0x00;
        bytes[Minecraft360ArchiveParser.SaveFileHeaderSize + 2] = 0x00;
        bytes[Minecraft360ArchiveParser.SaveFileHeaderSize + 3] = 0x00;
        return bytes;
    }

    private static void WriteUtf16BigEndian(Span<byte> destination, string value)
    {
        byte[] encoded = System.Text.Encoding.BigEndianUnicode.GetBytes(value);
        encoded.CopyTo(destination);
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
